using System;
using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Final presentation safety gate for VIRTUAL_SIMULATION poses.
    /// The algorithm remains authoritative for navigation; this component
    /// prevents an invalid or stale scene map from rendering a vehicle inside
    /// Catalina terrain, the island UAV base, the shore command platform, or
    /// another vehicle. It dynamically rebinds after every fleet regeneration.
    /// </summary>
    [DefaultExecutionOrder(20010)]
    public sealed class VirtualFleetSceneSafety : MonoBehaviour
    {
        private sealed class Entry
        {
            public string code;
            public string type;
            public Transform transform;
        }

        private struct SafePose
        {
            public Vector3 position;
            public Quaternion rotation;
            public bool valid;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<string, SafePose> safePoses =
            new Dictionary<string, SafePose>(StringComparer.OrdinalIgnoreCase);
        private readonly Collider[] overlapBuffer = new Collider[256];
        private readonly RaycastHit[] castBuffer = new RaycastHit[128];
        private VirtualFleetManager fleetManager;
        private float nextWarningAt;

        private const float SurfaceMargin = .22f;
        private const float AirMargin = .08f;
        private const float PairMargin = .18f;
        private const float SweepEpsilon = .02f;
        private const float SeaLevel = .03f;
        private const float MinimumLandHeight = .12f;

        public int SafetyCorrectionCount { get; private set; }

        public void Bind(VirtualFleetManager manager)
        {
            if (fleetManager == manager)
            {
                RefreshFleet();
                return;
            }

            if (fleetManager)
                fleetManager.FleetChanged -= RefreshFleet;
            fleetManager = manager;
            if (fleetManager)
                fleetManager.FleetChanged += RefreshFleet;
            RefreshFleet();
        }

        private void OnDestroy()
        {
            if (fleetManager)
                fleetManager.FleetChanged -= RefreshFleet;
        }

        private void RefreshFleet()
        {
            entries.Clear();
            safePoses.Clear();
            if (!fleetManager)
                return;

            AddEntries(fleetManager.Uavs, "UAV");
            AddEntries(fleetManager.Usvs, "USV");
            AddEntries(fleetManager.Targets, "TARGET");

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                safePoses[entry.code] = new SafePose
                {
                    position = entry.transform.position,
                    rotation = entry.transform.rotation,
                    valid = false
                };
            }
        }

        private void AddEntries(
            IReadOnlyList<VirtualFleetDeviceState> states,
            string fallbackType)
        {
            for (int i = 0; i < states.Count; i++)
            {
                VirtualFleetDeviceState state = states[i];
                if (state == null || !state.transform ||
                    string.IsNullOrWhiteSpace(state.deviceCode))
                    continue;
                entries.Add(new Entry
                {
                    code = state.deviceCode,
                    type = string.IsNullOrWhiteSpace(state.deviceType)
                        ? fallbackType
                        : state.deviceType.Trim().ToUpperInvariant(),
                    transform = state.transform
                });
            }
        }

        private void LateUpdate()
        {
            ValidateCurrentFleet();
        }

        /// <summary>
        /// Validate the currently rendered fleet once and project invalid
        /// initial poses to the nearest collision-free water position.  The
        /// platform bridge keeps this component disabled during streamed
        /// playback to avoid per-frame rollback jitter, but scenario loading
        /// invokes this method explicitly after all initial poses are applied.
        /// </summary>
        public int ValidateCurrentFleet()
        {
            if (!fleetManager || entries.Count == 0)
                return 0;

            int correctionsBefore = SafetyCorrectionCount;

            Physics.SyncTransforms();
            bool[] unsafeEntries = new bool[entries.Count];
            Bounds[] visualBounds = new Bounds[entries.Count];
            bool[] hasVisualBounds = new bool[entries.Count];
            string firstReason = string.Empty;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (!entry.transform)
                    continue;
                hasVisualBounds[i] = TryGetVisualBounds(
                    entry.transform,
                    out visualBounds[i]
                );
                if (!hasVisualBounds[i])
                    continue;
                if (TouchesStaticObstacle(entry, visualBounds[i]) ||
                    SweepsThroughStaticObstacle(entry, visualBounds[i]))
                {
                    unsafeEntries[i] = true;
                    if (string.IsNullOrEmpty(firstReason))
                        firstReason = entry.code + " entered scene obstacle";
                }
            }

            for (int left = 0; left < entries.Count; left++)
            for (int right = left + 1; right < entries.Count; right++)
            {
                if (!entries[left].transform || !entries[right].transform)
                    continue;
                if (!hasVisualBounds[left] || !hasVisualBounds[right] ||
                    !DevicesOverlap(
                        entries[left], visualBounds[left],
                        entries[right], visualBounds[right]))
                    continue;
                unsafeEntries[left] = true;
                unsafeEntries[right] = true;
                if (string.IsNullOrEmpty(firstReason))
                    firstReason = entries[left].code + " overlaps " + entries[right].code;
            }

            bool corrected = false;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (!entry.transform)
                    continue;
                if (!unsafeEntries[i])
                {
                    safePoses[entry.code] = CurrentPose(entry.transform);
                    continue;
                }

                if (!safePoses.TryGetValue(entry.code, out SafePose safe) || !safe.valid)
                {
                    if (!TryFindNearestSafePose(
                            i,
                            visualBounds,
                            hasVisualBounds,
                            out safe))
                        continue;
                    safePoses[entry.code] = safe;
                }
                entry.transform.SetPositionAndRotation(safe.position, safe.rotation);
                fleetManager.TryApplyPoseImmediate(
                    entry.code,
                    safe.position,
                    safe.rotation,
                    "SAFETY_HOLD"
                );
                corrected = true;
                SafetyCorrectionCount++;
            }

            if (!corrected)
                return 0;
            Physics.SyncTransforms();
            if (Time.unscaledTime >= nextWarningAt)
            {
                nextWarningAt = Time.unscaledTime + 1f;
                Debug.LogWarning(
                    "[VirtualFleetSceneSafety] Rejected unsafe pose: " + firstReason
                );
            }
            return SafetyCorrectionCount - correctionsBefore;
        }

        private bool TouchesStaticObstacle(Entry entry, Bounds bounds)
        {
            float margin = IsAir(entry) ? AirMargin : SurfaceMargin;
            // Catalina's collision mesh also contains the submerged terrain
            // below the visible sea.  A broad OverlapBox therefore reports a
            // hit over valid open water.  Classify the terrain from sampled
            // surface height instead: only ground above the waterline that
            // reaches the rendered vehicle is blocking.
            if (TouchesRaisedTerrain(bounds, margin))
                return true;

            Vector3 halfExtents = bounds.extents + Vector3.one * margin;
            int count = Physics.OverlapBoxNonAlloc(
                bounds.center,
                halfExtents,
                overlapBuffer,
                Quaternion.identity,
                ~0,
                QueryTriggerInteraction.Ignore
            );
            for (int i = 0; i < count; i++)
            {
                Collider overlap = overlapBuffer[i];
                if (!overlap || IsOwnCollider(entry.transform, overlap.transform))
                    continue;
                if (IsCatalinaTerrain(overlap.transform))
                    continue;
                if (IsBlockingSceneObstacle(overlap.transform, IsAir(entry)))
                    return true;
            }
            return false;
        }

        private bool TouchesRaisedTerrain(Bounds bounds, float margin)
        {
            float minX = bounds.min.x - margin;
            float maxX = bounds.max.x + margin;
            float minZ = bounds.min.z - margin;
            float maxZ = bounds.max.z + margin;
            float midX = (minX + maxX) * .5f;
            float midZ = (minZ + maxZ) * .5f;
            Vector2[] samples =
            {
                new Vector2(midX, midZ),
                new Vector2(minX, minZ),
                new Vector2(minX, maxZ),
                new Vector2(maxX, minZ),
                new Vector2(maxX, maxZ),
                new Vector2(minX, midZ),
                new Vector2(maxX, midZ),
                new Vector2(midX, minZ),
                new Vector2(midX, maxZ)
            };

            float rayStartY = Mathf.Max(bounds.max.y + 120f, 120f);
            float rayDistance = rayStartY + 120f;
            for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                Vector2 sample = samples[sampleIndex];
                int count = Physics.RaycastNonAlloc(
                    new Vector3(sample.x, rayStartY, sample.y),
                    Vector3.down,
                    castBuffer,
                    rayDistance,
                    ~0,
                    QueryTriggerInteraction.Ignore
                );
                for (int hitIndex = 0; hitIndex < count; hitIndex++)
                {
                    RaycastHit hit = castBuffer[hitIndex];
                    if (!hit.transform || !IsCatalinaTerrain(hit.transform))
                        continue;
                    if (hit.point.y > Mathf.Max(SeaLevel, MinimumLandHeight) &&
                        bounds.min.y <= hit.point.y + margin)
                        return true;
                }
            }
            return false;
        }

        private bool TryFindNearestSafePose(
            int entryIndex,
            Bounds[] visualBounds,
            bool[] hasVisualBounds,
            out SafePose pose)
        {
            pose = default(SafePose);
            Entry entry = entries[entryIndex];
            if (!hasVisualBounds[entryIndex] || !entry.transform)
                return false;

            Bounds source = visualBounds[entryIndex];
            Vector3 origin = entry.transform.position;
            const int DirectionCount = 20;
            for (float radius = 3f; radius <= 96f; radius += 3f)
            {
                for (int directionIndex = 0; directionIndex < DirectionCount; directionIndex++)
                {
                    float angle = (directionIndex * 360f / DirectionCount +
                                   entryIndex * 137.50776f) * Mathf.Deg2Rad;
                    Vector3 candidatePosition = origin + new Vector3(
                        Mathf.Cos(angle) * radius,
                        0f,
                        Mathf.Sin(angle) * radius
                    );
                    Bounds candidateBounds = source;
                    candidateBounds.center += candidatePosition - origin;
                    if (TouchesStaticObstacle(entry, candidateBounds) ||
                        OverlapsAnyDevice(entryIndex, candidateBounds, visualBounds, hasVisualBounds))
                        continue;

                    pose = new SafePose
                    {
                        position = candidatePosition,
                        rotation = entry.transform.rotation,
                        valid = true
                    };
                    return true;
                }
            }
            return false;
        }

        private bool OverlapsAnyDevice(
            int entryIndex,
            Bounds candidate,
            Bounds[] visualBounds,
            bool[] hasVisualBounds)
        {
            for (int otherIndex = 0; otherIndex < entries.Count; otherIndex++)
            {
                if (otherIndex == entryIndex || !hasVisualBounds[otherIndex] ||
                    !entries[otherIndex].transform)
                    continue;
                if (DevicesOverlap(
                        entries[entryIndex], candidate,
                        entries[otherIndex], visualBounds[otherIndex]))
                    return true;
            }
            return false;
        }

        private bool SweepsThroughStaticObstacle(Entry entry, Bounds bounds)
        {
            if (!safePoses.TryGetValue(entry.code, out SafePose safe) || !safe.valid)
                return false;
            Vector3 movement = entry.transform.position - safe.position;
            float distance = movement.magnitude;
            if (distance <= SweepEpsilon)
                return false;

            Vector3 centerOffset = bounds.center - entry.transform.position;
            Vector3 startCenter = safe.position + centerOffset;
            float margin = IsAir(entry) ? AirMargin : SurfaceMargin;
            int count = Physics.BoxCastNonAlloc(
                startCenter,
                bounds.extents + Vector3.one * margin,
                movement / distance,
                castBuffer,
                Quaternion.identity,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore
            );
            for (int i = 0; i < count; i++)
            {
                Transform hit = castBuffer[i].transform;
                if (!hit || IsOwnCollider(entry.transform, hit))
                    continue;
                if (IsBlockingSceneObstacle(hit, IsAir(entry)))
                    return true;
            }
            return false;
        }

        private static bool DevicesOverlap(
            Entry first,
            Bounds a,
            Entry second,
            Bounds b)
        {
            bool bothAir = IsAir(first) && IsAir(second);
            if (bothAir)
            {
                a.Expand(PairMargin * 2f);
                b.Expand(PairMargin * 2f);
                return a.Intersects(b);
            }

            // Keep an overhead-visible separation between air and surface
            // vehicles too, while allowing normal vertical clearance.
            if (IsAir(first) != IsAir(second) &&
                Mathf.Abs(a.center.y - b.center.y) > a.extents.y + b.extents.y + .5f)
                return false;

            float radiusA = Mathf.Sqrt(
                a.extents.x * a.extents.x + a.extents.z * a.extents.z
            );
            float radiusB = Mathf.Sqrt(
                b.extents.x * b.extents.x + b.extents.z * b.extents.z
            );
            Vector2 delta = new Vector2(a.center.x - b.center.x, a.center.z - b.center.z);
            float required = radiusA + radiusB + PairMargin;
            return delta.sqrMagnitude < required * required;
        }

        private static bool IsBlockingSceneObstacle(Transform item, bool aircraft)
        {
            for (Transform cursor = item; cursor; cursor = cursor.parent)
            {
                string key = cursor.name.ToLowerInvariant();
                if (key.Contains("catalina_island_terrain"))
                    return true;
                if (!aircraft &&
                    (key.Contains("island_uav_base") ||
                     key.Contains("shore_command_base")))
                    return true;
            }
            return false;
        }

        private static bool IsCatalinaTerrain(Transform item)
        {
            for (Transform cursor = item; cursor; cursor = cursor.parent)
                if (cursor.name.ToLowerInvariant().Contains("catalina_island_terrain"))
                    return true;
            return false;
        }

        private static bool IsOwnCollider(Transform owner, Transform candidate)
        {
            return candidate == owner || candidate.IsChildOf(owner);
        }

        private static bool IsAir(Entry entry)
        {
            return entry.type == "UAV" ||
                   entry.code.StartsWith("UAV-", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetVisualBounds(Transform root, out Bounds bounds)
        {
            bounds = default(Bounds);
            if (!root)
                return false;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!renderer || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy)
                    continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                    bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }

        private static SafePose CurrentPose(Transform item)
        {
            return new SafePose
            {
                position = item.position,
                rotation = item.rotation,
                valid = item != null
            };
        }
    }
}
