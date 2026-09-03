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
        private float nextSceneTargetRefreshAt;
        private Vector3 overviewFocus;
        private float overviewDistance;
        private bool overviewFrameInitialized;

        // Algorithm simulation intentionally ships without world-space
        // trajectories. Keeping this as a compile-time gate prevents mission
        // state or camera-mode changes from silently enabling them again.
        private const bool TrajectoryRenderingEnabled = false;

        private ObservationMode interactionMode = ObservationMode.None;
        private Vector3 interactionFocusOffset;
        private float interactionYaw;
        private float interactionPitch;
        private float interactionDistance;
        private float lastPrimaryClickAt = -10f;
        private float lastInteractionAt = -10f;

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
            RefreshSceneTargets(true);
        }

        public bool TrySelectDevice(
            string requestedCode,
            out string canonicalCode,
            out string profile,
            out string error)
        {
            RefreshSceneTargets(true);
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
            RefreshSceneTargets(true);
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
            ResetCameraInteraction();
            overviewFrameInitialized = false;
            ActivateObserver();
            RefreshTrailVisibility();
        }

        public void SetLighthouse()
        {
            RefreshSceneTargets(true);
            selectedSubject = null;
            CurrentDeviceCode = string.Empty;
            mode = ObservationMode.Lighthouse;
            ResetCameraInteraction();
            ActivateObserver();
        }

        public void ReleaseToOriginalCamera()
        {
            mode = ObservationMode.None;
            ResetCameraInteraction();
            selectedSubject = null;
            CurrentDeviceCode = string.Empty;
            if (chaseCamera)
                chaseCamera.enabled = true;
            RefreshTrailVisibility();
        }

        public void SetMissionState(string state)
        {
            trailRecordingEnabled = false;
            DestroyFleetTrails();
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
            ResetCameraInteraction();
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

            RefreshSceneTargets(false);
            UpdateFleetTrails();
            Vector3 desiredPosition;
            Vector3 focusPoint;

            if (mode == ObservationMode.Device && selectedSubject)
                CalculateDeviceView(out desiredPosition, out focusPoint);
            else if (mode == ObservationMode.Lighthouse)
                CalculateLighthouseView(out desiredPosition, out focusPoint);
            else
                CalculateOverview(out desiredPosition, out focusPoint);

            ApplyMouseCameraControl(ref desiredPosition, ref focusPoint);

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

        private void ApplyMouseCameraControl(
            ref Vector3 desiredPosition,
            ref Vector3 focusPoint)
        {
            if (mode != ObservationMode.Overview && mode != ObservationMode.Device)
                return;

            bool pointerInside = Input.mousePosition.x >= 0f &&
                                 Input.mousePosition.y >= 0f &&
                                 Input.mousePosition.x <= Screen.width &&
                                 Input.mousePosition.y <= Screen.height;
            if (!pointerInside)
                return;

            bool reset = false;
            if (Input.GetMouseButtonDown(0))
            {
                float now = Time.unscaledTime;
                reset = now - lastPrimaryClickAt <= .32f;
                lastPrimaryClickAt = now;
            }
            if (reset)
            {
                ResetCameraInteraction();
                return;
            }

            float scroll = Mathf.Clamp(Input.mouseScrollDelta.y, -1f, 1f);
            bool interactionRequested = Input.GetMouseButton(1) ||
                                        Input.GetMouseButton(2) ||
                                        (Input.GetMouseButton(0) &&
                                         !Input.GetMouseButtonDown(0)) ||
                                        Mathf.Abs(scroll) > .001f;
            if (interactionRequested)
                lastInteractionAt = Time.unscaledTime;
            else if (interactionMode == mode &&
                     Time.unscaledTime - lastInteractionAt >= 2.5f)
            {
                // Manual inspection is temporary. Smoothly hand control back
                // to mission framing so a later threat manoeuvre cannot leave
                // active craft outside the WebGL viewport.
                ResetCameraInteraction();
                return;
            }
            if (interactionMode != mode)
            {
                if (!interactionRequested)
                    return;
                InitializeCameraInteraction(desiredPosition, focusPoint);
            }

            if (Input.GetMouseButton(1))
            {
                interactionYaw += Input.GetAxis("Mouse X") * 2.2f;
                interactionPitch = Mathf.Clamp(
                    interactionPitch - Input.GetAxis("Mouse Y") * 1.8f,
                    18f,
                    82f
                );
            }

            if (Input.GetMouseButton(2) ||
                (Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0)))
            {
                Quaternion orbit = Quaternion.Euler(
                    interactionPitch,
                    interactionYaw,
                    0f
                );
                Vector3 right = orbit * Vector3.right;
                Vector3 forward = orbit * Vector3.forward;
                right.y = 0f;
                forward.y = 0f;
                if (right.sqrMagnitude > .001f) right.Normalize();
                if (forward.sqrMagnitude > .001f) forward.Normalize();
                float scale = Mathf.Max(.012f, interactionDistance * .0018f);
                interactionFocusOffset -= right * Input.GetAxis("Mouse X") * scale;
                interactionFocusOffset -= forward * Input.GetAxis("Mouse Y") * scale;
            }

            if (Mathf.Abs(scroll) > .001f)
            {
                Vector3 currentFocus = focusPoint + interactionFocusOffset;
                Quaternion orbit = Quaternion.Euler(
                    interactionPitch,
                    interactionYaw,
                    0f
                );
                Vector3 currentPosition = currentFocus -
                                          orbit * Vector3.forward * interactionDistance;
                bool hasBefore = TryPointerPlanePoint(
                    currentPosition,
                    orbit,
                    currentFocus.y,
                    out Vector3 before
                );

                float zoomStep = scroll < 0f ? .030f : .045f;
                interactionDistance = Mathf.Clamp(
                    interactionDistance * Mathf.Exp(-scroll * zoomStep),
                    mode == ObservationMode.Device ? 3.5f : 8f,
                    mode == ObservationMode.Device ? 90f : 360f
                );
                Vector3 nextPosition = currentFocus -
                                       orbit * Vector3.forward * interactionDistance;
                if (hasBefore && TryPointerPlanePoint(
                        nextPosition,
                        orbit,
                        currentFocus.y,
                        out Vector3 after))
                {
                    Vector3 correction = before - after;
                    correction.y = 0f;
                    interactionFocusOffset += correction;
                }
            }

            focusPoint += interactionFocusOffset;
            Quaternion controlledRotation = Quaternion.Euler(
                interactionPitch,
                interactionYaw,
                0f
            );
            desiredPosition = focusPoint -
                              controlledRotation * Vector3.forward * interactionDistance;
        }

        private void InitializeCameraInteraction(Vector3 position, Vector3 focus)
        {
            Vector3 offset = position - focus;
            interactionDistance = Mathf.Max(1f, offset.magnitude);
            Quaternion look = Quaternion.LookRotation(focus - position, Vector3.up);
            interactionYaw = look.eulerAngles.y;
            interactionPitch = NormalizeSignedAngle(look.eulerAngles.x);
            interactionPitch = Mathf.Clamp(interactionPitch, 18f, 82f);
            interactionFocusOffset = Vector3.zero;
            interactionMode = mode;
        }

        private bool TryPointerPlanePoint(
            Vector3 cameraPosition,
            Quaternion cameraRotation,
            float planeY,
            out Vector3 point)
        {
            point = Vector3.zero;
            if (!observedCamera)
                return false;

            Vector3 savedPosition = transform.position;
            Quaternion savedRotation = transform.rotation;
            transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            Ray ray = observedCamera.ScreenPointToRay(Input.mousePosition);
            transform.SetPositionAndRotation(savedPosition, savedRotation);
            Plane plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            if (!plane.Raycast(ray, out float enter) || enter < 0f)
                return false;
            point = ray.GetPoint(enter);
            return true;
        }

        private void ResetCameraInteraction()
        {
            interactionMode = ObservationMode.None;
            interactionFocusOffset = Vector3.zero;
            interactionDistance = 0f;
        }

        private static float NormalizeSignedAngle(float angle)
        {
            angle %= 360f;
            return angle > 180f ? angle - 360f : angle;
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
            const float elevationDegrees = 55f;
            const float overviewFov = 52f;
            const float frameMargin = 1.12f;
            const float overviewYaw = -35f;
            Vector3 yawRight = Quaternion.Euler(0f, overviewYaw, 0f) * Vector3.right;
            Vector3 yawForward = Quaternion.Euler(0f, overviewYaw, 0f) * Vector3.forward;
            Vector3 rawCenter = Vector3.zero;
            int count = 0;
            List<float> xSamples = new List<float>();
            List<float> zSamples = new List<float>();
            for (int i = 0; i < sceneTargets.Count; i++)
            {
                Transform item = sceneTargets[i];
                if (!item) continue;
                rawCenter += item.position;
                xSamples.Add(item.position.x);
                zSamples.Add(item.position.z);
                count++;
            }
            if (count == 0)
                rawCenter = selectedSubject ? selectedSubject.position : Vector3.zero;
            else
            {
                // A mean is dominated by a single attacker that has moved far
                // away and makes the whole fleet unreadably small. Use a
                // robust median action centre, then frame the dense tactical
                // group while still allowing the view to follow it smoothly.
                xSamples.Sort();
                zSamples.Sort();
                int middle = count / 2;
                rawCenter = new Vector3(
                    count % 2 == 0
                        ? (xSamples[middle - 1] + xSamples[middle]) * .5f
                        : xSamples[middle],
                    rawCenter.y / count,
                    count % 2 == 0
                        ? (zSamples[middle - 1] + zSamples[middle]) * .5f
                        : zSamples[middle]
                );
            }

            List<float> planarDistances = new List<float>();
            for (int i = 0; i < sceneTargets.Count; i++)
            {
                Transform item = sceneTargets[i];
                if (!item) continue;
                Vector3 delta = item.position - rawCenter;
                planarDistances.Add(new Vector2(delta.x, delta.z).magnitude);
            }
            planarDistances.Sort();
            int percentileIndex = Mathf.Clamp(
                Mathf.CeilToInt(planarDistances.Count * .90f) - 1,
                0,
                Mathf.Max(0, planarDistances.Count - 1)
            );
            float coreRadius = planarDistances.Count == 0
                ? 1f
                : Mathf.Max(1f, planarDistances[percentileIndex]);
            float readableExtentLimit = coreRadius * 1.28f + 18f;

            float horizontalExtent = 1f;
            float forwardExtent = 1f;
            for (int i = 0; i < sceneTargets.Count; i++)
            {
                Transform item = sceneTargets[i];
                if (!item) continue;
                Vector3 delta = item.position - rawCenter;
                float hullMargin = IsUsv(item) ? 5f : IsUav(item) ? 3f : 8f;
                horizontalExtent = Mathf.Max(horizontalExtent, Mathf.Min(
                    readableExtentLimit,
                    Mathf.Abs(Vector3.Dot(delta, yawRight)) + hullMargin
                ));
                forwardExtent = Mathf.Max(forwardExtent, Mathf.Min(
                    readableExtentLimit,
                    Mathf.Abs(Vector3.Dot(delta, yawForward)) + hullMargin
                ));
            }
            float verticalHalfFov = overviewFov * Mathf.Deg2Rad * .5f;
            float aspect = observedCamera
                ? Mathf.Max(.75f, observedCamera.aspect)
                : 1.2f;
            float horizontalHalfFov = Mathf.Atan(
                Mathf.Tan(verticalHalfFov) * aspect
            );
            float groundProjection = Mathf.Sin(elevationDegrees * Mathf.Deg2Rad);
            float horizontalDistance = horizontalExtent / Mathf.Max(
                .05f,
                Mathf.Tan(horizontalHalfFov)
            );
            float verticalDistance = forwardExtent * groundProjection / Mathf.Max(
                .05f,
                Mathf.Tan(verticalHalfFov)
            );
            float requiredDistance = Mathf.Clamp(
                Mathf.Max(horizontalDistance, verticalDistance) * frameMargin + 5f,
                14f,
                280f
            );
            if (!overviewFrameInitialized)
            {
                overviewFocus = rawCenter;
                overviewDistance = requiredDistance;
                overviewFrameInitialized = true;
            }
            else
            {
                float focusT = 1f - Mathf.Exp(-2.2f * Time.unscaledDeltaTime);
                overviewFocus = Vector3.Lerp(overviewFocus, rawCenter, focusT);
                float relativeError = Mathf.Abs(requiredDistance - overviewDistance)
                    / Mathf.Max(1f, overviewDistance);
                if (requiredDistance > overviewDistance || relativeError > .06f)
                {
                    float response = requiredDistance > overviewDistance ? 8f : 1.35f;
                    float distanceT = 1f - Mathf.Exp(-response * Time.unscaledDeltaTime);
                    overviewDistance = Mathf.Lerp(
                        overviewDistance,
                        requiredDistance,
                        distanceT
                    );
                }
            }
            Vector3 offset = Quaternion.Euler(
                elevationDegrees,
                overviewYaw,
                0f
            ) * Vector3.back * overviewDistance;
            focus = overviewFocus + Vector3.up * 1.2f;
            position = focus + offset;
            desiredFieldOfView = Mathf.Clamp(
                overviewFov,
                42f,
                60f
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

        private void RefreshSceneTargets(bool force = false)
        {
            if (!force && Time.unscaledTime < nextSceneTargetRefreshAt)
                return;
            nextSceneTargetRefreshAt = Time.unscaledTime + .10f;
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
            AddFleetTargets(fleetManager.GetTargetTransforms());
            if (TrajectoryRenderingEnabled)
                SyncFleetTrails();
            else
                DestroyFleetTrails();
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
            if (!TrajectoryRenderingEnabled)
            {
                DestroyFleetTrails();
                return;
            }
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
            if (!TrajectoryRenderingEnabled)
                return;
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
                pair.Value.enabled = TrajectoryRenderingEnabled &&
                    mode == ObservationMode.Overview &&
                    points != null &&
                    points.Count > 1;
            }
        }

        private void DestroyFleetTrails()
        {
            foreach (KeyValuePair<Transform, LineRenderer> pair in fleetTrails)
                if (pair.Value)
                    Destroy(pair.Value.gameObject);
            fleetTrails.Clear();
            fleetTrailPoints.Clear();
            initializedTrailTargets.Clear();
            lastTrailDeviceCount = 0;
            if (trailRoot)
                Destroy(trailRoot.gameObject);
            trailRoot = null;
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
