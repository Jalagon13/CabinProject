using System.Collections.Generic;
using UnityEngine;

namespace CabinProject
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MarchingCubesVoxelDestruction))]
    public class ExcavationDebrisSpawner : MonoBehaviour
    {
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
        [SerializeField] private float _voxelSizeMultiplier = 0.9f;
        [SerializeField] private float _surfaceOffset = 0.03f;
        [SerializeField] private float _spawnJitter = 0.015f;

        [Header("Physics")]
        [SerializeField] private float _massPerVoxel = 0.08f;
        [SerializeField] private float _minimumMass = 0.05f;
        [SerializeField] private float _maximumMass = 2.5f;
        [SerializeField] private float _outwardImpulse = 0.65f;
        [SerializeField] private float _upwardImpulse = 0.2f;
        [SerializeField] private float _randomImpulse = 0.25f;
        [SerializeField] private float _spinImpulse = 0.35f;
        [SerializeField] private float _maxDepenetrationVelocity = 1.5f;
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

            for (int i = 0; i < debrisPieces.Count; i++)
            {
                SpawnPiece(debrisPieces[i], excavationPointWorld);
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

        private void SpawnPiece(List<Vector3Int> pieceVoxels, Vector3 excavationPointWorld)
        {
            if (pieceVoxels == null || pieceVoxels.Count < _minimumVoxelsPerPiece)
            {
                return;
            }

            HashSet<Vector3Int> voxelSet = new HashSet<Vector3Int>(pieceVoxels);
            Vector3 localCenter = CalculateLocalCenter(pieceVoxels);
            Vector3 worldCenter = _volume.transform.TransformPoint(localCenter);
            Mesh mesh = BuildPieceMesh(pieceVoxels, voxelSet, worldCenter, localCenter);
            if (mesh == null || mesh.vertexCount == 0)
            {
                if (mesh != null)
                {
                    Destroy(mesh);
                }

                return;
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
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.maxDepenetrationVelocity = _maxDepenetrationVelocity;

            debrisPiece.Initialize(_pieceLifetime, _shrinkStartNormalized);

            Vector3 impulseDirection = (worldCenter - excavationPointWorld).normalized;
            if (impulseDirection.sqrMagnitude < 0.0001f)
            {
                impulseDirection = Random.onUnitSphere;
            }

            Vector3 spawnOffset = (impulseDirection * _surfaceOffset) + (Random.insideUnitSphere * _spawnJitter);
            pieceObject.transform.position += spawnOffset;

            Vector3 impulse = (impulseDirection * _outwardImpulse)
                + (Vector3.up * _upwardImpulse)
                + (Random.insideUnitSphere * _randomImpulse);
            rigidbody.AddForce(impulse, ForceMode.Impulse);
            rigidbody.AddTorque(Random.insideUnitSphere * _spinImpulse, ForceMode.Impulse);
        }

        private Mesh BuildPieceMesh(List<Vector3Int> pieceVoxels, HashSet<Vector3Int> voxelSet, Vector3 worldCenter, Vector3 localCenter)
        {
            float sizeMultiplier = Mathf.Max(0.01f, _voxelSizeMultiplier);
            Vector3 halfExtents = Vector3.Scale(_volume.CellSize, Vector3.one * sizeMultiplier) * 0.5f;

            List<Vector3> vertices = new List<Vector3>(pieceVoxels.Count * 24);
            List<int> triangles = new List<int>(pieceVoxels.Count * 36);
            List<Vector3> normals = new List<Vector3>(pieceVoxels.Count * 24);
            List<Vector2> uvs = new List<Vector2>(pieceVoxels.Count * 24);

            for (int i = 0; i < pieceVoxels.Count; i++)
            {
                Vector3Int voxel = pieceVoxels[i];
                Vector3 localVoxelCenter = _volume.GetSampleLocalPosition(voxel.x, voxel.y, voxel.z);

                for (int directionIndex = 0; directionIndex < NeighborOffsets.Length; directionIndex++)
                {
                    Vector3Int neighbor = voxel + NeighborOffsets[directionIndex];
                    if (voxelSet.Contains(neighbor))
                    {
                        continue;
                    }

                    AddFace(vertices, triangles, normals, uvs, localVoxelCenter, localCenter, worldCenter, halfExtents, directionIndex);
                }
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            Mesh mesh = new Mesh
            {
                name = $"{name}_ExcavationDebrisMesh"
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void AddFace(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector3> normals,
            List<Vector2> uvs,
            Vector3 localVoxelCenter,
            Vector3 localCenter,
            Vector3 worldCenter,
            Vector3 halfExtents,
            int directionIndex)
        {
            Vector3[] faceVertices = GetFaceVertices(localVoxelCenter, halfExtents, directionIndex);
            Vector3 faceNormal = GetFaceNormal(directionIndex);
            int startIndex = vertices.Count;

            for (int i = 0; i < faceVertices.Length; i++)
            {
                Vector3 worldVertex = _volume.transform.TransformPoint(faceVertices[i]);
                vertices.Add(worldVertex - worldCenter);
                normals.Add(_volume.transform.TransformDirection(faceNormal).normalized);
            }

            triangles.Add(startIndex);
            triangles.Add(startIndex + 1);
            triangles.Add(startIndex + 2);
            triangles.Add(startIndex);
            triangles.Add(startIndex + 2);
            triangles.Add(startIndex + 3);

            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(0f, 1f));
        }

        private static Vector3[] GetFaceVertices(Vector3 center, Vector3 halfExtents, int directionIndex)
        {
            Vector3 min = center - halfExtents;
            Vector3 max = center + halfExtents;

            switch (directionIndex)
            {
                case 0:
                    return new[]
                    {
                        new Vector3(max.x, min.y, min.z),
                        new Vector3(max.x, max.y, min.z),
                        new Vector3(max.x, max.y, max.z),
                        new Vector3(max.x, min.y, max.z)
                    };
                case 1:
                    return new[]
                    {
                        new Vector3(min.x, min.y, max.z),
                        new Vector3(min.x, max.y, max.z),
                        new Vector3(min.x, max.y, min.z),
                        new Vector3(min.x, min.y, min.z)
                    };
                case 2:
                    return new[]
                    {
                        new Vector3(min.x, max.y, min.z),
                        new Vector3(min.x, max.y, max.z),
                        new Vector3(max.x, max.y, max.z),
                        new Vector3(max.x, max.y, min.z)
                    };
                case 3:
                    return new[]
                    {
                        new Vector3(min.x, min.y, max.z),
                        new Vector3(min.x, min.y, min.z),
                        new Vector3(max.x, min.y, min.z),
                        new Vector3(max.x, min.y, max.z)
                    };
                case 4:
                    return new[]
                    {
                        new Vector3(max.x, min.y, max.z),
                        new Vector3(max.x, max.y, max.z),
                        new Vector3(min.x, max.y, max.z),
                        new Vector3(min.x, min.y, max.z)
                    };
                default:
                    return new[]
                    {
                        new Vector3(min.x, min.y, min.z),
                        new Vector3(min.x, max.y, min.z),
                        new Vector3(max.x, max.y, min.z),
                        new Vector3(max.x, min.y, min.z)
                    };
            }
        }

        private static Vector3 GetFaceNormal(int directionIndex)
        {
            switch (directionIndex)
            {
                case 0:
                    return Vector3.right;
                case 1:
                    return Vector3.left;
                case 2:
                    return Vector3.up;
                case 3:
                    return Vector3.down;
                case 4:
                    return Vector3.forward;
                default:
                    return Vector3.back;
            }
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
