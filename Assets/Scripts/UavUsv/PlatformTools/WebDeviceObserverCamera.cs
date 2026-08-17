using System;
using System.Collections.Generic;
using UnityEngine;

namespace UavUsv.PlatformTools
{
    /// <summary>
    /// WebGL-only observation camera used by the Vue system overview.
    /// It never writes to mission agents and does not change ChaseCamera source code.
    /// </summary>
    public sealed class WebDeviceObserverCamera : MonoBehaviour
    {
        private enum ObservationMode
        {
            None,
            Device,
            Overview,
            Lighthouse
        }

        private readonly List<Transform> sceneTargets = new List<Transform>();
        private readonly RaycastHit[] occlusionHits = new RaycastHit[16];
        private readonly Dictionary<Transform, LineRenderer> fleetTrails =
            new Dictionary<Transform, LineRenderer>();
        private readonly Dictionary<Transform, List<Vector3>> fleetTrailPoints =
            new Dictionary<Transform, List<Vector3>>();
        private readonly HashSet<Transform> initializedTrailTargets =
            new HashSet<Transform>();
        private Camera observedCamera;
        private UavUsv.ChaseCamera chaseCamera;
        private UavUsv.VirtualFleetManager fleetManager;
        private Transform selectedSubject;
        private Transform lighthouse;
        private ObservationMode mode;
        private float desiredFieldOfView = 52f;
        private Transform trailRoot;
        private Material uavTrailMaterial;
        private Material usvTrailMaterial;
        private float nextTrailSampleAt;
        private bool trailSampleLogged;
        private int lastTrailDeviceCount = -1;
        private bool trailRecordingEnabled;

        private const int MaxTrailPoints = 180;
        private const float TrailSampleSeconds = .2f;
        private const float TrailMinDistance = 1.25f;
        private const float TrailMaxJumpDistance = 4f;

        public string CurrentDeviceCode { get; private set; } = string.Empty;
        public string CurrentModeName => ModeName(mode);
        public string CurrentProfileName => selectedSubject && IsUav(selectedSubject)
            ? "uav-overwatch"
            : selectedSubject ? "usv-chase" : CurrentModeName;

        public void Initialize(Camera camera, UavUsv.ChaseCamera chase)
        {
            observedCamera = camera;
            chaseCamera = chase;
            fleetManager = FindObjectOfType<UavUsv.VirtualFleetManager>();
            RefreshSceneTargets();
        }

        public bool TrySelectDevice(
            string requestedCode,
            out string canonicalCode,
            out string profile,
            out string error)
        {
            RefreshSceneTargets();
            string normalized = NormalizeDeviceCode(requestedCode);
            Transform match = null;
            for (int i = 0; i < sceneTargets.Count; i++)
            {
                Transform candidate = sceneTargets[i];
                if (!candidate || (!IsUsv(candidate) && !IsUav(candidate)))
                    continue;
                if (NormalizeDeviceCode(candidate.name) == normalized)
                {
                    match = candidate;
                    break;
                }
            }

            if (!match)
                match = FindSceneDevice(normalized);

            if (!match)
            {
                canonicalCode = normalized;
                profile = string.Empty;
                error = "Unity scene device not found: " + requestedCode;
                return false;
            }

            selectedSubject = match;
            CurrentDeviceCode = CanonicalDeviceCode(match);
            mode = ObservationMode.Device;
            ActivateObserver();
            canonicalCode = CurrentDeviceCode;
            profile = CurrentProfileName;
            error = string.Empty;
            return true;
        }

        private static Transform FindSceneDevice(string normalizedCode)
        {
            Transform[] transforms = FindObjectsOfType<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (!candidate || (!IsUsv(candidate) && !IsUav(candidate)))
                    continue;
                if (NormalizeDeviceCode(candidate.name) == normalizedCode)
                    return candidate;
            }
            return null;
        }

