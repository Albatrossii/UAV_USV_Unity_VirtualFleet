using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    public sealed class MultiAgentCaptureDefenseScenario : MonoBehaviour
    {
        public Transform shoreBase;
        public Transform[] dronePads;
        public Transform targetPoint;
        public Transform targetVessel;
        public Transform dynamicBarrier;
        public Transform[] obstacles;
        public Transform[] boats;
        public Transform[] drones;
        public DroneVisual[] droneVisuals;
        public AgentSensorSuite[] boatSensors;
        public AgentSensorSuite[] droneSensors;
        public ShoreBaseController baseController;
        public Transform coastlineCollisionRoot;
        public bool automatic = true;
        public float boatSpeed = 8.5f;
        public float searchBoatSpeed = 7f;
        public float droneSpeed = 11f;
        public float droneAltitude = 8f;
        public float captureRadius = 18f;
        public float defenseRadius = 30f;
        public float searchStartRadius = 72f;
        public float sensorRange = 38f;
        public float barrierSpeed = 4f;
        public float dispatchDelaySeconds = 0f;
        public float boatOrbitAngularSpeed = 0f;
        public float dronePatrolAngularSpeed = 0f;
        public float agentSeparation = 10f;
        public float droneSeparation = 10f;
        public float targetEscapeSpeed = 2.2f;
        public float targetPatrolSpeed = .5f;
        public float escapeArenaRadius = 16f;
        public float waterSafetyMargin = 4f;
        public float barrierDemoSeconds = 6f;
        public int barrierDemoPasses = 1;
        public float holdDistance = 3.5f;
        [Tooltip("Minimum center distance between USV and target freighter (no hull overlap).")]
        public float boatTargetClearance = 9.5f;
        public float boatCloseDuration = 22f;
        public float droneApproachDelay = 3f;
        public float droneTakeoffDuration = 5f;
        public float droneTakeoffClimbSpeed = 1.8f;
        public float droneTakeoffStagger = .75f;
        public float searchForceContactSeconds = 50f;
        [Tooltip("During search, report immediately when the target enters this sensor range.")]
        public float searchAcquireRange = 52f;
        public float missionTimeLimitSeconds = 100f;
        public bool showDebugOverlays = true;
        [Tooltip("Local demo hold when no ShoreBaseController is present.")]
        public float localReportHoldSeconds = 3.2f;
        public float localOrderHoldSeconds = 2.4f;

        [Header("Escort guard (护航守卫5)")]
        [Tooltip("After capture, redeploy around shore with blocker + guard/escort arcs. Capture itself stays equilateral.")]
        public bool useEscortGuardFormation = true;
        [Tooltip("Blocker distance = clip(ratio * own→threat, rMin, rMax).")]
        public float blockerRatio = 0.38f;
        public float blockerRMin = 14f;
        public float blockerRMax = 18f;
        [Tooltip("Half-angle of the threat-facing USV guard arc (degrees).")]
        public float guardArcHalfAngleDeg = 28f;
        public float minimumGuardSpacing = 10f;
        [Tooltip("Clearance between guard arc and UAV escort arc (degrees).")]
        public float escortClearanceDeg = 20f;
        [Tooltip("Virtual threat distance beyond the target along escape/approach axis (capture phase).")]
        public float escortThreatDistance = 48f;
        [Tooltip("Seconds to hold the capture lock before switching to shore escort-defense.")]
        public float defenseStartDelaySeconds = 5f;
        [Tooltip("USV guard-arc radius around the protected shore asset.")]
        public float defenseGuardRadius = 20f;
        [Tooltip("UAV escort-arc radius around the protected shore asset.")]
        public float defenseEscortRadius = 32f;
        public float defenseThreatApproachSpeed = 1.6f;
        public float defenseTimeLimitSeconds = 55f;

        [Header("Motion stability")]
        public float surfaceMaxTurnRate = 48f;
        public float surfaceAcceleration = 5f;
        public float surfaceTurnSlowdownAngle = 70f;
        public float airMaxTurnRate = 90f;
        public float airAcceleration = 9f;
        public float peerCorrectionSpeed = 2.5f;
        public float detourCommitSeconds = 3f;

        private readonly List<LineRenderer> commandLinks = new List<LineRenderer>();
        private readonly List<LineRenderer> sensorRings = new List<LineRenderer>();
        private readonly List<LineRenderer> tracks = new List<LineRenderer>();
        private readonly List<List<Vector3>> trackPoints = new List<List<Vector3>>();
        private LineRenderer captureRing;
        private LineRenderer defenseRing;
        private LineRenderer detectionLockLine;
        private readonly List<LineRenderer> scanCueLines = new List<LineRenderer>();
        private float detectionCueUntil = -1f;
        private int detectionBoatIndex = -1;
        private string detectionModeText = "USV 激光/雷达扇区搜索中";
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle buttonStyle;
        private float guiStyleScale = -1f;
        // Keep the mission inside the open Sydney center channel (water).
        private Vector3 targetCenterEnu = new Vector3(42f, -22f, 0f);
        private Vector3 targetVelocityEnu;
        private Vector3 barrierStartPosition;
        private Vector3 barrierOriginPosition;
        private bool barrierOriginInitialized;
        private Collider[] coastlineColliders;
        private float scenarioStarted;
        private float captureStarted = -1f;
        private float contactReportedAt = -1f;
        private string phase = "① 搜索中 — USV 接近探测";
        private string detectReporter = "-";
        private bool targetDetected;
        private bool captureAuthorized;
        private bool dronesAirborne;
        private bool dronesTakingOff;
        private float droneTakeoffStarted = -1f;
        private bool captureReady;
        private bool formationHolding;
        private bool captureComplete;
        private float captureCompleteAt = -1f;
        private bool defenseEscortActive;
        private bool defenseComplete;
        private float defenseStartedAt = -1f;
        private Vector3 protectedOwnCenter;
        private int activeAvoidanceCount;
        private Vector3 lockedTargetPosition;
        private Vector3[] boatDetours;
        private float[] boatDetourUntil;
        private Transform[] boatBypassObstacle;
        private int[] boatBypassSign;
        private float[] boatBypassUntil;
        private readonly Dictionary<Transform, float> surfaceSpeedState =
            new Dictionary<Transform, float>();
        private readonly Dictionary<Transform, Vector3> airVelocityState =
            new Dictionary<Transform, Vector3>();

        // Equilateral triangle on capture ring / defense ring (120° / 60° offset).
        // Escort-guard blocker + arcs are used only in the post-capture defense phase.
        private static readonly float[] BoatApproachAngles = { 0f, 120f, 240f };
        private static readonly float[] DroneDefenseAngles = { 60f, 180f, 300f };
        // Keep-out around buoys/barriers — large enough to clear hulls, small enough not to choke the channel.
        private const float SurfaceObstacleClearance = 4.2f;
        private const float AirObstacleClearance = 5f;

        private bool escortGuardPlanned;
        private Vector3 escortThreatDir = Vector3.forward;
        private int coreBoatIndex = -1;
        private int[] boatWingSlotByIndex; // boat → wing slot, or -1
        private int[] droneEscortSlotByIndex; // drone → escort slot, or -1
        private Vector3[] escortSlotOffsets; // relative XZ offsets for UAV escort arc
        private float plannedBoatRingRadius = 18f;
        private float plannedDroneRingRadius = 30f;
        private LineRenderer guardArcLine;
        private LineRenderer blockerMarker;

        public string Status => phase;
        public bool CaptureReady => captureReady;
        public bool FormationHolding => formationHolding;
        public bool DefenseEscortActive => defenseEscortActive;
        public bool DefenseComplete => defenseComplete;
        public float MissionElapsed =>
            captureStarted >= 0f ? Time.time - captureStarted : 0f;

        private float[] boatProgressStamp;
        private Vector3[] boatProgressPos;
        private int[] patrolWaypointIndex;
        private Vector3[][] patrolRoutes;

        public void SetCoastlineCollisionRoot(Transform root)
        {
            coastlineCollisionRoot = root;
            coastlineColliders = root
                ? root.GetComponentsInChildren<Collider>(true)
                : null;
        }

        private void Start()
        {
            scenarioStarted = Time.time;
            targetVelocityEnu = new Vector3(.22f, -.08f, 0f);
            if (coastlineCollisionRoot && (coastlineColliders == null || coastlineColliders.Length == 0))
                SetCoastlineCollisionRoot(coastlineCollisionRoot);
            BuildLineVisuals();
            ResetScenario();
        }

        public void NotifyBaseDispatch()
        {
            if (!targetDetected)
            {
                targetDetected = true;
                detectReporter = "base order";
                if (contactReportedAt < 0f)
                    contactReportedAt = Time.time;
            }

            captureAuthorized = true;
            if (captureStarted < 0f)
                captureStarted = Time.time;
            phase = "④ 围捕执行 — 岸基站已下令";
            // Takeoff is paced in Update after the order is visible.
        }

        public void ResetScenario()
        {
            scenarioStarted = Time.time;
            captureStarted = -1f;
            contactReportedAt = -1f;
            targetVelocityEnu = new Vector3(.22f, -.08f, 0f);
            targetDetected = false;
            captureAuthorized = false;
            dronesAirborne = false;
            dronesTakingOff = false;
            droneTakeoffStarted = -1f;
            captureReady = false;
            formationHolding = false;
            captureComplete = false;
            captureCompleteAt = -1f;
            defenseEscortActive = false;
            defenseComplete = false;
            defenseStartedAt = -1f;
            protectedOwnCenter = Vector3.zero;
            activeAvoidanceCount = 0;
            detectReporter = "-";
            detectionCueUntil = -1f;
            detectionBoatIndex = -1;
            detectionModeText = "三艘 USV 分区巡逻，尚未发现目标";
            ClearDetours();
            ClearEscortGuardPlan();

            surfaceSpeedState.Clear();
            airVelocityState.Clear();
            if (dynamicBarrier)
            {
                if (!barrierOriginInitialized)
                {
                    barrierOriginPosition = dynamicBarrier.position;
                    barrierOriginInitialized = true;
                }
                barrierStartPosition = barrierOriginPosition;
                // Put the obstacle on its path before the first rendered frame.
                dynamicBarrier.position = barrierStartPosition + new Vector3(-8f, 0f, 0f);
            }
            phase = "① 搜索中 — 三艘 USV 分区巡逻";
            SetTargetPose(Coordinates.ToUnity(targetCenterEnu.x, targetCenterEnu.y, .38f));

            Vector2 center = new Vector2(targetCenterEnu.x, targetCenterEnu.y);
            // Search starts are outside the visible sensor coverage of the target.
            Vector2[] preferredBoatStarts =
            {
                new Vector2(104f, 24f),
                new Vector2(-16f, 38f),
                new Vector2(-14f, -64f)
            };
            BuildSearchPatrolRoutes(preferredBoatStarts, center);
            for (int i = 0; boats != null && i < boats.Length; i++)
            {
                if (!boats[i])
                    continue;

                Vector2 start = i < preferredBoatStarts.Length
                    ? preferredBoatStarts[i]
                    : PointAround(center, 40f + i * 110f, searchStartRadius);
                start = ClampPatrolToOpenChannel(start, center);
                boats[i].position = Coordinates.ToUnity(start.x, start.y, .42f);
                Vector3 look = CurrentPatrolWaypoint(i);
                FaceToward(boats[i], look.sqrMagnitude > .01f ? look : Coordinates.ToUnity(center.x, center.y, .42f));
            }

            ParkDronesOnPads();

            for (int i = 0; i < trackPoints.Count; i++)
                trackPoints[i].Clear();

            EnsureProgressBuffers();
            for (int i = 0; boatProgressStamp != null && i < boatProgressStamp.Length; i++)
            {
                boatProgressStamp[i] = Time.time;
                boatProgressPos[i] = boats != null && i < boats.Length && boats[i] ? boats[i].position : Vector3.zero;
            }

            if (baseController)
                baseController.ResetMission();
        }

        private void EnsureProgressBuffers()
        {
            int count = boats != null ? boats.Length : 0;
            if (boatProgressStamp != null && boatProgressStamp.Length == count &&
                boatDetours != null && boatDetours.Length == count &&
                boatBypassSign != null && boatBypassSign.Length == count)
                return;
            boatProgressStamp = new float[count];
            boatProgressPos = new Vector3[count];
            boatDetours = new Vector3[count];
            boatDetourUntil = new float[count];
            boatBypassObstacle = new Transform[count];
            boatBypassSign = new int[count];
            boatBypassUntil = new float[count];
        }

        private void ClearDetours()
        {
            EnsureProgressBuffers();
            for (int i = 0; i < boatDetourUntil.Length; i++)
            {
                boatDetourUntil[i] = -1f;
                boatDetours[i] = Vector3.zero;
                boatBypassObstacle[i] = null;
                boatBypassSign[i] = 0;
                boatBypassUntil[i] = -1f;
            }
        }

        private int FindBoatIndex(Transform boat)
        {
            if (!boat || boats == null)
                return -1;
            for (int i = 0; i < boats.Length; i++)
            {
                if (boats[i] == boat)
                    return i;
            }
            return -1;
        }

        private static Vector2 PointAround(Vector2 center, float angleDegrees, float radius)
        {
            float angle = angleDegrees * Mathf.Deg2Rad;
            return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.M))
                automatic = !automatic;
            if (Input.GetKeyDown(KeyCode.R))
                ResetScenario();
            if (Input.GetKeyDown(KeyCode.V))
                showDebugOverlays = !showDebugOverlays;

            AnimateBarrier();
            if (!captureComplete)
                AnimateTarget();
            activeAvoidanceCount = 0;

            if (automatic && Time.time - scenarioStarted >= dispatchDelaySeconds)
            {
                float missionElapsed = Time.time - scenarioStarted;

                // Hard cap for capture only; defense has its own budget after that.
                if (!captureComplete && missionElapsed >= missionTimeLimitSeconds)
                    ForceCaptureSuccess("time limit");

                if (captureComplete)
                {
                    TickPostCaptureDefense();
                }
                else if (!targetDetected)
                {
                    // Three-sector patrol only; shrink/capture begins after base order.
                    if (missionElapsed > searchForceContactSeconds)
                        ForceTargetContact("search timeout");

                    if (!targetDetected)
                    {
                        DriveSearchPatrol();
                        phase = "① 搜索中 — 三艘 USV 分区巡逻，等待目标进入探测范围";
                        TryDetectTarget();
                    }
                }
                else if (!captureAuthorized)
                {
                    // Stop and face target while reporting/ordering.
                    HoldBoatsAfterContact();
                    TickLocalAuthorization();
                    if (baseController)
                        phase = baseController.status;
                    else if (contactReportedAt >= 0f &&
                             Time.time - contactReportedAt < localReportHoldSeconds)
                        phase = "② 发现目标 — " + detectReporter + " 上报岸基站";
                    else
                        phase = "③ 岸基站下令 — 执行围捕与起飞";
                }
                else
                {
                    if (!dronesAirborne && captureStarted >= 0f &&
                        Time.time - captureStarted >= droneApproachDelay)
                        LaunchDrones();

                    bool boatsHolding = DriveBoatsCapture();
                    bool dronesHolding = DriveDronesDefense();
                    captureReady = boatsHolding;
                    formationHolding = boatsHolding && dronesHolding;

                    if (formationHolding)
                        ForceCaptureSuccess("triangle locked");
                    else if (!boatsHolding)
                        phase = "④ 围捕执行 — USV 缩圈成三角";
                    else if (!dronesAirborne)
                        phase = "④ 围捕执行 — UAV 待命护航起飞";
                    else if (dronesTakingOff)
                        phase = "④ 围捕执行 — UAV 岸基起飞护航";
                    else
                        phase = "④ 围捕执行 — UAV 加入护航环";
                }
            }

            UpdateLineVisuals();
            UpdateDetectionVisuals();
            UpdateMissionRings();
            UpdateTracks();
            ApplyOverlayVisibility();
            if (showDebugOverlays)
                PulseSensorScans();
            // Defense lock: do not keep nudging boats after success.
            if (!defenseComplete && (!captureComplete || defenseEscortActive))
                ResolveBoatTargetClearance();
            if (!defenseComplete)
                ResolveWorldCollisions();
        }

        private void PulseSensorScans()
        {
            // Keep lidar/radar debug rays alive for demo while overlays are on.
            if (boatSensors != null)
            {
                for (int i = 0; i < boatSensors.Length; i++)
                {
                    if (boatSensors[i])
                        boatSensors[i].Scan();
                }
            }
            if (dronesAirborne && droneSensors != null)
            {
                for (int i = 0; i < droneSensors.Length; i++)
                {
                    if (droneSensors[i])
                        droneSensors[i].Scan();
                }
            }
        }

        private void DriveSearchPatrol()
        {
            if (boats == null)
                return;

            EnsurePatrolState();
            Vector2 mapCenter = new Vector2(targetCenterEnu.x, targetCenterEnu.y);
            for (int i = 0; i < boats.Length; i++)
            {
                Transform boat = boats[i];
                if (!boat)
                    continue;

                Vector3 waypoint = CurrentPatrolWaypoint(i);
                if (HorizontalDistance(boat.position, waypoint) <= holdDistance + 1.4f)
                    AdvancePatrolWaypoint(i);

                waypoint = CurrentPatrolWaypoint(i);
                Vector3 enu = Coordinates.ToEnu(waypoint);
                Vector2 safe = ClampPatrolToOpenChannel(new Vector2(enu.x, enu.y), mapCenter);
                waypoint = Coordinates.ToUnity(safe.x, safe.y, .42f);

                waypoint = CommitDetourIfBlocked(boat, i, waypoint);
                waypoint = UnstickIfNeeded(boat, i, waypoint);
                waypoint = SoftAvoidPeers(boat, waypoint, boats, agentSeparation);
                if (targetPoint && HorizontalDistance(boat.position, targetPoint.position) < boatTargetClearance + 6f)
                    waypoint = KeepClearOfTargetHull(waypoint);
                waypoint = ClampToWater(waypoint, .42f);
                MoveSurfaceAgent(boat, waypoint, searchBoatSpeed);
                TrackBoatProgress(boat, i, waypoint);
            }

            EnforcePeerClearance(boats, agentSeparation * .9f, true);
            TryDetectTargetByProximity();
        }

        private void BuildSearchPatrolRoutes(Vector2[] starts, Vector2 mapCenter)
        {
            int count = boats != null ? boats.Length : 0;
            patrolRoutes = new Vector3[count][];
            patrolWaypointIndex = new int[count];

            Vector2[][] enuLoops =
            {
                new[]
                {
                    new Vector2(104f, 24f), new Vector2(92f, 18f), new Vector2(78f, 8f),
                    new Vector2(64f, -4f), new Vector2(52f, -14f), new Vector2(48f, -20f)
                },
                new[]
                {
                    new Vector2(-16f, 38f), new Vector2(2f, 38f), new Vector2(18f, 30f),
                    new Vector2(30f, 18f), new Vector2(38f, 4f), new Vector2(40f, -8f)
                },
                new[]
                {
                    new Vector2(-14f, -64f), new Vector2(0f, -60f), new Vector2(14f, -52f),
                    new Vector2(26f, -42f), new Vector2(36f, -34f), new Vector2(42f, -28f)
                }
            };

            for (int i = 0; i < count; i++)
            {
                patrolWaypointIndex[i] = 0;
                Vector2[] loop = i < enuLoops.Length
                    ? enuLoops[i]
                    : new[]
                    {
                        starts[Mathf.Min(i, starts.Length - 1)] + new Vector2(16f, 0f),
                        starts[Mathf.Min(i, starts.Length - 1)] + new Vector2(0f, 12f),
                        starts[Mathf.Min(i, starts.Length - 1)] + new Vector2(-14f, 0f),
                        starts[Mathf.Min(i, starts.Length - 1)] + new Vector2(0f, -12f)
                    };
                var points = new Vector3[loop.Length];
                for (int w = 0; w < loop.Length; w++)
                {
                    Vector2 water = ClampPatrolToOpenChannel(loop[w], mapCenter);
                    points[w] = Coordinates.ToUnity(water.x, water.y, .42f);
                }

                patrolRoutes[i] = points;
            }
        }

        private void EnsurePatrolState()
        {
            int count = boats != null ? boats.Length : 0;
            if (patrolRoutes != null && patrolRoutes.Length == count &&
                patrolWaypointIndex != null && patrolWaypointIndex.Length == count)
                return;

            Vector2 center = new Vector2(targetCenterEnu.x, targetCenterEnu.y);
            BuildSearchPatrolRoutes(
                new[] { new Vector2(104f, 24f), new Vector2(-16f, 38f), new Vector2(-14f, -64f) },
                center);
        }

        private Vector3 CurrentPatrolWaypoint(int boatIndex)
        {
            EnsurePatrolState();
            if (boatIndex < 0 || boatIndex >= patrolRoutes.Length ||
                patrolRoutes[boatIndex] == null || patrolRoutes[boatIndex].Length == 0)
            {
                return boats != null && boatIndex >= 0 && boatIndex < boats.Length && boats[boatIndex]
                    ? boats[boatIndex].position
                    : Vector3.zero;
            }

            int idx = Mathf.Clamp(patrolWaypointIndex[boatIndex], 0, patrolRoutes[boatIndex].Length - 1);
            return patrolRoutes[boatIndex][idx];
        }

        private void AdvancePatrolWaypoint(int boatIndex)
        {
            EnsurePatrolState();
            if (boatIndex < 0 || boatIndex >= patrolRoutes.Length ||
                patrolRoutes[boatIndex] == null || patrolRoutes[boatIndex].Length == 0)
                return;

            patrolWaypointIndex[boatIndex] =
                (patrolWaypointIndex[boatIndex] + 1) % patrolRoutes[boatIndex].Length;
        }

        private Vector2 ClampPatrolToOpenChannel(Vector2 enu, Vector2 mapCenter)
        {
            enu.x = Mathf.Clamp(enu.x, -28f, 92f);
            enu.y = Mathf.Clamp(enu.y, -66f, 42f);
            if (IsLand(enu))
                enu = FindNearestWater(enu, mapCenter);
            return enu;
        }

        private float EffectiveSearchAcquireRange(int boatIndex)
        {
            float sensor = boatSensors != null && boatIndex >= 0 && boatIndex < boatSensors.Length && boatSensors[boatIndex]
                ? Mathf.Max(boatSensors[boatIndex].lidarRange, boatSensors[boatIndex].radarRange)
                : sensorRange;
            return Mathf.Min(sensor, Mathf.Max(10f, searchAcquireRange));
        }

        /// <summary>
        /// After contact, boats stop and face the target so the report/order beat is readable
        /// (no more driving outward to the search ring).
        /// </summary>
        private void HoldBoatsAfterContact()
        {
            if (boats == null)
                return;

            Vector3 look = targetPoint
                ? targetPoint.position
                : (targetVessel ? targetVessel.position : Vector3.zero);
            for (int i = 0; i < boats.Length; i++)
            {
                Transform boat = boats[i];
                if (!boat)
                    continue;

                BrakeSurfaceAgent(boat);
                if (look.sqrMagnitude > .01f)
                    RotateSurfaceToward(boat, look);

                Vector3 cleared = KeepClearOfTargetHull(boat.position);
                if (HorizontalDistance(cleared, boat.position) > .15f)
                    boat.position = Vector3.MoveTowards(boat.position, cleared, 2f * Time.deltaTime);
            }

            EnforcePeerClearance(boats, agentSeparation * .9f, true);
        }

        private void TryDetectTargetByProximity()
        {
            if (targetDetected || boats == null)
                return;

            Transform senseTarget = targetVessel ? targetVessel : targetPoint;
            if (!senseTarget)
                return;

            for (int i = 0; i < boats.Length; i++)
            {
                Transform boat = boats[i];
                if (!boat)
                    continue;

                float range = EffectiveSearchAcquireRange(i);
                if (HorizontalDistance(boat.position, senseTarget.position) > range)
                    continue;

                targetDetected = true;
                detectReporter = boat.name + " 近距雷达";
                contactReportedAt = Time.time;
                BeginDetectionCue(i, "近距雷达/激光锁定");
                phase = "② 发现目标 — " + detectReporter + " 上报岸基站";
                if (baseController)
                    baseController.NotifyTargetContact(detectReporter);
                return;
            }
        }

        private void TryDetectTarget()
        {
            if (targetDetected)
                return;

            Transform senseTarget = targetVessel ? targetVessel : targetPoint;
            if (!senseTarget)
                return;

            for (int i = 0; boatSensors != null && i < boatSensors.Length; i++)
            {
                AgentSensorSuite sensor = boatSensors[i];
                if (!sensor || boats == null || i >= boats.Length || !boats[i])
                    continue;

                if (HorizontalDistance(boats[i].position, senseTarget.position) > EffectiveSearchAcquireRange(i))
                    continue;

                sensor.Scan();
                if (!sensor.SeesTarget(senseTarget))
                    continue;

                targetDetected = true;
                detectReporter = boats[i].name + " 激光/雷达";
                contactReportedAt = Time.time;
                BeginDetectionCue(i, "激光/雷达扇区发现");
                phase = "② 发现目标 — " + detectReporter + " 上报岸基站";
                if (baseController)
                    baseController.NotifyTargetContact(detectReporter);
                return;
            }
        }

        private void BeginDetectionCue(int boatIndex, string mode)
        {
            detectionBoatIndex = boatIndex;
            detectionCueUntil = Time.time + 6f;
            detectionModeText = mode + " → 上报岸基站";
        }

        private void ParkDronesOnPads()
        {
            dronesAirborne = false;
            dronesTakingOff = false;
            droneTakeoffStarted = -1f;
            for (int i = 0; drones != null && i < drones.Length; i++)
            {
                Transform drone = drones[i];
                if (!drone)
                    continue;

                Transform pad = dronePads != null && i < dronePads.Length ? dronePads[i] : null;
                if (pad)
                {
                    drone.SetParent(pad, false);
                    drone.localPosition = new Vector3(0f, .28f, 0f);
                    drone.localRotation = Quaternion.identity;
                }
                else if (shoreBase)
                {
                    drone.SetParent(null, true);
                    drone.position = shoreBase.position + new Vector3(-6f + i * 6f, .62f, 2.4f);
                }

                if (droneVisuals != null && i < droneVisuals.Length && droneVisuals[i])
                    droneVisuals[i].spinning = false;
            }
        }

        private void LaunchDrones()
        {
            if (dronesAirborne)
                return;

            dronesAirborne = true;
            dronesTakingOff = true;
            droneTakeoffStarted = Time.time;
            for (int i = 0; drones != null && i < drones.Length; i++)
            {
                Transform drone = drones[i];
                if (!drone)
                    continue;

                // Leave the pad, but stay low — climb is driven over time in DriveDronesDefense.
                drone.SetParent(null, true);
                Vector3 p = drone.position;
                drone.position = new Vector3(p.x, Mathf.Max(p.y, .55f), p.z);

                if (droneVisuals != null && i < droneVisuals.Length && droneVisuals[i])
                    droneVisuals[i].spinning = true;
            }
        }

        private void AnimateTarget()
        {
            if (!targetPoint || captureComplete)
                return;

            if (targetDetected)
                EscapeFromPursuers();
            else
                PatrolBeforeContact();
        }

        private void PatrolBeforeContact()
        {
            Vector3 enu = Coordinates.ToEnu(targetPoint.position);
            if (targetVelocityEnu.sqrMagnitude < .01f)
                targetVelocityEnu = new Vector3(.22f, -.08f, 0f);

            // Smooth patrol — no sudden reflect jitter.
            Vector3 desired = targetVelocityEnu.normalized * targetPatrolSpeed;
            targetVelocityEnu = Vector3.Lerp(targetVelocityEnu, desired, 1.2f * Time.deltaTime);
            enu += targetVelocityEnu * Time.deltaTime;

            Vector3 offset = enu - targetCenterEnu;
            if (Mathf.Abs(offset.x) > 10f)
            {
                targetVelocityEnu.x = -Mathf.Abs(targetVelocityEnu.x) * Mathf.Sign(offset.x);
                enu.x = targetCenterEnu.x + Mathf.Sign(offset.x) * 10f;
            }
            if (Mathf.Abs(offset.y) > 7f)
            {
                targetVelocityEnu.y = -Mathf.Abs(targetVelocityEnu.y) * Mathf.Sign(offset.y);
                enu.y = targetCenterEnu.y + Mathf.Sign(offset.y) * 7f;
            }

            Vector3 current = targetPoint.position;
            Vector3 next = Coordinates.ToUnity(enu.x, enu.y, .38f);
            Vector3 unityVel = Coordinates.ToUnity(targetVelocityEnu.x, targetVelocityEnu.y, 0f);
            unityVel.y = 0f;
            next = SoftSteerTarget(current, next, ref unityVel, 5.5f, targetPatrolSpeed);
            Vector3 enuVel = Coordinates.ToEnu(unityVel);
            targetVelocityEnu = Vector3.Lerp(targetVelocityEnu, new Vector3(enuVel.x, enuVel.y, 0f), .35f);
            SetTargetPose(next);
        }

        private void EscapeFromPursuers()
        {
            Vector3 targetPos = targetPoint.position;
            Vector3 escapeDir = ComputeEscapeDirection(targetPos);
            Vector3 desiredVelocity = escapeDir * targetEscapeSpeed;

            // Mild weave — keep it readable, not frantic zig-zag.
            float juke = Mathf.Sin(Time.time * .7f) * .22f;
            Vector3 side = Vector3.Cross(Vector3.up, escapeDir);
            desiredVelocity += side * juke;

            Vector3 currentUnityVel = Coordinates.ToUnity(targetVelocityEnu.x, targetVelocityEnu.y, 0f);
            currentUnityVel.y = 0f;
            Vector3 blended = Vector3.Lerp(currentUnityVel, desiredVelocity, 1.4f * Time.deltaTime);
            if (blended.sqrMagnitude > .001f)
                blended = blended.normalized * Mathf.Clamp(blended.magnitude, targetPatrolSpeed, targetEscapeSpeed);

            Vector3 next = targetPos + blended * Time.deltaTime;
            Vector3 center = Coordinates.ToUnity(targetCenterEnu.x, targetCenterEnu.y, 0f);
            Vector3 fromCenter = next - center;
            fromCenter.y = 0f;
            if (fromCenter.magnitude > escapeArenaRadius)
            {
                fromCenter = fromCenter.normalized * escapeArenaRadius;
                next = center + fromCenter;
                Vector3 inward = (-fromCenter).normalized;
                if (Vector3.Dot(blended, fromCenter.normalized) > 0f)
                    blended = Vector3.Lerp(blended, inward * blended.magnitude, .4f);
            }

            next = SoftSteerTarget(targetPos, next, ref blended, 6.2f, targetEscapeSpeed);
            Vector3 enuVel = Coordinates.ToEnu(blended);
            targetVelocityEnu = new Vector3(enuVel.x, enuVel.y, 0f);
            SetTargetPose(new Vector3(next.x, .38f, next.z));
        }

        /// <summary>
        /// Soft obstacle clearance — slide a little, never teleport to far waypoints / nearest water.
        /// </summary>
        private Vector3 SoftSteerTarget(
            Vector3 current,
            Vector3 next,
            ref Vector3 unityVelocity,
            float clearance,
            float speedLimit)
        {
            Vector3 steered = next;
            steered.y = current.y;

            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Length; i++)
                    SoftPushTargetFromObstacle(obstacles[i], clearance, ref steered, ref unityVelocity);
            }
            SoftPushTargetFromObstacle(dynamicBarrier, clearance, ref steered, ref unityVelocity);

            // Target must not ram friendly USVs.
            if (boats != null)
            {
                for (int i = 0; i < boats.Length; i++)
                {
                    Transform boat = boats[i];
                    if (!boat)
                        continue;
                    SoftPushTargetFromBoat(boat, boatTargetClearance, ref steered, ref unityVelocity);
                }
            }

            steered = ClampTargetToWater(current, steered, .38f);
            steered = LimitTargetStep(current, steered, speedLimit);

            Vector3 delta = steered - current;
            delta.y = 0f;
            if (delta.sqrMagnitude > .0004f)
            {
                float speed = Mathf.Clamp(unityVelocity.magnitude, targetPatrolSpeed * .5f, speedLimit);
                unityVelocity = Vector3.Lerp(unityVelocity, delta.normalized * speed, .25f);
            }

            return steered;
        }

        private static Vector3 LimitTargetStep(Vector3 current, Vector3 proposed, float speedLimit)
        {
            Vector3 delta = proposed - current;
            delta.y = 0f;
            float maxStep = Mathf.Max(.05f, speedLimit) * Time.deltaTime * 1.2f;
            if (delta.magnitude > maxStep)
                proposed = current + delta.normalized * maxStep;
            proposed.y = current.y;
            return proposed;
        }

        private Vector3 ClampTargetToWater(Vector3 current, Vector3 proposed, float height)
        {
            Vector3 enu = Coordinates.ToEnu(proposed);
            Vector2 xy = new Vector2(enu.x, enu.y);
            if (!IsLand(xy))
            {
                proposed.y = height;
                return proposed;
            }

            Vector3 mid = Vector3.Lerp(current, proposed, .4f);
            Vector3 midEnu = Coordinates.ToEnu(mid);
            if (!IsLand(new Vector2(midEnu.x, midEnu.y)))
            {
                mid.y = height;
                return mid;
            }

            Vector3 stay = current;
            stay.y = height;
            return stay;
        }

        private void SoftPushTargetFromObstacle(
            Transform obstacle,
            float clearance,
            ref Vector3 steered,
            ref Vector3 velocity)
        {
            if (!obstacle || !obstacle.gameObject.activeInHierarchy)
                return;
            if (obstacle.name.Contains("ShoreBase"))
                return;

            float keep = ObstacleRadius(obstacle) + clearance;
            float dist = HorizontalDistance(steered, obstacle.position);
            if (dist >= keep)
                return;

            Vector3 away = steered - obstacle.position;
            away.y = 0f;
            if (away.sqrMagnitude < .001f)
                away = Vector3.right;
            Vector3 awayN = away.normalized;
            float penetrate = keep - dist;
            steered += awayN * Mathf.Min(penetrate, 1.8f);

            float speed = velocity.magnitude;
            Vector3 velDir = speed > .001f ? velocity.normalized : awayN;
            float into = Vector3.Dot(velDir, -awayN);
            if (into > 0f)
            {
                Vector3 slid = Vector3.ProjectOnPlane(velocity, awayN);
                if (slid.sqrMagnitude < .001f)
                    slid = Vector3.Cross(Vector3.up, awayN) * Mathf.Max(speed, targetPatrolSpeed);
                velocity = Vector3.Lerp(velocity, slid.normalized * Mathf.Max(speed, targetPatrolSpeed), .4f * into);
            }
        }

        private void SoftPushTargetFromBoat(
            Transform boat,
            float clearance,
            ref Vector3 steered,
            ref Vector3 velocity)
        {
            if (!boat)
                return;

            float dist = HorizontalDistance(steered, boat.position);
            if (dist >= clearance)
                return;

            Vector3 away = steered - boat.position;
            away.y = 0f;
            if (away.sqrMagnitude < .001f)
                away = Vector3.right;
            Vector3 awayN = away.normalized;
            steered += awayN * Mathf.Min(clearance - dist, 1.6f);

            float speed = velocity.magnitude;
            Vector3 velDir = speed > .001f ? velocity.normalized : awayN;
            float into = Vector3.Dot(velDir, -awayN);
            if (into > 0f)
            {
                Vector3 slid = Vector3.ProjectOnPlane(velocity, awayN);
                if (slid.sqrMagnitude < .001f)
                    slid = Vector3.Cross(Vector3.up, awayN) * Mathf.Max(speed, targetPatrolSpeed);
                velocity = Vector3.Lerp(velocity, slid.normalized * Mathf.Max(speed, targetPatrolSpeed), .45f * into);
            }
        }

        private Vector3 ComputeEscapeDirection(Vector3 targetPos)
        {
            var bearings = new List<float>();
            Vector3 centroid = Vector3.zero;
            int count = 0;

            void AddPursuer(Transform agent)
            {
                if (!agent)
                    return;
                Vector3 delta = agent.position - targetPos;
                delta.y = 0f;
                if (delta.sqrMagnitude < .01f)
                    return;
                bearings.Add(Mathf.Atan2(delta.z, delta.x));
                centroid += agent.position;
                count++;
            }

            if (boats != null)
            {
                for (int i = 0; i < boats.Length; i++)
                    AddPursuer(boats[i]);
            }
            if (dronesAirborne && drones != null)
            {
                for (int i = 0; i < drones.Length; i++)
                    AddPursuer(drones[i]);
            }

            if (count == 0)
                return Vector3.forward;

            centroid /= count;
            Vector3 awayFromFleet = targetPos - centroid;
            awayFromFleet.y = 0f;
            if (awayFromFleet.sqrMagnitude < .001f)
                awayFromFleet = Vector3.right;
            awayFromFleet.Normalize();

            if (bearings.Count < 2)
                return awayFromFleet;

            bearings.Sort();
            float bestGap = -1f;
            float bestMid = 0f;
            for (int i = 0; i < bearings.Count; i++)
            {
                float a = bearings[i];
                float b = bearings[(i + 1) % bearings.Count];
                float gap = i + 1 < bearings.Count ? b - a : (b + Mathf.PI * 2f) - a;
                if (gap > bestGap)
                {
                    bestGap = gap;
                    bestMid = a + gap * .5f;
                }
            }

            Vector3 gapDir = new Vector3(Mathf.Cos(bestMid), 0f, Mathf.Sin(bestMid));
            return (gapDir * .7f + awayFromFleet * .3f).normalized;
        }

        private void FreezeCaptureFormation()
        {
            if (!targetPoint)
                return;

            Vector3 center = lockedTargetPosition.sqrMagnitude > .01f
                ? lockedTargetPosition
                : targetPoint.position;
            lockedTargetPosition = center;
            targetVelocityEnu = Vector3.zero;
            SetTargetPose(center);

            for (int i = 0; boats != null && i < boats.Length; i++)
            {
                if (!boats[i])
                    continue;
                Vector3 slot = FixedBoatSlot(i, center);
                if (HorizontalDistance(boats[i].position, slot) > .2f ||
                    TooCloseToNavigationObstacle(boats[i].position, SurfaceObstacleClearance))
                    MoveSurfaceAgent(boats[i], slot, boatSpeed * .45f);
                else
                {
                    BrakeSurfaceAgent(boats[i]);
                    RotateSurfaceToward(boats[i], center);
                }
            }

            for (int i = 0; drones != null && i < drones.Length; i++)
            {
                if (!drones[i])
                    continue;
                Vector3 slot = FixedDroneSlot(i, center);
                if (Vector3.Distance(drones[i].position, slot) > .25f)
                    MoveAirAgent(drones[i], slot, droneSpeed * .45f);
                else
                {
                    BrakeAirAgent(drones[i]);
                    RotateAirToward(drones[i], center);
                }
                if (droneVisuals != null && i < droneVisuals.Length && droneVisuals[i])
                    droneVisuals[i].spinning = true;
            }
        }

        private Vector3 FixedBoatSlot(int index, Vector3 center)
        {
            return BoatRingSlot(index, center, captureRadius);
        }

        private Vector3 BoatRingSlot(int index, Vector3 center, float radius)
        {
            // Capture: fixed equilateral triangle. Escort-guard slots are defense-only.
            float angle = BoatApproachAngles[index % BoatApproachAngles.Length] * Mathf.Deg2Rad;
            return new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                .42f,
                center.z + Mathf.Sin(angle) * radius
            );
        }

        private Vector3 FixedDroneSlot(int index, Vector3 center)
        {
            return DroneRingSlot(index, center, defenseRadius);
        }

        private Vector3 DroneRingSlot(int index, Vector3 center, float radius)
        {
            // Capture: 60° offset equilateral ring. Escort-guard slots are defense-only.
            float angle = DroneDefenseAngles[index % DroneDefenseAngles.Length] * Mathf.Deg2Rad;
            float altitude = DroneAltitudeFor(index);
            return new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                altitude,
                center.z + Mathf.Sin(angle) * radius
            );
        }

        private void ClearEscortGuardPlan()
        {
            escortGuardPlanned = false;
            coreBoatIndex = -1;
            boatWingSlotByIndex = null;
            droneEscortSlotByIndex = null;
            escortSlotOffsets = null;
            escortThreatDir = Vector3.forward;
            if (guardArcLine)
                guardArcLine.positionCount = 0;
            if (blockerMarker)
                blockerMarker.positionCount = 0;
        }

        /// <summary>
        /// Threat axis: target escape / open-water bearing beyond the vessel (shore→target),
        /// matching 护航守卫5 own→enemy geometry with own = contained target.
        /// </summary>
        private Vector3 ResolveEscortThreatDirection(Vector3 center)
        {
            Vector3 fromShore = Vector3.zero;
            if (shoreBase)
            {
                fromShore = EscortGuardGeometry.Horizontal(center - shoreBase.position);
                if (fromShore.sqrMagnitude > 1f)
                    fromShore.Normalize();
            }

            Vector3 escape = EscortGuardGeometry.Horizontal(
                Coordinates.ToUnity(targetVelocityEnu.x, targetVelocityEnu.y, 0f));
            if (escape.sqrMagnitude > .04f)
                return escape.normalized;

            if (fromShore.sqrMagnitude > .01f)
                return fromShore;

            return Vector3.forward;
        }

        private void EnsureEscortGuardPlan(
            Vector3 own,
            Vector3 threatPos,
            float boatRingRadius,
            float droneRingRadius,
            bool force = false)
        {
            if (!useEscortGuardFormation || boats == null || boats.Length == 0)
                return;

            Vector3 threatDir = EscortGuardGeometry.NormalizeHorizontal(
                threatPos - own,
                ResolveEscortThreatDirection(own));
            if (escortGuardPlanned && !force)
            {
                // Keep slots stable; only refresh threat dir gently for viz/blocker.
                if (Vector3.Dot(escortThreatDir, threatDir) > .85f)
                    escortThreatDir = Vector3.Slerp(escortThreatDir, threatDir, .08f).normalized;
                return;
            }

            escortThreatDir = threatDir;
            int boatCount = boats.Length;
            boatWingSlotByIndex = new int[boatCount];
            for (int i = 0; i < boatCount; i++)
                boatWingSlotByIndex[i] = -1;

            float rMin = Mathf.Min(blockerRMin, boatRingRadius);
            float rMax = Mathf.Max(blockerRMax * (boatRingRadius / Mathf.Max(captureRadius, 1f)), boatRingRadius * .85f);
            Vector3 blocker = EscortGuardGeometry.ComputeBlockerPoint(
                own,
                threatPos,
                blockerRatio,
                rMin,
                rMax,
                escortThreatDir,
                out _);
            blocker.y = .42f;

            coreBoatIndex = 0;
            float bestCore = float.PositiveInfinity;
            for (int i = 0; i < boatCount; i++)
            {
                if (!boats[i])
                    continue;
                float d = HorizontalDistance(boats[i].position, blocker);
                if (d < bestCore)
                {
                    bestCore = d;
                    coreBoatIndex = i;
                }
            }

            int wingCount = Mathf.Max(0, boatCount - 1);
            var wingCandidates = new List<int>(wingCount);
            var wingPositions = new List<Vector3>(wingCount);
            for (int i = 0; i < boatCount; i++)
            {
                if (i == coreBoatIndex || !boats[i])
                    continue;
                wingCandidates.Add(i);
                wingPositions.Add(boats[i].position);
            }

            Vector3[] wingGoals = EscortGuardGeometry.WingGoalsWithSpacing(
                own,
                escortThreatDir,
                wingCandidates.Count,
                boatRingRadius,
                guardArcHalfAngleDeg * Mathf.Deg2Rad,
                minimumGuardSpacing,
                .42f);
            int[] wingAssign = EscortGuardGeometry.AssignMinCost(wingPositions.ToArray(), wingGoals);
            for (int c = 0; c < wingCandidates.Count; c++)
                boatWingSlotByIndex[wingCandidates[c]] = wingAssign[c];

            int droneCount = drones?.Length ?? 0;
            droneEscortSlotByIndex = new int[droneCount];
            for (int i = 0; i < droneCount; i++)
                droneEscortSlotByIndex[i] = -1;

            float half = EscortGuardGeometry.EffectiveGuardArcHalfAngle(
                Mathf.Max(1, wingCount),
                guardArcHalfAngleDeg * Mathf.Deg2Rad,
                boatRingRadius,
                minimumGuardSpacing);
            escortSlotOffsets = EscortGuardGeometry.EscortGoalOffsets(
                escortThreatDir,
                droneCount,
                droneRingRadius,
                half,
                escortClearanceDeg * Mathf.Deg2Rad);
            plannedBoatRingRadius = boatRingRadius;
            plannedDroneRingRadius = droneRingRadius;

            if (droneCount > 0)
            {
                var dronePos = new Vector3[droneCount];
                for (int i = 0; i < droneCount; i++)
                    dronePos[i] = drones[i] ? drones[i].position : own;
                int[] escortAssign = EscortGuardGeometry.AssignEscortSlots(
                    own, dronePos, escortSlotOffsets);
                for (int i = 0; i < droneCount; i++)
                    droneEscortSlotByIndex[i] = escortAssign[i];
            }

            escortGuardPlanned = true;
        }

        private bool TryEscortGuardBoatSlot(int index, Vector3 center, float radius, out Vector3 slot)
        {
            slot = default;
            if (!escortGuardPlanned || boatWingSlotByIndex == null)
                return false;
            if (index < 0 || index >= boatWingSlotByIndex.Length)
                return false;

            if (index == coreBoatIndex)
            {
                Vector3 threatPos = defenseEscortActive && targetPoint
                    ? targetPoint.position
                    : center + escortThreatDir * Mathf.Max(8f, escortThreatDistance);
                Vector3 blocker = EscortGuardGeometry.ComputeBlockerPoint(
                    center,
                    threatPos,
                    blockerRatio,
                    Mathf.Min(blockerRMin, radius) * .9f,
                    Mathf.Max(Mathf.Min(blockerRMax, radius), radius * .85f),
                    escortThreatDir,
                    out _);
                Vector3 onRing = center + escortThreatDir * radius;
                onRing.y = .42f;
                float blend = defenseEscortActive
                    ? 1f
                    : Mathf.InverseLerp(captureRadius + 12f, captureRadius, radius);
                Vector3 blended = Vector3.Lerp(onRing, new Vector3(blocker.x, .42f, blocker.z), blend);
                Vector3 fromCenter = EscortGuardGeometry.Horizontal(blended - center);
                if (fromCenter.sqrMagnitude < .01f)
                    fromCenter = escortThreatDir * radius;
                else
                    fromCenter = fromCenter.normalized * radius;
                slot = new Vector3(center.x + fromCenter.x, .42f, center.z + fromCenter.z);
                return true;
            }

            int wingSlot = boatWingSlotByIndex[index];
            if (wingSlot < 0)
                return false;

            int wingCount = 0;
            for (int i = 0; i < boatWingSlotByIndex.Length; i++)
            {
                if (boatWingSlotByIndex[i] >= 0)
                    wingCount++;
            }

            Vector3[] goals = EscortGuardGeometry.WingGoalsWithSpacing(
                center,
                escortThreatDir,
                wingCount,
                radius,
                guardArcHalfAngleDeg * Mathf.Deg2Rad,
                minimumGuardSpacing * (radius / Mathf.Max(captureRadius, 1f)),
                .42f);
            if (wingSlot >= goals.Length)
                return false;
            slot = goals[wingSlot];
            return true;
        }

        private bool TryEscortGuardDroneSlot(int index, Vector3 center, float radius, out Vector3 slot)
        {
            slot = default;
            if (!escortGuardPlanned || droneEscortSlotByIndex == null || escortSlotOffsets == null)
                return false;
            if (index < 0 || index >= droneEscortSlotByIndex.Length)
                return false;

            int escortSlot = droneEscortSlotByIndex[index];
            if (escortSlot < 0 || escortSlot >= escortSlotOffsets.Length)
                return false;

            float scale = plannedDroneRingRadius > .1f ? radius / plannedDroneRingRadius : 1f;
            Vector3 offset = escortSlotOffsets[escortSlot] * scale;
            slot = new Vector3(
                center.x + offset.x,
                DroneAltitudeFor(index),
                center.z + offset.z);
            return true;
        }

        /// <summary>
        /// After contact, shrink the boat ring from a wide approach radius down to captureRadius.
        /// </summary>
        private float CurrentBoatRingRadius()
        {
            float outer = Mathf.Max(captureRadius + 12f, searchStartRadius * .72f);
            if (!targetDetected || captureStarted < 0f)
                return outer;

            float t = Mathf.Clamp01((Time.time - captureStarted) / Mathf.Max(.1f, boatCloseDuration));
            t = t * t * (3f - 2f * t); // smoothstep — visible gradual close-in
            return Mathf.Lerp(outer, captureRadius, t);
        }

        private bool BoatRingFullyClosed()
        {
            return CurrentBoatRingRadius() <= captureRadius + .35f;
        }

        private void SetTargetPose(Vector3 position)
        {
            bool allowThreatMove = !captureComplete || (defenseEscortActive && !defenseComplete);
            if (targetPoint && allowThreatMove)
                position = ClampTargetToWater(targetPoint.position, position, .38f);
            else
                position = ClampToWater(position, .38f);
            if (targetPoint)
            {
                targetPoint.gameObject.SetActive(true);
                targetPoint.position = new Vector3(position.x, .46f, position.z);
            }
            if (targetVessel)
            {
                targetVessel.gameObject.SetActive(true);
                targetVessel.position = new Vector3(position.x, .5f, position.z);
                Vector3 heading = Coordinates.ToUnity(targetVelocityEnu.x, targetVelocityEnu.y, 0f);
                heading.y = 0f;
                if (heading.sqrMagnitude > .001f)
                {
                    targetVessel.rotation = Quaternion.Slerp(
                        targetVessel.rotation,
                        Quaternion.LookRotation(heading.normalized, Vector3.up) * Quaternion.Euler(0f, -90f, 0f),
                        3.5f * Time.deltaTime
                    );
                }
            }
        }

        private void ForceTargetContact(string reason)
        {
            if (targetDetected)
                return;

            targetDetected = true;
            detectReporter = reason;
            contactReportedAt = Time.time;
            BeginDetectionCue(0, "搜索超时强制接触");
            phase = "② 发现目标 — " + reason + " 上报岸基站";
            if (baseController)
                baseController.NotifyTargetContact(reason);
        }

        private void TickLocalAuthorization()
        {
            if (captureAuthorized || baseController)
                return;
            if (contactReportedAt < 0f)
                return;

            float elapsed = Time.time - contactReportedAt;
            if (elapsed >= localReportHoldSeconds + localOrderHoldSeconds)
                NotifyBaseDispatch();
        }

        private void ForceCaptureSuccess(string reason)
        {
            if (captureComplete)
                return;

            if (!targetDetected)
                ForceTargetContact(reason);
            captureAuthorized = true;
            if (captureStarted < 0f)
                captureStarted = Time.time;
            if (!dronesAirborne)
                LaunchDrones();

            captureComplete = true;
            captureCompleteAt = Time.time;
            captureReady = true;
            formationHolding = true;
            lockedTargetPosition = targetPoint ? targetPoint.position : lockedTargetPosition;
            targetVelocityEnu = Vector3.zero;
            FreezeCaptureFormation();
            phase = "⑤ 围捕成功 — 等边编队锁定，准备护航防卫";
            if (baseController)
                baseController.NotifyCaptureComplete();
        }

        private void TickPostCaptureDefense()
        {
            if (defenseComplete)
            {
                FreezeDefenseFormation();
                phase = "⑥ 护航防卫成功 — 岸基阻断+护航弧锁定";
                return;
            }

            if (!defenseEscortActive)
            {
                FreezeCaptureFormation();
                phase = "⑤ 围捕成功 — 等边编队锁定";
                if (captureCompleteAt >= 0f &&
                    Time.time - captureCompleteAt >= defenseStartDelaySeconds)
                {
                    if (useEscortGuardFormation)
                        BeginDefenseEscort();
                    else
                    {
                        defenseComplete = true;
                        phase = "⑤ 围捕成功 — 等边编队锁定";
                    }
                }
                return;
            }

            if (defenseStartedAt >= 0f &&
                Time.time - defenseStartedAt >= defenseTimeLimitSeconds)
            {
                CompleteDefenseEscort("time limit");
                return;
            }

            AnimateDefenseThreat();
            bool boatsHolding = DriveBoatsDefenseEscort();
            bool dronesHolding = DriveDronesDefenseEscort();
            if (boatsHolding && dronesHolding)
                CompleteDefenseEscort("escort locked");
            else if (!boatsHolding)
                phase = "⑥ 护航防卫 — 敌船指向岸基站，USV 阻断展开";
            else
                phase = "⑥ 护航防卫 — 敌船逼近岸基站，UAV 护航弧就位";
        }

        private Vector3 GetProtectedOwnCenter()
        {
            if (shoreBase)
            {
                // Defense protects the actual shore base, so the threat axis is readable:
                // enemy vessel -> shore base.
                Vector3 own = shoreBase.position;
                own.y = .38f;
                return ClampToWater(own, .38f);
            }

            if (lockedTargetPosition.sqrMagnitude > .01f)
                return lockedTargetPosition;
            return targetPoint ? targetPoint.position : Vector3.zero;
        }

        private void BeginDefenseEscort()
        {
            defenseEscortActive = true;
            defenseComplete = false;
            defenseStartedAt = Time.time;
            protectedOwnCenter = GetProtectedOwnCenter();
            ClearEscortGuardPlan();

            if (!dronesAirborne)
                LaunchDrones();

            Vector3 threat = targetPoint ? targetPoint.position : protectedOwnCenter + Vector3.forward * 40f;
            EnsureEscortGuardPlan(
                protectedOwnCenter,
                threat,
                defenseGuardRadius,
                defenseEscortRadius,
                force: true);

            // Release the captured freighter so it becomes an approaching threat to the shore base.
            lockedTargetPosition = Vector3.zero;
            Vector3 delta = EscortGuardGeometry.Horizontal(protectedOwnCenter - (targetPoint ? targetPoint.position : protectedOwnCenter));
            if (delta.sqrMagnitude < .01f)
                delta = -escortThreatDir;
            Vector3 approachUnity = delta.normalized * defenseThreatApproachSpeed;
            // ENU velocity: Unity (x,z) → ENU (east, north) = (x, z)
            targetVelocityEnu = new Vector3(approachUnity.x, approachUnity.z, 0f);

            phase = "⑥ 护航防卫 — 敌船转向岸基站，我方展开阻断";
            if (baseController)
                baseController.NotifyDefenseStarted();
        }

        private void CompleteDefenseEscort(string reason)
        {
            if (defenseComplete)
                return;

            defenseComplete = true;
            defenseEscortActive = true;
            targetVelocityEnu = Vector3.zero;
            FreezeDefenseFormation();
            phase = "⑥ 护航防卫成功 — 岸基阻断+护航弧锁定";
            if (baseController)
                baseController.NotifyDefenseComplete();
            Debug.Log("[CaptureDefense] Defense escort complete: " + reason);
        }

        private void AnimateDefenseThreat()
        {
            if (!targetPoint || defenseComplete)
                return;

            // Enemy intent: move directly toward the shore base/protected own point.
            Vector3 toOwn = EscortGuardGeometry.Horizontal(protectedOwnCenter - targetPoint.position);
            float dist = toOwn.magnitude;
            if (dist < defenseGuardRadius + boatTargetClearance + 4f)
            {
                targetVelocityEnu = Vector3.zero;
                return;
            }

            Vector3 dir = toOwn / Mathf.Max(dist, .01f);
            Vector3 desiredVelocity = dir * defenseThreatApproachSpeed;
            Vector3 next = targetPoint.position + desiredVelocity * Time.deltaTime;
            next = SoftSteerTarget(
                targetPoint.position,
                next,
                ref desiredVelocity,
                8.5f,
                defenseThreatApproachSpeed
            );
            targetVelocityEnu = new Vector3(desiredVelocity.x, desiredVelocity.z, 0f);
            SetTargetPose(next);
        }

        private void FreezeDefenseFormation()
        {
            // Hold current escort pose — no more slot chasing / avoidance nudges after success.
            targetVelocityEnu = Vector3.zero;
            for (int i = 0; boats != null && i < boats.Length; i++)
            {
                if (!boats[i])
                    continue;
                surfaceSpeedState[boats[i]] = 0f;
                RotateSurfaceToward(boats[i], targetPoint ? targetPoint.position : protectedOwnCenter);
            }

            for (int i = 0; drones != null && i < drones.Length; i++)
            {
                if (!drones[i])
                    continue;
                airVelocityState[drones[i]] = Vector3.zero;
                RotateAirToward(drones[i], protectedOwnCenter.sqrMagnitude > .01f
                    ? protectedOwnCenter
                    : (shoreBase ? shoreBase.position : drones[i].position));
                if (droneVisuals != null && i < droneVisuals.Length && droneVisuals[i])
                    droneVisuals[i].spinning = true;
            }
        }

        private bool DriveBoatsDefenseEscort()
        {
            if (boats == null)
                return true;

            Vector3 own = protectedOwnCenter;
            Vector3 threat = targetPoint ? targetPoint.position : own + escortThreatDir * 40f;
            EnsureEscortGuardPlan(own, threat, defenseGuardRadius, defenseEscortRadius);

            float maxDistance = 0f;
            for (int i = 0; i < boats.Length; i++)
            {
                Transform boat = boats[i];
                if (!boat)
                    continue;
                if (!TryEscortGuardBoatSlot(i, own, defenseGuardRadius, out Vector3 slot))
                    continue;

                slot = AvoidBlockingObstacles(boat.position, slot, SurfaceObstacleClearance, i);
                slot = KeepClearOfTargetHull(slot);
                slot = ClampToWater(slot, .42f);
                float toSlot = HorizontalDistance(boat.position, slot);
                maxDistance = Mathf.Max(maxDistance, toSlot);
                if (toSlot <= holdDistance &&
                    !TooCloseToNavigationObstacle(boat.position, SurfaceObstacleClearance))
                {
                    BrakeSurfaceAgent(boat);
                    RotateSurfaceToward(boat, threat);
                    continue;
                }

                slot = CommitDetourIfBlocked(boat, i, slot);
                slot = SoftAvoidPeers(boat, slot, boats, agentSeparation);
                slot = KeepClearOfTargetHull(slot);
                slot = ClampToWater(slot, .42f);
                MoveSurfaceAgent(boat, slot, boatSpeed);
                TrackBoatProgress(boat, i, slot);
            }

            EnforcePeerClearance(boats, agentSeparation * .9f, true);
            return maxDistance < holdDistance;
        }

        private bool DriveDronesDefenseEscort()
        {
            if (!dronesAirborne || drones == null)
                return false;

            Vector3 own = protectedOwnCenter;
            float maxDistance = 0f;
            for (int i = 0; i < drones.Length; i++)
            {
                Transform drone = drones[i];
                if (!drone)
                    continue;
                if (!TryEscortGuardDroneSlot(i, own, defenseEscortRadius, out Vector3 slot))
                    continue;

                float toSlot = Vector3.Distance(drone.position, slot);
                maxDistance = Mathf.Max(maxDistance, toSlot);
                if (toSlot <= holdDistance)
                {
                    BrakeAirAgent(drone);
                    RotateAirToward(drone, own);
                    if (droneVisuals != null && i < droneVisuals.Length && droneVisuals[i])
                        droneVisuals[i].spinning = true;
                    continue;
                }

                slot = SoftAvoidPeers(drone, slot, drones, droneSeparation);
                MoveAirAgent(drone, slot, droneSpeed);
                if (droneVisuals != null && i < droneVisuals.Length && droneVisuals[i])
                    droneVisuals[i].spinning = true;
            }

            return maxDistance < holdDistance;
        }

        private bool DriveBoatsCapture()
        {
            if (boats == null || targetPoint == null)
                return true;

            Vector3 center = targetPoint.position;
            float ring = CurrentBoatRingRadius();
            float maxDistance = 0f;
            for (int i = 0; i < boats.Length; i++)
            {
                Transform boat = boats[i];
                if (!boat)
                    continue;

                // Follow the shrinking ring so the triangle forms over time.
                Vector3 slot = BoatRingSlot(i, center, ring);
                slot = AvoidBlockingObstacles(boat.position, slot, SurfaceObstacleClearance, i);
                slot = KeepClearOfTargetHull(slot);
                slot = ClampToWater(slot, .42f);
                float toSlot = HorizontalDistance(boat.position, slot);
                maxDistance = Mathf.Max(
                    maxDistance,
                    HorizontalDistance(boat.position, slot)
                );
                if (BoatRingFullyClosed() && toSlot <= holdDistance &&
                    !TooCloseToNavigationObstacle(boat.position, SurfaceObstacleClearance))
                {
                    BrakeSurfaceAgent(boat);
                    RotateSurfaceToward(boat, center);
                    continue;
                }

                slot = CommitDetourIfBlocked(boat, i, slot);
                slot = SoftAvoidPeers(boat, slot, boats, agentSeparation);
                MoveSurfaceAgent(boat, slot, boatSpeed);
                TrackBoatProgress(boat, i, slot);
            }

            EnforcePeerClearance(boats, agentSeparation * .9f, true);
            return BoatRingFullyClosed() && maxDistance < holdDistance;
        }

        private bool DriveDronesDefense()
        {
            if (!dronesAirborne || drones == null || targetPoint == null)
                return false;

            Vector3 center = targetPoint.position;
            float maxDistance = 0f;
            int climbing = 0;
            for (int i = 0; i < drones.Length; i++)
            {
                Transform drone = drones[i];
                if (!drone)
                    continue;

                float cruiseAlt = DroneAltitudeFor(i);
                float age = droneTakeoffStarted < 0f
                    ? 99f
                    : Time.time - droneTakeoffStarted - i * droneTakeoffStagger;

                // Staggered spool-up: still sitting on / above pad.
                if (age < 0f)
                {
                    climbing++;
                    maxDistance = Mathf.Max(maxDistance, Vector3.Distance(drone.position, FixedDroneSlot(i, center)));
                    continue;
                }

                Vector3 pos = drone.position;
                bool stillClimbing = pos.y < cruiseAlt - .25f && age < droneTakeoffDuration + 2.5f;
                if (stillClimbing)
                {
                    climbing++;
                    float climbT = Mathf.Clamp01(age / Mathf.Max(.1f, droneTakeoffDuration));
                    Vector3 next = pos;
                    next.y = Mathf.MoveTowards(pos.y, cruiseAlt, droneTakeoffClimbSpeed * Time.deltaTime);

                    // After roughly half climb, begin a gentle drift toward the defense slot.
                    if (climbT > .5f)
                    {
                        Vector3 slot = FixedDroneSlot(i, center);
                        Vector3 lateral = Vector3.MoveTowards(
                            new Vector3(pos.x, 0f, pos.z),
                            new Vector3(slot.x, 0f, slot.z),
                            droneSpeed * .4f * climbT * Time.deltaTime
                        );
                        next.x = lateral.x;
                        next.z = lateral.z;
                    }

                    drone.position = next;
                    if ((next - pos).sqrMagnitude > .0001f)
                        RotateAirToward(drone, next + (next - pos));
                    maxDistance = Mathf.Max(maxDistance, Vector3.Distance(drone.position, FixedDroneSlot(i, center)));
                    continue;
                }

                Vector3 goal = FixedDroneSlot(i, center);
                float toSlot = Vector3.Distance(drone.position, goal);
                maxDistance = Mathf.Max(
                    maxDistance,
                    Vector3.Distance(drone.position, FixedDroneSlot(i, center))
                );
                if (toSlot <= holdDistance)
                {
                    BrakeAirAgent(drone);
                    RotateAirToward(drone, center);
                    continue;
                }

                goal = SoftAvoidPeers(drone, goal, drones, droneSeparation);
                goal.y = cruiseAlt;
                MoveAirAgent(drone, goal, droneSpeed);
            }

            dronesTakingOff = climbing > 0;
            EnforcePeerClearance(drones, droneSeparation * .9f, false);
            return !dronesTakingOff && maxDistance < holdDistance;
        }

        private Vector3 CommitDetourIfBlocked(Transform boat, int index, Vector3 goal)
        {
            EnsureProgressBuffers();

            // Keep a committed detour so the boat does not oscillate left/right around a buoy.
            if (index >= 0 && index < boatDetourUntil.Length && Time.time < boatDetourUntil[index])
            {
                Vector3 detour = boatDetours[index];
                if (HorizontalDistance(boat.position, detour) < 2.5f ||
                    !PathBlocked(boat.position, goal, SurfaceObstacleClearance, out _))
                {
                    boatDetourUntil[index] = -1f;
                }
                else
                {
                    return detour;
                }
            }

            if (!PathBlocked(boat.position, goal, SurfaceObstacleClearance, out Transform blocker))
                return goal;

            Vector3 chosen = SteerAroundObstacle(
                boat.position,
                goal,
                blocker,
                SurfaceObstacleClearance,
                index
            );
            chosen = ClampToWater(chosen, .42f);
            if (index >= 0 && index < boatDetours.Length)
            {
                boatDetours[index] = chosen;
                boatDetourUntil[index] = Time.time + Mathf.Max(2.5f, detourCommitSeconds);
            }
            activeAvoidanceCount++;
            return chosen;
        }

        private bool PathBlocked(Vector3 from, Vector3 to, float clearance, out Transform blocker)
        {
            Transform found = null;
            float best = float.PositiveInfinity;

            void Consider(Transform obstacle)
            {
                if (!obstacle || !obstacle.gameObject.activeInHierarchy || obstacle.name.Contains("ShoreBase"))
                    return;
                float radius = ObstacleRadius(obstacle) + clearance;
                float dist = SegmentDistance(from, to, obstacle.position);
                if (dist < radius && dist < best)
                {
                    best = dist;
                    found = obstacle;
                }
            }

            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Length; i++)
                    Consider(obstacles[i]);
            }
            Consider(dynamicBarrier);

            blocker = found;
            return found != null;
        }

        private Vector3 SoftAvoidPeers(Transform self, Vector3 desired, Transform[] peers, float minDistance)
        {
            if (peers == null)
                return desired;

            Vector3 adjusted = desired;
            for (int i = 0; i < peers.Length; i++)
            {
                Transform peer = peers[i];
                if (!peer || peer == self)
                    continue;
                float dist = HorizontalDistance(adjusted, peer.position);
                if (dist < minDistance)
                    adjusted = PushAway(adjusted, peer.position, minDistance);
            }
            adjusted.y = desired.y;
            return adjusted;
        }

        private Vector3 SeekWithLocalAvoidance(
            Transform agent,
            Vector3 goal,
            AgentSensorSuite[] sensors,
            int index,
            bool surfaceAgent)
        {
            Vector3 planned = goal;
            float peerSep = surfaceAgent ? agentSeparation : droneSeparation;
            float clearance = surfaceAgent ? 5.5f : 5f;

            // Light sensor dodge — only when a hit is close.
            if (sensors != null && index >= 0 && index < sensors.Length && sensors[index])
            {
                AgentSensorSuite sensor = sensors[index];
                sensor.Scan();
                if (sensor.hitCount > 0 && sensor.nearestDistance < clearance + 4f)
                {
                    planned = sensor.SteerAway(planned, clearance);
                    activeAvoidanceCount++;
                }
            }

            planned = AvoidBlockingObstacles(
                agent.position,
                planned,
                clearance,
                surfaceAgent ? FindBoatIndex(agent) : -1
            );

            if (targetVessel && targetVessel.gameObject.activeInHierarchy)
            {
                float keep = Mathf.Max(boatTargetClearance, ObstacleRadius(targetVessel) + (surfaceAgent ? 6f : 4f));
                if (HorizontalDistance(planned, targetVessel.position) < keep)
                    planned = PushAway(planned, targetVessel.position, keep);
            }

            planned = SeparateFromPeers(agent, planned, boats, peerSep);
            // Boats stay on water; drones are airborne — no cross-domain peer dodge.
            if (!surfaceAgent && dronesAirborne)
                planned = SeparateFromPeers(agent, planned, drones, peerSep);

            if (surfaceAgent)
            {
                planned = KeepClearOfTargetHull(planned);
                planned = ClampToWater(planned, .42f);
            }

            planned.y = goal.y;
            return planned;
        }

        private void TrackBoatProgress(Transform boat, int index, Vector3 goal)
        {
            EnsureProgressBuffers();
            if (boat == null || index < 0 || index >= boatProgressStamp.Length)
                return;

            float moved = HorizontalDistance(boat.position, boatProgressPos[index]);
            float closer = HorizontalDistance(boatProgressPos[index], goal) - HorizontalDistance(boat.position, goal);
            if (moved > 1.5f || closer > 1f)
            {
                boatProgressStamp[index] = Time.time;
                boatProgressPos[index] = boat.position;
            }
        }

        private Vector3 UnstickIfNeeded(Transform boat, int index, Vector3 goal)
        {
            EnsureProgressBuffers();
            if (boat == null || index < 0 || index >= boatProgressStamp.Length)
                return goal;

            // If a boat barely moves for a few seconds, force a wide side detour around the blocker.
            if (Time.time - boatProgressStamp[index] < 2.2f)
                return goal;

            Transform blocker = NearestBlocker(boat.position);
            Vector3 toGoal = goal - boat.position;
            toGoal.y = 0f;
            if (toGoal.sqrMagnitude < .01f)
                toGoal = Vector3.forward;

            Vector3 toCenter = Coordinates.ToUnity(targetCenterEnu.x, targetCenterEnu.y, .42f) - boat.position;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude < .01f)
                toCenter = toGoal;

            Vector3 route = (toGoal.normalized * .62f + toCenter.normalized * .38f).normalized;
            if (route.sqrMagnitude < .01f)
                route = toGoal.normalized;

            Vector3 side = Vector3.Cross(Vector3.up, route).normalized;
            float stale = Time.time - boatProgressStamp[index];
            int attempt = Mathf.FloorToInt(stale / Mathf.Max(.5f, detourCommitSeconds));
            float sign = ((attempt + index) % 2 == 0) ? 1f : -1f;
            Vector3 detour = boat.position + side * (16f * sign) + route * 16f;
            if (blocker)
            {
                float keep = ObstacleRadius(blocker) + SurfaceObstacleClearance + 3f;
                if (HorizontalDistance(detour, blocker.position) < keep)
                    detour = PushAway(detour, blocker.position, keep + 4f);
            }

            detour = ClampToWater(detour, .42f);
            activeAvoidanceCount++;
            phase = boats[index].name + " 遇到障碍，切换搜索航线";
            return detour;
        }

        private Transform NearestBlocker(Vector3 from)
        {
            Transform best = null;
            float bestDist = float.PositiveInfinity;
            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Length; i++)
                {
                    Transform obstacle = obstacles[i];
                    if (!obstacle || obstacle.name.Contains("ShoreBase"))
                        continue;
                    float d = HorizontalDistance(from, obstacle.position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = obstacle;
                    }
                }
            }
            if (dynamicBarrier)
            {
                float d = HorizontalDistance(from, dynamicBarrier.position);
                if (d < bestDist)
                    best = dynamicBarrier;
            }
            return best;
        }

        private Vector3 PlanSurfaceMove(
            Transform boat,
            Vector3 slot,
            AgentSensorSuite[] sensors,
            int index,
            float peerSeparation)
        {
            return SeekWithLocalAvoidance(boat, slot, sensors, index, true);
        }

        private Vector3 PlanAirMove(
            Transform drone,
            Vector3 slot,
            AgentSensorSuite[] sensors,
            int index,
            float altitude)
        {
            Vector3 planned = SeekWithLocalAvoidance(drone, slot, sensors, index, false);
            planned.y = altitude;
            return planned;
        }

        private Vector3 AvoidAllBodies(
            Transform self,
            Vector3 desired,
            float obstacleClearance,
            float peerClearance,
            bool surfaceAgent)
        {
            Vector3 adjusted = AvoidBlockingObstacles(self.position, desired, obstacleClearance);
            adjusted = AvoidDynamicBarrier(adjusted, obstacleClearance);

            if (targetVessel && targetVessel.gameObject.activeInHierarchy)
            {
                float keep = Mathf.Max(boatTargetClearance, ObstacleRadius(targetVessel) + obstacleClearance);
                if (HorizontalDistance(adjusted, targetVessel.position) < keep)
                    adjusted = PushAway(adjusted, targetVessel.position, keep);
            }

            adjusted = SeparateFromPeers(self, adjusted, boats, peerClearance);
            // Surface craft ignore airborne drones (and vice versa for peer separation).
            if (!surfaceAgent && dronesAirborne)
                adjusted = SeparateFromPeers(self, adjusted, drones, peerClearance);

            if (surfaceAgent)
                adjusted = KeepClearOfTargetHull(adjusted);

            adjusted.y = desired.y;
            return adjusted;
        }

        private void ResolveWorldCollisions()
        {
            // Only separate when agents are actually overlapping / too close.
            EnforcePeerClearance(boats, agentSeparation * .92f, true);
            if (dronesAirborne)
                EnforcePeerClearance(drones, droneSeparation * .92f, false);

            if (boats != null)
            {
                for (int i = 0; i < boats.Length; i++)
                {
                    Transform boat = boats[i];
                    if (!boat)
                        continue;
                    Vector3 fixedPos = PushOutOfObstacles(boat.position, SurfaceObstacleClearance);
                    fixedPos = ClampToWater(fixedPos, .42f);
                    if (HorizontalDistance(fixedPos, boat.position) > .2f)
                    {
                        boat.position = Vector3.MoveTowards(boat.position, fixedPos, 12f * Time.deltaTime);
                        surfaceSpeedState[boat] = 0f;
                    }
                }
            }

            if (dronesAirborne && drones != null)
            {
                for (int i = 0; i < drones.Length; i++)
                {
                    Transform drone = drones[i];
                    if (!drone)
                        continue;
                    float altitude = DroneAltitudeFor(i);
                    Vector3 fixedPos = AvoidBlockingObstacles(
                        drone.position,
                        drone.position,
                        AirObstacleClearance
                    );
                    fixedPos.y = altitude;
                    if (HorizontalDistance(fixedPos, drone.position) > .2f)
                        drone.position = Vector3.MoveTowards(drone.position, fixedPos, 3f * Time.deltaTime);
                    else
                        drone.position = new Vector3(drone.position.x, altitude, drone.position.z);
                }
            }
        }

        private Vector3 ApplySensorAvoidance(
            Transform agent,
            Vector3 desired,
            AgentSensorSuite[] sensors,
            int index,
            float clearance)
        {
            if (sensors == null || index < 0 || index >= sensors.Length || !sensors[index])
                return desired;

            AgentSensorSuite sensor = sensors[index];
            sensor.Scan();
            Vector3 steered = sensor.SteerAway(desired, clearance);
            if (sensor.hitCount > 0 && sensor.nearestDistance < clearance + 6f)
                activeAvoidanceCount++;
            return steered;
        }

        private Vector3 SeparateFromPeers(Transform self, Vector3 desired, Transform[] peers, float minDistance)
        {
            if (peers == null)
                return desired;

            Vector3 adjusted = desired;
            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 0; i < peers.Length; i++)
                {
                    Transform peer = peers[i];
                    if (!peer || peer == self)
                        continue;

                    // Repel from both the peer's current pose and the candidate slot.
                    float distDesired = HorizontalDistance(adjusted, peer.position);
                    if (distDesired < minDistance)
                    {
                        float push = minDistance - distDesired + 1.25f;
                        adjusted = PushAway(adjusted, peer.position, distDesired + push);
                    }

                    // Extra tangential push around the mission target so agents don't stack on the ring.
                    if (targetPoint && distDesired < minDistance * 1.15f)
                    {
                        Vector3 fromTarget = adjusted - targetPoint.position;
                        fromTarget.y = 0f;
                        Vector3 peerFromTarget = peer.position - targetPoint.position;
                        peerFromTarget.y = 0f;
                        if (fromTarget.sqrMagnitude > .01f && peerFromTarget.sqrMagnitude > .01f)
                        {
                            float angSelf = Mathf.Atan2(fromTarget.z, fromTarget.x);
                            float angPeer = Mathf.Atan2(peerFromTarget.z, peerFromTarget.x);
                            float delta = Mathf.DeltaAngle(angPeer * Mathf.Rad2Deg, angSelf * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                            float minAngle = minDistance / Mathf.Max(captureRadius, 1f);
                            if (Mathf.Abs(delta) < minAngle)
                            {
                                float sign = delta >= 0f ? 1f : -1f;
                                if (Mathf.Abs(delta) < .001f)
                                    sign = self.GetInstanceID() > peer.GetInstanceID() ? 1f : -1f;
                                float newAng = angPeer + sign * minAngle;
                                float radius = fromTarget.magnitude;
                                adjusted = targetPoint.position + new Vector3(Mathf.Cos(newAng) * radius, 0f, Mathf.Sin(newAng) * radius);
                                adjusted.y = desired.y;
                            }
                        }
                    }
                }
            }

            adjusted.y = desired.y;
            return adjusted;
        }

        private void EnforcePeerClearance(Transform[] agents, float minDistance, bool clampWater)
        {
            if (agents == null)
                return;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < agents.Length; i++)
                {
                    Transform a = agents[i];
                    if (!a)
                        continue;

                    for (int j = i + 1; j < agents.Length; j++)
                    {
                        Transform b = agents[j];
                        if (!b)
                            continue;

                        Vector3 pa = a.position;
                        Vector3 pb = b.position;
                        float dist = HorizontalDistance(pa, pb);
                        if (dist >= minDistance)
                            continue;

                        Vector3 separation = pa - pb;
                        separation.y = 0f;
                        if (separation.sqrMagnitude < .001f)
                        {
                            float angle = (i * 97f + j * 53f) * Mathf.Deg2Rad;
                            separation = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        }
                        else
                        {
                            separation.Normalize();
                        }

                        float overlap = minDistance - Mathf.Max(dist, .05f);
                        float correction = Mathf.Min(
                            overlap * .5f,
                            Mathf.Max(.1f, peerCorrectionSpeed) * Time.deltaTime
                        );
                        Vector3 awayA = pa + separation * correction;
                        Vector3 awayB = pb - separation * correction;
                        awayA.y = pa.y;
                        awayB.y = pb.y;

                        if (clampWater)
                        {
                            Vector3 enuA = Coordinates.ToEnu(awayA);
                            Vector3 enuB = Coordinates.ToEnu(awayB);
                            if (!IsLand(new Vector2(enuA.x, enuA.y)))
                                a.position = awayA;
                            if (!IsLand(new Vector2(enuB.x, enuB.y)))
                                b.position = awayB;
                        }
                        else
                        {
                            a.position = awayA;
                            b.position = awayB;
                        }

                        activeAvoidanceCount++;
                    }
                }
            }
        }

        private float DroneAltitudeFor(int index)
        {
            // Stagger heights so drones never occupy the same air slab.
            return droneAltitude + (index - 1) * 1.6f;
        }

        private Vector3 SlotAroundTarget(float angleDegrees, float radius, float height, float angularSpeed)
        {
            Vector3 target = targetPoint.position;
            float angle = angleDegrees * Mathf.Deg2Rad + MissionElapsed * angularSpeed;
            Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            return new Vector3(target.x + offset.x, height, target.z + offset.z);
        }

        private Vector3 SoftProjectToRing(Vector3 point, float radius, float height, float inwardSlack, float outwardSlack)
        {
            if (!targetPoint)
                return point;

            Vector3 fromTarget = point - targetPoint.position;
            fromTarget.y = 0f;
            if (fromTarget.sqrMagnitude < .001f)
                fromTarget = Vector3.forward;

            float distance = fromTarget.magnitude;
            float clamped = Mathf.Clamp(distance, Mathf.Max(1f, radius - inwardSlack), radius + outwardSlack);
            Vector3 result = targetPoint.position + fromTarget.normalized * clamped;
            result.y = height;
            return result;
        }

        private Vector3 ProjectToRing(Vector3 point, float radius, float height)
        {
            return SoftProjectToRing(point, radius, height, 0f, 0f);
        }

        private Vector3 AvoidDynamicBarrier(Vector3 desired, float clearance)
        {
            if (!dynamicBarrier || !dynamicBarrier.gameObject.activeInHierarchy)
                return desired;

            float keep = ObstacleRadius(dynamicBarrier) + clearance + .75f;
            return HorizontalDistance(desired, dynamicBarrier.position) < keep
                ? PushAway(desired, dynamicBarrier.position, keep)
                : desired;
        }

        private Vector3 KeepOutsideTarget(Vector3 point, float minimumRadius)
        {
            return KeepClearOfTargetHull(point, minimumRadius);
        }

        private Vector3 KeepClearOfTargetHull(Vector3 point)
        {
            return KeepClearOfTargetHull(point, boatTargetClearance);
        }

        private Vector3 KeepClearOfTargetHull(Vector3 point, float minimumRadius)
        {
            Transform hub = targetVessel && targetVessel.gameObject.activeInHierarchy
                ? targetVessel
                : targetPoint;
            if (!hub)
                return point;

            float keep = Mathf.Max(minimumRadius, boatTargetClearance);
            if (HorizontalDistance(point, hub.position) >= keep)
                return point;

            Vector3 cleared = PushAway(point, hub.position, keep);
            return ClampToWater(cleared, point.y);
        }

        private void ResolveBoatTargetClearance()
        {
            Transform hub = targetVessel && targetVessel.gameObject.activeInHierarchy
                ? targetVessel
                : targetPoint;
            if (!hub || boats == null)
                return;

            float keep = boatTargetClearance;
            for (int i = 0; i < boats.Length; i++)
            {
                Transform boat = boats[i];
                if (!boat)
                    continue;

                float dist = HorizontalDistance(boat.position, hub.position);
                if (dist >= keep)
                    continue;

                // Split the correction: push boat out more, ease target away a little.
                Vector3 boatPos = PushAway(boat.position, hub.position, keep);
                boatPos = ClampToWater(boatPos, .42f);
                boat.position = Vector3.MoveTowards(boat.position, boatPos, 3.5f * Time.deltaTime);

                if (!captureComplete && targetPoint)
                {
                    Vector3 targetPos = hub.position;
                    Vector3 away = targetPos - boat.position;
                    away.y = 0f;
                    if (away.sqrMagnitude > .001f)
                    {
                        Vector3 eased = targetPos + away.normalized * .35f;
                        eased = ClampTargetToWater(targetPos, eased, .38f);
                        eased = LimitTargetStep(targetPos, eased, targetEscapeSpeed);
                        SetTargetPose(eased);
                    }
                }
            }
        }

        private Vector3 AvoidStaticObstacles(Vector3 current, Vector3 desired, float clearance)
        {
            return AvoidBlockingObstacles(current, desired, clearance, -1);
        }

        private Vector3 AvoidBlockingObstacles(Vector3 current, Vector3 desired, float clearance)
        {
            return AvoidBlockingObstacles(current, desired, clearance, -1);
        }

        private Vector3 AvoidBlockingObstacles(
            Vector3 current,
            Vector3 desired,
            float clearance,
            int boatIndex)
        {
            Vector3 adjusted = desired;
            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Length; i++)
                {
                    Transform obstacle = obstacles[i];
                    if (!obstacle || !obstacle.gameObject.activeInHierarchy)
                        continue;
                    if (obstacle.name.Contains("ShoreBase"))
                        continue;
                    adjusted = SteerAroundObstacle(current, adjusted, obstacle, clearance, boatIndex);
                }
            }

            if (dynamicBarrier && dynamicBarrier.gameObject.activeInHierarchy)
                adjusted = SteerAroundObstacle(current, adjusted, dynamicBarrier, clearance, boatIndex);

            return adjusted;
        }

        /// <summary>
        /// Smooth single-side bypass around a circular obstacle. Side is locked per boat so
        /// trajectories do not zigzag left/right every frame.
        /// </summary>
        private Vector3 SteerAroundObstacle(
            Vector3 current,
            Vector3 desired,
            Transform obstacle,
            float clearance,
            int boatIndex)
        {
            if (!obstacle)
                return desired;

            float keep = ObstacleRadius(obstacle) + clearance;
            float distCurrent = HorizontalDistance(current, obstacle.position);
            float distDesired = HorizontalDistance(desired, obstacle.position);

            // Already past the obstacle toward the goal — do not keep steering sideways.
            Vector3 obsToGoal = Flatten(desired - obstacle.position);
            Vector3 obsToBoat = Flatten(current - obstacle.position);
            if (obsToGoal.sqrMagnitude > .01f &&
                distCurrent > keep * .92f &&
                Vector3.Dot(obsToBoat, obsToGoal) > 0f &&
                SegmentDistance(current, desired, obstacle.position) >= keep)
            {
                return desired;
            }

            bool blocked =
                distDesired < keep ||
                distCurrent < keep ||
                SegmentDistance(current, desired, obstacle.position) < keep;
            if (!blocked)
                return desired;

            int sign = ResolveBypassSign(boatIndex, current, obstacle, desired);
            Vector3 forward = Flatten(desired - current);
            if (forward.sqrMagnitude < .01f)
                forward = Flatten(desired - obstacle.position);
            if (forward.sqrMagnitude < .01f)
                forward = Vector3.right;
            forward.Normalize();
            Vector3 lateral = Vector3.Cross(Vector3.up, forward).normalized;

            // One clear waypoint: beside the buoy, then a bit past it along the goal heading.
            Vector3 waypoint =
                obstacle.position +
                lateral * (keep * 1.08f * sign) +
                forward * Mathf.Max(keep * .75f, 4f);
            waypoint.y = desired.y;

            // If still inside the keep-out at current pose, bias farther outward first.
            if (distCurrent < keep)
            {
                Vector3 flee = PushAway(current, obstacle.position, keep + 1.5f);
                flee.y = desired.y;
                // Blend flee with forward bypass so the boat slides around instead of bouncing.
                waypoint = Vector3.Lerp(flee, waypoint, .55f);
                waypoint.y = desired.y;
            }

            return waypoint;
        }

        private int ResolveBypassSign(
            int boatIndex,
            Vector3 current,
            Transform obstacle,
            Vector3 desired)
        {
            EnsureProgressBuffers();
            if (boatIndex >= 0 &&
                boatIndex < boatBypassSign.Length &&
                boatBypassObstacle[boatIndex] == obstacle &&
                Time.time < boatBypassUntil[boatIndex] &&
                boatBypassSign[boatIndex] != 0)
            {
                return boatBypassSign[boatIndex];
            }

            Vector3 forward = Flatten(desired - current);
            if (forward.sqrMagnitude < .01f)
                forward = Flatten(desired - obstacle.position);
            if (forward.sqrMagnitude < .01f)
                forward = Vector3.right;
            forward.Normalize();
            Vector3 lateral = Vector3.Cross(Vector3.up, forward);
            Vector3 fromObs = Flatten(current - obstacle.position);
            int sign = Vector3.Dot(fromObs, lateral) >= 0f ? 1 : -1;
            // Stable fallback when sitting on the centerline.
            if (Mathf.Abs(Vector3.Dot(fromObs.normalized, lateral.normalized)) < .08f)
                sign = boatIndex >= 0 && (boatIndex % 2 == 0) ? 1 : -1;

            if (boatIndex >= 0 && boatIndex < boatBypassSign.Length)
            {
                boatBypassObstacle[boatIndex] = obstacle;
                boatBypassSign[boatIndex] = sign;
                boatBypassUntil[boatIndex] = Time.time + Mathf.Max(3.5f, detourCommitSeconds);
            }

            return sign;
        }

        /// <summary>
        /// Radial push only — used on movement micro-steps so destination steering is not
        /// re-decided every frame (which caused the zigzag tracks).
        /// </summary>
        private Vector3 PushOutOfObstacles(Vector3 point, float clearance)
        {
            Vector3 adjusted = point;
            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Length; i++)
                {
                    Transform obstacle = obstacles[i];
                    if (!obstacle || !obstacle.gameObject.activeInHierarchy ||
                        obstacle.name.Contains("ShoreBase"))
                        continue;

                    float keep = ObstacleRadius(obstacle) + clearance;
                    if (HorizontalDistance(adjusted, obstacle.position) < keep)
                        adjusted = PushAway(adjusted, obstacle.position, keep);
                }
            }

            if (dynamicBarrier && dynamicBarrier.gameObject.activeInHierarchy)
            {
                float keep = ObstacleRadius(dynamicBarrier) + clearance;
                if (HorizontalDistance(adjusted, dynamicBarrier.position) < keep)
                    adjusted = PushAway(adjusted, dynamicBarrier.position, keep);
            }

            return adjusted;
        }

        private bool TooCloseToNavigationObstacle(Vector3 point, float clearance)
        {
            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Length; i++)
                {
                    Transform obstacle = obstacles[i];
                    if (!obstacle || !obstacle.gameObject.activeInHierarchy ||
                        obstacle.name.Contains("ShoreBase"))
                        continue;

                    if (HorizontalDistance(point, obstacle.position) <
                        ObstacleRadius(obstacle) + clearance)
                        return true;
                }
            }

            return dynamicBarrier &&
                   dynamicBarrier.gameObject.activeInHierarchy &&
                   HorizontalDistance(point, dynamicBarrier.position) <
                   ObstacleRadius(dynamicBarrier) + clearance;
        }

        private static float ObstacleRadius(Transform obstacle)
        {
            string name = obstacle.name;
            if (name.Contains("Lighthouse"))
                return 5.5f;
            if (name.Contains("Buoy"))
                return 2.2f;
            if (name.Contains("Barrier"))
                return 2.8f;
            if (name.Contains("ShoreBase"))
                return 4f;
            if (name.Contains("Target"))
                return 6.5f;
            return 3.2f;
        }

        private static Vector3 PushAway(Vector3 point, Vector3 obstacle, float radius)
        {
            Vector3 away = point - obstacle;
            away.y = 0f;
            if (away.sqrMagnitude < .001f)
                away = Vector3.right;
            Vector3 result = obstacle + away.normalized * radius;
            result.y = point.y;
            return result;
        }

        private static Vector3 Flatten(Vector3 point)
        {
            point.y = 0f;
            return point;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static float SegmentDistance(Vector3 start, Vector3 end, Vector3 point)
        {
            Vector2 a = new Vector2(start.x, start.z);
            Vector2 b = new Vector2(end.x, end.z);
            Vector2 p = new Vector2(point.x, point.z);
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > .001f
                ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude)
                : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        private void MoveSurfaceAgent(Transform agent, Vector3 destination, float speed, bool fullSpeed = false)
        {
            int boatIndex = FindBoatIndex(agent);
            destination = ClampToWater(destination, .42f);
            destination = AvoidBlockingObstacles(
                agent.position,
                destination,
                SurfaceObstacleClearance,
                boatIndex
            );
            destination = ClampToWater(destination, .42f);
            destination = SoftAvoidPeers(agent, destination, boats, agentSeparation * .85f);
            destination = KeepClearOfTargetHull(destination);
            destination = ClampToWater(destination, .42f);

            Vector3 toDestination = destination - agent.position;
            toDestination.y = 0f;
            float distance = toDestination.magnitude;
            float currentSpeed = surfaceSpeedState.TryGetValue(agent, out float storedSpeed)
                ? storedSpeed
                : 0f;
            if (distance > .15f)
            {
                Vector3 desiredDirection = toDestination / distance;
                Quaternion desiredRotation =
                    Quaternion.LookRotation(desiredDirection, Vector3.up) *
                    Quaternion.Euler(0f, -90f, 0f);
                float turnError = Quaternion.Angle(agent.rotation, desiredRotation);
                agent.rotation = Quaternion.RotateTowards(
                    agent.rotation,
                    desiredRotation,
                    Mathf.Max(1f, surfaceMaxTurnRate) * Time.deltaTime
                );

                float factor = fullSpeed ? 1f : Mathf.Max(.5f, Mathf.Clamp01(distance / 12f));
                float turnScale = Mathf.Lerp(
                    1f,
                    .22f,
                    Mathf.Clamp01(turnError / Mathf.Max(10f, surfaceTurnSlowdownAngle))
                );
                float desiredSpeed = speed * factor * turnScale;
                currentSpeed = Mathf.MoveTowards(
                    currentSpeed,
                    desiredSpeed,
                    Mathf.Max(.1f, surfaceAcceleration) * Time.deltaTime
                );

                Vector3 forward = agent.right;
                forward.y = 0f;
                if (forward.sqrMagnitude > .001f)
                    forward.Normalize();
                float step = Mathf.Min(currentSpeed * Time.deltaTime, distance);
                Vector3 next = agent.position + forward * step;
                next.y = .42f;
                next = KeepClearOfTargetHull(next);
                // Micro-step: radial push only — do not re-pick left/right each frame.
                next = PushOutOfObstacles(next, SurfaceObstacleClearance);
                next = ClampToWater(next, .42f);
                Vector3 nextEnu = Coordinates.ToEnu(next);
                if (!IsLand(new Vector2(nextEnu.x, nextEnu.y)))
                    agent.position = next;
                else
                    currentSpeed = 0f;
            }
            else
            {
                currentSpeed = Mathf.MoveTowards(
                    currentSpeed,
                    0f,
                    Mathf.Max(.1f, surfaceAcceleration) * Time.deltaTime
                );
            }
            surfaceSpeedState[agent] = currentSpeed;
        }

        private void BrakeSurfaceAgent(Transform agent)
        {
            Vector3 cleared = PushOutOfObstacles(agent.position, SurfaceObstacleClearance);
            cleared = KeepClearOfTargetHull(cleared);
            cleared = ClampToWater(cleared, .42f);
            if (HorizontalDistance(cleared, agent.position) > .15f)
            {
                agent.position = Vector3.MoveTowards(agent.position, cleared, 12f * Time.deltaTime);
                surfaceSpeedState[agent] = 0f;
                return;
            }

            float current = surfaceSpeedState.TryGetValue(agent, out float stored) ? stored : 0f;
            float nextSpeed = Mathf.MoveTowards(
                current,
                0f,
                Mathf.Max(.1f, surfaceAcceleration) * Time.deltaTime
            );
            Vector3 forward = agent.right;
            forward.y = 0f;
            if (forward.sqrMagnitude > .001f && nextSpeed > .01f)
            {
                forward.Normalize();
                Vector3 next = agent.position + forward * nextSpeed * Time.deltaTime;
                next.y = .42f;
                Vector3 nextEnu = Coordinates.ToEnu(next);
                if (!IsLand(new Vector2(nextEnu.x, nextEnu.y)))
                    agent.position = next;
                else
                    nextSpeed = 0f;
            }
            surfaceSpeedState[agent] = nextSpeed;
        }

        private void RotateSurfaceToward(Transform agent, Vector3 point)
        {
            Vector3 direction = point - agent.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < .001f)
                return;
            Quaternion facing =
                Quaternion.LookRotation(direction.normalized, Vector3.up) *
                Quaternion.Euler(0f, -90f, 0f);
            agent.rotation = Quaternion.RotateTowards(
                agent.rotation,
                facing,
                Mathf.Max(1f, surfaceMaxTurnRate) * Time.deltaTime
            );
        }

        private bool IsLand(Vector2 enu)
        {
            if (coastlineColliders == null || coastlineColliders.Length == 0)
                return enu.x < -100f || enu.x > 108f || enu.y < -140f || enu.y > 72f;

            // Center sample only — margin sampling was falsely marking open water as land
            // and trapping boats in local loops near the lighthouse/shore.
            Ray ray = new Ray(Coordinates.ToUnity(enu.x, enu.y, 80f), Vector3.down);
            for (int i = 0; i < coastlineColliders.Length; i++)
            {
                Collider coast = coastlineColliders[i];
                if (coast && coast.Raycast(ray, out _, 160f))
                    return true;
            }
            return false;
        }

        private Vector2 FindNearestWater(Vector2 enu, Vector2 preferToward)
        {
            if (!IsLand(enu))
                return enu;

            Vector2 toward = preferToward - enu;
            if (toward.sqrMagnitude < .01f)
                toward = new Vector2(targetCenterEnu.x, targetCenterEnu.y) - enu;
            if (toward.sqrMagnitude < .01f)
                toward = Vector2.right;
            toward.Normalize();

            for (float step = 2f; step <= 80f; step += 2f)
            {
                Vector2 candidate = enu + toward * step;
                if (!IsLand(candidate))
                    return candidate;

                // Spiral search around the preferred direction.
                for (int k = 0; k < 8; k++)
                {
                    float ang = k * .785398f;
                    Vector2 offset = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * step;
                    candidate = enu + offset;
                    if (!IsLand(candidate))
                        return candidate;
                }
            }

            return new Vector2(targetCenterEnu.x, targetCenterEnu.y);
        }

        private Vector3 ClampToWater(Vector3 unityPos, float height)
        {
            Vector3 enu = Coordinates.ToEnu(unityPos);
            Vector2 xy = new Vector2(enu.x, enu.y);
            if (!IsLand(xy))
            {
                unityPos.y = height;
                return unityPos;
            }

            Vector2 safe = FindNearestWater(xy, new Vector2(targetCenterEnu.x, targetCenterEnu.y));
            return Coordinates.ToUnity(safe.x, safe.y, height);
        }

        private void MoveAirAgent(Transform agent, Vector3 destination, float speed, bool fullSpeed = false)
        {
            destination = SoftAvoidPeers(agent, destination, drones, droneSeparation * .85f);
            Vector3 toDestination = destination - agent.position;
            float distance = toDestination.magnitude;
            Vector3 velocity = airVelocityState.TryGetValue(agent, out Vector3 storedVelocity)
                ? storedVelocity
                : Vector3.zero;
            if (distance > .15f)
            {
                float factor = fullSpeed ? 1f : Mathf.Max(.45f, Mathf.Clamp01(distance / 10f));
                Vector3 desiredVelocity = toDestination.normalized * speed * factor;
                velocity = Vector3.MoveTowards(
                    velocity,
                    desiredVelocity,
                    Mathf.Max(.1f, airAcceleration) * Time.deltaTime
                );
                Vector3 step = velocity * Time.deltaTime;
                if (step.magnitude > distance)
                    step = toDestination;
                agent.position += step;
            }
            else
            {
                velocity = Vector3.MoveTowards(
                    velocity,
                    Vector3.zero,
                    Mathf.Max(.1f, airAcceleration) * Time.deltaTime
                );
            }

            airVelocityState[agent] = velocity;
            Vector3 horizontalVelocity = velocity;
            horizontalVelocity.y = 0f;
            if (horizontalVelocity.sqrMagnitude > .001f)
                RotateAirToward(agent, agent.position + horizontalVelocity);
        }

        private void BrakeAirAgent(Transform agent)
        {
            Vector3 velocity = airVelocityState.TryGetValue(agent, out Vector3 stored)
                ? stored
                : Vector3.zero;
            Vector3 nextVelocity = Vector3.MoveTowards(
                velocity,
                Vector3.zero,
                Mathf.Max(.1f, airAcceleration) * Time.deltaTime
            );
            agent.position += nextVelocity * Time.deltaTime;
            airVelocityState[agent] = nextVelocity;
        }

        private void RotateAirToward(Transform agent, Vector3 point)
        {
            Vector3 direction = point - agent.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < .001f)
                return;
            Quaternion facing = Quaternion.LookRotation(direction.normalized, Vector3.up);
            agent.rotation = Quaternion.RotateTowards(
                agent.rotation,
                facing,
                Mathf.Max(1f, airMaxTurnRate) * Time.deltaTime
            );
        }

        private void AnimateBarrier()
        {
            if (!dynamicBarrier)
                return;

            float missionTime = Time.time - scenarioStarted - dispatchDelaySeconds;
            if (missionTime < 0f)
            {
                dynamicBarrier.position = barrierStartPosition + new Vector3(-8f, 0f, 0f);
                return;
            }

            Vector3 start = barrierStartPosition + new Vector3(-8f, 0f, 0f);
            Vector3 end = barrierStartPosition + new Vector3(14f, 0f, 6f);

            // Only demo dynamic avoidance for a couple of passes, then park.
            if (missionTime >= barrierDemoSeconds)
            {
                dynamicBarrier.position = end;
                return;
            }

            float passDuration = Mathf.Max(.5f, barrierDemoSeconds / Mathf.Max(1, barrierDemoPasses));
            int pass = Mathf.Min(barrierDemoPasses - 1, Mathf.FloorToInt(missionTime / passDuration));
            float u = Mathf.Clamp01((missionTime - pass * passDuration) / passDuration);
            // Ease across the channel; odd passes reverse.
            bool reverse = (pass % 2) == 1;
            dynamicBarrier.position = Vector3.Lerp(reverse ? end : start, reverse ? start : end, u);
        }

        private static void FaceToward(Transform source, Vector3 target)
        {
            Vector3 delta = target - source.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > .001f)
                source.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
        }

        private void BuildLineVisuals()
        {
            Transform[] agents = Agents();
            for (int i = 0; i < agents.Length; i++)
            {
                commandLinks.Add(CreateLine("Base Command Link " + i, new Color(.2f, .95f, 1f, .54f), .08f));
                sensorRings.Add(CreateLine("Sensor Coverage " + i, new Color(.18f, 1f, .42f, .34f), .07f));
                tracks.Add(CreateLine("Agent Track " + i, new Color(1f, .58f, .08f, .68f), .1f));
                trackPoints.Add(new List<Vector3>());
            }

            captureRing = CreateLine("USV Capture Ring", new Color(.12f, .62f, 1f, .85f), .16f);
            defenseRing = CreateLine("UAV Escort Ring", new Color(.12f, .62f, 1f, .72f), .12f);
            guardArcLine = CreateLine("USV Guard Arc", new Color(.12f, .62f, 1f, .88f), .14f);
            blockerMarker = CreateLine("Blocker Point", new Color(1f, .15f, .15f, .95f), .18f);
            detectionLockLine = CreateLine("Detection Lock Beam", new Color(1f, .92f, .15f, .95f), .22f);

            scanCueLines.Clear();
            int boatCount = boats != null ? boats.Length : 0;
            for (int i = 0; i < boatCount; i++)
                scanCueLines.Add(CreateLine("Scan Cue " + i, new Color(.2f, 1f, .55f, .55f), .08f));
        }

        private LineRenderer CreateLine(string name, Color color, float width)
        {
            var go = new GameObject(name);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = SceneFactory.Material(name + " Material", color, 0f, .5f);
            line.widthMultiplier = width;
            line.numCapVertices = 3;
            line.numCornerVertices = 3;
            line.useWorldSpace = true;
            line.positionCount = 0;
            return line;
        }

        private Transform[] Agents()
        {
            var all = new List<Transform>();
            if (boats != null)
                all.AddRange(boats);
            if (drones != null)
                all.AddRange(drones);
            return all.ToArray();
        }

        private void UpdateLineVisuals()
        {
            Transform[] agents = Agents();
            Vector3 baseAnchor = shoreBase ? shoreBase.position + Vector3.up * 7.5f : Vector3.zero;
            int boatCount = boats?.Length ?? 0;
            for (int i = 0; i < agents.Length; i++)
            {
                Transform agent = agents[i];
                if (!agent)
                    continue;

                bool isBoat = i < boatCount;
                bool showLink = targetDetected || isBoat;
                LineRenderer link = commandLinks[i];
                if (showLink)
                {
                    link.positionCount = 2;
                    link.SetPosition(0, baseAnchor);
                    link.SetPosition(1, agent.position + Vector3.up * (isBoat ? 1.2f : .2f));
                }
                else
                {
                    link.positionCount = 0;
                }

                float range;
                if (isBoat)
                {
                    // Match acquire logic: detection uses max(lidar, radar), not lidar alone.
                    range = boatSensors != null && i < boatSensors.Length && boatSensors[i]
                        ? Mathf.Max(boatSensors[i].lidarRange, boatSensors[i].radarRange)
                        : sensorRange;
                }
                else
                {
                    int di = i - boatCount;
                    range = droneSensors != null && di >= 0 && di < droneSensors.Length && droneSensors[di]
                        ? Mathf.Max(droneSensors[di].lidarRange, droneSensors[di].radarRange)
                        : sensorRange;
                }
                float height = isBoat ? .65f : (dronesAirborne ? droneAltitude : .8f);
                DrawCircle(sensorRings[i], agent.position, isBoat || dronesAirborne ? range : 4f, height);
            }
        }

        private void UpdateDetectionVisuals()
        {
            Transform senseTarget = targetVessel ? targetVessel : targetPoint;
            Vector3 targetPos = senseTarget
                ? senseTarget.position + Vector3.up * 1.2f
                : Vector3.zero;

            // While searching: show a cue as soon as a USV has the target in range/FOV.
            for (int i = 0; i < scanCueLines.Count; i++)
            {
                LineRenderer cue = scanCueLines[i];
                if (!cue)
                    continue;

                if (targetDetected || !senseTarget ||
                    boats == null || i >= boats.Length || !boats[i])
                {
                    cue.positionCount = 0;
                    continue;
                }

                Transform boat = boats[i];
                float range = EffectiveSearchAcquireRange(i);
                float dist = HorizontalDistance(boat.position, senseTarget.position);
                if (dist > range)
                {
                    cue.positionCount = 0;
                    continue;
                }

                // Approximate forward FOV using boat right axis (hull facing).
                Vector3 forward = boat.right;
                forward.y = 0f;
                Vector3 toTarget = senseTarget.position - boat.position;
                toTarget.y = 0f;
                float fov = boatSensors != null && i < boatSensors.Length && boatSensors[i]
                    ? boatSensors[i].horizontalFovDegrees * .5f + 8f
                    : 90f;
                if (forward.sqrMagnitude > .001f && toTarget.sqrMagnitude > .001f &&
                    Vector3.Angle(forward.normalized, toTarget.normalized) > fov)
                {
                    cue.positionCount = 0;
                    continue;
                }

                cue.enabled = true;
                cue.positionCount = 2;
                cue.SetPosition(0, boat.position + Vector3.up * 1.1f);
                cue.SetPosition(1, targetPos);
                detectionModeText = boat.name + " 扫描中… 距目标 " + dist.ToString("0") + "m";
            }

            // On contact: bright lock beam from reporter boat to target for several seconds.
            if (detectionLockLine)
            {
                bool showLock = detectionCueUntil > Time.time &&
                                detectionBoatIndex >= 0 &&
                                boats != null &&
                                detectionBoatIndex < boats.Length &&
                                boats[detectionBoatIndex] &&
                                senseTarget;
                if (showLock)
                {
                    float pulse = .85f + .15f * Mathf.Sin(Time.time * 10f);
                    detectionLockLine.enabled = true;
                    detectionLockLine.widthMultiplier = .18f + .08f * pulse;
                    detectionLockLine.positionCount = 2;
                    detectionLockLine.SetPosition(0, boats[detectionBoatIndex].position + Vector3.up * 1.4f);
                    detectionLockLine.SetPosition(1, targetPos);
                }
                else
                {
                    detectionLockLine.positionCount = 0;
                }
            }
        }

        private void UpdateMissionRings()
        {
            if (!targetPoint || !targetDetected)
            {
                if (captureRing)
                    captureRing.positionCount = 0;
                if (defenseRing)
                    defenseRing.positionCount = 0;
                if (guardArcLine)
                    guardArcLine.positionCount = 0;
                if (blockerMarker)
                    blockerMarker.positionCount = 0;
                return;
            }

            if (defenseEscortActive || defenseComplete)
            {
                Vector3 own = protectedOwnCenter.sqrMagnitude > .01f
                    ? protectedOwnCenter
                    : GetProtectedOwnCenter();
                // Red = threat keep-out around enemy; blue = escort ring around protected own.
                DrawCircle(captureRing, targetPoint.position, Mathf.Max(10f, boatTargetClearance + 2f), .72f);
                DrawCircle(defenseRing, own, defenseEscortRadius, droneAltitude);
                UpdateEscortGuardOverlays(own, defenseGuardRadius);
                return;
            }

            // Red ring shrinks with the boats so the close-in process is visible.
            DrawCircle(captureRing, targetPoint.position, CurrentBoatRingRadius(), .72f);
            DrawCircle(defenseRing, targetPoint.position, defenseRadius, droneAltitude);
            // Guard arc / blocker are defense-phase overlays only.
            if (guardArcLine)
                guardArcLine.positionCount = 0;
            if (blockerMarker)
                blockerMarker.positionCount = 0;
        }

        private void UpdateEscortGuardOverlays(Vector3 center, float ring)
        {
            if (!useEscortGuardFormation || !defenseEscortActive || !escortGuardPlanned)
            {
                if (guardArcLine)
                    guardArcLine.positionCount = 0;
                if (blockerMarker)
                    blockerMarker.positionCount = 0;
                return;
            }

            int wingCount = 0;
            if (boatWingSlotByIndex != null)
            {
                for (int i = 0; i < boatWingSlotByIndex.Length; i++)
                {
                    if (boatWingSlotByIndex[i] >= 0)
                        wingCount++;
                }
            }

            float half = EscortGuardGeometry.EffectiveGuardArcHalfAngle(
                Mathf.Max(1, wingCount),
                guardArcHalfAngleDeg * Mathf.Deg2Rad,
                ring,
                minimumGuardSpacing * (ring / Mathf.Max(captureRadius, 1f)));

            if (guardArcLine)
            {
                const int segments = 24;
                guardArcLine.positionCount = segments + 1;
                Vector3 threat = escortThreatDir;
                Vector3 lateral = EscortGuardGeometry.Rotate90(threat);
                for (int i = 0; i <= segments; i++)
                {
                    float phi = Mathf.Lerp(-half, half, i / (float)segments);
                    Vector3 offset = ring * (Mathf.Cos(phi) * threat + Mathf.Sin(phi) * lateral);
                    guardArcLine.SetPosition(i, new Vector3(center.x + offset.x, .78f, center.z + offset.z));
                }
            }

            if (blockerMarker)
            {
                if (!TryEscortGuardBoatSlot(coreBoatIndex, center, ring, out Vector3 slot))
                    slot = center + escortThreatDir * ring;
                slot.y = .85f;
                float arm = 1.6f;
                blockerMarker.positionCount = 5;
                blockerMarker.SetPosition(0, slot + new Vector3(-arm, 0f, -arm));
                blockerMarker.SetPosition(1, slot + new Vector3(arm, 0f, arm));
                blockerMarker.SetPosition(2, slot);
                blockerMarker.SetPosition(3, slot + new Vector3(-arm, 0f, arm));
                blockerMarker.SetPosition(4, slot + new Vector3(arm, 0f, -arm));
            }
        }

        private void DrawCircle(LineRenderer line, Vector3 center, float radius, float height)
        {
            const int segments = 72;
            line.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                line.SetPosition(i, new Vector3(center.x + Mathf.Cos(angle) * radius, height, center.z + Mathf.Sin(angle) * radius));
            }
        }

        private void UpdateTracks()
        {
            Transform[] agents = Agents();
            int boatCount = boats?.Length ?? 0;
            for (int i = 0; i < agents.Length && i < trackPoints.Count; i++)
            {
                Transform agent = agents[i];
                if (!agent)
                    continue;

                // Drones only leave tracks after takeoff.
                if (i >= boatCount && !dronesAirborne)
                {
                    tracks[i].positionCount = 0;
                    continue;
                }

                List<Vector3> points = trackPoints[i];
                Vector3 p = agent.position;
                p.y = i < boatCount ? .72f : .18f;
                if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], p) > 1.25f)
                {
                    points.Add(p);
                    if (points.Count > 180)
                        points.RemoveAt(0);
                }

                tracks[i].positionCount = points.Count;
                for (int j = 0; j < points.Count; j++)
                    tracks[i].SetPosition(j, points[j]);
            }
        }

        private void OnGUI()
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return;

            const float uiScale = 1f;
            if (titleStyle == null || !Mathf.Approximately(guiStyleScale, uiScale))
            {
                guiStyleScale = uiScale;
                titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(20f * uiScale),
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                };
                bodyStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(13f * uiScale),
                    normal = { textColor = new Color(.9f, .96f, 1f) }
                };
                buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = Mathf.RoundToInt(13f * uiScale)
                };
            }
            var bannerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(22f * uiScale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            string baseStatus = baseController ? baseController.status : "Local auto";
            string stageLine = BuildStageChecklist();
            string targetPosText = FormatTargetEnu();
            int boatHits = CountHits(boatSensors);
            int droneHits = dronesAirborne ? CountHits(droneSensors) : 0;

            // Large center banner so the audience can read the current step.
            Rect banner = new Rect(
                Screen.width * .18f,
                18f * uiScale,
                Screen.width * .64f,
                46f * uiScale
            );
            GUI.Box(banner, "");
            GUI.Label(banner, phase, bannerStyle);

            Rect panel = new Rect(
                16f * uiScale,
                72f * uiScale,
                460f * uiScale,
                348f * uiScale
            );
            GUI.Box(panel, "");
            GUI.Label(
                new Rect(
                    30f * uiScale,
                    82f * uiScale,
                    420f * uiScale,
                    28f * uiScale
                ),
                "海空协同围捕+护航防卫 — 3USV + 3UAV",
                titleStyle
            );
            GUI.Label(
                new Rect(
                    30f * uiScale,
                    114f * uiScale,
                    420f * uiScale,
                    258f * uiScale
                ),
                "流程：三向接近搜索 → 发现上报 → 下令缩圈围捕 → 护航防卫\n" +
                stageLine + "\n\n" +
                "当前: " + phase + "\n" +
                "岸基: " + baseStatus + "\n" +
                "上报方: " + (targetDetected ? detectReporter : "尚未发现") + "\n" +
                "发现方式: " + detectionModeText + "\n" +
                "传感: 激光/雷达命中 艇" + boatHits + " 机" + droneHits + "\n" +
                "敌方位置: " + targetPosText + "\n" +
                "绿圈=传感覆盖  黄线=锁定  红圈=围捕/威胁区  蓝圈=防守圈\n" +
                "编队: 搜索=三向接近  围捕=等边三角  防卫=岸基阻断护航\n" +
                "标注: 我方USV/UAV  敌方目标(黑船)\n" +
                "UAV: " + (dronesTakingOff ? "起飞中" : (dronesAirborne ? (defenseEscortActive ? "防卫护航" : "空中护航") : "停机坪")) +
                (defenseComplete ? "  防卫锁定" : (captureComplete ? "  围捕锁定" : "")) + "\n" +
                "M暂停 R重置 B强制上报 V轨迹/绿圈 " + (showDebugOverlays ? "开" : "关"),
                bodyStyle
            );

            if (GUI.Button(
                    new Rect(
                        30f * uiScale,
                        376f * uiScale,
                        92f * uiScale,
                        28f * uiScale
                    ),
                    automatic ? "Pause" : "Resume",
                    buttonStyle))
                automatic = !automatic;
            if (GUI.Button(
                    new Rect(
                        132f * uiScale,
                        376f * uiScale,
                        92f * uiScale,
                        28f * uiScale
                    ),
                    "Reset",
                    buttonStyle))
                ResetScenario();
            if (GUI.Button(
                    new Rect(
                        234f * uiScale,
                        376f * uiScale,
                        108f * uiScale,
                        28f * uiScale
                    ),
                    showDebugOverlays ? "Hide Extra" : "Show Extra",
                    buttonStyle))
                showDebugOverlays = !showDebugOverlays;
        }

        private string FormatTargetEnu()
        {
            if (!targetPoint)
                return "-";
            Vector3 enu = Coordinates.ToEnu(targetPoint.position);
            // Demo ENU readout (Sydney local frame). Lat/Lon can be bridged later.
            return $"E {enu.x:0.0}  N {enu.y:0.0}  (ENU)";
        }

        private string BuildStageChecklist()
        {
            return
                (targetDetected ? "[✓]" : "[►]") + " ①搜索   " +
                (targetDetected ? (captureAuthorized ? "[✓]" : "[►]") : "[ ]") + " ②发现上报   " +
                (captureAuthorized ? (captureComplete ? "[✓]" : "[►]") : "[ ]") + " ③围捕   " +
                (captureComplete ? (defenseComplete ? "[✓]" : "[►]") : "[ ]") + " ④防卫";
        }

        private static int CountHits(AgentSensorSuite[] sensors)
        {
            if (sensors == null)
                return 0;
            int total = 0;
            for (int i = 0; i < sensors.Length; i++)
            {
                if (sensors[i])
                    total += sensors[i].hitCount;
            }
            return total;
        }

        private void ApplyOverlayVisibility()
        {
            // V：隐藏绿圈（传感覆盖）与轨迹；蓝圈（围捕/护航环）始终保留。
            SetLinesEnabled(commandLinks, showDebugOverlays);
            SetLinesEnabled(sensorRings, showDebugOverlays);
            SetLinesEnabled(tracks, showDebugOverlays);
            SetLinesEnabled(scanCueLines, showDebugOverlays);
            if (detectionLockLine)
                detectionLockLine.enabled = showDebugOverlays;
            SyncSensorDebugRays(showDebugOverlays);
            if (captureRing)
                captureRing.enabled = targetDetected;
            if (defenseRing)
                defenseRing.enabled = targetDetected && (dronesAirborne || defenseEscortActive);
            // 蓝圈 / 半蓝守卫弧在按 V 后仍保留。
            if (guardArcLine)
                guardArcLine.enabled = useEscortGuardFormation && defenseEscortActive;
            if (blockerMarker)
                blockerMarker.enabled = showDebugOverlays && useEscortGuardFormation && defenseEscortActive;
        }

        private void SyncSensorDebugRays(bool enabled)
        {
            if (boatSensors != null)
            {
                for (int i = 0; i < boatSensors.Length; i++)
                {
                    if (boatSensors[i])
                        boatSensors[i].drawDebugRays = enabled;
                }
            }
            if (droneSensors != null)
            {
                for (int i = 0; i < droneSensors.Length; i++)
                {
                    if (droneSensors[i])
                        droneSensors[i].drawDebugRays = enabled && dronesAirborne;
                }
            }
        }

        private static void SetLinesEnabled(List<LineRenderer> lines, bool enabled)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i])
                    lines[i].enabled = enabled;
            }
        }

        private void OnDestroy()
        {
            DestroyLines(commandLinks);
            DestroyLines(sensorRings);
            DestroyLines(tracks);
            DestroyLines(scanCueLines);
            DestroyLine(captureRing);
            DestroyLine(defenseRing);
            DestroyLine(guardArcLine);
            DestroyLine(blockerMarker);
            DestroyLine(detectionLockLine);
        }

        private static void DestroyLines(List<LineRenderer> lines)
        {
            for (int i = 0; i < lines.Count; i++)
                DestroyLine(lines[i]);
        }

        private static void DestroyLine(LineRenderer line)
        {
            if (line && line.sharedMaterial)
                Destroy(line.sharedMaterial);
            if (line)
                Destroy(line.gameObject);
        }
    }
}
