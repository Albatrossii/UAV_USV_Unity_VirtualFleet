using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Lightweight lidar/radar suite for USV/UAV agents.
    /// Matches the PPT sensor stack: range sensing with noise, FOV, and avoidance vectors.
    /// </summary>
    public sealed class AgentSensorSuite : MonoBehaviour
    {
        public enum SensorKind
        {
            Surface,
            Air
        }

        public struct Detection
        {
            public Transform source;
            public Vector3 point;
            public float distance;
            public Vector3 direction;
            public string label;
        }

        public SensorKind kind = SensorKind.Surface;
        public float lidarRange = 36f;
        public float radarRange = 48f;
        public float horizontalFovDegrees = 140f;
        public int rayCount = 28;
        public float noiseMeters = .35f;
        public LayerMask obstacleMask = ~0;
        public bool drawDebugRays;
        public Transform[] knownObstacles;

        public readonly List<Detection> detections = new List<Detection>(32);
        public Vector3 avoidanceVector { get; private set; }
        public int hitCount => detections.Count;
        public float nearestDistance { get; private set; } = float.PositiveInfinity;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[8];

        public void Configure(SensorKind sensorKind, float lidar, float radar, Transform[] obstacles)
        {
            kind = sensorKind;
            lidarRange = lidar;
            radarRange = radar;
            knownObstacles = obstacles;
        }

        private void LateUpdate()
        {
            // Scanning is driven by the mission controller so avoidance uses fresh hits.
        }

        public void Scan()
        {
            detections.Clear();
            avoidanceVector = Vector3.zero;
            nearestDistance = float.PositiveInfinity;

            Vector3 origin = transform.position + Vector3.up * (kind == SensorKind.Surface ? .9f : .2f);
            Vector3 forward = kind == SensorKind.Surface ? transform.right : transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < .001f)
                forward = Vector3.forward;
            forward.Normalize();

            float halfFov = horizontalFovDegrees * .5f;
            float maxRange = Mathf.Max(lidarRange, radarRange);

            for (int i = 0; i < rayCount; i++)
            {
                float t = rayCount == 1 ? .5f : i / (float)(rayCount - 1);
                float yaw = Mathf.Lerp(-halfFov, halfFov, t);
                Vector3 direction = Quaternion.AngleAxis(yaw, Vector3.up) * forward;
                float range = Mathf.Abs(yaw) < 35f ? radarRange : lidarRange;
                range = Mathf.Min(range, maxRange);

                int hitCount = Physics.RaycastNonAlloc(
                    origin,
                    direction,
                    hitBuffer,
                    range,
                    obstacleMask,
                    QueryTriggerInteraction.Ignore
                );
                if (hitCount > 0)
                {
                    RaycastHit hit = hitBuffer[0];
                    for (int h = 1; h < hitCount; h++)
                    {
                        if (hitBuffer[h].distance < hit.distance)
                            hit = hitBuffer[h];
                    }

                    if (IsSelfHit(hit.transform))
                        continue;

                    float noisyDistance = Mathf.Max(.2f, hit.distance + Random.Range(-noiseMeters, noiseMeters));
                    Register(hit.transform, hit.point, noisyDistance, direction, hit.transform.name);
                    if (drawDebugRays)
                        Debug.DrawLine(origin, hit.point, Color.green, .02f);
                }
                else if (drawDebugRays)
                {
                    Debug.DrawRay(origin, direction * range, new Color(.2f, .7f, 1f, .25f), .02f);
                }
            }

            // Fallback geometric sensing for primitives without colliders.
            if (knownObstacles != null)
            {
                for (int i = 0; i < knownObstacles.Length; i++)
                {
                    Transform obstacle = knownObstacles[i];
                    if (!obstacle || !obstacle.gameObject.activeInHierarchy || IsSelfHit(obstacle))
                        continue;

                    Vector3 toObstacle = obstacle.position - origin;
                    toObstacle.y = 0f;
                    float distance = toObstacle.magnitude;
                    float senseRange = kind == SensorKind.Air ? radarRange : lidarRange;
                    if (distance > senseRange || distance < .01f)
                        continue;

                    float angle = Vector3.Angle(forward, toObstacle);
                    if (angle > halfFov + 8f)
                        continue;

                    if (AlreadyTracked(obstacle, distance))
                        continue;

                    Register(obstacle, obstacle.position, distance, toObstacle.normalized, obstacle.name);
                }
            }

            if (avoidanceVector.sqrMagnitude > .001f)
                avoidanceVector = avoidanceVector.normalized;
        }

        public bool SeesTarget(Transform target)
        {
            if (!target)
                return false;

            for (int i = 0; i < detections.Count; i++)
            {
                Detection detection = detections[i];
                if (!detection.source)
                    continue;
                if (detection.source == target || detection.source.IsChildOf(target) || target.IsChildOf(detection.source))
                    return true;
                if (!string.IsNullOrEmpty(detection.label) &&
                    (detection.label.Contains("TargetVessel") || detection.label.Contains("CaptureTarget")))
                    return true;
            }

            return false;
        }

        public Vector3 SteerAway(Vector3 desiredWorldPoint, float clearance)
        {
            if (detections.Count == 0)
                return desiredWorldPoint;

            Vector3 adjusted = desiredWorldPoint;
            for (int i = 0; i < detections.Count; i++)
            {
                Detection detection = detections[i];
                if (IsMissionTarget(detection.label))
                    continue;

                float keepOut = clearance + EstimatedRadius(detection.label);
                if (detection.distance >= keepOut)
                    continue;

                Vector3 away = adjusted - detection.point;
                away.y = 0f;
                if (away.sqrMagnitude < .001f)
                    away = -detection.direction;
                adjusted = detection.point + away.normalized * keepOut;
                adjusted.y = desiredWorldPoint.y;
            }

            if (avoidanceVector.sqrMagnitude > .001f)
            {
                adjusted += avoidanceVector * (clearance * .35f);
                adjusted.y = desiredWorldPoint.y;
            }

            return adjusted;
        }

        private static bool IsMissionTarget(string label)
        {
            return !string.IsNullOrEmpty(label) &&
                   (label.Contains("TargetVessel") || label.Contains("CaptureTarget"));
        }

        private void Register(Transform source, Vector3 point, float distance, Vector3 direction, string label)
        {
            detections.Add(new Detection
            {
                source = source,
                point = point,
                distance = distance,
                direction = direction.normalized,
                label = label
            });

            nearestDistance = Mathf.Min(nearestDistance, distance);
            if (!IsMissionTarget(label))
            {
                float weight = Mathf.Clamp01(1f - distance / Mathf.Max(lidarRange, radarRange));
                avoidanceVector += -direction.normalized * weight;
            }
        }

        private bool AlreadyTracked(Transform obstacle, float distance)
        {
            for (int i = 0; i < detections.Count; i++)
            {
                if (detections[i].source == obstacle && Mathf.Abs(detections[i].distance - distance) < 2f)
                    return true;
            }
            return false;
        }

        private bool IsSelfHit(Transform hit)
        {
            if (!hit)
                return true;
            return hit == transform || hit.IsChildOf(transform);
        }

        private static float EstimatedRadius(string label)
        {
            if (string.IsNullOrEmpty(label))
                return 4f;
            if (label.Contains("Lighthouse"))
                return 8f;
            if (label.Contains("ShoreBase"))
                return 10f;
            if (label.Contains("Buoy") || label.Contains("Barrier"))
                return 4.5f;
            if (label.Contains("Target"))
                return 5f;
            return 4f;
        }
    }
}