        public bool TrySelectFirst(
            string kind,
            out string canonicalCode,
            out string profile,
            out string error)
        {
            RefreshSceneTargets();
            bool wantUav = string.Equals(kind, "UAV", StringComparison.OrdinalIgnoreCase);
            for (int i = 0; i < sceneTargets.Count; i++)
            {
                Transform candidate = sceneTargets[i];
                if (candidate && (wantUav ? IsUav(candidate) : IsUsv(candidate)))
                    return TrySelectDevice(candidate.name, out canonicalCode, out profile, out error);
            }

            canonicalCode = string.Empty;
            profile = string.Empty;
            error = "Unity scene has no " + kind + " device";
            return false;
        }

        public void SetOverview()
        {
            selectedSubject = null;
            CurrentDeviceCode = string.Empty;
            mode = ObservationMode.Overview;
            ActivateObserver();
            RefreshTrailVisibility();
        }

        public void SetLighthouse()
        {
            RefreshSceneTargets();
            selectedSubject = null;
            CurrentDeviceCode = string.Empty;
            mode = ObservationMode.Lighthouse;
            ActivateObserver();
        }

        public void ReleaseToOriginalCamera()
        {
            mode = ObservationMode.None;
            selectedSubject = null;
            CurrentDeviceCode = string.Empty;
            if (chaseCamera)
                chaseCamera.enabled = true;
            RefreshTrailVisibility();
        }

        public void SetMissionState(string state)
        {
            string normalized = (state ?? string.Empty).Trim().ToUpperInvariant();
            if (normalized == "RUNNING" && !trailRecordingEnabled)
                ResetFleetTrails();
            trailRecordingEnabled = normalized == "RUNNING";
            RefreshTrailVisibility();
        }

        public void ResetFleetTrails()
        {
            foreach (KeyValuePair<Transform, List<Vector3>> pair in fleetTrailPoints)
                pair.Value.Clear();
            initializedTrailTargets.Clear();

            foreach (KeyValuePair<Transform, LineRenderer> pair in fleetTrails)
            {
                if (!pair.Value)
                    continue;
                pair.Value.positionCount = 0;
                pair.Value.enabled = false;
            }

            trailRecordingEnabled = false;
            nextTrailSampleAt = Time.unscaledTime + TrailSampleSeconds;
            trailSampleLogged = false;
        }

        public bool RecenterCurrentDevice(out string error)
        {
            if (!selectedSubject)
            {
                error = "No Unity device is currently selected";
                return false;
            }

            mode = ObservationMode.Device;
            ActivateObserver();
            error = string.Empty;
            return true;
        }

        private void ActivateObserver()
        {
            if (!observedCamera)
                observedCamera = GetComponent<Camera>();
            if (!chaseCamera)
                chaseCamera = GetComponent<UavUsv.ChaseCamera>();
            if (chaseCamera)
                chaseCamera.enabled = false;
        }

