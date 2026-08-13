using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Adds runtime collision geometry to the presentation models and guards
    /// against visible interpenetration. In local-demo mode it restores the
    /// previous safe pose; with ROS-authoritative poses it reports risk only,
    /// leaving avoidance and motion ownership in ROS/Gazebo.
    /// </summary>
    [DefaultExecutionOrder(20000)]
    public sealed class RuntimeCollisionSafety : MonoBehaviour
    {
        private const float VesselClearance = .08f;
        private const float WarningHoldSeconds = 1.25f;

        private readonly List<MeshCollider> terrainColliders =
            new List<MeshCollider>();
        private readonly Collider[] terrainOverlapBuffer = new Collider[64];
        private Transform[] vessels = new Transform[0];
        private Transform[] aircraft = new Transform[0];
        private BoxCollider[] vesselColliders = new BoxCollider[0];
        private Vector3[] lastSafePositions = new Vector3[0];
        private Quaternion[] lastSafeRotations = new Quaternion[0];
        private bool[] hasSafePose = new bool[0];
        private bool enforceLocalSafety;
        private float warningUntil = -1f;
        private string warningDetail = "";

        public string StatusDisplay => Time.unscaledTime <= warningUntil
            ? "安全：碰撞风险 · " + warningDetail
            : "安全：正常 · 船间/山体防碰撞开启";

        public void Configure(
            Transform terrainRoot,
            Transform[] surfaceVessels,
            Transform[] aerialVehicles,
            bool externalAuthoritative)
        {
            enforceLocalSafety = !externalAuthoritative;
            vessels = surfaceVessels ?? new Transform[0];
            aircraft = aerialVehicles ?? new Transform[0];
            vesselColliders = new BoxCollider[vessels.Length];
            lastSafePositions = new Vector3[vessels.Length];
            lastSafeRotations = new Quaternion[vessels.Length];
            hasSafePose = new bool[vessels.Length];

            AddTerrainColliders(terrainRoot);
            for (int i = 0; i < vessels.Length; i++)
            {
                vesselColliders[i] = AddVehicleCollider(vessels[i], false);
                AddKinematicBody(vessels[i]);
            }
            for (int i = 0; i < aircraft.Length; i++)
            {
                AddVehicleCollider(aircraft[i], true);
                AddKinematicBody(aircraft[i]);
            }

            Physics.SyncTransforms();
            RememberSafePoses();
        }

        private void LateUpdate()
        {
            if (vessels.Length == 0)
                return;

            Physics.SyncTransforms();
            bool[] colliding = new bool[vessels.Length];
            string firstRisk = "";

            for (int i = 0; i < vessels.Length; i++)
            {
                if (!vessels[i] || !vesselColliders[i])
                    continue;

                if (TouchesTerrain(vesselColliders[i]))
                {
                    colliding[i] = true;
                    if (string.IsNullOrEmpty(firstRisk))
                        firstRisk = DisplayName(vessels[i]) + " 接近山体";
                }
            }

            for (int i = 0; i < vessels.Length; i++)
            for (int j = i + 1; j < vessels.Length; j++)
            {
                if (!vessels[i] || !vessels[j] ||
                    !vesselColliders[i] || !vesselColliders[j])
                    continue;

                if (!VesselsOverlap(vesselColliders[i], vesselColliders[j]))
                    continue;

                colliding[i] = true;
                colliding[j] = true;
                if (string.IsNullOrEmpty(firstRisk))
                {
                    firstRisk = DisplayName(vessels[i]) + " ↔ " +
                                DisplayName(vessels[j]);
                }
            }

            bool anyCollision = false;
            for (int i = 0; i < colliding.Length; i++)
                anyCollision |= colliding[i];

            if (anyCollision)
            {
                warningDetail = firstRisk;
                warningUntil = Time.unscaledTime + WarningHoldSeconds;
                if (enforceLocalSafety)
                {
                    for (int i = 0; i < colliding.Length; i++)
                    {
                        if (!colliding[i] || !hasSafePose[i] || !vessels[i])
                            continue;
                        vessels[i].SetPositionAndRotation(
                            lastSafePositions[i],
                            lastSafeRotations[i]
                        );
                    }
                    Physics.SyncTransforms();
                }
                return;
            }

            RememberSafePoses();
        }

        private void AddTerrainColliders(Transform terrainRoot)
        {
            if (!terrainRoot)
                return;

            foreach (MeshFilter filter in
                     terrainRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh)
                    continue;

                MeshCollider collider = filter.GetComponent<MeshCollider>();
                if (!collider)
                    collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;
                collider.isTrigger = false;
                terrainColliders.Add(collider);
            }
        }

        private static BoxCollider AddVehicleCollider(
            Transform vehicle,
            bool aircraftVehicle)
        {
            if (!vehicle)
                return null;

            BoxCollider collider = vehicle.GetComponent<BoxCollider>();
            if (!collider)
                collider = vehicle.gameObject.AddComponent<BoxCollider>();
            string name = vehicle.name;
            if (aircraftVehicle)
            {
                collider.center = new Vector3(0f, .04f, 0f);
                collider.size = new Vector3(1.2f, .44f, 1.2f);
            }
            else if (name.StartsWith("usv_"))
            {
                // USV-M1500 real dimensions: 1.50 x 1.10 x .60 m.
                collider.center = new Vector3(0f, .27f, 0f);
                collider.size = new Vector3(1.5f, .54f, 1.1f);
            }
            else if (name == "friendly_ship")
            {
                collider.center = new Vector3(-.3f, .72f, 0f);
                collider.size = new Vector3(13.8f, 1.8f, 5.2f);
            }
            else
            {
                collider.center = new Vector3(-.35f, .78f, 0f);
                collider.size = new Vector3(16.5f, 1.95f, 5.8f);
            }
            collider.isTrigger = false;
            return collider;
        }

        private static void AddKinematicBody(Transform vehicle)
        {
            if (!vehicle)
                return;

            Rigidbody body = vehicle.GetComponent<Rigidbody>();
            if (!body)
                body = vehicle.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        private bool TouchesTerrain(BoxCollider vessel)
        {
            Vector3 center = vessel.transform.TransformPoint(vessel.center);
            Vector3 scale = vessel.transform.lossyScale;
            Vector3 halfExtents = Vector3.Scale(
                vessel.size * .5f,
                new Vector3(
                    Mathf.Abs(scale.x),
                    Mathf.Abs(scale.y),
                    Mathf.Abs(scale.z)
                )
            );
            int count = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                terrainOverlapBuffer,
                vessel.transform.rotation,
                ~0,
                QueryTriggerInteraction.Ignore
            );
            for (int i = 0; i < count; i++)
            {
                Collider overlap = terrainOverlapBuffer[i];
                if (overlap && overlap != vessel &&
                    overlap is MeshCollider terrain &&
                    terrainColliders.Contains(terrain))
                    return true;
            }
            return false;
        }

        private static bool VesselsOverlap(
            BoxCollider first,
            BoxCollider second)
        {
            Bounds a = first.bounds;
            Bounds b = second.bounds;
            float radiusA = Mathf.Sqrt(
                a.extents.x * a.extents.x + a.extents.z * a.extents.z
            );
            float radiusB = Mathf.Sqrt(
                b.extents.x * b.extents.x + b.extents.z * b.extents.z
            );
            Vector2 delta = new Vector2(
                a.center.x - b.center.x,
                a.center.z - b.center.z
            );
            return delta.sqrMagnitude <
                   Mathf.Pow(radiusA + radiusB + VesselClearance, 2f);
        }

        private void RememberSafePoses()
        {
            for (int i = 0; i < vessels.Length; i++)
            {
                if (!vessels[i])
                    continue;
                lastSafePositions[i] = vessels[i].position;
                lastSafeRotations[i] = vessels[i].rotation;
                hasSafePose[i] = true;
            }
        }

        private static string DisplayName(Transform value)
        {
            if (!value)
                return "未知载具";
            if (value.name == "friendly_ship")
                return "友船";
            if (value.name == "enemy_ship")
                return "敌船";
            return value.name;
        }
    }
}
