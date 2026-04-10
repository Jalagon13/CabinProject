// File: MarchingCubesVoxelDestruction.cs
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace CabinProject
{
    [RequireComponent(typeof(Transform))]
    [RequireComponent(typeof(ExcavationDebrisSpawner))]
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
        private Mesh _runtimeColliderMesh;
        private ExcavationDebrisSpawner _excavationDebrisSpawner;

        private int _gridResolution = 32;
        private float _excavationRadius = 0.5f;
        private float _isoValue = 0.5f;
        [SerializeField] private float _generatedUvScale = 1f;
        [SerializeField] private bool _enableFieldNoise = true;
        [SerializeField] private float _fieldNoiseFrequency = 2f;
        [SerializeField] private float _fieldNoiseAmplitude = 0.06f;
        [SerializeField] private int _fieldNoiseOctaves = 2;
        [SerializeField] private int _fieldNoiseSeed = 1337;
        [SerializeField] private bool _enableRenderDisplacement = false;
        [SerializeField] private float _renderDisplacementAmplitude = 0.05f;
        [SerializeField] private float _renderDisplacementFrequency = 3f;
        [SerializeField] private float _floatingFragmentCleanupRadiusMultiplier = 2f;
        [SerializeField] private int _maxFloatingFragmentVoxelCount = 8;
        private ComputeShader _computeShader;

        private ComputeBuffer _densityBuffer;
        private ComputeBuffer _triangleBuffer;
        private ComputeBuffer _triangleCountBuffer;
        private float[] _densityValues;
        private readonly List<StatueExposureTracker> _exposureTrackers = new List<StatueExposureTracker>();

        private int _marchingCubesKernelIndex;
        private int _excavateKernelIndex;
        private int _pointResolution;
        private Vector3 _fieldMinLocal;
        private Vector3 _cellSize;
        private Bounds _sourceLocalBounds;


        public bool IsReady { get; private set; }
        public int PointResolution => _pointResolution;
        public float IsoValue => _isoValue;
        public Vector3 FieldMinLocal => _fieldMinLocal;
        public Vector3 CellSize => _cellSize;
        public float MaxCellSize => Mathf.Max(_cellSize.x, Mathf.Max(_cellSize.y, _cellSize.z));

        private void Awake()
        {
            _sourceMeshFilter = GetComponent<MeshFilter>();
            _sourceMeshRenderer = GetComponent<MeshRenderer>();
            _sourceColliders = GetComponents<Collider>();
            _excavationDebrisSpawner = GetComponent<ExcavationDebrisSpawner>();
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
            List<int> newlyExposedSamples = ApplyCpuExcavation(hitPointWorld);
            RemoveFloatingFragmentsNearExcavation(hitPointWorld, newlyExposedSamples);
            _densityBuffer.SetData(_densityValues);

            RegenerateMesh();
            List<int> debrisEligibleSamples = FilterDebrisSamplesOutsideStatues(newlyExposedSamples);
            _excavationDebrisSpawner?.SpawnDebris(debrisEligibleSamples, hitPointWorld);
            NotifyExposureTrackers(newlyExposedSamples);
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
            _fieldNoiseFrequency = Mathf.Max(0.01f, _fieldNoiseFrequency);
            _fieldNoiseAmplitude = Mathf.Max(0f, _fieldNoiseAmplitude);
            _fieldNoiseOctaves = Mathf.Clamp(_fieldNoiseOctaves, 1, 4);
            _renderDisplacementFrequency = Mathf.Max(0.01f, _renderDisplacementFrequency);
            _renderDisplacementAmplitude = Mathf.Clamp(_renderDisplacementAmplitude, 0f, 0.15f);
            _sourceLocalBounds = _sourceMeshFilter.sharedMesh.bounds;
            _cellSize = CalculateCellSize(_sourceLocalBounds.size, _gridResolution);
            _fieldMinLocal = _sourceLocalBounds.min - _cellSize;

            CreateRuntimeObjects();
            CreateBuffers();
            InitializeDensityField();
            DisableSourceRendering();
            RegenerateMesh();

            IsReady = true;
            RegisterChildExposureTrackers();
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

            _runtimeColliderMesh = new Mesh
            {
                name = $"{name}_MarchingCubesColliderMesh",
                indexFormat = IndexFormat.UInt32
            };
            _runtimeColliderMesh.MarkDynamic();
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
            _densityValues = new float[pointCount];
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
                        float baseDensity = Mathf.Clamp01(0.5f - (signedDistance / surfaceBand));
                        _densityValues[index] = ApplyFieldNoiseToDensity(baseDensity, localPosition);
                    }
                }
            }

            _densityBuffer.SetData(_densityValues);
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
            Vector3[] colliderVertices = new Vector3[triangleCount * 3];
            int[] indices = new int[triangleCount * 3];

            for (int i = 0; i < triangleCount; i++)
            {
                int vertexIndex = i * 3;
                Vector3 vertexA = gpuTriangles[i].VertexA;
                Vector3 vertexB = gpuTriangles[i].VertexB;
                Vector3 vertexC = gpuTriangles[i].VertexC;
                vertices[vertexIndex] = vertexA;
                // Flip winding so the generated surface faces outward in Unity.
                vertices[vertexIndex + 1] = vertexC;
                vertices[vertexIndex + 2] = vertexB;
                colliderVertices[vertexIndex] = vertexA;
                colliderVertices[vertexIndex + 1] = vertexC;
                colliderVertices[vertexIndex + 2] = vertexB;

                indices[vertexIndex] = vertexIndex;
                indices[vertexIndex + 1] = vertexIndex + 1;
                indices[vertexIndex + 2] = vertexIndex + 2;
            }

            if (_enableRenderDisplacement)
            {
                ApplyRenderDisplacement(vertices);
            }

            _runtimeMesh.vertices = vertices;
            _runtimeMesh.triangles = indices;
            _runtimeMesh.RecalculateBounds();
            _runtimeMesh.RecalculateNormals();
            _runtimeMesh.uv = GenerateProjectedUvs(_runtimeMesh.vertices, _runtimeMesh.normals);
            _runtimeMesh.RecalculateTangents();

            _runtimeColliderMesh.Clear();
            _runtimeColliderMesh.vertices = colliderVertices;
            _runtimeColliderMesh.triangles = indices;
            _runtimeColliderMesh.RecalculateBounds();

            _runtimeMeshCollider.sharedMesh = null;
            _runtimeMeshCollider.sharedMesh = _runtimeColliderMesh;
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

        private Vector2[] GenerateProjectedUvs(Vector3[] vertices, Vector3[] normals)
        {
            Vector2[] uvs = new Vector2[vertices.Length];
            float uvScale = Mathf.Max(0.01f, _generatedUvScale);

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 normal = normals != null && i < normals.Length ? normals[i] : Vector3.up;
                Vector3 absoluteNormal = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
                Vector3 vertex = vertices[i];

                if (absoluteNormal.y >= absoluteNormal.x && absoluteNormal.y >= absoluteNormal.z)
                {
                    uvs[i] = new Vector2(vertex.x, vertex.z) * uvScale;
                }
                else if (absoluteNormal.x >= absoluteNormal.z)
                {
                    uvs[i] = new Vector2(vertex.z, vertex.y) * uvScale;
                }
                else
                {
                    uvs[i] = new Vector2(vertex.x, vertex.y) * uvScale;
                }
            }

            return uvs;
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

            if (_runtimeColliderMesh != null)
            {
                Destroy(_runtimeColliderMesh);
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

        public void RegisterExposureTracker(StatueExposureTracker tracker)
        {
            if (tracker == null)
            {
                return;
            }

            if (!_exposureTrackers.Contains(tracker))
            {
                _exposureTrackers.Add(tracker);
            }

            if (IsReady)
            {
                tracker.BindToVolume(this);
            }
        }

        public void UnregisterExposureTracker(StatueExposureTracker tracker)
        {
            if (tracker == null)
            {
                return;
            }

            _exposureTrackers.Remove(tracker);
        }

        public int ToFlatIndex(int x, int y, int z)
        {
            return ToIndex(x, y, z, _pointResolution);
        }

        public bool TryGetSampleCoordinates(int flatIndex, out Vector3Int coordinates)
        {
            coordinates = default;
            if (flatIndex < 0 || flatIndex >= _pointResolution * _pointResolution * _pointResolution)
            {
                return false;
            }

            int planeSize = _pointResolution * _pointResolution;
            int z = flatIndex / planeSize;
            int remainder = flatIndex - (z * planeSize);
            int y = remainder / _pointResolution;
            int x = remainder - (y * _pointResolution);
            coordinates = new Vector3Int(x, y, z);
            return true;
        }

        public Vector3 GetSampleLocalPosition(int x, int y, int z)
        {
            return GetLocalPosition(x, y, z);
        }

        public bool IsSampleSolid(int flatIndex)
        {
            return flatIndex >= 0
                && _densityValues != null
                && flatIndex < _densityValues.Length
                && _densityValues[flatIndex] >= _isoValue;
        }

        public Vector3 GetExcavationPoint(Ray ray, Vector3 fallbackPointWorld, float fallbackDistance)
        {
            float closestDistance = fallbackDistance;
            Vector3 closestPoint = fallbackPointWorld;

            for (int i = _exposureTrackers.Count - 1; i >= 0; i--)
            {
                StatueExposureTracker tracker = _exposureTrackers[i];
                if (tracker == null)
                {
                    _exposureTrackers.RemoveAt(i);
                    continue;
                }

                if (!tracker.TryGetSurfaceHit(ray, fallbackDistance, out Vector3 statueHitPoint, out float statueHitDistance))
                {
                    continue;
                }

                if (statueHitDistance < closestDistance)
                {
                    closestDistance = statueHitDistance;
                    closestPoint = statueHitPoint;
                }
            }

            return closestPoint;
        }

        public Vector3 GetExcavationSurfaceNormal(Vector3 hitPointWorld)
        {
            if (_densityValues == null || _densityValues.Length == 0)
            {
                return transform.up;
            }

            Vector3 hitPointLocal = transform.InverseTransformPoint(hitPointWorld);
            Vector3 gradient = EstimateDensityGradient(hitPointLocal);
            if (gradient.sqrMagnitude <= 0.000001f)
            {
                Vector3 fallback = (hitPointWorld - transform.position).normalized;
                return fallback.sqrMagnitude > 0.000001f ? fallback : transform.up;
            }

            Vector3 outwardLocalNormal = -gradient.normalized;
            return transform.TransformDirection(outwardLocalNormal).normalized;
        }

        public void HideRemainingStone()
        {
            if (_runtimeMeshObject == null)
            {
                return;
            }

            _runtimeMeshObject.SetActive(false);
        }

        private void RegisterChildExposureTrackers()
        {
            StatueExposureTracker[] childTrackers = GetComponentsInChildren<StatueExposureTracker>(true);
            Debug.Log($"{name} found {childTrackers.Length} child statue exposure tracker(s).", this);
            foreach (StatueExposureTracker tracker in childTrackers)
            {
                RegisterExposureTracker(tracker);
            }
        }

        private List<int> ApplyCpuExcavation(Vector3 hitPointWorld)
        {
            List<int> newlyExposedSamples = new List<int>();
            if (_densityValues == null)
            {
                return newlyExposedSamples;
            }

            for (int z = 0; z < _pointResolution; z++)
            {
                for (int y = 0; y < _pointResolution; y++)
                {
                    for (int x = 0; x < _pointResolution; x++)
                    {
                        int index = ToIndex(x, y, z, _pointResolution);
                        Vector3 localPosition = GetLocalPosition(x, y, z);
                        Vector3 worldPosition = transform.TransformPoint(localPosition);
                        float distanceToHit = Vector3.Distance(worldPosition, hitPointWorld);
                        if (distanceToHit > _excavationRadius)
                        {
                            continue;
                        }

                        float previousDensity = _densityValues[index];
                        float normalizedDistance = Mathf.Clamp01(distanceToHit / _excavationRadius);
                        float excavatedDensity = normalizedDistance;
                        float newDensity = Mathf.Min(previousDensity, excavatedDensity);
                        _densityValues[index] = ApplyFieldNoiseToDensity(newDensity, localPosition);

                        if (previousDensity >= _isoValue && newDensity < _isoValue)
                        {
                            newlyExposedSamples.Add(index);
                        }
                    }
                }
            }

            return newlyExposedSamples;
        }

        private List<int> FilterDebrisSamplesOutsideStatues(List<int> removedSamples)
        {
            List<int> debrisSamples = new List<int>();
            if (removedSamples == null || removedSamples.Count == 0)
            {
                return debrisSamples;
            }

            for (int i = 0; i < removedSamples.Count; i++)
            {
                if (!TryGetSampleCoordinates(removedSamples[i], out Vector3Int coordinates))
                {
                    continue;
                }

                Vector3 worldPosition = transform.TransformPoint(GetSampleLocalPosition(coordinates.x, coordinates.y, coordinates.z));
                if (IsPointInsideAnyTrackedStatue(worldPosition))
                {
                    continue;
                }

                debrisSamples.Add(removedSamples[i]);
            }

            return debrisSamples;
        }

        private void RemoveFloatingFragmentsNearExcavation(Vector3 hitPointWorld, List<int> newlyExposedSamples)
        {
            if (_densityValues == null || _maxFloatingFragmentVoxelCount <= 0)
            {
                return;
            }

            float cleanupRadius = Mathf.Max(_excavationRadius, _excavationRadius * _floatingFragmentCleanupRadiusMultiplier);
            Vector3 hitPointLocal = transform.InverseTransformPoint(hitPointWorld);
            Vector3 localMin = hitPointLocal - Vector3.one * cleanupRadius;
            Vector3 localMax = hitPointLocal + Vector3.one * cleanupRadius;

            Vector3Int min = LocalPositionToSampleCoordinates(localMin, true);
            Vector3Int max = LocalPositionToSampleCoordinates(localMax, false);
            int regionWidth = max.x - min.x + 1;
            int regionHeight = max.y - min.y + 1;
            int regionDepth = max.z - min.z + 1;
            if (regionWidth <= 0 || regionHeight <= 0 || regionDepth <= 0)
            {
                return;
            }

            int regionCount = regionWidth * regionHeight * regionDepth;
            bool[] isCandidate = new bool[regionCount];
            bool[] visited = new bool[regionCount];
            Queue<int> queue = new Queue<int>();
            List<int> component = new List<int>();

            for (int z = min.z; z <= max.z; z++)
            {
                for (int y = min.y; y <= max.y; y++)
                {
                    for (int x = min.x; x <= max.x; x++)
                    {
                        int flatIndex = ToIndex(x, y, z, _pointResolution);
                        if (_densityValues[flatIndex] < _isoValue)
                        {
                            continue;
                        }

                        Vector3 sampleLocal = GetLocalPosition(x, y, z);
                        if ((sampleLocal - hitPointLocal).sqrMagnitude > cleanupRadius * cleanupRadius)
                        {
                            continue;
                        }

                        isCandidate[ToRegionIndex(x, y, z, min, regionWidth, regionHeight)] = true;
                    }
                }
            }

            for (int z = min.z; z <= max.z; z++)
            {
                for (int y = min.y; y <= max.y; y++)
                {
                    for (int x = min.x; x <= max.x; x++)
                    {
                        int regionIndex = ToRegionIndex(x, y, z, min, regionWidth, regionHeight);
                        if (!isCandidate[regionIndex] || visited[regionIndex])
                        {
                            continue;
                        }

                        component.Clear();
                        queue.Clear();
                        queue.Enqueue(regionIndex);
                        visited[regionIndex] = true;
                        bool touchesBoundary = false;

                        while (queue.Count > 0)
                        {
                            int currentRegionIndex = queue.Dequeue();
                            component.Add(currentRegionIndex);
                            RegionIndexToCoordinates(currentRegionIndex, min, regionWidth, regionHeight, out int currentX, out int currentY, out int currentZ);

                            if (currentX == min.x || currentX == max.x
                                || currentY == min.y || currentY == max.y
                                || currentZ == min.z || currentZ == max.z)
                            {
                                touchesBoundary = true;
                            }

                            TryEnqueueNeighbor(currentX - 1, currentY, currentZ, min, max, regionWidth, regionHeight, isCandidate, visited, queue);
                            TryEnqueueNeighbor(currentX + 1, currentY, currentZ, min, max, regionWidth, regionHeight, isCandidate, visited, queue);
                            TryEnqueueNeighbor(currentX, currentY - 1, currentZ, min, max, regionWidth, regionHeight, isCandidate, visited, queue);
                            TryEnqueueNeighbor(currentX, currentY + 1, currentZ, min, max, regionWidth, regionHeight, isCandidate, visited, queue);
                            TryEnqueueNeighbor(currentX, currentY, currentZ - 1, min, max, regionWidth, regionHeight, isCandidate, visited, queue);
                            TryEnqueueNeighbor(currentX, currentY, currentZ + 1, min, max, regionWidth, regionHeight, isCandidate, visited, queue);
                        }

                        if (touchesBoundary || component.Count > _maxFloatingFragmentVoxelCount)
                        {
                            continue;
                        }

                        for (int i = 0; i < component.Count; i++)
                        {
                            RegionIndexToCoordinates(component[i], min, regionWidth, regionHeight, out int voxelX, out int voxelY, out int voxelZ);
                            int flatIndex = ToIndex(voxelX, voxelY, voxelZ, _pointResolution);
                            if (_densityValues[flatIndex] < _isoValue)
                            {
                                continue;
                            }

                            Vector3 localPosition = GetLocalPosition(voxelX, voxelY, voxelZ);
                            _densityValues[flatIndex] = ApplyFieldNoiseToDensity(0f, localPosition);
                            newlyExposedSamples?.Add(flatIndex);
                        }
                    }
                }
            }
        }

        private Vector3Int LocalPositionToSampleCoordinates(Vector3 localPosition, bool roundDown)
        {
            Vector3 relativePosition = localPosition - _fieldMinLocal;
            int x = roundDown
                ? Mathf.FloorToInt(relativePosition.x / _cellSize.x)
                : Mathf.CeilToInt(relativePosition.x / _cellSize.x);
            int y = roundDown
                ? Mathf.FloorToInt(relativePosition.y / _cellSize.y)
                : Mathf.CeilToInt(relativePosition.y / _cellSize.y);
            int z = roundDown
                ? Mathf.FloorToInt(relativePosition.z / _cellSize.z)
                : Mathf.CeilToInt(relativePosition.z / _cellSize.z);

            int maxIndex = _pointResolution - 1;
            return new Vector3Int(
                Mathf.Clamp(x, 0, maxIndex),
                Mathf.Clamp(y, 0, maxIndex),
                Mathf.Clamp(z, 0, maxIndex));
        }

        private static int ToRegionIndex(int x, int y, int z, Vector3Int min, int regionWidth, int regionHeight)
        {
            return (x - min.x) + regionWidth * ((y - min.y) + regionHeight * (z - min.z));
        }

        private static void RegionIndexToCoordinates(int regionIndex, Vector3Int min, int regionWidth, int regionHeight, out int x, out int y, out int z)
        {
            int planeSize = regionWidth * regionHeight;
            int localZ = regionIndex / planeSize;
            int remainder = regionIndex - (localZ * planeSize);
            int localY = remainder / regionWidth;
            int localX = remainder - (localY * regionWidth);
            x = min.x + localX;
            y = min.y + localY;
            z = min.z + localZ;
        }

        private static void TryEnqueueNeighbor(
            int x,
            int y,
            int z,
            Vector3Int min,
            Vector3Int max,
            int regionWidth,
            int regionHeight,
            bool[] isCandidate,
            bool[] visited,
            Queue<int> queue)
        {
            if (x < min.x || x > max.x || y < min.y || y > max.y || z < min.z || z > max.z)
            {
                return;
            }

            int regionIndex = ToRegionIndex(x, y, z, min, regionWidth, regionHeight);
            if (!isCandidate[regionIndex] || visited[regionIndex])
            {
                return;
            }

            visited[regionIndex] = true;
            queue.Enqueue(regionIndex);
        }

        private bool IsPointInsideAnyTrackedStatue(Vector3 worldPoint)
        {
            for (int i = _exposureTrackers.Count - 1; i >= 0; i--)
            {
                StatueExposureTracker tracker = _exposureTrackers[i];
                if (tracker == null)
                {
                    _exposureTrackers.RemoveAt(i);
                    continue;
                }

                if (tracker.ContainsWorldPoint(worldPoint))
                {
                    return true;
                }
            }

            return false;
        }

        private Vector3 EstimateDensityGradient(Vector3 localPosition)
        {
            float offsetX = Mathf.Max(_cellSize.x * 0.5f, 0.0001f);
            float offsetY = Mathf.Max(_cellSize.y * 0.5f, 0.0001f);
            float offsetZ = Mathf.Max(_cellSize.z * 0.5f, 0.0001f);

            float sampleXPositive = SampleDensityLocal(localPosition + new Vector3(offsetX, 0f, 0f));
            float sampleXNegative = SampleDensityLocal(localPosition - new Vector3(offsetX, 0f, 0f));
            float sampleYPositive = SampleDensityLocal(localPosition + new Vector3(0f, offsetY, 0f));
            float sampleYNegative = SampleDensityLocal(localPosition - new Vector3(0f, offsetY, 0f));
            float sampleZPositive = SampleDensityLocal(localPosition + new Vector3(0f, 0f, offsetZ));
            float sampleZNegative = SampleDensityLocal(localPosition - new Vector3(0f, 0f, offsetZ));

            return new Vector3(
                (sampleXPositive - sampleXNegative) / (offsetX * 2f),
                (sampleYPositive - sampleYNegative) / (offsetY * 2f),
                (sampleZPositive - sampleZNegative) / (offsetZ * 2f));
        }

        private float SampleDensityLocal(Vector3 localPosition)
        {
            Vector3 relative = localPosition - _fieldMinLocal;
            float x = Mathf.Clamp(relative.x / _cellSize.x, 0f, _pointResolution - 1f);
            float y = Mathf.Clamp(relative.y / _cellSize.y, 0f, _pointResolution - 1f);
            float z = Mathf.Clamp(relative.z / _cellSize.z, 0f, _pointResolution - 1f);

            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            int z0 = Mathf.FloorToInt(z);
            int x1 = Mathf.Min(x0 + 1, _pointResolution - 1);
            int y1 = Mathf.Min(y0 + 1, _pointResolution - 1);
            int z1 = Mathf.Min(z0 + 1, _pointResolution - 1);

            float tx = x - x0;
            float ty = y - y0;
            float tz = z - z0;

            float c000 = _densityValues[ToIndex(x0, y0, z0, _pointResolution)];
            float c100 = _densityValues[ToIndex(x1, y0, z0, _pointResolution)];
            float c010 = _densityValues[ToIndex(x0, y1, z0, _pointResolution)];
            float c110 = _densityValues[ToIndex(x1, y1, z0, _pointResolution)];
            float c001 = _densityValues[ToIndex(x0, y0, z1, _pointResolution)];
            float c101 = _densityValues[ToIndex(x1, y0, z1, _pointResolution)];
            float c011 = _densityValues[ToIndex(x0, y1, z1, _pointResolution)];
            float c111 = _densityValues[ToIndex(x1, y1, z1, _pointResolution)];

            float c00 = Mathf.Lerp(c000, c100, tx);
            float c10 = Mathf.Lerp(c010, c110, tx);
            float c01 = Mathf.Lerp(c001, c101, tx);
            float c11 = Mathf.Lerp(c011, c111, tx);
            float c0 = Mathf.Lerp(c00, c10, ty);
            float c1 = Mathf.Lerp(c01, c11, ty);
            return Mathf.Lerp(c0, c1, tz);
        }

        private float ApplyFieldNoiseToDensity(float baseDensity, Vector3 localPosition)
        {
            if (!_enableFieldNoise || _fieldNoiseAmplitude <= 0f)
            {
                return Mathf.Clamp01(baseDensity);
            }

            float surfaceDistance = Mathf.Abs(baseDensity - _isoValue);
            float attenuation = 1f - Mathf.Clamp01(surfaceDistance / Mathf.Max(_fieldNoiseAmplitude * 2f, 0.0001f));
            if (attenuation <= 0f)
            {
                return Mathf.Clamp01(baseDensity);
            }

            float noise = SampleFractalNoise(localPosition, _fieldNoiseFrequency, _fieldNoiseOctaves, _fieldNoiseSeed);
            float perturbedDensity = baseDensity + (noise * _fieldNoiseAmplitude * attenuation);
            return Mathf.Clamp01(perturbedDensity);
        }

        private void ApplyRenderDisplacement(Vector3[] vertices)
        {
            if (vertices == null || vertices.Length == 0)
            {
                return;
            }

            float amplitude = Mathf.Min(_renderDisplacementAmplitude, MaxCellSize * 0.15f);
            if (amplitude <= 0f)
            {
                return;
            }

            float frequency = Mathf.Max(0.01f, _renderDisplacementFrequency);
            int seed = _fieldNoiseSeed ^ 0x5A5A5A5A;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                float nx = SampleFractalNoise(vertex + new Vector3(19.37f, 0f, 0f), frequency, 2, seed + 11);
                float ny = SampleFractalNoise(vertex + new Vector3(0f, 47.11f, 0f), frequency, 2, seed + 29);
                float nz = SampleFractalNoise(vertex + new Vector3(0f, 0f, 73.53f), frequency, 2, seed + 47);
                Vector3 direction = new Vector3(nx, ny, nz);
                float magnitude = direction.magnitude;
                if (magnitude <= 0.00001f)
                {
                    continue;
                }

                direction /= magnitude;
                vertices[i] = vertex + (direction * amplitude * 0.5f);
            }
        }

        private static float SampleFractalNoise(Vector3 position, float frequency, int octaves, int seed)
        {
            float sum = 0f;
            float amplitude = 1f;
            float totalAmplitude = 0f;
            float currentFrequency = Mathf.Max(0.0001f, frequency);

            for (int octave = 0; octave < octaves; octave++)
            {
                float octaveSeed = seed * (1f + (octave * 0.731f));
                Vector3 p = position * currentFrequency + new Vector3(octaveSeed, octaveSeed * 1.7f, octaveSeed * 2.3f);
                float xy = Mathf.PerlinNoise(p.x, p.y);
                float yz = Mathf.PerlinNoise(p.y, p.z);
                float zx = Mathf.PerlinNoise(p.z, p.x);
                float averaged = ((xy + yz + zx) / 3f) * 2f - 1f;
                sum += averaged * amplitude;
                totalAmplitude += amplitude;
                amplitude *= 0.5f;
                currentFrequency *= 2f;
            }

            return totalAmplitude > 0f ? sum / totalAmplitude : 0f;
        }

        private void NotifyExposureTrackers(List<int> newlyExposedSamples)
        {
            if (newlyExposedSamples == null || newlyExposedSamples.Count == 0)
            {
                return;
            }

            for (int i = _exposureTrackers.Count - 1; i >= 0; i--)
            {
                StatueExposureTracker tracker = _exposureTrackers[i];
                if (tracker == null)
                {
                    _exposureTrackers.RemoveAt(i);
                    continue;
                }

                tracker.OnVoxelSamplesExposed(newlyExposedSamples);
            }
        }
    }
}
