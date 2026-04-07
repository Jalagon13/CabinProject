using System;
using System.Collections.Generic;
using UnityEngine;

namespace CabinProject
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    public class StatueExposureTracker : MonoBehaviour
    {
        [SerializeField, Range(0f, 100f)] private float _completionPercent = 97f;
        [SerializeField] private bool _logExposureProgress = true;

        private MarchingCubesVoxelDestruction _volume;
        private MeshFilter _meshFilter;
        private Mesh _statueMesh;
        private bool[] _isCoverageSample;
        private bool[] _isExposedCoverageSample;
        private int _totalCoverageSamples;
        private int _exposedCoverageSamples;
        private bool _hasCompleted;
        private float _lastLoggedPercent = -1f;

        public event Action<StatueExposureTracker> OnExposureComplete;

        public float ExposurePercent { get; private set; }

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _statueMesh = _meshFilter != null ? _meshFilter.sharedMesh : null;
        }

        private void OnEnable()
        {
            ResolveAndRegisterWithVolume();
        }

        private void Start()
        {
            ResolveAndRegisterWithVolume();
        }

        private void OnDisable()
        {
            if (_volume != null)
            {
                _volume.UnregisterExposureTracker(this);
            }
        }

        public void BindToVolume(MarchingCubesVoxelDestruction volume)
        {
            if (volume == null || !volume.IsReady)
            {
                return;
            }

            _volume = volume;
            Debug.Log($"{name} bound to voxel volume {_volume.name}.", this);
            BuildCoverageData();
        }

        public void OnVoxelSamplesExposed(List<int> exposedSamples)
        {
            if (_isCoverageSample == null || _isExposedCoverageSample == null || exposedSamples == null || exposedSamples.Count == 0)
            {
                return;
            }

            bool exposureChanged = false;
            for (int i = 0; i < exposedSamples.Count; i++)
            {
                int sampleIndex = exposedSamples[i];
                if (sampleIndex < 0 || sampleIndex >= _isCoverageSample.Length)
                {
                    continue;
                }

                if (!_isCoverageSample[sampleIndex] || _isExposedCoverageSample[sampleIndex])
                {
                    continue;
                }

                _isExposedCoverageSample[sampleIndex] = true;
                _exposedCoverageSamples++;
                exposureChanged = true;
            }

            if (exposureChanged)
            {
                UpdateExposurePercent();
            }
        }

        private void ResolveAndRegisterWithVolume()
        {
            if (_volume == null)
            {
                _volume = GetComponentInParent<MarchingCubesVoxelDestruction>();
            }

            if (_volume == null)
            {
                Debug.LogWarning($"StatueExposureTracker on {name} could not find a parent {nameof(MarchingCubesVoxelDestruction)}.", this);
                return;
            }

            _volume.RegisterExposureTracker(this);
        }

        private void BuildCoverageData()
        {
            if (_statueMesh == null)
            {
                Debug.LogWarning($"StatueExposureTracker on {name} requires a readable MeshFilter mesh.", this);
                return;
            }

            int pointCount = _volume.PointResolution * _volume.PointResolution * _volume.PointResolution;
            _isCoverageSample = new bool[pointCount];
            _isExposedCoverageSample = new bool[pointCount];
            _totalCoverageSamples = 0;
            _exposedCoverageSamples = 0;
            _hasCompleted = false;
            _lastLoggedPercent = -1f;

            Bounds statueWorldBounds = TransformBounds(_statueMesh.bounds, transform.localToWorldMatrix);
            float shellDistanceWorld = _volume.MaxCellSize;
            statueWorldBounds.Expand(shellDistanceWorld * 2f);

            Bounds statueLocalBounds = TransformBounds(statueWorldBounds, _volume.transform.worldToLocalMatrix);

            Vector3 expandedBoundsMin = statueLocalBounds.min;
            Vector3 expandedBoundsMax = statueLocalBounds.max;

            Vector3Int minSample = WorldBoundsToSampleCoordinates(expandedBoundsMin, true);
            Vector3Int maxSample = WorldBoundsToSampleCoordinates(expandedBoundsMax, false);

            Vector3[] vertices = _statueMesh.vertices;
            int[] triangles = _statueMesh.triangles;
            float shellDistanceLocal = GetShellDistanceInStatueLocal(shellDistanceWorld);

            for (int z = minSample.z; z <= maxSample.z; z++)
            {
                for (int y = minSample.y; y <= maxSample.y; y++)
                {
                    for (int x = minSample.x; x <= maxSample.x; x++)
                    {
                        int flatIndex = _volume.ToFlatIndex(x, y, z);
                        Vector3 sampleLocal = _volume.GetSampleLocalPosition(x, y, z);
                        Vector3 sampleWorld = _volume.transform.TransformPoint(sampleLocal);
                        Vector3 sampleInStatueLocal = transform.InverseTransformPoint(sampleWorld);

                        float minDistanceSquared = GetMinDistanceSquaredToMesh(sampleInStatueLocal, vertices, triangles);
                        if (minDistanceSquared > shellDistanceLocal * shellDistanceLocal)
                        {
                            continue;
                        }

                        _isCoverageSample[flatIndex] = true;
                        _totalCoverageSamples++;

                        if (!_volume.IsSampleSolid(flatIndex))
                        {
                            _isExposedCoverageSample[flatIndex] = true;
                            _exposedCoverageSamples++;
                        }
                    }
                }
            }

            if (_totalCoverageSamples == 0)
            {
                Debug.LogWarning($"StatueExposureTracker on {name} did not find any coverage samples near the statue surface.", this);
                return;
            }

            Debug.Log($"{name} initialized exposure tracking with {_totalCoverageSamples} coverage samples on {_volume.name}.", this);

            UpdateExposurePercent();
        }

        private void UpdateExposurePercent()
        {
            ExposurePercent = _totalCoverageSamples <= 0
                ? 0f
                : (_exposedCoverageSamples / (float)_totalCoverageSamples) * 100f;

            float roundedPercent = Mathf.Round(ExposurePercent * 10f) / 10f;
            if (_logExposureProgress && !Mathf.Approximately(roundedPercent, _lastLoggedPercent))
            {
                Debug.Log($"{name} exposure: {roundedPercent:F1}%", this);
                _lastLoggedPercent = roundedPercent;
            }

            if (_hasCompleted || ExposurePercent < _completionPercent)
            {
                return;
            }

            _hasCompleted = true;
            _volume.HideRemainingStone();
            Debug.Log($"{name} reached exposure threshold: {roundedPercent:F1}% complete", this);
            OnExposureComplete?.Invoke(this);
        }

        private Vector3Int WorldBoundsToSampleCoordinates(Vector3 volumeLocalPosition, bool roundDown)
        {
            Vector3 relativePosition = volumeLocalPosition - _volume.FieldMinLocal;
            int x = roundDown
                ? Mathf.FloorToInt(relativePosition.x / _volume.CellSize.x)
                : Mathf.CeilToInt(relativePosition.x / _volume.CellSize.x);
            int y = roundDown
                ? Mathf.FloorToInt(relativePosition.y / _volume.CellSize.y)
                : Mathf.CeilToInt(relativePosition.y / _volume.CellSize.y);
            int z = roundDown
                ? Mathf.FloorToInt(relativePosition.z / _volume.CellSize.z)
                : Mathf.CeilToInt(relativePosition.z / _volume.CellSize.z);

            int maxIndex = _volume.PointResolution - 1;
            return new Vector3Int(
                Mathf.Clamp(x, 0, maxIndex),
                Mathf.Clamp(y, 0, maxIndex),
                Mathf.Clamp(z, 0, maxIndex));
        }

        private float GetShellDistanceInStatueLocal(float shellDistanceWorld)
        {
            Vector3 localX = transform.InverseTransformVector(Vector3.right * shellDistanceWorld);
            Vector3 localY = transform.InverseTransformVector(Vector3.up * shellDistanceWorld);
            Vector3 localZ = transform.InverseTransformVector(Vector3.forward * shellDistanceWorld);
            return Mathf.Max(localX.magnitude, Mathf.Max(localY.magnitude, localZ.magnitude));
        }

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 center = matrix.MultiplyPoint3x4(bounds.center);
            Vector3 extents = bounds.extents;

            Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
            Vector3 worldExtents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));

            return new Bounds(center, worldExtents * 2f);
        }

        private static float GetMinDistanceSquaredToMesh(Vector3 point, Vector3[] vertices, int[] triangles)
        {
            float minDistanceSquared = float.PositiveInfinity;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                Vector3 closestPoint = ClosestPointOnTriangle(point, a, b, c);
                float distanceSquared = (point - closestPoint).sqrMagnitude;
                if (distanceSquared < minDistanceSquared)
                {
                    minDistanceSquared = distanceSquared;
                }
            }

            return minDistanceSquared;
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
    }
}
