using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Exchanges authoritative Gazebo poses and Unity planning commands with ROS.
    /// Gazebo remains the owner of motion state.
    /// </summary>
    public sealed class ExternalPoseWebSocketClient : MonoBehaviour
    {
        [Serializable]
        private sealed class PoseData
        {
            public float[] position;
            public float[] orientation;
        }

        [Serializable]
        private sealed class VehicleStatusData
        {
            public bool online;
            public bool armed;
            public string mode;
            public float battery_percent;
            public string status_text;
        }

        [Serializable]
        private sealed class FleetVehicleData
        {
            public string id;
            public float[] position;
            public float[] orientation;
            public VehicleStatusData status;
        }

        [Serializable]
        private sealed class FleetData
        {
            public int expected_usvs;
            public int expected_uavs;
            public int received_usvs;
            public int received_uavs;
            public bool ready;
        }

        [Serializable]
        private sealed class CaptureStateData
        {
            public int state;
            public string state_name;
            public string target_id;
            public string reason;
            public int configured_uavs;
            public int configured_usvs;
            public int active_uavs;
            public int active_usvs;
            public int allocation_generation;
            public bool degraded;
        }

        [Serializable]
        private sealed class CaptureAssignmentData
        {
            public string vehicle_id;
            public string role_name;
            public bool active;
            public string status;
        }

        [Serializable]
        private sealed class CaptureRolesData
        {
            public string target_id;
            public float[] capture_center;
            public float capture_radius;
            public int generation;
            public CaptureAssignmentData[] assignments;
        }

        [Serializable]
        private sealed class MissionData
        {
            public CaptureStateData capture;
            public CaptureRolesData roles;
        }

        [Serializable]
        private sealed class PoseFrame
        {
            public int schema_version;
            public long timestamp_ms;
            public uint sequence;
            public FleetData fleet;
            public FleetVehicleData[] usvs;
            public FleetVehicleData[] uavs;
            public FleetVehicleData friendly_ship;
            public FleetVehicleData target;
            public PoseData boat;
            public PoseData drone;
            public PoseData lighthouse;
            public PoseData buoy_west;
            public PoseData buoy_south;
            public PoseData buoy_east;
            public PoseData target_vessel;
            public MissionData mission;
            public ControlData control;
        }

        [Serializable]
        private sealed class ControlData
        {
            public string state;
            public string message;
            public long path_id;
            public int waypoint_index;
            public int waypoint_count;
            public string mode;
        }

        [Serializable]
        private sealed class PathPoint
        {
            public float x;
            public float y;
        }

        [Serializable]
        private sealed class BoatPathCommand
        {
            public string type = "boat_path";
            public long path_id;
            public PathPoint[] points;
        }

        [Serializable]
        private sealed class BoatStopCommand
        {
            public string type = "boat_stop";
            public long command_id;
        }

        [Serializable]
        private sealed class SelectCameraCommand
        {
            public string type = "select_camera";
            public string camera_id;
        }

        [Serializable]
        private sealed class MessageTypeProbe
        {
            public string type;
        }

        [Serializable]
        private sealed class CameraFrameMessage
        {
            public string type;
            public string camera_id;
            public string encoding;
            public int width;
            public int height;
            public long timestamp_ms;
            public float age_seconds;
            public string jpeg_base64;
        }

        public Transform boat;
        public Transform drone;
        [Tooltip("Protocol-v2 fleet transforms, ordered usv_01..usv_03.")]
        public Transform[] boats;
        [Tooltip("Protocol-v2 fleet transforms, ordered uav_01..uav_03.")]
        public Transform[] drones;
        public string[] expectedUsvIds = { "usv_01", "usv_02", "usv_03" };
        public string[] expectedUavIds = { "uav_01", "uav_02", "uav_03" };
        public Transform lighthouse;
        public Transform buoyWest;
        public Transform buoySouth;
        public Transform buoyEast;
        [Tooltip("ROS/Gazebo heterogeneous_332 entity friendly_ship.")]
        public Transform friendlyShip;
        [Tooltip("ROS/Gazebo heterogeneous_332 hostile entity enemy_ship.")]
        public Transform targetVessel;
        [Tooltip("Optional marker driven by mission.roles.capture_center.")]
        public Transform captureCenterMarker;
        [Tooltip("Unity visual offset from the Gazebo x500 model origin.")]
        public float droneHeightOffset;
        public string serverUrl = "ws://127.0.0.1:8765/uav_usv";
        public float smoothing = 14f;
        public float reconnectDelay = 1.0f;

        public string connectionStatus { get; private set; } = "Waiting for ROS WebSocket";
        public string controlStatus { get; private set; } = "Controller idle";
        public string fleetStatus { get; private set; } = "舰队：等待协议 v2 数据";
        public string missionStatus { get; private set; } = "任务：等待 /capture/state";
        public string controlMode { get; private set; } = "unknown";
        public bool isConnected { get; private set; }
        public bool fleetReady { get; private set; }
        public bool acceptsCommands =>
            controlMode == "direct" || controlMode == "nav2";
        public string selectedCameraId { get; private set; } = "usv_01";
        public string latestCameraId { get; private set; } = "";
        public float latestCameraAgeSeconds { get; private set; } = 999f;
        public long latestCameraTimestampMs { get; private set; }
        public Texture2D latestCameraTexture { get; private set; }
        public string cameraStatus { get; private set; } = "Gazebo 相机：未连接";
        public float cameraFps { get; private set; }
        public int cameraWidth { get; private set; }
        public int cameraHeight { get; private set; }

        private CancellationTokenSource cancellation;
        private Task receiveTask;
        private readonly ConcurrentQueue<string> outgoing = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
        private Vector3[] boatTargets = Array.Empty<Vector3>();
        private Vector3[] droneTargets = Array.Empty<Vector3>();
        private Quaternion[] boatRotations = Array.Empty<Quaternion>();
        private Quaternion[] droneRotations = Array.Empty<Quaternion>();
        private bool[] hasBoatTargets = Array.Empty<bool>();
        private bool[] hasDroneTargets = Array.Empty<bool>();
        private bool[] droneDetached = Array.Empty<bool>();
        private Vector3 friendlyShipTarget;
        private Quaternion friendlyShipRotation = Quaternion.identity;
        private bool hasFriendlyShipTarget;
        private Vector3 targetVesselTarget;
        private Quaternion targetVesselRotation = Quaternion.identity;
        private bool hasTargetVesselTarget;
        private bool hasTargets;
        private float lastPacketTime = -100f;
        private float lastCameraPacketTime = -100f;
        private uint lastSequence;

        private void Start()
        {
            EnsureFleetBuffers();
            cancellation = new CancellationTokenSource();
            receiveTask = Task.Run(() => ReceiveLoop(cancellation.Token));
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using var socket = new ClientWebSocket();
                using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                Task sendTask = null;
                try
                {
                    connectionStatus = "Connecting " + serverUrl;
                    await socket.ConnectAsync(new Uri(serverUrl), token);
                    connectionStatus = "Connected " + serverUrl;
                    isConnected = true;
                    sendTask = SendLoop(socket, connectionCancellation.Token);

                    var buffer = new byte[65536];
                    var builder = new StringBuilder();

                    while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                    {
                        builder.Clear();
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                                break;
                            }

                            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        }
                        while (!result.EndOfMessage && !token.IsCancellationRequested);

                        if (builder.Length > 0)
                            incoming.Enqueue(builder.ToString());
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    connectionStatus = "WebSocket failed: " + ex.Message;
                }
                finally
                {
                    isConnected = false;
                    connectionCancellation.Cancel();
                    if (sendTask != null)
                    {
                        try { await sendTask; } catch (OperationCanceledException) { }
                    }
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(reconnectDelay), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task SendLoop(ClientWebSocket socket, CancellationToken token)
        {
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                if (!outgoing.TryDequeue(out string json))
                {
                    await Task.Delay(20, token);
                    continue;
                }

                byte[] data = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(
                    new ArraySegment<byte>(data),
                    WebSocketMessageType.Text,
                    true,
                    token
                );
            }
        }

        public long SendBoatPath(IReadOnlyList<Vector2> path)
        {
            if (path == null || path.Count < 2)
                return 0;

            long pathId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var points = new PathPoint[path.Count];
            for (int i = 0; i < path.Count; i++)
                points[i] = new PathPoint { x = path[i].x, y = path[i].y };

            outgoing.Enqueue(JsonUtility.ToJson(new BoatPathCommand
            {
                path_id = pathId,
                points = points
            }));
            return pathId;
        }

        public void SendBoatStop()
        {
            outgoing.Enqueue(JsonUtility.ToJson(new BoatStopCommand
            {
                command_id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }));
        }

        /// <summary>
        /// Ask the ROS bridge to forward one Gazebo camera uplink
        /// (usv_01..03 / uav_01..03). Works in observe mode.
        /// </summary>
        public void SelectCamera(string cameraId)
        {
            if (string.IsNullOrEmpty(cameraId))
                return;

            selectedCameraId = cameraId;
            cameraStatus = "Gazebo 相机：请求 " + cameraId;
            outgoing.Enqueue(JsonUtility.ToJson(new SelectCameraCommand
            {
                camera_id = cameraId
            }));
        }

        public bool HasFreshCamera(string cameraId, float maxAgeSeconds = 2.5f)
        {
            return latestCameraTexture != null &&
                   latestCameraId == cameraId &&
                   latestCameraAgeSeconds <= maxAgeSeconds &&
                   Time.realtimeSinceStartup - lastCameraPacketTime <= maxAgeSeconds;
        }

        private void Update()
        {
            // Drain a few inbound messages per frame so pose + camera coexist.
            for (int i = 0; i < 8; i++)
            {
                if (!incoming.TryDequeue(out string json) || string.IsNullOrEmpty(json))
                    break;
                RouteInbound(json);
            }

            if (hasTargets)
            {
                float alpha = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
                SmoothFleet(boats, boatTargets, boatRotations, hasBoatTargets, alpha);
                SmoothFleet(drones, droneTargets, droneRotations, hasDroneTargets, alpha);
                if (friendlyShip && hasFriendlyShipTarget)
                {
                    friendlyShip.position = Vector3.Lerp(
                        friendlyShip.position,
                        friendlyShipTarget,
                        alpha
                    );
                    friendlyShip.rotation = Quaternion.Slerp(
                        friendlyShip.rotation,
                        friendlyShipRotation,
                        alpha
                    );
                }
                if (targetVessel && hasTargetVesselTarget)
                {
                    targetVessel.position = Vector3.Lerp(
                        targetVessel.position,
                        targetVesselTarget,
                        alpha
                    );
                    targetVessel.rotation = Quaternion.Slerp(
                        targetVessel.rotation,
                        targetVesselRotation,
                        alpha
                    );
                }
            }

            if (Time.realtimeSinceStartup - lastPacketTime > 1.5f && hasTargets)
                connectionStatus = "Connected, waiting for fresh pose";

            if (!string.IsNullOrEmpty(selectedCameraId) &&
                Time.realtimeSinceStartup - lastCameraPacketTime > 2.5f)
            {
                cameraStatus = "Gazebo 相机：等待 " + selectedCameraId + " 上行";
            }
        }

        private void RouteInbound(string json)
        {
            try
            {
                MessageTypeProbe probe = JsonUtility.FromJson<MessageTypeProbe>(json);
                if (probe != null && probe.type == "camera_frame")
                {
                    ParseCameraFrame(json);
                    return;
                }
            }
            catch
            {
                // Fall through to pose parsing.
            }

            ParseFrame(json);
        }

        private void ParseCameraFrame(string json)
        {
            try
            {
                CameraFrameMessage frame = JsonUtility.FromJson<CameraFrameMessage>(json);
                if (frame == null ||
                    string.IsNullOrEmpty(frame.jpeg_base64) ||
                    frame.encoding != "jpeg")
                {
                    cameraStatus = "Gazebo 相机：无效帧";
                    return;
                }

                byte[] jpeg = Convert.FromBase64String(frame.jpeg_base64);
                if (latestCameraTexture == null)
                    latestCameraTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);

                if (!latestCameraTexture.LoadImage(jpeg, false))
                {
                    cameraStatus = "Gazebo 相机：JPEG 解码失败";
                    return;
                }

                latestCameraId = frame.camera_id ?? "";
                latestCameraAgeSeconds = frame.age_seconds;
                latestCameraTimestampMs = frame.timestamp_ms;
                float now = Time.realtimeSinceStartup;
                float interval = now - lastCameraPacketTime;
                if (lastCameraPacketTime > 0f && interval > .001f && interval < 2f)
                {
                    float instantFps = Mathf.Clamp(1f / interval, 0f, 60f);
                    cameraFps = cameraFps <= 0f
                        ? instantFps
                        : Mathf.Lerp(cameraFps, instantFps, .18f);
                }
                lastCameraPacketTime = Time.realtimeSinceStartup;
                cameraWidth = latestCameraTexture.width;
                cameraHeight = latestCameraTexture.height;
                cameraStatus =
                    "Gazebo · " + latestCameraId +
                    " · " + cameraWidth + "x" + cameraHeight +
                    " · " + cameraFps.ToString("0.0") + " FPS";
            }
            catch (Exception ex)
            {
                cameraStatus = "Gazebo 相机解析失败: " + ex.Message;
            }
        }

        private void ParseFrame(string json)
        {
            try
            {
                PoseFrame frame = JsonUtility.FromJson<PoseFrame>(json);
                bool hasFleetV2 =
                    frame != null &&
                    frame.schema_version >= 2 &&
                    frame.usvs != null &&
                    frame.uavs != null &&
                    frame.usvs.Length > 0 &&
                    frame.uavs.Length > 0;
                if (frame == null ||
                    (!hasFleetV2 && (!Valid(frame.boat) || !Valid(frame.drone))))
                {
                    connectionStatus = "Invalid WebSocket pose packet";
                    return;
                }

                if (hasTargets && frame.sequence != 0 && frame.sequence <= lastSequence)
                    return;

                lastSequence = frame.sequence;
                EnsureFleetBuffers();
                if (hasFleetV2)
                {
                    ApplyFleetFrame(frame.usvs, boats, expectedUsvIds, false);
                    ApplyFleetFrame(frame.uavs, drones, expectedUavIds, true);
                    UpdateFleetStatus(frame.fleet, frame.usvs.Length, frame.uavs.Length);
                }
                else
                {
                    ApplyLegacyPose(frame.boat, 0, false);
                    ApplyLegacyPose(frame.drone, 0, true);
                    fleetReady = true;
                    fleetStatus = "兼容模式：1 USV + 1 UAV";
                }

                ApplyOptionalPose(frame.lighthouse, lighthouse);
                ApplyOptionalPose(frame.buoy_west, buoyWest);
                ApplyOptionalPose(frame.buoy_south, buoySouth);
                ApplyOptionalPose(frame.buoy_east, buoyEast);
                if (frame.friendly_ship != null && Valid(frame.friendly_ship))
                    SetFriendlyShipPose(
                        frame.friendly_ship.position,
                        frame.friendly_ship.orientation
                    );
                if (frame.target != null && Valid(frame.target))
                    SetTargetVesselPose(frame.target.position, frame.target.orientation);
                else if (Valid(frame.target_vessel))
                    SetTargetVesselPose(
                        frame.target_vessel.position,
                        frame.target_vessel.orientation
                    );
                UpdateMissionStatus(frame.mission);
                if (frame.control != null && !string.IsNullOrEmpty(frame.control.state))
                {
                    if (!string.IsNullOrEmpty(frame.control.mode))
                        controlMode = frame.control.mode;
                    controlStatus = frame.control.state;
                    if (frame.control.waypoint_count > 0)
                    {
                        controlStatus += $" {Mathf.Min(frame.control.waypoint_index + 1, frame.control.waypoint_count)}" +
                                         $"/{frame.control.waypoint_count}";
                    }
                    if (!string.IsNullOrEmpty(frame.control.message))
                        controlStatus += ": " + frame.control.message;
                }

                hasTargets = true;
                lastPacketTime = Time.realtimeSinceStartup;
                connectionStatus =
                    (hasFleetV2 ? "ROS fleet v2" : "ROS legacy") +
                    " · seq " + frame.sequence;
            }
            catch (Exception ex)
            {
                connectionStatus = "WebSocket parse failed: " + ex.Message;
            }
        }

        private void EnsureFleetBuffers()
        {
            if (boats == null || boats.Length == 0)
                boats = boat ? new[] { boat } : Array.Empty<Transform>();
            if (drones == null || drones.Length == 0)
                drones = drone ? new[] { drone } : Array.Empty<Transform>();

            if (boatTargets.Length != boats.Length)
            {
                boatTargets = new Vector3[boats.Length];
                boatRotations = IdentityRotations(boats.Length);
                hasBoatTargets = new bool[boats.Length];
            }
            if (droneTargets.Length != drones.Length)
            {
                droneTargets = new Vector3[drones.Length];
                droneRotations = IdentityRotations(drones.Length);
                hasDroneTargets = new bool[drones.Length];
                droneDetached = new bool[drones.Length];
            }
        }

        private static Quaternion[] IdentityRotations(int count)
        {
            var rotations = new Quaternion[count];
            for (int i = 0; i < rotations.Length; i++)
                rotations[i] = Quaternion.identity;
            return rotations;
        }

        private void ApplyFleetFrame(
            FleetVehicleData[] vehicles,
            Transform[] transforms,
            string[] expectedIds,
            bool isDrone)
        {
            if (vehicles == null || transforms == null)
                return;

            for (int sourceIndex = 0; sourceIndex < vehicles.Length; sourceIndex++)
            {
                FleetVehicleData vehicle = vehicles[sourceIndex];
                if (!Valid(vehicle))
                    continue;

                int targetIndex = FindVehicleIndex(
                    vehicle.id,
                    expectedIds,
                    sourceIndex,
                    transforms.Length
                );
                if (targetIndex < 0)
                    continue;

                if (isDrone)
                {
                    droneTargets[targetIndex] = Coordinates.ToPresentation(
                        vehicle.position[0],
                        vehicle.position[1],
                        vehicle.position[2]
                    ) + Vector3.up * droneHeightOffset;
                    droneRotations[targetIndex] = EnuRotation(vehicle.orientation);
                    hasDroneTargets[targetIndex] = true;
                    DetachDrone(targetIndex);
                }
                else
                {
                    boatTargets[targetIndex] = Coordinates.ToPresentation(
                        vehicle.position[0],
                        vehicle.position[1],
                        vehicle.position[2]
                    );
                    boatRotations[targetIndex] = EnuRotation(vehicle.orientation);
                    hasBoatTargets[targetIndex] = true;
                }
            }
        }

        private void ApplyLegacyPose(PoseData pose, int index, bool isDrone)
        {
            if (!Valid(pose))
                return;

            if (isDrone && index < droneTargets.Length)
            {
                droneTargets[index] = Coordinates.ToPresentation(
                    pose.position[0],
                    pose.position[1],
                    pose.position[2]
                ) + Vector3.up * droneHeightOffset;
                droneRotations[index] = EnuRotation(pose.orientation);
                hasDroneTargets[index] = true;
                DetachDrone(index);
            }
            else if (!isDrone && index < boatTargets.Length)
            {
                boatTargets[index] = Coordinates.ToPresentation(
                    pose.position[0],
                    pose.position[1],
                    pose.position[2]
                );
                boatRotations[index] = EnuRotation(pose.orientation);
                hasBoatTargets[index] = true;
            }
        }

        private void DetachDrone(int index)
        {
            if (index < 0 || index >= drones.Length || droneDetached[index])
                return;

            Transform syncedDrone = drones[index];
            if (syncedDrone)
                syncedDrone.SetParent(null, true);
            droneDetached[index] = true;
        }

        private static int FindVehicleIndex(
            string id,
            string[] expectedIds,
            int fallbackIndex,
            int transformCount)
        {
            if (!string.IsNullOrEmpty(id) && expectedIds != null)
            {
                for (int i = 0; i < expectedIds.Length && i < transformCount; i++)
                {
                    if (string.Equals(id, expectedIds[i], StringComparison.Ordinal))
                        return i;
                }
            }
            return fallbackIndex >= 0 && fallbackIndex < transformCount
                ? fallbackIndex
                : -1;
        }

        private void SetTargetVesselPose(float[] position, float[] orientation)
        {
            if (!targetVessel)
                return;

            targetVessel.gameObject.SetActive(true);
            targetVesselTarget = Coordinates.ToPresentation(
                position[0],
                position[1],
                position[2]
            );
            targetVesselRotation = EnuRotation(orientation);
            hasTargetVesselTarget = true;
        }

        private void SetFriendlyShipPose(float[] position, float[] orientation)
        {
            if (!friendlyShip)
                return;

            friendlyShip.gameObject.SetActive(true);
            friendlyShipTarget = Coordinates.ToPresentation(
                position[0],
                position[1],
                position[2]
            );
            friendlyShipRotation = EnuRotation(orientation);
            hasFriendlyShipTarget = true;
        }

        private void UpdateFleetStatus(
            FleetData fleet,
            int receivedUsvs,
            int receivedUavs)
        {
            int expectedUsvs = fleet != null && fleet.expected_usvs > 0
                ? fleet.expected_usvs
                : expectedUsvIds != null ? expectedUsvIds.Length : boats.Length;
            int expectedUavs = fleet != null && fleet.expected_uavs > 0
                ? fleet.expected_uavs
                : expectedUavIds != null ? expectedUavIds.Length : drones.Length;
            int actualUsvs = fleet != null ? fleet.received_usvs : receivedUsvs;
            int actualUavs = fleet != null ? fleet.received_uavs : receivedUavs;
            fleetReady = fleet != null
                ? fleet.ready
                : actualUsvs >= expectedUsvs && actualUavs >= expectedUavs;
            fleetStatus =
                $"舰队：USV {actualUsvs}/{expectedUsvs} · UAV {actualUavs}/{expectedUavs}" +
                (fleetReady ? " · 已就绪" : " · 等待实体");
        }

        private void UpdateMissionStatus(MissionData mission)
        {
            CaptureStateData capture = mission != null ? mission.capture : null;
            CaptureRolesData roles = mission != null ? mission.roles : null;

            if (roles != null &&
                roles.capture_center != null &&
                roles.capture_center.Length >= 3 &&
                captureCenterMarker)
            {
                captureCenterMarker.position = Coordinates.ToPresentation(
                    roles.capture_center[0],
                    roles.capture_center[1],
                    roles.capture_center[2]
                );
            }

            if (capture == null || string.IsNullOrEmpty(capture.state_name))
            {
                missionStatus = "任务：等待 /capture/state";
                return;
            }

            string stateName = CaptureStateName(capture.state, capture.state_name);
            string degraded = capture.degraded ? " · 降级运行" : "";
            string reason = string.IsNullOrEmpty(capture.reason)
                ? ""
                : " · " + capture.reason;
            int activeRoles = 0;
            int totalRoles = 0;
            if (roles != null && roles.assignments != null)
            {
                totalRoles = roles.assignments.Length;
                for (int i = 0; i < roles.assignments.Length; i++)
                {
                    if (roles.assignments[i] != null && roles.assignments[i].active)
                        activeRoles++;
                }
            }

            string roleText = totalRoles > 0
                ? $" · 角色 {activeRoles}/{totalRoles}"
                : "";
            missionStatus =
                $"任务：{stateName} · USV {capture.active_usvs}/{capture.configured_usvs}" +
                $" · UAV {capture.active_uavs}/{capture.configured_uavs}" +
                roleText + degraded + reason;
        }

        private static string CaptureStateName(int state, string fallback)
        {
            switch (state)
            {
                case 0: return "搜索";
                case 1: return "跟踪";
                case 2: return "接近";
                case 3: return "围捕";
                case 4: return "保持";
                case 5: return "成功";
                case 6: return "失败";
                default: return fallback;
            }
        }

        private static void SmoothFleet(
            Transform[] transforms,
            Vector3[] positions,
            Quaternion[] rotations,
            bool[] valid,
            float alpha)
        {
            if (transforms == null)
                return;

            for (int i = 0; i < transforms.Length; i++)
            {
                Transform subject = transforms[i];
                if (!subject || i >= valid.Length || !valid[i])
                    continue;
                subject.position = Vector3.Lerp(subject.position, positions[i], alpha);
                subject.rotation = Quaternion.Slerp(
                    subject.rotation,
                    rotations[i],
                    alpha
                );
            }
        }

        private static bool Valid(PoseData pose)
        {
            return pose != null &&
                   pose.position != null &&
                   pose.position.Length >= 3 &&
                   pose.orientation != null &&
                   pose.orientation.Length >= 4;
        }

        private static bool Valid(FleetVehicleData pose)
        {
            return pose != null &&
                   pose.position != null &&
                   pose.position.Length >= 3 &&
                   pose.orientation != null &&
                   pose.orientation.Length >= 4;
        }

        private static Quaternion EnuRotation(float[] q)
        {
            var result = new Quaternion(-q[0], -q[2], -q[1], q[3]);
            return result.normalized;
        }

        private static void ApplyOptionalPose(PoseData pose, Transform target)
        {
            if (!target || !Valid(pose))
                return;

            target.gameObject.SetActive(true);
            target.position = Coordinates.ToPresentation(
                pose.position[0],
                pose.position[1],
                pose.position[2]
            );
            target.rotation = EnuRotation(pose.orientation);
        }

        private void OnDestroy()
        {
            if (isConnected && acceptsCommands)
                SendBoatStop();
            cancellation?.Cancel();
            try { receiveTask?.Wait(800); } catch (Exception) { }
            cancellation?.Dispose();
            if (latestCameraTexture)
                Destroy(latestCameraTexture);
        }
    }
}
