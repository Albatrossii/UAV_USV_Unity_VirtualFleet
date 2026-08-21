using System;
using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Protocol-level terrain guard for the Catalina virtual-fleet scene.
    /// It validates requested poses before they become authoritative, so the
    /// Web bridge can own transforms without allowing vessels or aircraft to
    /// pass through the terrain mesh.
    /// </summary>
    public sealed class TerrainNavigationSafety : MonoBehaviour
    {
        private const float SeaLevel = .03f;
        private const float LandHeightTolerance = .035f;
        private const float VesselFootprintRadius = .95f;
        private const float UavTerrainClearance = 1.8f;
        private const float SegmentSampleSpacing = .45f;
        private const float SearchRadiusStep = 1.15f;
        private const int SearchAngles = 32;
        private const int SearchRings = 48;
        private const float RayOriginHeight = 120f;
        private const float RayDistance = 240f;

        private readonly List<MeshCollider> obstacleColliders =
            new List<MeshCollider>();

        public bool IsReady => obstacleColliders.Count > 0;

        public void Configure(params Transform[] obstacleRoots)
        {
            obstacleColliders.Clear();
            if (obstacleRoots == null)
                return;

            var registeredObjects = new HashSet<int>();
            for (int rootIndex = 0; rootIndex < obstacleRoots.Length; rootIndex++)
            {
                Transform obstacleRoot = obstacleRoots[rootIndex];
                if (!obstacleRoot)
                    continue;

                foreach (MeshFilter filter in
                         obstacleRoot.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!filter.sharedMesh ||
                        !registeredObjects.Add(filter.gameObject.GetInstanceID()))
                        continue;
                    MeshCollider collider = filter.GetComponent<MeshCollider>();
                    if (!collider)
                        collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    collider.convex = false;
                    collider.isTrigger = false;
                    obstacleColliders.Add(collider);
                }
            }
            Physics.SyncTransforms();
        }

        public bool TryResolvePose(
            string deviceType,
            Vector3 previous,
            Vector3 proposed,
            bool initialPlacement,
            out Vector3 resolved,
            out string reason)
        {
            resolved = proposed;
            reason = "";
            if (!IsReady)
                return true;

            string kind = (deviceType ?? "").Trim().ToUpperInvariant();
            if (kind == "UAV")
            {
                float requiredY = RequiredAircraftHeight(previous, proposed);
                if (resolved.y < requiredY)
                {
                    resolved.y = requiredY;
                    reason = "UAV_TERRAIN_CLEARANCE";
                }
                return true;
            }

            resolved.y = SeaLevel;
            if (!SurfacePathBlocked(previous, resolved, VesselFootprintRadius))
                return true;

            if (initialPlacement && TryFindNearestSafeWater(resolved, out Vector3 safe))
            {
                resolved = safe;
                reason = "SURFACE_SPAWN_MOVED_TO_WATER";
                return true;
            }

            Vector3 lastSafe = LastSafePointOnSegment(
                previous,
                resolved,
                VesselFootprintRadius
            );
            if (IsSafeWater(lastSafe, VesselFootprintRadius))
            {
                resolved = lastSafe;
                resolved.y = SeaLevel;
                reason = "SURFACE_TERRAIN_PATH_BLOCKED";
                return true;
            }

            if (TryFindNearestSafeWater(previous, out Vector3 fallback))
            {
                resolved = fallback;
                reason = "SURFACE_RECOVERED_TO_WATER";
                return true;
            }

            reason = "NO_SAFE_WATER_POSITION";
            return false;
        }

        public bool IsSafeWater(Vector3 point, float clearance)
        {
            if (TerrainAboveWater(point.x, point.z))
                return false;

            const int samples = 12;
            for (int i = 0; i < samples; i++)
            {
                float angle = i * Mathf.PI * 2f / samples;
                float x = point.x + Mathf.Cos(angle) * clearance;
                float z = point.z + Mathf.Sin(angle) * clearance;
                if (TerrainAboveWater(x, z))
                    return false;
            }
            return true;
        }

        private bool TryFindNearestSafeWater(Vector3 origin, out Vector3 safe)
        {
            origin.y = SeaLevel;
            if (IsSafeWater(origin, VesselFootprintRadius))
            {
                safe = origin;
                return true;
            }

            for (int ring = 1; ring <= SearchRings; ring++)
            {
                float radius = ring * SearchRadiusStep;
                for (int i = 0; i < SearchAngles; i++)
                {
                    float angle = i * Mathf.PI * 2f / SearchAngles;
                    Vector3 candidate = new Vector3(
                        origin.x + Mathf.Cos(angle) * radius,
                        SeaLevel,
                        origin.z + Mathf.Sin(angle) * radius
                    );
                    if (IsSafeWater(candidate, VesselFootprintRadius))
                    {
                        safe = candidate;
                        return true;
                    }
                }
            }

            safe = origin;
            return false;
        }

        private bool SurfacePathBlocked(
            Vector3 from,
            Vector3 to,
            float clearance)
        {
            Vector3 flatFrom = new Vector3(from.x, SeaLevel, from.z);
            Vector3 flatTo = new Vector3(to.x, SeaLevel, to.z);
            float distance = Vector3.Distance(flatFrom, flatTo);
            int samples = Mathf.Max(1, Mathf.CeilToInt(distance / SegmentSampleSpacing));
            for (int i = 0; i <= samples; i++)
            {
                Vector3 point = Vector3.Lerp(flatFrom, flatTo, i / (float)samples);
                if (!IsSafeWater(point, clearance))
                    return true;
            }
            return false;
        }

        private Vector3 LastSafePointOnSegment(
            Vector3 from,
            Vector3 to,
            float clearance)
        {
            Vector3 flatFrom = new Vector3(from.x, SeaLevel, from.z);
            Vector3 flatTo = new Vector3(to.x, SeaLevel, to.z);
            float distance = Vector3.Distance(flatFrom, flatTo);
            int samples = Mathf.Max(1, Mathf.CeilToInt(distance / SegmentSampleSpacing));
            Vector3 lastSafe = flatFrom;
            for (int i = 0; i <= samples; i++)
            {
                Vector3 point = Vector3.Lerp(flatFrom, flatTo, i / (float)samples);
                if (!IsSafeWater(point, clearance))
                    break;
                lastSafe = point;
            }
            return lastSafe;
        }

        private float RequiredAircraftHeight(Vector3 from, Vector3 to)
        {
            float distance = Vector2.Distance(
                new Vector2(from.x, from.z),
                new Vector2(to.x, to.z)
            );
            int samples = Mathf.Max(1, Mathf.CeilToInt(distance / SegmentSampleSpacing));
            float required = SeaLevel + UavTerrainClearance;
            for (int i = 0; i <= samples; i++)
            {
                Vector3 point = Vector3.Lerp(from, to, i / (float)samples);
                if (TryTerrainHeight(point.x, point.z, out float height))
                    required = Mathf.Max(required, height + UavTerrainClearance);
            }
            return required;
        }

        private bool TerrainAboveWater(float x, float z)
        {
            return TryTerrainHeight(x, z, out float height) &&
                   height > SeaLevel + LandHeightTolerance;
        }

        private bool TryTerrainHeight(float x, float z, out float height)
        {
            height = float.NegativeInfinity;
            bool found = false;
            Ray ray = new Ray(
                new Vector3(x, RayOriginHeight, z),
                Vector3.down
            );
            for (int i = 0; i < obstacleColliders.Count; i++)
            {
                MeshCollider terrain = obstacleColliders[i];
                if (!terrain || !terrain.enabled)
                    continue;
                if (!terrain.Raycast(ray, out RaycastHit hit, RayDistance))
                    continue;
                found = true;
                height = Mathf.Max(height, hit.point.y);
            }
            return found;
        }
    }
}
