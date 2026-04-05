// File: MarchingCubesVoxelDestruction.cs
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace CabinProject
{
    [RequireComponent(typeof(Transform))]
    public class MarchingCubesVoxelDestruction : MonoBehaviour
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Triangle
        {
            public Vector3 VertexA;
            public Vector3 VertexB;
            public Vector3 VertexC;
        }

        [Header("Voxel Grid")]
        [SerializeField] private int _gridResolution = 32;
        [SerializeField] private float _excavationRadius = 0.5f;
        [SerializeField] private float _isoValue = 0.5f;
        [SerializeField] private ComputeShader _computeShader;

        private const int MarchingCubesKernelThreadSize = 8;
        private const int ExcavateKernelThreadSize = 8;
        private const int MaxTrianglesPerCell = 5;

        private static readonly int MarchingCubesKernel = Shader.PropertyToID("MarchingCubes");
        private static readonly int ExcavateKernel = Shader.PropertyToID("Excavate");
        private static readonly int DensityBufferId = Shader.PropertyToID("_DensityBuffer");
        private static readonly int TriangleBufferId = Shader.PropertyToID("_Triangles");
        private static readonly int CellResolutionId = Shader.PropertyToID("_CellResolution");
        private static readonly int PointResolutionId = Shader.PropertyToID("_PointResolution");
        private static readonly int IsoValueId = Shader.PropertyToID("_IsoValue");
        private static readonly int FieldMinLocalId = Shader.PropertyToID("_FieldMinLocal");
        private static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
        private static readonly int LocalToWorldId = Shader.PropertyToID("_LocalToWorld");
        private static readonly int HitPointWorldId = Shader.PropertyToID("_HitPointWorld");
        private static readonly int ExcavationRadiusId = Shader.PropertyToID("_ExcavationRadius");

        private MeshFilter _sourceMeshFilter;
        private MeshRenderer _sourceMeshRenderer;
        private Collider[] _sourceColliders;
        private Mesh _sourceMeshAsset;

        private GameObject _runtimeMeshObject;
        private MeshFilter _runtimeMeshFilter;
        private MeshRenderer _runtimeMeshRenderer;
        private MeshCollider _runtimeMeshCollider;
        private Mesh _runtimeMesh;

        private ComputeBuffer _densityBuffer;
        private ComputeBuffer _triangleBuffer;
        private ComputeBuffer _triangleCountBuffer;

        private int _marchingCubesKernelIndex;
        private int _excavateKernelIndex;
        private int _pointResolution;
        private Vector3 _fieldMinLocal;
        private Vector3 _cellSize;
        private Bounds _sourceLocalBounds;

        public bool IsReady { get; private set; }

        private void Awake()
        {
            _sourceMeshFilter = GetComponent<MeshFilter>();
            _sourceMeshRenderer = GetComponent<MeshRenderer>();
            _sourceColliders = GetComponents<Collider>();
            _sourceMeshAsset = _sourceMeshFilter != null ? _sourceMeshFilter.sharedMesh : null;
        }

        private void Start()
        {
            Initialize();
        }

        private void OnValidate()
        {
            _gridResolution = Mathf.Max(3, _gridResolution);
            _excavationRadius = Mathf.Max(0.01f, _excavationRadius);
            _isoValue = Mathf.Clamp01(_isoValue);
        }

        public void Excavate(Vector3 hitPointWorld)
        {
            if (!IsReady)
            {
                return;
            }

            UpdateSharedShaderParameters();
            _computeShader.SetVector(HitPointWorldId, hitPointWorld);
            _computeShader.SetFloat(ExcavationRadiusId, _excavationRadius);

            int groupCount = Mathf.CeilToInt(_pointResolution / (float)ExcavateKernelThreadSize);
            _computeShader.Dispatch(_excavateKernelIndex, groupCount, groupCount, groupCount);

            RegenerateMesh();
        }

        private void Initialize()
        {
            if (_computeShader == null)
            {
                Debug.LogError($"Marching cubes compute shader is missing on {name}.", this);
                return;
            }

            if (_sourceMeshFilter == null || _sourceMeshFilter.sharedMesh == null || _sourceMeshRenderer == null)
            {
                Debug.LogError($"MarchingCubesVoxelDestruction requires a MeshFilter and MeshRenderer on {name}.", this);
                return;
            }

            _marchingCubesKernelIndex = _computeShader.FindKernel("MarchingCubes");
            _excavateKernelIndex = _computeShader.FindKernel("Excavate");
            _pointResolution = _gridResolution + 1;
            _sourceLocalBounds = _sourceMeshFilter.sharedMesh.bounds;
            _cellSize = CalculateCellSize(_sourceLocalBounds.size, _gridResolution);
            _fieldMinLocal = _sourceLocalBounds.min - _cellSize;

            CreateRuntimeObjects();
            CreateBuffers();
            InitializeDensityField();
            DisableSourceRendering();
            RegenerateMesh();

            IsReady = true;
        }

        private void CreateRuntimeObjects()
        {
            _runtimeMeshObject = new GameObject($"{name}_MarchingCubes");
            _runtimeMeshObject.transform.SetParent(transform, false);
            _runtimeMeshObject.transform.localPosition = Vector3.zero;
            _runtimeMeshObject.transform.localRotation = Quaternion.identity;
            _runtimeMeshObject.transform.localScale = Vector3.one;

            _runtimeMeshFilter = _runtimeMeshObject.AddComponent<MeshFilter>();
            _runtimeMeshRenderer = _runtimeMeshObject.AddComponent<MeshRenderer>();
            _runtimeMeshCollider = _runtimeMeshObject.AddComponent<MeshCollider>();

            if (_sourceMeshRenderer.sharedMaterials != null && _sourceMeshRenderer.sharedMaterials.Length > 0)
            {
                _runtimeMeshRenderer.sharedMaterials = _sourceMeshRenderer.sharedMaterials;
            }
            else
            {
                _runtimeMeshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
            }

            _runtimeMesh = new Mesh
            {
                name = $"{name}_MarchingCubesMesh",
                indexFormat = IndexFormat.UInt32
            };
            _runtimeMesh.MarkDynamic();
            _runtimeMeshFilter.sharedMesh = _runtimeMesh;
        }

        private void CreateBuffers()
        {
            int pointCount = _pointResolution * _pointResolution * _pointResolution;
            int cellCount = _gridResolution * _gridResolution * _gridResolution;

            _densityBuffer = new ComputeBuffer(pointCount, sizeof(float));
            _triangleBuffer = new ComputeBuffer(cellCount * MaxTrianglesPerCell, Marshal.SizeOf<Triangle>(), ComputeBufferType.Append);
            _triangleCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        }

        private void InitializeDensityField()
        {
            int pointCount = _pointResolution * _pointResolution * _pointResolution;
            float[] densityValues = new float[pointCount];

            for (int z = 0; z < _pointResolution; z++)
            {
                for (int y = 0; y < _pointResolution; y++)
                {
                    for (int x = 0; x < _pointResolution; x++)
                    {
                        int index = ToIndex(x, y, z, _pointResolution);
                        Vector3 localPosition = GetLocalPosition(x, y, z);
                        densityValues[index] = _sourceLocalBounds.Contains(localPosition) ? 1f : 0f;
                    }
                }
            }

            _densityBuffer.SetData(densityValues);
        }

        private void DisableSourceRendering()
        {
            _sourceMeshRenderer.enabled = false;
            _sourceMeshFilter.sharedMesh = null;

            foreach (Collider sourceCollider in _sourceColliders)
            {
                sourceCollider.enabled = false;
            }
        }

        private void RegenerateMesh()
        {
            _triangleBuffer.SetCounterValue(0);
            UpdateSharedShaderParameters();
            _computeShader.SetBuffer(_marchingCubesKernelIndex, DensityBufferId, _densityBuffer);
            _computeShader.SetBuffer(_marchingCubesKernelIndex, TriangleBufferId, _triangleBuffer);

            int groupCount = Mathf.CeilToInt(_gridResolution / (float)MarchingCubesKernelThreadSize);
            _computeShader.Dispatch(_marchingCubesKernelIndex, groupCount, groupCount, groupCount);

            ComputeBuffer.CopyCount(_triangleBuffer, _triangleCountBuffer, 0);

            int[] triangleCountData = { 0 };
            _triangleCountBuffer.GetData(triangleCountData);
            int triangleCount = triangleCountData[0];

            ApplyMeshFromGpuTriangles(triangleCount);
        }

        private void ApplyMeshFromGpuTriangles(int triangleCount)
        {
            _runtimeMesh.Clear();

            if (triangleCount <= 0)
            {
                _runtimeMeshCollider.sharedMesh = null;
                return;
            }

            Triangle[] gpuTriangles = new Triangle[triangleCount];
            _triangleBuffer.GetData(gpuTriangles, 0, 0, triangleCount);

            Vector3[] vertices = new Vector3[triangleCount * 3];
            int[] indices = new int[triangleCount * 3];

            for (int i = 0; i < triangleCount; i++)
            {
                int vertexIndex = i * 3;
                vertices[vertexIndex] = gpuTriangles[i].VertexA;
                // Flip winding so the generated surface faces outward in Unity.
                vertices[vertexIndex + 1] = gpuTriangles[i].VertexC;
                vertices[vertexIndex + 2] = gpuTriangles[i].VertexB;

                indices[vertexIndex] = vertexIndex;
                indices[vertexIndex + 1] = vertexIndex + 1;
                indices[vertexIndex + 2] = vertexIndex + 2;
            }

            _runtimeMesh.vertices = vertices;
            _runtimeMesh.triangles = indices;
            _runtimeMesh.RecalculateBounds();
            _runtimeMesh.RecalculateNormals();

            _runtimeMeshCollider.sharedMesh = null;
            _runtimeMeshCollider.sharedMesh = _runtimeMesh;
        }

        private void UpdateSharedShaderParameters()
        {
            _computeShader.SetInt(CellResolutionId, _gridResolution);
            _computeShader.SetInt(PointResolutionId, _pointResolution);
            _computeShader.SetFloat(IsoValueId, _isoValue);
            _computeShader.SetVector(FieldMinLocalId, _fieldMinLocal);
            _computeShader.SetVector(CellSizeId, _cellSize);
            _computeShader.SetMatrix(LocalToWorldId, transform.localToWorldMatrix);

            _computeShader.SetBuffer(_marchingCubesKernelIndex, DensityBufferId, _densityBuffer);
            _computeShader.SetBuffer(_excavateKernelIndex, DensityBufferId, _densityBuffer);
        }

        private Vector3 GetLocalPosition(int x, int y, int z)
        {
            return _fieldMinLocal + Vector3.Scale(new Vector3(x, y, z), _cellSize);
        }

        private static Vector3 CalculateCellSize(Vector3 boundsSize, int gridResolution)
        {
            int interiorCells = Mathf.Max(1, gridResolution - 2);
            return new Vector3(
                boundsSize.x / interiorCells,
                boundsSize.y / interiorCells,
                boundsSize.z / interiorCells);
        }

        private static int ToIndex(int x, int y, int z, int resolution)
        {
            return x + resolution * (y + resolution * z);
        }

        private void OnDestroy()
        {
            ReleaseBuffer(ref _densityBuffer);
            ReleaseBuffer(ref _triangleBuffer);
            ReleaseBuffer(ref _triangleCountBuffer);

            if (_runtimeMesh != null)
            {
                Destroy(_runtimeMesh);
            }

            if (_runtimeMeshObject != null)
            {
                Destroy(_runtimeMeshObject);
            }

            if (_sourceMeshFilter != null)
            {
                _sourceMeshFilter.sharedMesh = _sourceMeshAsset;
            }
        }

        private static void ReleaseBuffer(ref ComputeBuffer buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Release();
            buffer = null;
        }
    }
}
