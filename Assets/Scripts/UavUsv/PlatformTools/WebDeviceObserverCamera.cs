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
        private float zoomScale = 1f;
        private float orbitYaw;
        private float orbitPitch;
        private Vector3 panOffset;
        private float lastPrimaryClickAt = -1f;
        private float previousPinchDistance;
        private Vector2 previousPinchCenter;
        private Vector3 lastFocusPoint;
        private Vector3 frozenBasePosition;
        private Vector3 frozenBaseFocus;
        private Vector3 previousMousePosition;
        private bool mouseDragActive;
        private bool manualView;
        private bool hasLastFocus;
        private bool snapNextView;
        private const float MinZoomScale = .42f;
        private const float MaxZoomScale = 2.25f;

        private const int MaxTrailPoints = 180;
        private const float TrailSampleSeconds = .2f;
        private const float TrailMinDistance = 1.25f;
        private const float TrailMaxJumpDistance = 4f;
        // Product decision: algorithm simulation currently presents only the
        // live fleet. Keep trajectory recording and rendering dormant in all
        // camera modes until a dedicated UI switch is introduced.
        private static readonly bool FleetTrailsEnabled = false;

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
            // WebGL simulation is interactive from the first rendered frame.
            // Waiting for an explicit overview command leaves the legacy chase
            // camera active and makes wheel/drag appear broken before a user
            // presses the global-view button.
            SetOverview();
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
            ResetInteraction();
            // A UI reset must be immediately visible even after a long manual
            // pan/zoom.  Interpolating from an off-centre frozen view made the
            // global-view button look unresponsive for several frames.
            snapNextView = true;
            ActivateObserver();
            RefreshTrailVisibility();
        }

        public void FitAll()
        {
            SetOverview();
        }

        public void AdjustZoom(float steps)
        {
            if (Mathf.Abs(steps) < .001f) return;
            zoomScale = Mathf.Clamp(zoomScale * Mathf.Exp(-steps * .115f), MinZoomScale, MaxZoomScale);
        }

        private void AdjustZoomAtScreenPoint(float steps, Vector2 screenPoint)
        {
            if (Mathf.Abs(steps) < .001f) return;
            EnterManualView();
            float oldScale = zoomScale;
            Vector3 anchor;
            bool hasAnchor = TryScreenAnchor(screenPoint, out anchor);
            AdjustZoom(steps);
            if (hasAnchor && oldScale > .001f)
            {
                float ratio = zoomScale / oldScale;
                Vector3 shift = (anchor - frozenBaseFocus) * (1f - ratio);
                shift.y = 0f;
                panOffset += shift;
            }
        }

        private bool TryScreenAnchor(Vector2 screenPoint, out Vector3 anchor)
        {
            float planeHeight = hasLastFocus ? lastFocusPoint.y : 0f;
            Plane plane = new Plane(Vector3.up, new Vector3(0f, planeHeight, 0f));
            Ray ray = observedCamera.ScreenPointToRay(screenPoint);
            if (plane.Raycast(ray, out float distance))
            {
                anchor = ray.GetPoint(distance);
                return true;
            }
            anchor = hasLastFocus ? lastFocusPoint : Vector3.zero;
            return false;
        }

        private void EnterManualView()
        {
            if (manualView) return;
            manualView = true;
            frozenBasePosition = transform.position;
            frozenBaseFocus = hasLastFocus
                ? lastFocusPoint
                : transform.position + transform.forward * 40f;
            zoomScale = 1f;
            orbitYaw = 0f;
            orbitPitch = 0f;
            panOffset = Vector3.zero;
        }

        private void ResetInteraction()
        {
            zoomScale = 1f;
            orbitYaw = 0f;
            orbitPitch = 0f;
            panOffset = Vector3.zero;
            previousPinchDistance = 0f;
            previousPinchCenter = Vector2.zero;
            manualView = false;
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
            if (!FleetTrailsEnabled)
            {
                ResetFleetTrails();
                return;
            }
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

            HandleInteractiveInput();
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

            if (manualView)
            {
                desiredPosition = frozenBasePosition;
                focusPoint = frozenBaseFocus;
            }

            ApplyInteractiveTransform(ref desiredPosition, ref focusPoint);
            lastFocusPoint = focusPoint;
            hasLastFocus = true;

            if (mode == ObservationMode.Device && selectedSubject)
                desiredPosition = ResolveDeviceOcclusion(focusPoint, desiredPosition);
            desiredPosition.y = Mathf.Max(desiredPosition.y, 2.4f);
            Quaternion desiredRotation = Quaternion.LookRotation(
                focusPoint - desiredPosition,
                Vector3.up
            );
            if (snapNextView)
            {
                transform.position = desiredPosition;
                transform.rotation = desiredRotation;
                observedCamera.fieldOfView = desiredFieldOfView;
                snapNextView = false;
                return;
            }
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

        private void HandleInteractiveInput()
        {
            AdjustZoomAtScreenPoint(Input.mouseScrollDelta.y, Input.mousePosition);
            bool orbiting = Input.GetMouseButton(1);
            bool panning = Input.GetMouseButton(2) || Input.GetMouseButton(0);
            bool mouseDragging = orbiting || panning;
            Vector2 mouseDelta = Vector2.zero;
            if (mouseDragging)
            {
                Vector3 currentMousePosition = Input.mousePosition;
                if (mouseDragActive)
                    mouseDelta = currentMousePosition - previousMousePosition;
                else
                    mouseDragActive = true;
                previousMousePosition = currentMousePosition;
            }
            else
            {
                mouseDragActive = false;
            }
            if (orbiting && mouseDelta.sqrMagnitude > .01f)
            {
                EnterManualView();
                orbitYaw += mouseDelta.x * .18f;
                orbitPitch = Mathf.Clamp(orbitPitch - mouseDelta.y * .15f, -28f, 38f);
            }
            if (panning && mouseDelta.sqrMagnitude > .01f)
            {
                EnterManualView();
                float scale = Mathf.Clamp(Vector3.Distance(transform.position, lastFocusPoint) * .00085f, .012f, .12f);
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                panOffset += (-transform.right * mouseDelta.x - forward * mouseDelta.y) * scale;
                panOffset.y = 0f;
            }
            if (Input.GetMouseButtonDown(0) && !Input.GetKey(KeyCode.LeftShift))
            {
                float now = Time.unscaledTime;
                if (now - lastPrimaryClickAt <= .32f) FitAll();
                lastPrimaryClickAt = now;
            }
            if (Input.touchCount >= 2)
            {
                Touch first = Input.GetTouch(0);
                Touch second = Input.GetTouch(1);
                float distance = Vector2.Distance(first.position, second.position);
                Vector2 pinchCenter = (first.position + second.position) * .5f;
                if (previousPinchDistance > 0f)
                {
                    AdjustZoomAtScreenPoint(
                        (distance - previousPinchDistance) / Mathf.Max(40f, Screen.dpi > 0f ? Screen.dpi : 160f) * 3f,
                        pinchCenter
                    );
                    Vector2 centerDelta = pinchCenter - previousPinchCenter;
                    float scale = Mathf.Clamp(Vector3.Distance(transform.position, lastFocusPoint) * .0015f, .018f, .24f);
                    Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                    panOffset += (-transform.right * centerDelta.x - forward * centerDelta.y) * scale;
                }
                previousPinchDistance = distance;
                previousPinchCenter = pinchCenter;
            }
            else
            {
                previousPinchDistance = 0f;
                previousPinchCenter = Vector2.zero;
                if (Input.touchCount == 1 && Input.GetTouch(0).phase == TouchPhase.Moved)
                {
                    EnterManualView();
                    Vector2 delta = Input.GetTouch(0).deltaPosition;
                    float scale = Mathf.Clamp(Vector3.Distance(transform.position, lastFocusPoint) * .0015f, .018f, .24f);
                    Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                    panOffset += (-transform.right * delta.x - forward * delta.y) * scale;
                }
            }
            if (Input.GetKeyDown(KeyCode.Home)) FitAll();
        }

        private void ApplyInteractiveTransform(ref Vector3 position, ref Vector3 focus)
        {
            focus += panOffset;
            position += panOffset;
            Vector3 offset = position - focus;
            Quaternion rotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            position = focus + rotation * offset.normalized * Mathf.Max(4f, offset.magnitude) * zoomScale;
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
            // Product-level "global view" means an actual overhead tactical
            // view.  Keep a two-degree tilt to avoid a LookRotation up-vector
            // singularity while remaining visually top-down.
            const float elevationDegrees = 88f;
            const float overviewFov = 52f;
            const float frameMargin = 1.18f;
            float verticalHalfFov = overviewFov * Mathf.Deg2Rad * .5f;
            float aspect = observedCamera
                ? Mathf.Max(.75f, observedCamera.aspect)
                : 1.2f;
            float horizontalHalfFov = Mathf.Atan(
                Mathf.Tan(verticalHalfFov) * aspect
            );
            float groundProjection = Mathf.Sin(elevationDegrees * Mathf.Deg2Rad);
            float horizontalDistance = spread / Mathf.Max(
                .05f,
                Mathf.Tan(horizontalHalfFov)
            );
            float verticalDistance = spread * groundProjection / Mathf.Max(
                .05f,
                Mathf.Tan(verticalHalfFov)
            );
            float distance = Mathf.Clamp(
                Mathf.Max(horizontalDistance, verticalDistance) * frameMargin + 5f,
                14f,
                220f
            );
            Vector3 offset = Quaternion.Euler(
                elevationDegrees,
                0f,
                0f
            ) * Vector3.back * distance;
            focus = groupCenter + Vector3.up * 1.2f;
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
            // Mission targets are the semantic centre of an experiment. This
            // keeps the hostile/protected vessel in frame while the formation
            // closes, instead of letting a large reserve fleet or the shore
            // pull the camera centre away from the action.
            for (int i = 0; i < sceneTargets.Count; i++)
            {
                Transform item = sceneTargets[i];
                if (!item || !item.gameObject.activeInHierarchy ||
                    !item.name.StartsWith("TARGET-", StringComparison.OrdinalIgnoreCase))
                    continue;
                center += item.position;
                count++;
            }
            if (count > 0)
                center /= count;

            int allCount = 0;
            Vector3 allCenter = Vector3.zero;
            for (int i = 0; i < sceneTargets.Count; i++)
            {
                Transform item = sceneTargets[i];
                if (!item || !item.gameObject.activeInHierarchy)
                    continue;
                allCenter += item.position;
                allCount++;
            }

            if (allCount == 0)
            {
                center = selectedSubject ? selectedSubject.position : Vector3.zero;
                spread = 1f;
                return;
            }

            if (count == 0)
                center = allCenter / allCount;
            spread = 1f;
            for (int i = 0; i < sceneTargets.Count; i++)
            {
                Transform item = sceneTargets[i];
                if (!item || !item.gameObject.activeInHierarchy)
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
            AddFleetTargets(fleetManager.GetTargetTransforms());
            SyncFleetTrails();
        }

        private void AddFleetTargets(Transform[] targets)
        {
            if (targets == null)
                return;
            for (int i = 0; i < targets.Length; i++)
            {
                Transform target = targets[i];
                if (target && target.gameObject.activeInHierarchy && !sceneTargets.Contains(target))
                    sceneTargets.Add(target);
            }
        }

        private void SyncFleetTrails()
        {
            if (!FleetTrailsEnabled)
            {
                RefreshTrailVisibility();
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
            if (!FleetTrailsEnabled)
            {
                RefreshTrailVisibility();
                return;
            }
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
                    FleetTrailsEnabled &&
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