        private void LateUpdate()
        {
            if (mode == ObservationMode.None || !observedCamera)
                return;

            RefreshSceneTargets();
            UpdateFleetTrails();
            Vector3 desiredPosition;
            Vector3 focusPoint;

            if (mode == ObservationMode.Device && selectedSubject)
                CalculateDeviceView(out desiredPosition, out focusPoint);
            else if (mode == ObservationMode.Lighthouse)
                CalculateLighthouseView(out desiredPosition, out focusPoint);
            else
                CalculateOverview(out desiredPosition, out focusPoint);

            if (mode == ObservationMode.Device && selectedSubject)
                desiredPosition = ResolveDeviceOcclusion(focusPoint, desiredPosition);
            desiredPosition.y = Mathf.Max(desiredPosition.y, 2.4f);
            Quaternion desiredRotation = Quaternion.LookRotation(
                focusPoint - desiredPosition,
                Vector3.up
            );
            float positionT = 1f - Mathf.Exp(-3.2f * Time.unscaledDeltaTime);
            float rotationT = 1f - Mathf.Exp(-4.8f * Time.unscaledDeltaTime);
            float fovT = 1f - Mathf.Exp(-3.5f * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, desiredPosition, positionT);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationT);
            observedCamera.fieldOfView = Mathf.Lerp(
                observedCamera.fieldOfView,
                desiredFieldOfView,
                fovT
            );
        }

        private void CalculateDeviceView(out Vector3 position, out Vector3 focus)
        {
            Vector3 forward = DeviceForward(selectedSubject);
            Vector3 subject = selectedSubject.position;
            GetVisualMetrics(
                selectedSubject,
                out float visualRadius,
                out float visualTop,
                out float visualCenterHeight
            );
            if (IsUav(selectedSubject))
            {
                float height = Mathf.Clamp(
                    Mathf.Max(visualTop + 5f, 6f),
                    6f,
                    14f
                );
                float back = Mathf.Clamp(
                    Mathf.Max(visualRadius * 3.2f, 8f),
                    8f,
                    14f
                );
                position = subject - forward * back + Vector3.up * height;
                focus = subject +
                    forward * Mathf.Max(2f, visualRadius * 1.5f) +
                    Vector3.up * Mathf.Max(.8f, visualCenterHeight);
                desiredFieldOfView = 45f;
                return;
            }

            float distance = Mathf.Clamp(
                Mathf.Max(6f, visualRadius * 4f),
                6f,
                10f
            );
            float heightUsv = Mathf.Clamp(
                Mathf.Max(visualTop + 5f, 6f),
                6f,
                10f
            );
            focus = subject + Vector3.up * Mathf.Max(.35f, visualCenterHeight);
            position = focus - forward * distance + Vector3.up * heightUsv;
            desiredFieldOfView = 45f;
        }

        private Vector3 ResolveDeviceOcclusion(Vector3 focus, Vector3 desiredPosition)
        {
            Vector3 offset = desiredPosition - focus;
            float distance = offset.magnitude;
            if (distance < .1f)
                return desiredPosition;

            Vector3 direction = offset / distance;
            int hitCount = Physics.SphereCastNonAlloc(
                focus,
                .3f,
                direction,
                occlusionHits,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore
            );
            float nearestObstacle = distance;
            for (int i = 0; i < hitCount; i++)
            {
                Transform hitTransform = occlusionHits[i].transform;
                if (!hitTransform || IsSelectedSubjectPart(hitTransform))
                    continue;
                nearestObstacle = Mathf.Min(nearestObstacle, occlusionHits[i].distance);
            }

            if (nearestObstacle >= distance)
                return desiredPosition;
            if (nearestObstacle <= 1.2f)
                return focus + Vector3.up * 8f - direction * 1.5f;

            return focus + direction * Mathf.Max(1.2f, nearestObstacle - .6f);
        }

        private bool IsSelectedSubjectPart(Transform candidate)
        {
            return selectedSubject &&
                (candidate == selectedSubject || candidate.IsChildOf(selectedSubject));
        }

        private static void GetVisualMetrics(
            Transform subject,
            out float radius,
            out float top,
            out float centerHeight)
        {
            radius = 0f;
            top = 0f;
            centerHeight = 0f;
            if (!subject)
                return;

            Renderer[] renderers = subject.GetComponentsInChildren<Renderer>(false);
            Bounds bounds = default;
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
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!found)
                return;

            radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            top = Mathf.Max(0f, bounds.max.y - subject.position.y);
            centerHeight = Mathf.Max(0f, bounds.center.y - subject.position.y);
        }

        private void CalculateOverview(out Vector3 position, out Vector3 focus)
        {
            Vector3 groupCenter;
            float spread;
            CalculateGroupFrame(out groupCenter, out spread);
            int deviceCount = Mathf.Max(1, sceneTargets.Count);
            float baseDistance = deviceCount <= 8
                ? 26f
                : deviceCount <= 24
                    ? 36f
                    : deviceCount <= 80
                        ? 48f
                        : 60f;
            float spreadMultiplier = deviceCount <= 8 ? 1.8f : 1.25f;
            float distance = Mathf.Clamp(
                baseDistance + spread * spreadMultiplier,
                baseDistance,
                220f
            );
            Vector3 offset = Quaternion.Euler(55f, -35f, 0f) * Vector3.back * distance;
            focus = groupCenter + Vector3.up * 1.2f;
            position = focus + offset;
            float baseFov = deviceCount <= 8
                ? 44f
                : deviceCount <= 24
                    ? 48f
                    : deviceCount <= 80
                        ? 52f
                        : 56f;
            desiredFieldOfView = Mathf.Clamp(
                baseFov + Mathf.InverseLerp(16f, 120f, spread) * 8f,
                baseFov,
                64f
            );
        }

        private void CalculateLighthouseView(out Vector3 position, out Vector3 focus)
        {
            Vector3 groupCenter;
            float spread;
            CalculateGroupFrame(out groupCenter, out spread);
            if (!lighthouse)
            {
                CalculateOverview(out position, out focus);
                return;
            }

            Vector3 outward = lighthouse.position - groupCenter;
            outward.y = 0f;
            if (outward.sqrMagnitude < .01f)
                outward = Vector3.back;
            outward.Normalize();
            position = lighthouse.position + outward * 12f + Vector3.up * 24f;
            focus = Vector3.Lerp(lighthouse.position, groupCenter, .8f) + Vector3.up * 2f;
            desiredFieldOfView = Mathf.Clamp(56f + spread * .12f, 56f, 70f);
        }

        private void CalculateGroupFrame(out Vector3 center, out float spread)
        {
            center = Vector3.zero;
            int count = 0;
            for (int i = 0; i < sceneTargets.Count; i++)
            {
                Transform item = sceneTargets[i];
                if (!item)
                    continue;
                center += item.position;
                count++;
            }

            if (count == 0)
            {
                center = selectedSubject ? selectedSubject.position : Vector3.zero;
                spread = 1f;
                return;
            }

            center /= count;
            spread = 1f;
            for (int i = 0; i < sceneTargets.Count; i++)
            {
                Transform item = sceneTargets[i];
                if (!item)
                    continue;
                Vector3 delta = item.position - center;
                delta.y *= .35f;
                spread = Mathf.Max(spread, delta.magnitude);
            }
        }

        private void RefreshSceneTargets()
        {
            sceneTargets.Clear();
            if (!fleetManager)
                fleetManager = FindObjectOfType<UavUsv.VirtualFleetManager>();

            if (!chaseCamera)
                chaseCamera = GetComponent<UavUsv.ChaseCamera>();
            if (chaseCamera)
            {
                lighthouse = chaseCamera.lookAt;
                Transform[] targets = chaseCamera.groupTargets;
                if (targets != null)
                {
                    for (int i = 0; i < targets.Length; i++)
                    {
                        // The virtual fleet scene shares the legacy camera
                        // director with the demo scene. Once a fleet manager
                        // exists, only fleet devices belong in the overview
                        // bounds; legacy mission ships can be far away.
                        if (!fleetManager &&
                            targets[i] &&
                            !sceneTargets.Contains(targets[i]))
                            sceneTargets.Add(targets[i]);
                    }
                }
            }

            if (!fleetManager)
                return;

            AddFleetTargets(fleetManager.GetUavTransforms());
            AddFleetTargets(fleetManager.GetUsvTransforms());
            SyncFleetTrails();
        }

        private void AddFleetTargets(Transform[] targets)
        {
            if (targets == null)
                return;
            for (int i = 0; i < targets.Length; i++)
            {
                Transform target = targets[i];
                if (target && !sceneTargets.Contains(target))
                    sceneTargets.Add(target);
            }
        }

        private void SyncFleetTrails()
        {
            if (!trailRoot)
            {
                GameObject root = new GameObject("VirtualFleetTrails");
                trailRoot = root.transform;
            }

            HashSet<Transform> activeTargets = new HashSet<Transform>();
            for (int i = 0; i < sceneTargets.Count; i++)
            {
                Transform target = sceneTargets[i];
                if (!target || (!IsUav(target) && !IsUsv(target)))
                    continue;

                activeTargets.Add(target);
                if (!fleetTrails.ContainsKey(target))
                {
                    GameObject trailObject = new GameObject(target.name + "-Trajectory");
                    trailObject.transform.SetParent(trailRoot, false);
                    LineRenderer line = trailObject.AddComponent<LineRenderer>();
                    line.useWorldSpace = true;
                    line.alignment = LineAlignment.View;
                    line.textureMode = LineTextureMode.Stretch;
                    line.numCapVertices = 3;
                    line.numCornerVertices = 3;
                    bool isUav = IsUav(target);
                    Color trailColor = isUav
                        ? new Color(.2f, .88f, 1f, .72f)
                        : new Color(1f, .58f, .08f, .72f);
                    line.widthMultiplier = isUav ? .10f : .12f;
                    line.sharedMaterial = GetTrailMaterial(isUav);
                    line.startColor = line.endColor = trailColor;
                    line.shadowCastingMode =
                        UnityEngine.Rendering.ShadowCastingMode.Off;
                    line.receiveShadows = false;
                    fleetTrails.Add(target, line);
                    fleetTrailPoints.Add(target, new List<Vector3>(MaxTrailPoints));
                }
            }

            List<Transform> staleTargets = new List<Transform>();
            foreach (KeyValuePair<Transform, LineRenderer> pair in fleetTrails)
            {
                if (!pair.Key || !activeTargets.Contains(pair.Key))
                    staleTargets.Add(pair.Key);
            }

            for (int i = 0; i < staleTargets.Count; i++)
            {
                Transform stale = staleTargets[i];
                if (fleetTrails.TryGetValue(stale, out LineRenderer line) && line)
                    Destroy(line.gameObject);
                fleetTrails.Remove(stale);
                fleetTrailPoints.Remove(stale);
                initializedTrailTargets.Remove(stale);
            }

            if (fleetTrails.Count != lastTrailDeviceCount)
            {
                lastTrailDeviceCount = fleetTrails.Count;
                Debug.Log(
                    "[WebDeviceObserverCamera] VirtualFleetTrails refreshed: " +
                    fleetTrails.Count + " devices."
                );
            }
        }

        private Material GetTrailMaterial(bool isUav)
        {
            Material material = isUav
                ? uavTrailMaterial
                : usvTrailMaterial;
            if (material)
                return material;

            Color trailColor = isUav
                ? new Color(.2f, .88f, 1f, .95f)
                : new Color(1f, .58f, .08f, .95f);
            material = SceneFactory.Material(
                "VirtualFleetTrajectoryMaterial-" + (isUav ? "UAV" : "USV"),
                trailColor,
                0f,
                .5f
            );
            if (isUav)
                uavTrailMaterial = material;
            else
                usvTrailMaterial = material;
            return material;
        }

        private void UpdateFleetTrails()
        {
            if (Time.unscaledTime < nextTrailSampleAt)
                return;
            nextTrailSampleAt = Time.unscaledTime + TrailSampleSeconds;

            foreach (KeyValuePair<Transform, LineRenderer> pair in fleetTrails)
            {
                Transform target = pair.Key;
                LineRenderer line = pair.Value;
                if (!target || !line || !fleetTrailPoints.TryGetValue(target, out List<Vector3> points))
                    continue;

                if (!trailRecordingEnabled)
                {
                    line.enabled = mode == ObservationMode.Overview && points.Count > 1;
                    continue;
                }

                Vector3 point = target.position + Vector3.up * (IsUav(target) ? .08f : .18f);
                if (!initializedTrailTargets.Contains(target))
                {
                    points.Clear();
                    points.Add(point);
                    line.positionCount = 1;
                    line.SetPosition(0, point);
                    line.enabled = false;
                    initializedTrailTargets.Add(target);
                    continue;
                }

                float distanceFromLastPoint = points.Count == 0
                    ? 0f
                    : Vector3.Distance(points[points.Count - 1], point);
                if (distanceFromLastPoint > TrailMaxJumpDistance)
                {
                    // A first algorithm frame can be far from the generated
                    // spawn position. Treat that jump as a new origin instead
                    // of drawing a long line across the scene.
                    points.Clear();
                    points.Add(point);
                    line.positionCount = 1;
                    line.SetPosition(0, point);
                    line.enabled = false;
                    continue;
                }

                if (points.Count == 0 ||
                    distanceFromLastPoint >= TrailMinDistance)
                {
                    points.Add(point);
                    if (points.Count > MaxTrailPoints)
                    {
                        points.RemoveAt(0);
                        line.positionCount = points.Count;
                        line.SetPositions(points.ToArray());
                    }
                    else
                    {
                        line.positionCount = points.Count;
                        line.SetPosition(points.Count - 1, point);
                    }

                    if (!trailSampleLogged && points.Count >= 2)
                    {
                        trailSampleLogged = true;
                        Debug.Log(
                            "[WebDeviceObserverCamera] Virtual fleet trajectory " +
                            "sampled: " + target.name +
                            " points=" + points.Count +
                            " mode=" + CurrentModeName
                        );
                    }
                }

                line.enabled = mode == ObservationMode.Overview && points.Count > 1;
            }
        }

        private void RefreshTrailVisibility()
        {
            foreach (KeyValuePair<Transform, LineRenderer> pair in fleetTrails)
            {
                if (!pair.Value)
                    continue;
                List<Vector3> points;
                fleetTrailPoints.TryGetValue(pair.Key, out points);
                pair.Value.enabled =
                    mode == ObservationMode.Overview &&
                    points != null &&
                    points.Count > 1;
            }
        }

        private static Vector3 DeviceForward(Transform subject)
        {
            Vector3 forward = IsUsv(subject) ? subject.right : subject.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > .001f ? forward.normalized : Vector3.forward;
        }

        private static bool IsUsv(Transform subject)
        {
            if (!subject)
                return false;
            string name = subject.name;
            return name.StartsWith("USV-", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("usv_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUav(Transform subject)
        {
            if (!subject)
                return false;
            string name = subject.name;
            return name.StartsWith("UAV-", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("uav_", StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeDeviceCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            string upper = value.Trim().ToUpperInvariant().Replace("_", "-");
            string prefix = upper.StartsWith("UAV") ? "UAV" : upper.StartsWith("USV") ? "USV" : string.Empty;
            if (string.IsNullOrEmpty(prefix))
                return upper;
            string digits = string.Empty;
            for (int i = prefix.Length; i < upper.Length; i++)
            {
                if (char.IsDigit(upper[i]))
                    digits += upper[i];
            }
            if (!int.TryParse(digits, out int index))
                return upper;
            return prefix + "-" + index.ToString("000");
        }

        private static string CanonicalDeviceCode(Transform subject)
        {
            return NormalizeDeviceCode(subject ? subject.name : string.Empty);
        }

        private static string ModeName(ObservationMode value)
        {
            switch (value)
            {
                case ObservationMode.Device: return "device-follow";
                case ObservationMode.Overview: return "overview";
                case ObservationMode.Lighthouse: return "lighthouse";
                default: return "action";
            }
        }

        private void OnDestroy()
        {
            if (chaseCamera)
                chaseCamera.enabled = true;
            if (uavTrailMaterial)
                Destroy(uavTrailMaterial);
            if (usvTrailMaterial)
                Destroy(usvTrailMaterial);
            if (trailRoot)
                Destroy(trailRoot.gameObject);
        }
    }
}
