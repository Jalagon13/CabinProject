// File: MarchingCubesVoxelDestruction.cs
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace CabinProject
{
    [RequireComponent(typeof(Transform))]
    public class MarchingCubesVoxelDestruction : MonoBehaviour
    {
        private const float RaycastEpsilon = 0.00001f;

        [StructLayout(LayoutKind.Sequential)]
        private struct Triangle
        {
            public Vector3 VertexA;
            public Vector3 VertexB;
            public Vector3 VertexC;
        }

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
        private Vector3[] _sourceVertices;
        private int[] _sourceTriangles;

        private GameObject _runtimeMeshObject;
        private MeshFilter _runtimeMeshFilter;
        private MeshRenderer _runtimeMeshRenderer;
        private MeshCollider _runtimeMeshCollider;
        private Mesh _runtimeMesh;

        private int _gridResolution = 32;
        private float _excavationRadius = 0.5f;
        private float _isoValue = 0.5f;
        private ComputeShader _computeShader;

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
            _sourceVertices = _sourceMeshAsset != null ? _sourceMeshAsset.vertices : null;
            _sourceTriangles = _sourceMeshAsset != null ? _sourceMeshAsset.triangles : null;
        }

        private void Start()
        {
            Initialize();
        }

        public void Excavate(Vector3 hitPointWorld)
        {
            if (!IsReady)
            {
                return;
            }

            _gridResolution = Mathf.Max(3, _gridResolution);
            _excavationRadius = Mathf.Max(0.01f, _excavationRadius);
            _isoValue = Mathf.Clamp01(_isoValue);
            _pointResolution = _gridResolution + 1;

            UpdateSharedShaderParameters();
            _computeShader.SetVector(HitPointWorldId, hitPointWorld);
            _computeShader.SetFloat(ExcavationRadiusId, _excavationRadius);

            int groupCount = Mathf.CeilToInt(_pointResolution / (float)ExcavateKernelThreadSize);
            _computeShader.Dispatch(_excavateKernelIndex, groupCount, groupCount, groupCount);

            RegenerateMesh();
        }

        private void Initialize()
        {
            _computeShader = CrosshairManager.Instance.ComputeShader;
            _gridResolution = CrosshairManager.Instance.GridResolution;
            _isoValue = CrosshairManager.Instance.IsoValue;
            _excavationRadius = CrosshairManager.Instance.ExcavationRadius;
        
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
            _gridResolution = Mathf.Max(3, _gridResolution);
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
            float surfaceBand = Mathf.Max(_cellSize.x, Mathf.Max(_cellSize.y, _cellSize.z));

            for (int z = 0; z < _pointResolution; z++)
            {
                for (int y = 0; y < _pointResolution; y++)
                {
                    for (int x = 0; x < _pointResolution; x++)
                    {
                        int index = ToIndex(x, y, z, _pointResolution);
                        Vector3 localPosition = GetLocalPosition(x, y, z);
                        float signedDistance = CalculateSignedDistanceToMesh(localPosition);
                        densityValues[index] = Mathf.Clamp01(0.5f - (signedDistance / surfaceBand));
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

        private float CalculateSignedDistanceToMesh(Vector3 point)
        {
            if (_sourceVertices == null || _sourceTriangles == null || _sourceTriangles.Length < 3)
            {
                return float.PositiveInfinity;
            }

            Vector3 rayDirection = new Vector3(1f, 0.371f, 0.173f).normalized;
            int intersectionCount = 0;
            float minDistanceSquared = float.PositiveInfinity;

            for (int i = 0; i < _sourceTriangles.Length; i += 3)
            {
                Vector3 a = _sourceVertices[_sourceTriangles[i]];
                Vector3 b = _sourceVertices[_sourceTriangles[i + 1]];
                Vector3 c = _sourceVertices[_sourceTriangles[i + 2]];

                Vector3 closestPoint = ClosestPointOnTriangle(point, a, b, c);
                float distanceSquared = (point - closestPoint).sqrMagnitude;
                if (distanceSquared < minDistanceSquared)
                {
                    minDistanceSquared = distanceSquared;
                }

                if (RayIntersectsTriangle(point, rayDirection, a, b, c, out float hitDistance) && hitDistance > RaycastEpsilon)
                {
                    intersectionCount++;
                }
            }

            float distance = Mathf.Sqrt(minDistanceSquared);
            bool isInside = (intersectionCount & 1) == 1;
            return isInside ? -distance : distance;
        }

        private static bool RayIntersectsTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            Vector3 edgeAB = b - a;
            Vector3 edgeAC = c - a;
            Vector3 pVector = Vector3.Cross(direction, edgeAC);
            float determinant = Vector3.Dot(edgeAB, pVector);

            if (Mathf.Abs(determinant) < RaycastEpsilon)
            {
                distance = 0f;
                return false;
            }

            float inverseDeterminant = 1f / determinant;
            Vector3 tVector = origin - a;
            float u = Vector3.Dot(tVector, pVector) * inverseDeterminant;
            if (u < 0f || u > 1f)
            {
                distance = 0f;
                return false;
            }

            Vector3 qVector = Vector3.Cross(tVector, edgeAB);
            float v = Vector3.Dot(direction, qVector) * inverseDeterminant;
            if (v < 0f || u + v > 1f)
            {
                distance = 0f;
                return false;
            }

            distance = Vector3.Dot(edgeAC, qVector) * inverseDeterminant;
            return distance >= 0f;
        }

        private static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = point - a;

            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f)
            {
                return a;
            }

            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3)
            {
                return b;
            }

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + (ab * v);
            }

            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6)
            {
                return c;
            }

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + (ac * w);
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + ((c - b) * w);
            }

            float denominator = 1f / (va + vb + vc);
            float barycentricV = vb * denominator;
            float barycentricW = vc * denominator;
            return a + (ab * barycentricV) + (ac * barycentricW);
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
