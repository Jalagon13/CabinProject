using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace CabinProject
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MarchingCubesVoxelDestruction))]
    public class ExcavationDebrisSpawner : MonoBehaviour
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Triangle
        {
            public Vector3 VertexA;
            public Vector3 VertexB;
            public Vector3 VertexC;
        }

        private static readonly Vector3Int[] NeighborOffsets =
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1)
        };

        [Header("Generation")]
        [SerializeField] private bool _spawnDebris = true;
        [SerializeField] private int _maxSourceVoxelsPerExcavation = 96;
        [SerializeField] private int _maxDebrisPiecesPerExcavation = 6;
        [SerializeField] private int _targetVoxelsPerPiece = 10;
        [SerializeField] private int _minimumVoxelsPerPiece = 3;
        [SerializeField] private int _pieceMarchingCubesPadding = 1;
        [SerializeField] private float _surfaceOffset = 0.03f;
        [SerializeField] private float _spawnJitter = 0.015f;

        [Header("Physics")]
        [SerializeField] private float _massPerVoxel = 0.08f;
        [SerializeField] private float _minimumMass = 0.05f;
        [SerializeField] private float _maximumMass = 2.5f;
        [SerializeField] private float _outwardImpulse = 0.65f;
        [SerializeField] private float _upwardImpulse = 0.2f;
        [SerializeField] private float _randomImpulse = 0.25f;
        [SerializeField] private float _maxDepenetrationVelocity = 1.5f;
        [SerializeField] private float _linearDamping = 1.5f;
        [SerializeField] private float _angularDamping = 2.5f;
        [SerializeField] private float _sleepThreshold = 0.05f;
        [SerializeField] private float _settleDelay = 0.2f;
        [SerializeField] private float _settleLinearVelocityThreshold = 0.1f;
        [SerializeField] private float _settleAngularVelocityThreshold = 0.75f;
        [SerializeField] private float _settleCheckDuration = 0.15f;
        [SerializeField] private float _selfCollisionIgnoreDuration = 0.12f;
        [SerializeField] private PhysicsMaterial _debrisColliderMaterial;

        [Header("Cleanup")]
        [SerializeField] private float _pieceLifetime = 2.5f;
        [SerializeField, Range(0f, 1f)] private float _shrinkStartNormalized = 0.7f;

        [Header("Hierarchy")]
        [SerializeField] private Transform _debrisParentOverride;
        [SerializeField] private bool _preferLocalManagersParent = true;

        private MarchingCubesVoxelDestruction _volume;
        private MeshRenderer _sourceRenderer;
        private Transform _runtimeParent;

        private const int MarchingCubesKernelThreadSize = 8;
        private const int MaxTrianglesPerCell = 5;

        private static readonly int DensityBufferId = Shader.PropertyToID("_DensityBuffer");
        private static readonly int TriangleBufferId = Shader.PropertyToID("_Triangles");
        private static readonly int CellResolutionId = Shader.PropertyToID("_CellResolution");
        private static readonly int PointResolutionId = Shader.PropertyToID("_PointResolution");
        private static readonly int IsoValueId = Shader.PropertyToID("_IsoValue");
        private static readonly int FieldMinLocalId = Shader.PropertyToID("_FieldMinLocal");
        private static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
        private static readonly int LocalToWorldId = Shader.PropertyToID("_LocalToWorld");

        private void Awake()
        {
            _volume = GetComponent<MarchingCubesVoxelDestruction>();
            _sourceRenderer = GetComponent<MeshRenderer>();
        }

        public void SpawnDebris(List<int> removedSamples, Vector3 excavationPointWorld)
        {
            if (!_spawnDebris || _volume == null || removedSamples == null || removedSamples.Count == 0)
            {
                return;
            }

            List<Vector3Int> voxels = BuildUniqueVoxelList(removedSamples);
            if (voxels.Count < _minimumVoxelsPerPiece)
            {
                return;
            }

            List<List<Vector3Int>> connectedComponents = BuildConnectedComponents(voxels);
            List<List<Vector3Int>> debrisPieces = SplitIntoPieces(connectedComponents);
            List<Collider> spawnedColliders = new List<Collider>(debrisPieces.Count);

            for (int i = 0; i < debrisPieces.Count; i++)
            {
                Collider pieceCollider = SpawnPiece(debrisPieces[i], excavationPointWorld);
                if (pieceCollider != null)
                {
                    spawnedColliders.Add(pieceCollider);
                }
            }

            if (spawnedColliders.Count > 1)
            {
                StartCoroutine(RestoreSiblingCollisions(spawnedColliders, _selfCollisionIgnoreDuration));
            }
        }

        private List<Vector3Int> BuildUniqueVoxelList(List<int> removedSamples)
        {
            int maxSamples = Mathf.Max(1, _maxSourceVoxelsPerExcavation);
            List<Vector3Int> voxels = new List<Vector3Int>(Mathf.Min(removedSamples.Count, maxSamples));
            HashSet<int> uniqueSamples = new HashSet<int>();

            for (int i = 0; i < removedSamples.Count; i++)
            {
                if (!uniqueSamples.Add(removedSamples[i]))
                {
                    continue;
                }

                if (!_volume.TryGetSampleCoordinates(removedSamples[i], out Vector3Int coordinates))
                {
                    continue;
                }

                voxels.Add(coordinates);
            }

            if (voxels.Count <= maxSamples)
            {
                return voxels;
            }

            Shuffle(voxels);
            voxels.RemoveRange(maxSamples, voxels.Count - maxSamples);
            return voxels;
        }

        private List<List<Vector3Int>> BuildConnectedComponents(List<Vector3Int> voxels)
        {
            List<List<Vector3Int>> components = new List<List<Vector3Int>>();
            HashSet<Vector3Int> unvisited = new HashSet<Vector3Int>(voxels);
            Queue<Vector3Int> queue = new Queue<Vector3Int>();

            while (unvisited.Count > 0)
            {
                Vector3Int start = default;
                foreach (Vector3Int voxel in unvisited)
                {
                    start = voxel;
                    break;
                }

                List<Vector3Int> component = new List<Vector3Int>();
                queue.Enqueue(start);
                unvisited.Remove(start);

                while (queue.Count > 0)
                {
                    Vector3Int current = queue.Dequeue();
                    component.Add(current);

                    for (int i = 0; i < NeighborOffsets.Length; i++)
                    {
                        Vector3Int neighbor = current + NeighborOffsets[i];
                        if (!unvisited.Remove(neighbor))
                        {
                            continue;
                        }

                        queue.Enqueue(neighbor);
                    }
                }

                if (component.Count >= _minimumVoxelsPerPiece)
                {
                    components.Add(component);
                }
            }

            components.Sort((left, right) => right.Count.CompareTo(left.Count));
            return components;
        }

        private List<List<Vector3Int>> SplitIntoPieces(List<List<Vector3Int>> connectedComponents)
        {
            List<List<Vector3Int>> pieces = new List<List<Vector3Int>>();
            if (connectedComponents.Count == 0)
            {
                return pieces;
            }

            int totalVoxelCount = 0;
            for (int i = 0; i < connectedComponents.Count; i++)
            {
                totalVoxelCount += connectedComponents[i].Count;
            }

            int maxPieces = Mathf.Max(1, _maxDebrisPiecesPerExcavation);
            int requestedPieces = Mathf.Clamp(
                Mathf.RoundToInt(totalVoxelCount / Mathf.Max(1f, _targetVoxelsPerPiece)),
                1,
                maxPieces);
            requestedPieces = Mathf.Max(requestedPieces, Mathf.Min(connectedComponents.Count, maxPieces));

            int[] pieceCounts = new int[connectedComponents.Count];
            for (int i = 0; i < connectedComponents.Count; i++)
            {
                pieceCounts[i] = 1;
            }

            int remaining = requestedPieces - connectedComponents.Count;
            while (remaining > 0)
            {
                int bestIndex = -1;
                float bestScore = 0f;

                for (int i = 0; i < connectedComponents.Count; i++)
                {
                    float averageSize = connectedComponents[i].Count / (float)(pieceCounts[i] + 1);
                    if (averageSize < _minimumVoxelsPerPiece || averageSize <= bestScore)
                    {
                        continue;
                    }

                    bestScore = averageSize;
                    bestIndex = i;
                }

                if (bestIndex < 0)
                {
                    break;
                }

                pieceCounts[bestIndex]++;
                remaining--;
            }

            for (int i = 0; i < connectedComponents.Count; i++)
            {
                List<List<Vector3Int>> splitPieces = SplitConnectedComponent(connectedComponents[i], pieceCounts[i]);
                for (int pieceIndex = 0; pieceIndex < splitPieces.Count; pieceIndex++)
                {
                    if (splitPieces[pieceIndex].Count >= _minimumVoxelsPerPiece)
                    {
                        pieces.Add(splitPieces[pieceIndex]);
                    }
                }
            }

            pieces.Sort((left, right) => right.Count.CompareTo(left.Count));
            if (pieces.Count > maxPieces)
            {
                pieces.RemoveRange(maxPieces, pieces.Count - maxPieces);
            }

            return pieces;
        }

        private List<List<Vector3Int>> SplitConnectedComponent(List<Vector3Int> component, int pieceCount)
        {
            List<List<Vector3Int>> splitPieces = new List<List<Vector3Int>>();
            if (pieceCount <= 1 || component.Count < (_minimumVoxelsPerPiece * 2))
            {
                splitPieces.Add(new List<Vector3Int>(component));
                return splitPieces;
            }

            HashSet<Vector3Int> componentSet = new HashSet<Vector3Int>(component);
            List<Vector3Int> seeds = SelectSeeds(component, pieceCount);
            Dictionary<Vector3Int, int> assignments = new Dictionary<Vector3Int, int>(component.Count);
            Queue<Vector3Int> frontier = new Queue<Vector3Int>();

            for (int i = 0; i < seeds.Count; i++)
            {
                assignments[seeds[i]] = i;
                frontier.Enqueue(seeds[i]);
            }

            while (frontier.Count > 0)
            {
                Vector3Int current = frontier.Dequeue();
                int pieceIndex = assignments[current];

                for (int i = 0; i < NeighborOffsets.Length; i++)
                {
                    Vector3Int neighbor = current + NeighborOffsets[i];
                    if (!componentSet.Contains(neighbor) || assignments.ContainsKey(neighbor))
                    {
                        continue;
                    }

                    assignments[neighbor] = pieceIndex;
                    frontier.Enqueue(neighbor);
                }
            }

            while (splitPieces.Count < seeds.Count)
            {
                splitPieces.Add(new List<Vector3Int>());
            }

            foreach (KeyValuePair<Vector3Int, int> pair in assignments)
            {
                splitPieces[pair.Value].Add(pair.Key);
            }

            List<Vector3Int> unassigned = new List<Vector3Int>();
            for (int i = 0; i < component.Count; i++)
            {
                if (!assignments.ContainsKey(component[i]))
                {
                    unassigned.Add(component[i]);
                }
            }

            for (int i = 0; i < unassigned.Count; i++)
            {
                int nearestPieceIndex = FindNearestPieceIndex(unassigned[i], splitPieces);
                splitPieces[nearestPieceIndex].Add(unassigned[i]);
            }

            MergeSmallPieces(splitPieces);
            return splitPieces;
        }

        private List<Vector3Int> SelectSeeds(List<Vector3Int> component, int pieceCount)
        {
            List<Vector3Int> seeds = new List<Vector3Int>();
            if (component.Count == 0)
            {
                return seeds;
            }

            seeds.Add(component[Random.Range(0, component.Count)]);
            while (seeds.Count < pieceCount && seeds.Count < component.Count)
            {
                float bestDistance = -1f;
                Vector3Int bestSeed = component[0];

                for (int i = 0; i < component.Count; i++)
                {
                    Vector3Int candidate = component[i];
                    float minDistance = float.PositiveInfinity;
                    for (int seedIndex = 0; seedIndex < seeds.Count; seedIndex++)
                    {
                        float distance = (candidate - seeds[seedIndex]).sqrMagnitude;
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                        }
                    }

                    if (minDistance <= bestDistance)
                    {
                        continue;
                    }

                    bestDistance = minDistance;
                    bestSeed = candidate;
                }

                if (seeds.Contains(bestSeed))
                {
                    break;
                }

                seeds.Add(bestSeed);
            }

            return seeds;
        }

        private void MergeSmallPieces(List<List<Vector3Int>> splitPieces)
        {
            for (int i = splitPieces.Count - 1; i >= 0; i--)
            {
                if (splitPieces[i].Count == 0)
                {
                    splitPieces.RemoveAt(i);
                }
            }

            for (int i = splitPieces.Count - 1; i >= 0; i--)
            {
                if (splitPieces[i].Count >= _minimumVoxelsPerPiece || splitPieces.Count <= 1)
                {
                    continue;
                }

                Vector3Int voxel = splitPieces[i][0];
                int mergeTargetIndex = FindNearestPieceIndex(voxel, splitPieces, i);
                splitPieces[mergeTargetIndex].AddRange(splitPieces[i]);
                splitPieces.RemoveAt(i);
            }
        }

        private int FindNearestPieceIndex(Vector3Int voxel, List<List<Vector3Int>> pieces, int excludedPieceIndex = -1)
        {
            int bestIndex = 0;
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < pieces.Count; i++)
            {
                if (i == excludedPieceIndex || pieces[i].Count == 0)
                {
                    continue;
                }

                Vector3 average = CalculateAverageVoxel(pieces[i]);
                float distance = (average - (Vector3)voxel).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestIndex = i;
            }

            return bestIndex;
        }

        private Vector3 CalculateAverageVoxel(List<Vector3Int> voxels)
        {
            Vector3 average = Vector3.zero;
            for (int i = 0; i < voxels.Count; i++)
            {
                average += (Vector3)voxels[i];
            }

            return voxels.Count > 0 ? average / voxels.Count : Vector3.zero;
        }

        private Collider SpawnPiece(List<Vector3Int> pieceVoxels, Vector3 excavationPointWorld)
        {
            if (pieceVoxels == null || pieceVoxels.Count < _minimumVoxelsPerPiece)
            {
                return null;
            }

            HashSet<Vector3Int> voxelSet = new HashSet<Vector3Int>(pieceVoxels);
            Vector3 worldCenter = _volume.transform.TransformPoint(CalculateLocalCenter(pieceVoxels));
            Mesh mesh = BuildPieceMesh(pieceVoxels, voxelSet, worldCenter);
            if (mesh == null || mesh.vertexCount == 0)
            {
                if (mesh != null)
                {
                    Destroy(mesh);
                }

                return null;
            }

            GameObject pieceObject = new GameObject($"{name}_DebrisPiece");
            pieceObject.transform.SetParent(GetRuntimeParent(), true);
            pieceObject.transform.position = worldCenter;
            pieceObject.transform.rotation = Quaternion.identity;
            pieceObject.transform.localScale = Vector3.one;

            MeshFilter meshFilter = pieceObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = pieceObject.AddComponent<MeshRenderer>();
            MeshCollider meshCollider = pieceObject.AddComponent<MeshCollider>();
            Rigidbody rigidbody = pieceObject.AddComponent<Rigidbody>();
            ExcavationDebrisPiece debrisPiece = pieceObject.AddComponent<ExcavationDebrisPiece>();

            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterials = GetDebrisMaterials();

            meshCollider.sharedMesh = mesh;
            meshCollider.convex = true;
            meshCollider.material = _debrisColliderMaterial;

            rigidbody.mass = Mathf.Clamp(pieceVoxels.Count * _massPerVoxel, _minimumMass, _maximumMass);
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rigidbody.maxDepenetrationVelocity = _maxDepenetrationVelocity;
            rigidbody.linearDamping = Mathf.Max(0f, _linearDamping);
            rigidbody.angularDamping = Mathf.Max(0f, _angularDamping);
            rigidbody.sleepThreshold = Mathf.Max(0f, _sleepThreshold);

            debrisPiece.Initialize(
                _pieceLifetime,
                _shrinkStartNormalized,
                rigidbody,
                _settleDelay,
                _settleLinearVelocityThreshold,
                _settleAngularVelocityThreshold,
                _settleCheckDuration);

            Vector3 impulseDirection = _volume.GetExcavationSurfaceNormal(excavationPointWorld);
            if (impulseDirection.sqrMagnitude < 0.0001f)
            {
                impulseDirection = (worldCenter - excavationPointWorld).normalized;
            }

            if (impulseDirection.sqrMagnitude < 0.0001f)
            {
                impulseDirection = transform.up;
            }

            Vector3 spawnOffset = (impulseDirection * _surfaceOffset) + (Random.insideUnitSphere * _spawnJitter);
            pieceObject.transform.position += spawnOffset;

            Vector3 impulse = (impulseDirection * _outwardImpulse)
                + (Vector3.up * _upwardImpulse)
                + (Random.insideUnitSphere * _randomImpulse);
            rigidbody.AddForce(impulse, ForceMode.Impulse);
            return meshCollider;
        }

        private Mesh BuildPieceMesh(List<Vector3Int> pieceVoxels, HashSet<Vector3Int> voxelSet, Vector3 worldCenter)
        {
            ComputeShader computeShader = CrosshairManager.Instance != null ? CrosshairManager.Instance.ComputeShader : null;
            if (computeShader == null || voxelSet.Count == 0)
            {
                return null;
            }

            Vector3Int min = pieceVoxels[0];
            Vector3Int max = pieceVoxels[0];
            for (int i = 1; i < pieceVoxels.Count; i++)
            {
                min = Vector3Int.Min(min, pieceVoxels[i]);
                max = Vector3Int.Max(max, pieceVoxels[i]);
            }

            int padding = Mathf.Max(1, _pieceMarchingCubesPadding);
            Vector3Int regionMin = min - (Vector3Int.one * padding);
            Vector3Int regionMax = max + (Vector3Int.one * padding);
            Vector3Int pointDimensions = (regionMax - regionMin) + Vector3Int.one;
            int pointResolution = Mathf.Max(pointDimensions.x, Mathf.Max(pointDimensions.y, pointDimensions.z));
            if (pointResolution < 2)
            {
                return null;
            }

            int pointCount = pointResolution * pointResolution * pointResolution;
            int cellResolution = pointResolution - 1;
            int cellCount = cellResolution * cellResolution * cellResolution;
            float[] densityValues = new float[pointCount];

            for (int z = regionMin.z; z <= regionMax.z; z++)
            {
                for (int y = regionMin.y; y <= regionMax.y; y++)
                {
                    for (int x = regionMin.x; x <= regionMax.x; x++)
                    {
                        int localX = x - regionMin.x;
                        int localY = y - regionMin.y;
                        int localZ = z - regionMin.z;
                        int flatIndex = localX + pointResolution * (localY + (pointResolution * localZ));
                        densityValues[flatIndex] = voxelSet.Contains(new Vector3Int(x, y, z)) ? 1f : 0f;
                    }
                }
            }

            using ComputeBuffer densityBuffer = new ComputeBuffer(pointCount, sizeof(float));
            using ComputeBuffer triangleBuffer = new ComputeBuffer(Mathf.Max(1, cellCount * MaxTrianglesPerCell), Marshal.SizeOf<Triangle>(), ComputeBufferType.Append);
            using ComputeBuffer triangleCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

            densityBuffer.SetData(densityValues);
            triangleBuffer.SetCounterValue(0);

            int kernelIndex = computeShader.FindKernel("MarchingCubes");
            computeShader.SetInt(CellResolutionId, cellResolution);
            computeShader.SetInt(PointResolutionId, pointResolution);
            computeShader.SetFloat(IsoValueId, _volume.IsoValue);
            computeShader.SetVector(FieldMinLocalId, GetSampleLocalPosition(regionMin));
            computeShader.SetVector(CellSizeId, _volume.CellSize);
            computeShader.SetMatrix(LocalToWorldId, Matrix4x4.identity);
            computeShader.SetBuffer(kernelIndex, DensityBufferId, densityBuffer);
            computeShader.SetBuffer(kernelIndex, TriangleBufferId, triangleBuffer);

            int groupCount = Mathf.CeilToInt(cellResolution / (float)MarchingCubesKernelThreadSize);
            computeShader.Dispatch(kernelIndex, Mathf.Max(1, groupCount), Mathf.Max(1, groupCount), Mathf.Max(1, groupCount));

            ComputeBuffer.CopyCount(triangleBuffer, triangleCountBuffer, 0);
            int[] triangleCountData = { 0 };
            triangleCountBuffer.GetData(triangleCountData);
            int triangleCount = triangleCountData[0];
            if (triangleCount <= 0)
            {
                return null;
            }

            Triangle[] generatedTriangles = new Triangle[triangleCount];
            triangleBuffer.GetData(generatedTriangles, 0, 0, triangleCount);

            Vector3[] vertices = new Vector3[triangleCount * 3];
            int[] indices = new int[triangleCount * 3];

            for (int i = 0; i < triangleCount; i++)
            {
                int vertexIndex = i * 3;
                vertices[vertexIndex] = ToPieceLocalVertex(generatedTriangles[i].VertexA, worldCenter);
                vertices[vertexIndex + 1] = ToPieceLocalVertex(generatedTriangles[i].VertexC, worldCenter);
                vertices[vertexIndex + 2] = ToPieceLocalVertex(generatedTriangles[i].VertexB, worldCenter);

                indices[vertexIndex] = vertexIndex;
                indices[vertexIndex + 1] = vertexIndex + 1;
                indices[vertexIndex + 2] = vertexIndex + 2;
            }

            Mesh mesh = new Mesh
            {
                name = $"{name}_ExcavationDebrisMesh",
                indexFormat = IndexFormat.UInt32
            };
            mesh.vertices = vertices;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.uv = GenerateProjectedUvs(mesh.vertices, mesh.normals);
            mesh.RecalculateTangents();
            return mesh;
        }

        private Vector3 CalculateLocalCenter(List<Vector3Int> pieceVoxels)
        {
            Vector3 center = Vector3.zero;
            for (int i = 0; i < pieceVoxels.Count; i++)
            {
                Vector3Int voxel = pieceVoxels[i];
                center += _volume.GetSampleLocalPosition(voxel.x, voxel.y, voxel.z);
            }

            return center / pieceVoxels.Count;
        }

        private Vector3 GetSampleLocalPosition(Vector3Int coordinates)
        {
            return _volume.FieldMinLocal + Vector3.Scale((Vector3)coordinates, _volume.CellSize);
        }

        private Vector3 ToPieceLocalVertex(Vector3 sourceLocalVertex, Vector3 worldCenter)
        {
            return _volume.transform.TransformPoint(sourceLocalVertex) - worldCenter;
        }

        private Vector2[] GenerateProjectedUvs(Vector3[] vertices, Vector3[] normals)
        {
            Vector2[] uvs = new Vector2[vertices.Length];

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 normal = normals != null && i < normals.Length ? normals[i] : Vector3.up;
                Vector3 absoluteNormal = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
                Vector3 vertex = vertices[i];

                if (absoluteNormal.y >= absoluteNormal.x && absoluteNormal.y >= absoluteNormal.z)
                {
                    uvs[i] = new Vector2(vertex.x, vertex.z);
                }
                else if (absoluteNormal.x >= absoluteNormal.z)
                {
                    uvs[i] = new Vector2(vertex.z, vertex.y);
                }
                else
                {
                    uvs[i] = new Vector2(vertex.x, vertex.y);
                }
            }

            return uvs;
        }

        private Material[] GetDebrisMaterials()
        {
            if (_sourceRenderer != null && _sourceRenderer.sharedMaterials != null && _sourceRenderer.sharedMaterials.Length > 0)
            {
                return _sourceRenderer.sharedMaterials;
            }

            return new[] { new Material(Shader.Find("Standard")) };
        }

        private Transform GetRuntimeParent()
        {
            if (_runtimeParent != null)
            {
                return _runtimeParent;
            }

            if (_debrisParentOverride != null)
            {
                _runtimeParent = _debrisParentOverride;
                return _runtimeParent;
            }

            if (_preferLocalManagersParent)
            {
                GameObject localManagers = GameObject.Find("LOCAL_MANAGERS");
                if (localManagers != null)
                {
                    _runtimeParent = localManagers.transform;
                }
            }

            if (_runtimeParent == null)
            {
                GameObject runtimeParent = new GameObject($"{name}_DebrisRoot");
                _runtimeParent = runtimeParent.transform;
            }

            return _runtimeParent;
        }

        private System.Collections.IEnumerator RestoreSiblingCollisions(List<Collider> spawnedColliders, float ignoreDuration)
        {
            float delay = Mathf.Max(0f, ignoreDuration);

            for (int i = 0; i < spawnedColliders.Count; i++)
            {
                Collider left = spawnedColliders[i];
                if (left == null)
                {
                    continue;
                }

                for (int j = i + 1; j < spawnedColliders.Count; j++)
                {
                    Collider right = spawnedColliders[j];
                    if (right == null)
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(left, right, true);
                }
            }

            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            for (int i = 0; i < spawnedColliders.Count; i++)
            {
                Collider left = spawnedColliders[i];
                if (left == null)
                {
                    continue;
                }

                for (int j = i + 1; j < spawnedColliders.Count; j++)
                {
                    Collider right = spawnedColliders[j];
                    if (right == null)
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(left, right, false);
                }
            }
        }

        private static void Shuffle<T>(List<T> values)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int swapIndex = Random.Range(0, i + 1);
                T temp = values[i];
                values[i] = values[swapIndex];
                values[swapIndex] = temp;
            }
        }
    }
}
