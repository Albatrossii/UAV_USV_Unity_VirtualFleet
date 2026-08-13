using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Read-only camera director. It observes mission transforms but never writes to them.
    /// The main camera favors readable action; a trajectory inset records mission motion.
    /// </summary>
    public sealed class ChaseCamera : MonoBehaviour
    {
        private enum ViewMode
        {
            Action,
            Overview,
            BoatFollow,
            DroneFollow,
            FreeOrbit
        }

        public Transform target;
        public Transform companion;
        public Transform lookAt;
        public Transform[] groupTargets;
        public float distance = 8.5f;
        public float height = 3.2f;
        public float sideOffset = -1.1f;
        public float minDistance = 10.5f;
        public float maxDistance = 28f;
        public float minHeight = 4.2f;
        public float maxHeight = 11f;
        public float lookAhead = 4.5f;
        public float lookHeight = 1.2f;
        public float lighthouseInfluence = .14f;
        public float positionSmooth = 3f;
        public float rotationSmooth = 4.5f;
        public bool useTargetRightAsForward;

        [Header("Readable action camera")]
        public float actionYaw = -35f;
        public float actionPitch = 32f;
        public float actionWorldPadding = 4f;
        public float actionFitPadding = 1f;
        public float actionMinDistance = 24f;
        public float actionMaxDistance = 78f;
        public float actionDistanceDeadZone = 1.5f;
        public float actionSecondBoatRadius = 48f;
        public float actionAllBoatsRadius = 36f;
        public float actionNearestDroneRadius = 70f;
        public float actionAllDronesRadius = 42f;
        public float actionMembershipHysteresis = 7f;
        public float droneAirborneHeight = 1.15f;
        public float takeoffShotSeconds = 5.5f;

        [Header("Global overview")]
        public float overviewYaw = -35f;
        public float overviewPitch = 58f;
        public float overviewWorldPadding = 12f;
        public float overviewFitPadding = 1.12f;
        public float overviewMinDistance = 55f;
        public float overviewMaxDistance = 260f;
        public float overviewDistanceDeadZone = 2.5f;

        [Header("Trajectory statistics inset")]
        public bool showTacticalInset = true;
        public Rect tacticalViewport = new Rect(.62f, .6f, .37f, .39f);
        public float trajectoryWorldPadding = 8f;
        public float trajectorySampleSeconds = .2f;
        public float trajectoryMinSampleDistance = .25f;
        public float trajectoryResetJumpDistance = 24f;
        public int trajectoryMaxSamplesPerAgent = 900;
        public int trajectoryDrawSegmentsPerAgent = 140;
        public bool showAgentLabels = true;

        private ViewMode mode = ViewMode.Action;
        private bool initialized;
        private float orbitYaw = -35f;
        private float orbitPitch = 22f;
        private float orbitDistance = 42f;
        private float actionDistance;
        private float overviewDistance;
        private float takeoffShotUntil = -1f;
        private bool hadAirborneDrone;
        private bool includeSecondBoat;
        private bool includeThirdBoat;
        private bool includeAllDrones;
        private Camera attachedCamera;
        private Texture2D trajectoryPixel;
        private float trajectoryNextSampleTime;
        private float trajectoryStartedAt;
        private readonly Dictionary<Transform, TrajectorySeries> trajectories =
            new Dictionary<Transform, TrajectorySeries>();
        private readonly float[] formationReadiness = new float[3];
        private Transform[] boatTargets = new Transform[0];
        private Transform[] droneTargets = new Transform[0];
        private Transform focusTarget;
        private MultiAgentCaptureDefenseScenario captureScenario;
        private GUIStyle usvLabelStyle;
        private GUIStyle uavLabelStyle;
        private GUIStyle targetLabelStyle;
        private GUIStyle cameraHelpStyle;
        private GUIStyle trajectoryTextStyle;
        private float guiStyleScale = -1f;

        private sealed class TrajectorySeries
        {
            public readonly List<Vector3> points = new List<Vector3>();
            public string label;
            public Color color;
            public float distance;
        }

        private void Awake()
        {
            attachedCamera = GetComponent<Camera>();
        }

        public void SetGroupTargets(Transform[] targets)
        {
            groupTargets = targets;

            var boats = new List<Transform>();
            var drones = new List<Transform>();
            focusTarget = null;
            if (groupTargets != null)
            {
                for (int i = 0; i < groupTargets.Length; i++)
                {
                    Transform subject = groupTargets[i];
                    if (!subject)
                        continue;

                    string objectName = subject.name;
                    if (IsUsvName(objectName))
                        boats.Add(subject);
                    else if (IsUavName(objectName))
                        drones.Add(subject);
                    else if (IsEnemyName(objectName))
                        focusTarget = subject;
                }
            }

            boatTargets = boats.ToArray();
            droneTargets = drones.ToArray();
            captureScenario = FindObjectOfType<MultiAgentCaptureDefenseScenario>();
            ResetTrajectories();
            actionDistance = 0f;
            overviewDistance = 0f;
            initialized = false;
        }

        private void Update()
        {
            UpdateTakeoffDirector();

            if (Input.GetKeyDown(KeyCode.C))
                SetMode(ViewMode.Action);
            else if (Input.GetKeyDown(KeyCode.Alpha1))
                SetMode(ViewMode.Overview);
            else if (Input.GetKeyDown(KeyCode.Alpha2))
                SetMode(ViewMode.BoatFollow);
            else if (Input.GetKeyDown(KeyCode.Alpha3))
                SetMode(ViewMode.DroneFollow);
            else if (Input.GetKeyDown(KeyCode.Alpha4))
                SetMode(ViewMode.FreeOrbit);
            else if (Input.GetKeyDown(KeyCode.Tab))
                SetMode((ViewMode)(((int)mode + 1) % 5));

            if (mode == ViewMode.FreeOrbit)
            {
                if (Input.GetMouseButton(1))
                {
                    orbitYaw += Input.GetAxis("Mouse X") * 4f;
                    orbitPitch = Mathf.Clamp(
                        orbitPitch - Input.GetAxis("Mouse Y") * 3f,
                        8f,
                        78f
                    );
                }

                orbitDistance = Mathf.Clamp(
                    orbitDistance - Input.mouseScrollDelta.y * 3f,
                    8f,
                    180f
                );
            }
        }

        private void LateUpdate()
        {
            UpdateTrajectoryData();

            if (mode == ViewMode.Action)
            {
                UpdateActionView();
                return;
            }

            if (mode == ViewMode.Overview)
            {
                UpdateOverview();
                return;
            }

            if (!target)
                return;

            if (mode == ViewMode.FreeOrbit)
            {
                UpdateFreeOrbit();
                return;
            }

            if (mode == ViewMode.DroneFollow && companion)
            {
                UpdateSubjectFollow(
                    companion,
                    null,
                    18f,
                    8.5f,
                    1.1f,
                    1.6f,
                    false
                );
                return;
            }

            UpdateSubjectFollow(
                target,
                null,
                distance,
                height,
                sideOffset,
                lookHeight,
                useTargetRightAsForward
            );
        }

        private void UpdateSubjectFollow(
            Transform subject,
            Transform secondary,
            float baseDistance,
            float baseHeight,
            float side,
            float focusHeight,
            bool targetRightAsForward)
        {
            Vector3 forward = targetRightAsForward ? subject.right : subject.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < .001f)
            {
                Vector3 fallback = lookAt
                    ? lookAt.position - subject.position
                    : Vector3.forward;
                fallback.y = 0f;
                forward = fallback.sqrMagnitude > .001f ? fallback.normalized : Vector3.forward;
            }
            else
            {
                forward.Normalize();
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 subjectCenter = subject.position;
            float subjectSpread = 0f;
            if (secondary)
            {
                subjectCenter = (subject.position + secondary.position) * .5f;
                subjectSpread = Vector3.Distance(subject.position, secondary.position);
            }

            if (lookAt)
            {
                Vector3 lighthouseFlat = lookAt.position;
                lighthouseFlat.y = subjectCenter.y;
                subjectSpread = Mathf.Max(
                    subjectSpread,
                    Vector3.Distance(subject.position, lighthouseFlat) * .18f
                );
            }

            float visualRadius = 0f;
            float visualTop = 0f;
            float visualCenterHeight = 0f;
            if (TryGetVisualBounds(subject, out Bounds subjectBounds))
            {
                visualRadius = Mathf.Max(
                    subjectBounds.extents.x,
                    subjectBounds.extents.z
                );
                visualTop = Mathf.Max(0f, subjectBounds.max.y - subject.position.y);
                visualCenterHeight = Mathf.Max(
                    0f,
                    subjectBounds.center.y - subject.position.y
                );
            }

            float dynamicDistance = Mathf.Clamp(
                Mathf.Max(
                    baseDistance + subjectSpread * .55f,
                    visualRadius * 2.15f
                ),
                minDistance,
                maxDistance
            );
            float dynamicHeight = Mathf.Clamp(
                Mathf.Max(
                    baseHeight + subjectSpread * .18f,
                    visualTop + 3f
                ),
                minHeight,
                maxHeight
            );
            Vector3 desiredPosition =
                subjectCenter - forward * dynamicDistance +
                right * side +
                Vector3.up * dynamicHeight;

            Vector3 focusPoint = subjectCenter + Vector3.up * Mathf.Max(
                focusHeight,
                visualCenterHeight
            );
            if (lookAt)
                focusPoint = Vector3.Lerp(
                    focusPoint,
                    lookAt.position + Vector3.up * 2.2f,
                    lighthouseInfluence
                );

            MoveCamera(desiredPosition, focusPoint);
        }

        private void UpdateActionView()
        {
            Bounds bounds = default;
            bool takeoffShot = Time.time < takeoffShotUntil &&
                TryGetBounds(droneTargets, out bounds);
            if (!takeoffShot && !TryGetActionBounds(out bounds))
            {
                UpdateOverview();
                return;
            }

            bounds = ExpandBounds(bounds, actionWorldPadding);
            UpdateFittedView(
                bounds,
                actionYaw,
                takeoffShot ? 26f : actionPitch,
                actionFitPadding,
                actionMinDistance,
                actionMaxDistance,
                actionDistanceDeadZone,
                ref actionDistance
            );
        }

        private void UpdateOverview()
        {
            if (!TryGetGroupBounds(out Bounds bounds))
                return;

            bounds = ExpandBounds(bounds, overviewWorldPadding);
            UpdateFittedView(
                bounds,
                overviewYaw,
                overviewPitch,
                overviewFitPadding,
                overviewMinDistance,
                overviewMaxDistance,
                overviewDistanceDeadZone,
                ref overviewDistance
            );
        }

        private void UpdateFittedView(
            Bounds bounds,
            float yaw,
            float pitch,
            float fitPadding,
            float minimumDistance,
            float maximumDistance,
            float distanceDeadZone,
            ref float retainedDistance)
        {
            Vector3 focus = bounds.center;
            focus.y = Mathf.Max(1.2f, focus.y);

            Camera camera = attachedCamera ? attachedCamera : GetComponent<Camera>();
            float verticalFov = camera ? camera.fieldOfView : 58f;
            float aspect = camera ? Mathf.Max(.1f, camera.aspect) : 16f / 9f;
            float verticalHalfFov = verticalFov * .5f * Mathf.Deg2Rad;
            float horizontalHalfFov = Mathf.Atan(Mathf.Tan(verticalHalfFov) * aspect);
            float limitingHalfFov = Mathf.Max(
                .1f,
                Mathf.Min(verticalHalfFov, horizontalHalfFov)
            );
            float radius = Mathf.Max(1f, bounds.extents.magnitude);
            float requiredDistance = radius / Mathf.Sin(limitingHalfFov);
            requiredDistance = Mathf.Clamp(
                requiredDistance * fitPadding,
                minimumDistance,
                maximumDistance
            );

            if (retainedDistance <= 0f ||
                Mathf.Abs(requiredDistance - retainedDistance) > distanceDeadZone)
                retainedDistance = requiredDistance;

            Quaternion viewRotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 desiredPosition =
                focus - viewRotation * Vector3.forward * retainedDistance;
            MoveCamera(desiredPosition, focus);
        }

        private void UpdateFreeOrbit()
        {
            Vector3 focus = GetSceneFocus();
            Quaternion rotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            Vector3 desiredPosition =
                focus - rotation * Vector3.forward * orbitDistance;
            MoveCamera(desiredPosition, focus);
        }

        private Vector3 GetSceneFocus()
        {
            if (focusTarget)
                return focusTarget.position + Vector3.up * 1.2f;

            if (TryGetGroupBounds(out Bounds groupBounds))
            {
                Vector3 groupFocus = groupBounds.center;
                groupFocus.y = Mathf.Max(1.2f, groupFocus.y);
                return groupFocus;
            }

            Vector3 focus = target ? target.position : Vector3.zero;
            if (companion)
                focus = (focus + companion.position) * .5f;
            if (lookAt)
                focus = Vector3.Lerp(focus, lookAt.position, .18f);
            focus.y += 1.2f;
            return focus;
        }

        private bool TryGetActionBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            Transform missionTarget = focusTarget ? focusTarget : lookAt;
            if (!missionTarget)
                missionTarget = target;
            if (!missionTarget)
                return false;

            Vector3 missionCenter = missionTarget.position;
            EncapsulateSubject(ref bounds, ref found, missionTarget);

            Transform nearestBoat = null;
            Transform secondBoat = null;
            Transform thirdBoat = null;
            float nearestDistance = float.PositiveInfinity;
            float secondDistance = float.PositiveInfinity;
            float thirdDistance = float.PositiveInfinity;
            for (int i = 0; i < boatTargets.Length; i++)
            {
                Transform boat = boatTargets[i];
                if (!IsVisibleSubject(boat))
                    continue;

                float candidateDistance = HorizontalDistance(boat.position, missionCenter);
                if (candidateDistance < nearestDistance)
                {
                    thirdBoat = secondBoat;
                    thirdDistance = secondDistance;
                    secondBoat = nearestBoat;
                    secondDistance = nearestDistance;
                    nearestBoat = boat;
                    nearestDistance = candidateDistance;
                }
                else if (candidateDistance < secondDistance)
                {
                    thirdBoat = secondBoat;
                    thirdDistance = secondDistance;
                    secondBoat = boat;
                    secondDistance = candidateDistance;
                }
                else if (candidateDistance < thirdDistance)
                {
                    thirdBoat = boat;
                    thirdDistance = candidateDistance;
                }
            }

            if (nearestBoat)
                EncapsulateSubject(ref bounds, ref found, nearestBoat);
            includeSecondBoat = secondBoat && (
                includeSecondBoat
                    ? secondDistance <= actionSecondBoatRadius + actionMembershipHysteresis
                    : secondDistance <= actionSecondBoatRadius
            );
            includeThirdBoat = thirdBoat && (
                includeThirdBoat
                    ? thirdDistance <= actionAllBoatsRadius + actionMembershipHysteresis
                    : thirdDistance <= actionAllBoatsRadius
            );
            if (includeSecondBoat)
                EncapsulateSubject(ref bounds, ref found, secondBoat);
            if (includeThirdBoat)
                EncapsulateSubject(ref bounds, ref found, thirdBoat);

            Transform nearestDrone = null;
            float nearestDroneDistance = float.PositiveInfinity;
            float farthestAirborneDroneDistance = 0f;
            int airborneDroneCount = 0;
            for (int i = 0; i < droneTargets.Length; i++)
            {
                Transform drone = droneTargets[i];
                if (!IsAirborneDrone(drone))
                    continue;

                float candidateDistance = HorizontalDistance(drone.position, missionCenter);
                airborneDroneCount++;
                farthestAirborneDroneDistance = Mathf.Max(
                    farthestAirborneDroneDistance,
                    candidateDistance
                );
                if (candidateDistance < nearestDroneDistance)
                {
                    nearestDrone = drone;
                    nearestDroneDistance = candidateDistance;
                }
            }

            if (nearestDrone && nearestDroneDistance <= actionNearestDroneRadius)
                EncapsulateSubject(ref bounds, ref found, nearestDrone);

            includeAllDrones = airborneDroneCount > 1 && (
                includeAllDrones
                    ? farthestAirborneDroneDistance <=
                        actionAllDronesRadius + actionMembershipHysteresis
                    : farthestAirborneDroneDistance <= actionAllDronesRadius
            );
            for (int i = 0; includeAllDrones && i < droneTargets.Length; i++)
            {
                Transform drone = droneTargets[i];
                if (!IsAirborneDrone(drone) || drone == nearestDrone)
                    continue;
                if (HorizontalDistance(drone.position, missionCenter) <= actionAllDronesRadius)
                    EncapsulateSubject(ref bounds, ref found, drone);
            }

            return found;
        }

        private bool TryGetGroupBounds(out Bounds bounds)
        {
            return TryGetBounds(groupTargets, out bounds);
        }

        private static bool TryGetBounds(Transform[] subjects, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            if (subjects == null)
                return false;

            for (int i = 0; i < subjects.Length; i++)
            {
                Transform subject = subjects[i];
                if (!IsVisibleSubject(subject))
                    continue;
                EncapsulateSubject(ref bounds, ref found, subject);
            }
            return found;
        }

        private static void EncapsulateSubject(
            ref Bounds bounds,
            ref bool found,
            Transform subject)
        {
            if (TryGetVisualBounds(subject, out Bounds visualBounds))
            {
                if (!found)
                {
                    bounds = visualBounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(visualBounds);
                }
                return;
            }

            Encapsulate(ref bounds, ref found, subject.position);
        }

        private static bool TryGetVisualBounds(
            Transform subject,
            out Bounds bounds)
        {
            bounds = default;
            if (!subject)
                return false;

            Renderer[] renderers = subject.GetComponentsInChildren<Renderer>(false);
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
            return found;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool found, Vector3 point)
        {
            if (!found)
            {
                bounds = new Bounds(point, Vector3.zero);
                found = true;
            }
            else
            {
                bounds.Encapsulate(point);
            }
        }

        private static Bounds ExpandBounds(Bounds bounds, float worldPadding)
        {
            float horizontalPadding = Mathf.Max(0f, worldPadding) * 2f;
            float verticalPadding = Mathf.Max(3f, worldPadding * .55f) * 2f;
            bounds.Expand(new Vector3(horizontalPadding, verticalPadding, horizontalPadding));
            return bounds;
        }

        private static bool IsVisibleSubject(Transform subject)
        {
            return subject && subject.gameObject.activeInHierarchy;
        }

        private bool IsAirborneDrone(Transform drone)
        {
            return IsVisibleSubject(drone) && drone.position.y > droneAirborneHeight;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void UpdateTakeoffDirector()
        {
            bool hasAirborneDrone = false;
            for (int i = 0; i < droneTargets.Length; i++)
            {
                if (!IsAirborneDrone(droneTargets[i]))
                    continue;
                hasAirborneDrone = true;
                break;
            }

            if (hasAirborneDrone && !hadAirborneDrone)
            {
                takeoffShotUntil = Time.time + Mathf.Max(0f, takeoffShotSeconds);
                if (mode == ViewMode.Action)
                {
                    actionDistance = 0f;
                    initialized = false;
                }
            }
            else if (!hasAirborneDrone)
            {
                takeoffShotUntil = -1f;
            }

            hadAirborneDrone = hasAirborneDrone;
        }

        private void ResetTrajectories()
        {
            trajectories.Clear();
            trajectoryStartedAt = Time.unscaledTime;
            trajectoryNextSampleTime = 0f;

            for (int i = 0; i < boatTargets.Length; i++)
                RegisterTrajectory(boatTargets[i], BoatTrajectoryColor(i));
            for (int i = 0; i < droneTargets.Length; i++)
                RegisterTrajectory(droneTargets[i], DroneTrajectoryColor(i));
            RegisterTrajectory(focusTarget, EnemyTrajectoryColor());
            SampleTrajectories(true);
        }

        private void RegisterTrajectory(Transform subject, Color color)
        {
            if (!subject || trajectories.ContainsKey(subject))
                return;

            trajectories.Add(
                subject,
                new TrajectorySeries
                {
                    label = IsEnemyName(subject.name) ? "TARGET" : subject.name,
                    color = color
                }
            );
        }

        private void UpdateTrajectoryData()
        {
            if (!showTacticalInset || Time.unscaledTime < trajectoryNextSampleTime)
                return;

            trajectoryNextSampleTime = Time.unscaledTime +
                Mathf.Max(.05f, trajectorySampleSeconds);
            SampleTrajectories(false);
        }

        private void SampleTrajectories(bool force)
        {
            if (!force && ShouldResetTrajectoryForTeleport())
            {
                foreach (TrajectorySeries existing in trajectories.Values)
                {
                    existing.points.Clear();
                    existing.distance = 0f;
                }
                trajectoryStartedAt = Time.unscaledTime;
                force = true;
            }

            foreach (KeyValuePair<Transform, TrajectorySeries> pair in trajectories)
            {
                Transform subject = pair.Key;
                if (!subject)
                    continue;

                TrajectorySeries series = pair.Value;
                Vector3 point = subject.position;
                if (series.points.Count > 0)
                {
                    Vector3 previous = series.points[series.points.Count - 1];
                    float moved = Vector3.Distance(previous, point);
                    if (!force && moved < Mathf.Max(.02f, trajectoryMinSampleDistance))
                        continue;
                    series.distance += moved;
                }

                series.points.Add(point);
                int maxSamples = Mathf.Max(60, trajectoryMaxSamplesPerAgent);
                if (series.points.Count > maxSamples)
                    series.points.RemoveAt(0);
            }
        }

        private bool ShouldResetTrajectoryForTeleport()
        {
            float threshold = Mathf.Max(8f, trajectoryResetJumpDistance);
            foreach (KeyValuePair<Transform, TrajectorySeries> pair in trajectories)
            {
                if (!pair.Key || pair.Value.points.Count == 0)
                    continue;
                Vector3 previous = pair.Value.points[pair.Value.points.Count - 1];
                if (Vector3.Distance(previous, pair.Key.position) > threshold)
                    return true;
            }
            return false;
        }

        private static Color BoatTrajectoryColor(int index)
        {
            return FriendlyTrajectoryColor();
        }

        private static Color DroneTrajectoryColor(int index)
        {
            return AirTrajectoryColor();
        }

        private static Color FriendlyTrajectoryColor()
        {
            // USV trails on the tactical inset — orange.
            return new Color(1f, .58f, .08f);
        }

        private static Color EnemyTrajectoryColor()
        {
            // Target vessel trail remains red.
            return new Color(1f, .18f, .14f);
        }

        private static Color AirTrajectoryColor()
        {
            // UAV trails on the tactical inset — cyan, distinct from USV orange.
            return new Color(.2f, .88f, 1f);
        }

        private static Color MissionRangeColor()
        {
            // Capture and defense formation ranges are blue.
            return new Color(.12f, .62f, 1f);
        }

        private void MoveCamera(Vector3 desiredPosition, Vector3 focusPoint)
        {
            if (!initialized)
            {
                transform.position = desiredPosition;
                initialized = true;
            }
            else
            {
                float positionT = 1f - Mathf.Exp(-positionSmooth * Time.deltaTime);
                transform.position = Vector3.Lerp(
                    transform.position,
                    desiredPosition,
                    positionT
                );
            }

            Quaternion desiredRotation = Quaternion.LookRotation(
                focusPoint - transform.position,
                Vector3.up
            );
            float rotationT = 1f - Mathf.Exp(-rotationSmooth * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationT);
        }

        private void SetMode(ViewMode nextMode)
        {
            if (mode == nextMode)
                return;

            mode = nextMode;
            initialized = false;
            if (mode == ViewMode.Action)
                actionDistance = 0f;
            else if (mode == ViewMode.Overview)
                overviewDistance = 0f;
            else if (mode == ViewMode.FreeOrbit)
            {
                Vector3 euler = transform.rotation.eulerAngles;
                orbitYaw = euler.y;
                orbitPitch = Mathf.Clamp(euler.x, 8f, 78f);
                orbitDistance = Mathf.Clamp(
                    Vector3.Distance(transform.position, GetSceneFocus()),
                    8f,
                    180f
                );
            }
        }

        private void OnGUI()
        {
            EnsureGuiStyles();
            float uiScale = RuntimeGuiScale();
            if (showAgentLabels)
                DrawAgentLabels();

            string modeName = mode == ViewMode.Action
                ? "ACTION"
                : mode == ViewMode.Overview
                    ? "GLOBAL"
                    : mode == ViewMode.BoatFollow
                        ? "USV FOLLOW"
                        : mode == ViewMode.DroneFollow
                            ? "UAV FOLLOW"
                            : "FREE ORBIT";
            float width = Mathf.Min(
                520f * uiScale,
                Mathf.Max(320f * uiScale, Screen.width - 32f * uiScale)
            );
            GUI.Box(
                new Rect(
                    Mathf.Max(16f * uiScale, Screen.width - width - 16f * uiScale),
                    Screen.height - 46f * uiScale,
                    width,
                    30f * uiScale
                ),
                "Camera " + modeName +
                "   C Action   1 Global   2 USV   3 UAV   4 Free   TAB Cycle",
                cameraHelpStyle
            );

            if (showTacticalInset && ShouldDrawTrajectoryInset())
                DrawTrajectoryStatistics();
        }

        private bool ShouldDrawTrajectoryInset()
        {
            if (!captureScenario)
                captureScenario = FindObjectOfType<MultiAgentCaptureDefenseScenario>();
            // Follow scenario V toggle: hide trajectories with green sensor rings.
            return !captureScenario || captureScenario.showDebugOverlays;
        }

        private void DrawTrajectoryStatistics()
        {
            if (Event.current.type != EventType.Repaint)
                return;

            EnsureTrajectoryPixel();
            Rect inset = TacticalGuiRect();
            GUI.Box(inset, GUIContent.none);
            GUI.Box(
                new Rect(inset.x, Mathf.Max(0f, inset.y - 24f), inset.width, 24f),
                "TRAJECTORY STATISTICS - ALL AGENTS",
                cameraHelpStyle
            );

            float statisticsWidth = inset.width >= 420f
                ? Mathf.Clamp(inset.width * .3f, 128f, 185f)
                : 0f;
            Rect plot = new Rect(
                inset.x + 8f,
                inset.y + 8f,
                Mathf.Max(40f, inset.width - statisticsWidth - 16f),
                Mathf.Max(40f, inset.height - 16f)
            );

            DrawSolidRect(plot, new Color(.015f, .045f, .075f, .96f));
            DrawTrajectoryGrid(plot);
            if (!TryGetTrajectoryBounds(out Vector2 worldMin, out Vector2 worldMax))
            {
                GUI.Label(plot, "WAITING FOR TRAJECTORY DATA", trajectoryTextStyle);
                return;
            }

            float padding = Mathf.Max(1f, trajectoryWorldPadding);
            worldMin -= Vector2.one * padding;
            worldMax += Vector2.one * padding;
            FitTrajectoryBoundsToPlot(plot, ref worldMin, ref worldMax);

            DrawFormationOverlays(plot, worldMin, worldMax);
            foreach (KeyValuePair<Transform, TrajectorySeries> pair in trajectories)
                DrawTrajectorySeries(plot, worldMin, worldMax, pair.Key, pair.Value);

            if (statisticsWidth > 0f)
            {
                Rect statistics = new Rect(
                    plot.xMax + 8f,
                    plot.y,
                    statisticsWidth - 8f,
                    plot.height
                );
                DrawTrajectoryLegend(statistics, false);
            }
            else
            {
                Rect legend = new Rect(
                    plot.x + 4f,
                    plot.yMax - Mathf.Min(46f, plot.height * .34f),
                    plot.width - 8f,
                    Mathf.Min(44f, plot.height * .32f)
                );
                DrawTrajectoryLegend(legend, true);
            }
        }

        private void DrawTrajectoryGrid(Rect plot)
        {
            Color grid = new Color(.25f, .58f, .72f, .18f);
            for (int i = 1; i < 5; i++)
            {
                float x = Mathf.Lerp(plot.x, plot.xMax, i / 5f);
                float y = Mathf.Lerp(plot.y, plot.yMax, i / 5f);
                DrawGuiLine(new Vector2(x, plot.y), new Vector2(x, plot.yMax), grid, 1f);
                DrawGuiLine(new Vector2(plot.x, y), new Vector2(plot.xMax, y), grid, 1f);
            }
        }

        private bool TryGetTrajectoryBounds(out Vector2 minimum, out Vector2 maximum)
        {
            minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            bool found = false;

            foreach (TrajectorySeries series in trajectories.Values)
            {
                for (int i = 0; i < series.points.Count; i++)
                {
                    Vector3 point = series.points[i];
                    Vector2 horizontal = new Vector2(point.x, point.z);
                    minimum = Vector2.Min(minimum, horizontal);
                    maximum = Vector2.Max(maximum, horizontal);
                    found = true;
                }
            }

            if (focusTarget && TryGetCaptureRadius(out float captureRadius))
            {
                Vector2 center = new Vector2(focusTarget.position.x, focusTarget.position.z);
                Vector2 radius = Vector2.one * captureRadius;
                minimum = Vector2.Min(minimum, center - radius);
                maximum = Vector2.Max(maximum, center + radius);
                found = true;
            }

            if (found && (maximum - minimum).sqrMagnitude < 1f)
            {
                minimum -= Vector2.one;
                maximum += Vector2.one;
            }
            return found;
        }

        private bool TryGetCaptureRadius(out float radius)
        {
            if (!captureScenario)
                captureScenario = FindObjectOfType<MultiAgentCaptureDefenseScenario>();
            radius = captureScenario ? Mathf.Max(.1f, captureScenario.captureRadius) : 0f;
            return captureScenario && radius > .1f;
        }

        private void DrawFormationOverlays(
            Rect plot,
            Vector2 worldMin,
            Vector2 worldMax
        )
        {
            if (!focusTarget || !TryGetCaptureRadius(out float captureRadius))
                return;

            DrawUsvEncirclement(plot, worldMin, worldMax, captureRadius);
            if (TryGetDefenseRadius(out float defenseRadius))
                DrawUavTriangle(plot, worldMin, worldMax, defenseRadius);
        }

        private bool TryGetDefenseRadius(out float radius)
        {
            if (!captureScenario)
                captureScenario = FindObjectOfType<MultiAgentCaptureDefenseScenario>();
            radius = captureScenario ? Mathf.Max(.1f, captureScenario.defenseRadius) : 0f;
            return captureScenario && radius > .1f;
        }

        private void DrawUsvEncirclement(
            Rect plot,
            Vector2 worldMin,
            Vector2 worldMax,
            float radius
        )
        {
            Vector3 center = focusTarget.position;
            Color ringColor = MissionRangeColor();
            ringColor.a = .82f;
            float readinessSum = 0f;
            int valid = 0;

            for (int i = 0; i < boatTargets.Length; i++)
            {
                Transform boat = boatTargets[i];
                if (!boat)
                    continue;

                float readiness = RadialFormationReadiness(
                    boat,
                    center,
                    radius,
                    Mathf.Max(14f, radius * 1.75f)
                );
                readinessSum += readiness;
                valid++;
                if (readiness <= .01f)
                    continue;

                Vector3 offset = boat.position - center;
                float bearing = Mathf.Atan2(offset.z, offset.x);
                float halfArc = Mathf.Deg2Rad * 60f * readiness;
                DrawWorldArc(
                    plot,
                    worldMin,
                    worldMax,
                    center,
                    radius,
                    bearing - halfArc,
                    bearing + halfArc,
                    ringColor,
                    1.7f
                );
            }

            float progress = valid > 0 ? readinessSum / valid : 0f;
            if (progress <= .01f)
                return;

            Vector3 labelWorld = center + Vector3.right * radius;
            Vector2 labelPoint = TrajectoryToGui(plot, worldMin, worldMax, labelWorld);
            DrawFormationLabel(
                new Rect(labelPoint.x + 4f, labelPoint.y - 12f, 128f, 18f),
                $"围捕圈 {progress * 100f:0}%",
                ringColor
            );
        }

        private void DrawUavTriangle(
            Rect plot,
            Vector2 worldMin,
            Vector2 worldMax,
            float radius
        )
        {
            int count = Mathf.Min(3, droneTargets.Length);
            if (count < 3)
                return;

            Vector3 center = focusTarget.position;
            float readinessSum = 0f;
            for (int i = 0; i < count; i++)
            {
                Transform drone = droneTargets[i];
                if (!drone || drone.position.y < droneAirborneHeight)
                {
                    formationReadiness[i] = 0f;
                    continue;
                }

                formationReadiness[i] = RadialFormationReadiness(
                    drone,
                    center,
                    radius,
                    Mathf.Max(18f, radius)
                );
                readinessSum += formationReadiness[i];
            }

            float progress = readinessSum / count;
            if (progress <= .01f)
                return;

            Color triangleColor = MissionRangeColor();
            triangleColor.a = .88f;
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                if (!droneTargets[i] || !droneTargets[next])
                    continue;
                float edgeProgress = Mathf.Min(
                    formationReadiness[i],
                    formationReadiness[next]
                );
                if (edgeProgress <= .01f)
                    continue;

                Vector2 from = TrajectoryToGui(
                    plot,
                    worldMin,
                    worldMax,
                    droneTargets[i].position
                );
                Vector2 to = TrajectoryToGui(
                    plot,
                    worldMin,
                    worldMax,
                    droneTargets[next].position
                );
                DrawGuiLine(from, Vector2.Lerp(from, to, edgeProgress), triangleColor, 1.8f);
            }

            Vector3 labelWorld = center - Vector3.right * radius;
            Vector2 labelPoint = TrajectoryToGui(plot, worldMin, worldMax, labelWorld);
            DrawFormationLabel(
                new Rect(labelPoint.x - 126f, labelPoint.y - 12f, 126f, 18f),
                $"护航环 {progress * 100f:0}%",
                triangleColor
            );
        }

        private static float RadialFormationReadiness(
            Transform subject,
            Vector3 center,
            float radius,
            float tolerance
        )
        {
            Vector3 delta = subject.position - center;
            delta.y = 0f;
            float radialError = Mathf.Abs(delta.magnitude - radius);
            float raw = 1f - radialError / Mathf.Max(1f, tolerance);
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(raw));
        }

        private void DrawWorldArc(
            Rect plot,
            Vector2 worldMin,
            Vector2 worldMax,
            Vector3 center,
            float radius,
            float startAngle,
            float endAngle,
            Color color,
            float width
        )
        {
            int segments = Mathf.Max(2, Mathf.CeilToInt((endAngle - startAngle) * 12f));
            Vector2 previous = Vector2.zero;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(startAngle, endAngle, i / (float)segments);
                Vector3 world = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y,
                    center.z + Mathf.Sin(angle) * radius
                );
                Vector2 current = TrajectoryToGui(plot, worldMin, worldMax, world);
                if (i > 0)
                    DrawGuiLine(previous, current, color, width);
                previous = current;
            }
        }

        private void DrawFormationLabel(Rect rect, string text, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.Label(rect, text, trajectoryTextStyle);
            GUI.color = previousColor;
        }

        private static void FitTrajectoryBoundsToPlot(
            Rect plot,
            ref Vector2 minimum,
            ref Vector2 maximum
        )
        {
            Vector2 size = maximum - minimum;
            size.x = Mathf.Max(1f, size.x);
            size.y = Mathf.Max(1f, size.y);
            float worldAspect = size.x / size.y;
            float plotAspect = Mathf.Max(.1f, plot.width / plot.height);
            Vector2 center = (minimum + maximum) * .5f;

            if (worldAspect > plotAspect)
                size.y = size.x / plotAspect;
            else
                size.x = size.y * plotAspect;

            minimum = center - size * .5f;
            maximum = center + size * .5f;
        }

        private void DrawTrajectorySeries(
            Rect plot,
            Vector2 worldMin,
            Vector2 worldMax,
            Transform subject,
            TrajectorySeries series
        )
        {
            if (series.points.Count == 0)
                return;

            int segmentBudget = Mathf.Max(24, trajectoryDrawSegmentsPerAgent);
            int stride = Mathf.Max(
                1,
                Mathf.CeilToInt((series.points.Count - 1) / (float)segmentBudget)
            );
            int fromIndex = 0;
            while (fromIndex < series.points.Count - 1)
            {
                int toIndex = Mathf.Min(fromIndex + stride, series.points.Count - 1);
                Vector2 from = TrajectoryToGui(
                    plot,
                    worldMin,
                    worldMax,
                    series.points[fromIndex]
                );
                Vector2 to = TrajectoryToGui(
                    plot,
                    worldMin,
                    worldMax,
                    series.points[toIndex]
                );
                DrawGuiLine(from, to, series.color, 2f);
                fromIndex = toIndex;
            }

            Vector3 current = subject
                ? subject.position
                : series.points[series.points.Count - 1];
            Vector2 marker = TrajectoryToGui(plot, worldMin, worldMax, current);
            DrawSolidRect(
                new Rect(marker.x - 3.5f, marker.y - 3.5f, 7f, 7f),
                series.color
            );
            DrawTrajectoryHeading(marker, subject, series.color);
            if (plot.width >= 190f)
            {
                Color previous = GUI.color;
                GUI.color = series.color;
                GUI.Label(
                    new Rect(marker.x + 3f, marker.y - 10f, 34f, 18f),
                    ShortTrajectoryLabel(series.label),
                    trajectoryTextStyle
                );
                GUI.color = previous;
            }
        }

        private void DrawTrajectoryHeading(
            Vector2 marker,
            Transform subject,
            Color color
        )
        {
            if (!subject)
                return;

            Vector3 worldHeading = IsUsvName(subject.name)
                ? subject.right
                : subject.forward;
            Vector2 heading = new Vector2(worldHeading.x, -worldHeading.z);
            if (heading.sqrMagnitude < .001f)
                return;

            heading.Normalize();
            Vector2 tip = marker + heading * 13f;
            DrawGuiLine(marker, tip, color, 2f);
            Vector2 side = new Vector2(-heading.y, heading.x);
            DrawGuiLine(tip, tip - heading * 4f + side * 3f, color, 2f);
            DrawGuiLine(tip, tip - heading * 4f - side * 3f, color, 2f);
        }

        private static Vector2 TrajectoryToGui(
            Rect plot,
            Vector2 worldMin,
            Vector2 worldMax,
            Vector3 point
        )
        {
            float x = Mathf.InverseLerp(worldMin.x, worldMax.x, point.x);
            float y = Mathf.InverseLerp(worldMin.y, worldMax.y, point.z);
            return new Vector2(
                Mathf.Lerp(plot.x, plot.xMax, x),
                Mathf.Lerp(plot.yMax, plot.y, y)
            );
        }

        private void DrawTrajectoryLegend(Rect rect, bool compact)
        {
            DrawSolidRect(rect, new Color(.01f, .03f, .05f, compact ? .78f : .9f));
            float elapsed = Mathf.Max(0f, Time.unscaledTime - trajectoryStartedAt);
            int minutes = Mathf.FloorToInt(elapsed / 60f);
            int seconds = Mathf.FloorToInt(elapsed) % 60;
            if (compact)
            {
                string text = $"TIME {minutes:00}:{seconds:00}   ";
                foreach (TrajectorySeries series in trajectories.Values)
                    text += $"{ShortTrajectoryLabel(series.label)} {FormatDistance(series.distance)}   ";
                GUI.Label(rect, text, trajectoryTextStyle);
                return;
            }

            GUI.Label(
                new Rect(rect.x, rect.y, rect.width, 23f),
                $"TIME  {minutes:00}:{seconds:00}",
                trajectoryTextStyle
            );
            float y = rect.y + 24f;
            foreach (TrajectorySeries series in trajectories.Values)
            {
                DrawSolidRect(new Rect(rect.x + 7f, y + 5f, 12f, 3f), series.color);
                GUI.Label(
                    new Rect(rect.x + 22f, y - 3f, rect.width - 24f, 18f),
                    $"{series.label}  {FormatDistance(series.distance)}",
                    trajectoryTextStyle
                );
                y += 18f;
                if (y > rect.yMax - 16f)
                    break;
            }
        }

        private static string ShortTrajectoryLabel(string label)
        {
            if (label == "TARGET")
                return "T";
            if (IsUsvName(label))
                return "S" + AgentNumber(label);
            if (IsUavName(label))
                return "A" + AgentNumber(label);
            return label;
        }

        private static string FormatDistance(float distance)
        {
            return distance >= 1000f
                ? $"{distance / 1000f:0.00}km"
                : $"{distance:0}m";
        }

        private void EnsureTrajectoryPixel()
        {
            if (trajectoryPixel)
                return;

            trajectoryPixel = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "Trajectory Plot Pixel",
                hideFlags = HideFlags.HideAndDontSave
            };
            trajectoryPixel.SetPixel(0, 0, Color.white);
            trajectoryPixel.Apply();
        }

        private void DrawSolidRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, trajectoryPixel);
            GUI.color = previous;
        }

        private void DrawGuiLine(Vector2 from, Vector2 to, Color color, float width)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < .1f)
                return;

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            GUI.DrawTexture(new Rect(from.x, from.y - width * .5f, length, width), trajectoryPixel);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private Rect TacticalGuiRect()
        {
            return new Rect(
                tacticalViewport.x * Screen.width,
                (1f - tacticalViewport.y - tacticalViewport.height) * Screen.height,
                tacticalViewport.width * Screen.width,
                tacticalViewport.height * Screen.height
            );
        }

        private void DrawAgentLabels()
        {
            float uiScale = RuntimeGuiScale();
            Camera camera = attachedCamera ? attachedCamera : GetComponent<Camera>();
            if (!camera || groupTargets == null)
                return;

            for (int i = 0; i < groupTargets.Length; i++)
            {
                Transform subject = groupTargets[i];
                if (!IsVisibleSubject(subject))
                    continue;

                string objectName = subject.name;
                bool isUav = IsUavName(objectName);
                bool isTarget = IsEnemyName(objectName);
                bool isFriendlyShip = objectName == "friendly_ship";
                float fallbackHeight = isUav ? 2f : isTarget ? 4.2f : 4.6f;
                Vector3 worldPoint = subject.position + Vector3.up * fallbackHeight;
                if (TryGetVisualBounds(subject, out Bounds visualBounds))
                {
                    float padding = isUav ? .45f : .75f;
                    worldPoint = new Vector3(
                        visualBounds.center.x,
                        visualBounds.max.y + padding,
                        visualBounds.center.z
                    );
                }
                Vector3 viewport = camera.WorldToViewportPoint(worldPoint);
                if (viewport.z <= 0f || viewport.x < .025f || viewport.x > .975f ||
                    viewport.y < .04f || viewport.y > .96f)
                    continue;

                Vector3 screen = camera.WorldToScreenPoint(worldPoint);
                string rolePrefix = captureScenario && captureScenario.DefenseEscortActive
                    ? "防卫 "
                    : "我方 ";
                string label = isTarget
                    ? "敌方 " + objectName
                    : isFriendlyShip
                        ? "保护 " + objectName
                    : rolePrefix + objectName;
                GUIStyle style = isTarget
                    ? targetLabelStyle
                    : isUav ? uavLabelStyle : usvLabelStyle;
                float labelWidth = 108f * uiScale;
                float labelHeightPixels = 26f * uiScale;
                float x = screen.x - labelWidth * .5f;
                float y = Screen.height - screen.y - labelHeightPixels * .5f;
                GUI.Box(new Rect(x, y, labelWidth, labelHeightPixels), label, style);
            }
        }

        private static bool IsUsvName(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   (value.StartsWith("USV-") || value.StartsWith("usv_"));
        }

        private static bool IsUavName(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   (value.StartsWith("UAV-") || value.StartsWith("uav_"));
        }

        private static bool IsEnemyName(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   (value.Contains("Target") || value == "enemy_ship");
        }

        private static string AgentNumber(string value)
        {
            int separator = value.LastIndexOfAny(new[] { '-', '_' });
            return separator >= 0 && separator + 1 < value.Length
                ? value.Substring(separator + 1)
                : value;
        }

        private void EnsureGuiStyles()
        {
            float uiScale = RuntimeGuiScale();
            if (usvLabelStyle != null &&
                Mathf.Approximately(guiStyleScale, uiScale))
                return;

            guiStyleScale = uiScale;
            // 3D nameplates: friendly / hostile tags (trajectory colors stay unchanged).
            usvLabelStyle = CreateLabelStyle(
                new Color(1f, .82f, .35f),
                uiScale
            );
            uavLabelStyle = CreateLabelStyle(
                new Color(.45f, .88f, 1f),
                uiScale
            );
            targetLabelStyle = CreateLabelStyle(
                new Color(.92f, .92f, .95f),
                uiScale
            );
            cameraHelpStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(12f * uiScale),
                normal = { textColor = new Color(.9f, .96f, 1f) }
            };
            trajectoryTextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = Mathf.RoundToInt(11f * uiScale),
                padding = new RectOffset(7, 5, 5, 4),
                wordWrap = true,
                normal = { textColor = new Color(.86f, .94f, 1f) }
            };
        }

        private static GUIStyle CreateLabelStyle(Color color, float uiScale)
        {
            return new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(13f * uiScale),
                fontStyle = FontStyle.Bold,
                normal = { textColor = color }
            };
        }

        private static float RuntimeGuiScale()
        {
            // Preserve the former 1080p size, but compensate gently when a 4K
            // Game target is downscaled inside the editor. The 1.35 cap keeps
            // labels readable without returning to the oversized UI variant.
            return Mathf.Clamp(
                Mathf.Sqrt(Screen.height / 1080f),
                1f,
                1.35f
            );
        }

        private void OnDestroy()
        {
            if (trajectoryPixel)
                Destroy(trajectoryPixel);
        }
    }
}
