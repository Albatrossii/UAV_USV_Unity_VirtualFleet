using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Scripting;
using UavUsv;

namespace UavUsv.PlatformTools
{
    [Preserve]
    public sealed class VirtualFleetPlatformBridge : MonoBehaviour
    {
        private const string RuntimeNotReadyCode = "runtime_not_ready";
        private static VirtualFleetPlatformBridge instance;
        private UavUsv.IVirtualFleetRuntime runtime;
        private UavUsv.VirtualFleetConfig currentConfig;
        private long currentRunId;
        private long lastSequence = -1;
        private MissionState missionState = MissionState.Stopped;
        private WebCommandBridge cameraBridge;
        private VirtualFleetPoseOwnershipGuard poseOwnershipGuard;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void VueWebGlPostMessage(string message);
#endif

        public void Initialize(UavUsv.IVirtualFleetRuntime fleetRuntime)
        {
            runtime = fleetRuntime;
        }

        private void Awake()
        {
            if (instance && instance != this)
            {
                Debug.LogWarning(
                    "[VirtualFleetPlatformBridge] Duplicate component removed from " +
                    gameObject.name
                );
                Destroy(this);
                return;
            }

            instance = this;
            TryFindRuntime();
            cameraBridge = FindObjectOfType<WebCommandBridge>();
        }

        [Preserve]
        public void SelectDevice(string json)
        {
            CameraCommandPayload payload = ParseCameraCommand(json);
            if (!EnsureCameraBridge(payload.requestId))
                return;
            cameraBridge.SelectDevice(
                payload.requestId,
                FirstNonEmpty(payload.deviceCode, payload.deviceId)
            );
        }

        [Preserve]
        public void SetCameraMode(string json)
        {
            CameraCommandPayload payload = ParseCameraCommand(json);
            if (!EnsureCameraBridge(payload.requestId))
                return;
            cameraBridge.SetCameraMode(payload.requestId, payload.mode);
        }

        [Preserve]
        public bool TryExecuteMissionCommand(
            string rawCommand,
            out string state,
            out string detail)
        {
            state = "ERROR";
            detail = string.Empty;
            string command = (rawCommand ?? string.Empty).Trim().ToLowerInvariant();
            if (!command.StartsWith("mission", StringComparison.Ordinal))
                return false;

            TryFindRuntime();
            if (runtime == null)
            {
                detail = "Virtual fleet runtime is not ready";
                return true;
            }

            switch (command)
            {
                case "missionstart":
                    runtime.StartMission();
                    missionState = MissionState.Running;
                    SetPresentationMissionState("RUNNING");
                    state = "RUNNING";
                    detail = "Virtual fleet mission started";
                    return true;
                case "missionpause":
                    runtime.PauseMission();
                    missionState = MissionState.Paused;
                    SetPresentationMissionState("PAUSED");
                    state = "PAUSED";
                    detail = "Virtual fleet mission paused";
                    return true;
                case "missionresume":
                    runtime.ResumeMission();
                    missionState = MissionState.Running;
                    SetPresentationMissionState("RUNNING");
                    state = "RUNNING";
                    detail = "Virtual fleet mission resumed";
                    return true;
                case "missionstop":
                case "missioncomplete":
                case "missionfail":
                case "missioncancel":
                    runtime.StopMission();
                    missionState = MissionState.Stopped;
                    SetPresentationMissionState("STOPPED");
                    state = command == "missioncomplete"
                        ? "COMPLETED"
                        : command == "missionfail"
                            ? "FAILED"
                            : command == "missioncancel"
                                ? "CANCELLED"
                                : "STOPPED";
                    detail = "Virtual fleet mission stopped";
                    return true;
                case "missionreset":
                    runtime.ResetMission();
                    missionState = MissionState.Stopped;
                    lastSequence = -1;
                    ResetPresentationTrails();
                    state = "STOPPED";
                    detail = "Virtual fleet mission reset";
                    return true;
                default:
                    detail = "Unsupported virtual fleet mission command: " + rawCommand;
                    return true;
            }
        }

        public void InitializePlatform(string json)
        {
            InitializePlatformPayload payload = JsonUtility.FromJson<InitializePlatformPayload>(json) ??
                new InitializePlatformPayload();
            payload.runtimeMode = string.IsNullOrWhiteSpace(payload.runtimeMode)
                ? VirtualFleetProtocol.RuntimeMode
                : payload.runtimeMode;
            payload.protocolVersion = string.IsNullOrWhiteSpace(payload.protocolVersion)
                ? VirtualFleetProtocol.Version
                : payload.protocolVersion;
            HandleInitialize(new InitializePlatformMessage
            {
                type = VirtualFleetMessageTypes.InitializePlatform,
                requestId = FirstNonEmpty(
                    payload.requestId,
                    NewRequestId(VirtualFleetMessageTypes.InitializePlatform)
                ),
                timestamp = Now(),
                payload = payload
            });
        }

        public void LoadScenario(string json)
        {
            FrontendScenarioPayload input =
                JsonUtility.FromJson<FrontendScenarioPayload>(json) ??
                new FrontendScenarioPayload();
            long runId = ParseLong(input.runId, 1);
            string algorithmCode = ResolveAlgorithm(
                FirstNonEmpty(input.algorithmCode, input.scenarioId)
            );
            HandleLoadScenario(new LoadScenarioMessage
            {
                type = VirtualFleetMessageTypes.LoadScenario,
                requestId = FirstNonEmpty(
                    input.requestId,
                    NewRequestId(VirtualFleetMessageTypes.LoadScenario)
                ),
                timestamp = Now(),
                payload = new VirtualFleetConfigPayload
                {
                    runtimeMode = string.IsNullOrWhiteSpace(input.runtimeMode)
                        ? VirtualFleetProtocol.RuntimeMode
                        : input.runtimeMode,
                    algorithmCode = algorithmCode,
                    runId = runId,
                    uavCount = input.uavCount > 0 ? input.uavCount : 3,
                    usvCount = input.usvCount > 0 ? input.usvCount : 3,
                    targetCount = input.targetCount > 0 ? input.targetCount : 1,
                    initialSpeedMps = input.initialSpeedMps,
                    initialHeadingDeg = input.initialHeadingDeg,
                    seed = input.seed
                }
            });
        }

        public void ApplyPoseBatch(string json)
        {
            VirtualPoseBatchPayload payload =
                JsonUtility.FromJson<VirtualPoseBatchPayload>(json) ??
                new VirtualPoseBatchPayload();
            payload.runtimeMode = string.IsNullOrWhiteSpace(payload.runtimeMode)
                ? VirtualFleetProtocol.RuntimeMode
                : payload.runtimeMode;
            HandlePoseBatch(new ApplyPoseBatchMessage
            {
                type = VirtualFleetMessageTypes.ApplyPoseBatch,
                requestId = FirstNonEmpty(
                    payload.requestId,
                    NewRequestId(VirtualFleetMessageTypes.ApplyPoseBatch)
                ),
                timestamp = Now(),
                payload = payload
            });
        }

        public void SetMissionState(string json)
        {
            FrontendMissionPayload input =
                JsonUtility.FromJson<FrontendMissionPayload>(json) ??
                new FrontendMissionPayload();
            string state = (input.state ?? string.Empty).Trim().ToUpperInvariant();
            string command = state == "RUNNING"
                ? VirtualFleetMessageTypes.MissionStart
                : state == "PAUSED"
                    ? VirtualFleetMessageTypes.MissionPause
                    : state == "STOPPED"
                        ? VirtualFleetMessageTypes.MissionStop
                        : VirtualFleetMessageTypes.MissionReset;
            HandleMission(new MissionCommandMessage
            {
                type = command,
                requestId = FirstNonEmpty(input.requestId, NewRequestId(command)),
                timestamp = Now(),
                payload = new MissionCommandPayload
                {
                    runId = ParseLong(input.runId, currentRunId),
                    state = state
                }
            });
        }

        [Preserve]
        public bool CanHandle(string type)
        {
            return type == VirtualFleetMessageTypes.InitializePlatform ||
                type == VirtualFleetMessageTypes.LoadScenario ||
                type == VirtualFleetMessageTypes.RegenerateScenario ||
                type == VirtualFleetMessageTypes.ApplyPoseBatch ||
                type == VirtualFleetMessageTypes.MissionStart ||
                type == VirtualFleetMessageTypes.MissionPause ||
                type == VirtualFleetMessageTypes.MissionResume ||
                type == VirtualFleetMessageTypes.MissionStop ||
                type == VirtualFleetMessageTypes.MissionReset;
        }

        [Preserve]
        public void Receive(string json)
        {
            VirtualFleetMessage header;
            try
            {
                header = JsonUtility.FromJson<VirtualFleetMessage>(json);
            }
            catch (Exception exception)
            {
                EmitError(string.Empty, "invalid_payload", exception.Message);
                return;
            }

            if (header == null || string.IsNullOrWhiteSpace(header.type))
            {
                EmitError(string.Empty, "invalid_payload", "Message type is required");
                return;
            }

            switch (header.type)
            {
                case VirtualFleetMessageTypes.InitializePlatform:
                    HandleInitialize(JsonUtility.FromJson<InitializePlatformMessage>(json));
                    break;
                case VirtualFleetMessageTypes.LoadScenario:
                    HandleLoadScenario(JsonUtility.FromJson<LoadScenarioMessage>(json));
                    break;
                case VirtualFleetMessageTypes.RegenerateScenario:
                    HandleRegenerate(JsonUtility.FromJson<RegenerateScenarioMessage>(json));
                    break;
                case VirtualFleetMessageTypes.ApplyPoseBatch:
                    HandlePoseBatch(JsonUtility.FromJson<ApplyPoseBatchMessage>(json));
                    break;
                case VirtualFleetMessageTypes.MissionStart:
                case VirtualFleetMessageTypes.MissionPause:
                case VirtualFleetMessageTypes.MissionResume:
                case VirtualFleetMessageTypes.MissionStop:
                case VirtualFleetMessageTypes.MissionReset:
                    HandleMission(JsonUtility.FromJson<MissionCommandMessage>(json));
                    break;
                default:
                    EmitError(header.requestId, "unsupported_message", "Unsupported message type");
                    break;
            }
        }

        private void HandleInitialize(InitializePlatformMessage message)
        {
            VirtualFleetValidationResult result = VirtualFleetMessageValidator.ValidateInitialize(message);
            if (!result.IsValid)
            {
                EmitError(RequestId(message), result.Code, result.Message);
                return;
            }

            Emit(new PlatformReadyResponse
            {
                type = "platformBridgeReady",
                requestId = RequestId(message),
                timestamp = Now(),
                payload = new PlatformReadyPayload
                {
                    ready = runtime != null,
                    controlsReady = runtime != null,
                    cameraReady = true,
                    algorithmReady = runtime != null,
                    visualSensorReady = false,
                    runtimeMode = VirtualFleetProtocol.RuntimeMode,
                    protocolVersion = VirtualFleetProtocol.Version,
                    buildId = message.payload.buildId,
                    maxUavCount = VirtualFleetProtocol.MaxUavCount,
                    maxUsvCount = VirtualFleetProtocol.MaxUsvCount,
                    capabilities = new[]
                    {
                        "virtual-fleet",
                        "dynamic-generation",
                        "object-pool",
                        "pose-batch",
                        "mission-control"
                    }
                }
            });
        }

        private void HandleLoadScenario(LoadScenarioMessage message)
        {
            VirtualFleetValidationResult result = VirtualFleetMessageValidator.ValidateLoadScenario(message);
            if (!result.IsValid)
            {
                EmitError(RequestId(message), result.Code, result.Message);
                return;
            }
            if (!EnsureRuntime(RequestId(message)))
                return;

            currentConfig = ToRuntimeConfig(message.payload);
            currentRunId = currentConfig.runId;
            lastSequence = -1;
            missionState = MissionState.Stopped;
            ResetPresentationTrails();
            runtime.Configure(currentConfig);
            runtime.Regenerate();
            EmitScenarioReady(message.requestId);
        }

        private void HandleRegenerate(RegenerateScenarioMessage message)
        {
            VirtualFleetValidationResult result =
                VirtualFleetMessageValidator.ValidateRegenerateScenario(message, missionState, currentRunId);
            if (!result.IsValid)
            {
                EmitError(RequestId(message), result.Code, result.Message);
                return;
            }
            if (!EnsureRuntime(RequestId(message)))
                return;

            if (currentConfig == null)
                currentConfig = new UavUsv.VirtualFleetConfig();
            currentConfig.runtimeMode = VirtualFleetProtocol.RuntimeMode;
            currentConfig.algorithmCode = message.payload.algorithmCode;
            currentConfig.runId = message.payload.runId;
            currentConfig.uavCount = message.payload.uavCount;
            currentConfig.usvCount = message.payload.usvCount;
            currentConfig.targetCount = message.payload.targetCount;
            currentConfig.formationType = AutomaticFormation(message.payload.algorithmCode);
            currentConfig.initialSpeedMps = message.payload.initialSpeedMps;
            currentConfig.initialHeadingDeg = message.payload.initialHeadingDeg;
            currentConfig.seed = message.payload.seed;
            currentRunId = currentConfig.runId;
            lastSequence = -1;
            ResetPresentationTrails();
            runtime.Configure(currentConfig);
            runtime.Regenerate();
            missionState = MissionState.Stopped;
            EmitScenarioReady(message.requestId);
        }

        private void HandlePoseBatch(ApplyPoseBatchMessage message)
        {
            VirtualFleetValidationResult result =
                VirtualFleetMessageValidator.ValidateApplyPoseBatch(message, currentRunId, lastSequence);
            if (!result.IsValid)
            {
                EmitError(RequestId(message), result.Code, result.Message);
                return;
            }
            if (!EnsureRuntime(RequestId(message)))
                return;

            lastSequence = message.payload.sequence;
            VirtualPoseBatchApplyResult applyResult =
                runtime.ApplyPoseBatch(ToRuntimePoseBatch(message.payload));
            UavUsv.VirtualFleetDeviceState trackedDevice = GetTrackedDeviceState("UAV-001");
            Emit(new PoseAppliedResponse
            {
                type = "poseFrameApplied",
                requestId = message.requestId,
                timestamp = Now(),
                payload = new PoseAppliedPayload
                {
                    success = applyResult.success,
                    code = applyResult.code,
                    message = applyResult.message,
                    runId = applyResult.runId,
                    sequence = lastSequence,
                    appliedCount = applyResult.appliedCount,
                    missingDeviceCodes = applyResult.missingDeviceCodes ?? new string[0],
                    unknownDeviceCodes = applyResult.unknownDeviceCodes ?? new string[0],
                    trackedDeviceCode = trackedDevice != null
                        ? trackedDevice.deviceCode
                        : string.Empty,
                    unityPositionX = trackedDevice != null
                        ? trackedDevice.position.x
                        : 0f,
                    unityPositionY = trackedDevice != null
                        ? trackedDevice.position.y
                        : 0f,
                    unityPositionZ = trackedDevice != null
                        ? trackedDevice.position.z
                        : 0f,
                    unityHeadingDeg = trackedDevice != null
                        ? NormalizeHeading(-trackedDevice.rotation.eulerAngles.y)
                        : 0f,
                    transformPositionX = trackedDevice != null &&
                        trackedDevice.transform
                        ? trackedDevice.transform.position.x
                        : 0f,
                    transformPositionY = trackedDevice != null &&
                        trackedDevice.transform
                        ? trackedDevice.transform.position.y
                        : 0f,
                    transformPositionZ = trackedDevice != null &&
                        trackedDevice.transform
                        ? trackedDevice.transform.position.z
                        : 0f,
                    transformHeadingDeg = trackedDevice != null &&
                        trackedDevice.transform
                        ? NormalizeHeading(-trackedDevice.transform.eulerAngles.y)
                        : 0f
                }
            });
        }

        private UavUsv.VirtualFleetDeviceState GetTrackedDeviceState(string deviceCode)
        {
            UavUsv.VirtualFleetScenarioController controller =
                runtime as UavUsv.VirtualFleetScenarioController;
            if (!controller)
                return null;

            UavUsv.VirtualFleetSnapshot snapshot = controller.GetSnapshot();
            UavUsv.VirtualFleetDeviceState[] devices = snapshot != null
                ? snapshot.devices
                : null;
            if (devices == null)
                return null;

            for (int i = 0; i < devices.Length; i++)
            {
                UavUsv.VirtualFleetDeviceState device = devices[i];
                if (device != null &&
                    string.Equals(
                        device.deviceCode,
                        deviceCode,
                        StringComparison.OrdinalIgnoreCase
                    ))
                    return device;
            }
            return null;
        }

        private static float NormalizeHeading(float heading)
        {
            float normalized = heading % 360f;
            return normalized < 0f ? normalized + 360f : normalized;
        }

        private void HandleMission(MissionCommandMessage message)
        {
            MissionState nextState;
            VirtualFleetValidationResult result =
                VirtualFleetMessageValidator.ValidateMissionCommand(
                    message, missionState, currentRunId, out nextState);
            if (!result.IsValid)
            {
                EmitError(RequestId(message), result.Code, result.Message);
                return;
            }
            if (!EnsureRuntime(RequestId(message)))
                return;

            switch (message.type)
            {
                case VirtualFleetMessageTypes.MissionStart:
                    runtime.StartMission();
                    break;
                case VirtualFleetMessageTypes.MissionPause:
                    runtime.PauseMission();
                    break;
                case VirtualFleetMessageTypes.MissionResume:
                    runtime.ResumeMission();
                    break;
                case VirtualFleetMessageTypes.MissionStop:
                    runtime.StopMission();
                    break;
                case VirtualFleetMessageTypes.MissionReset:
                    // Reset is allowed as a recovery action from RUNNING or
                    // PAUSED. Stop first so the runtime is never left locked.
                    runtime.StopMission();
                    runtime.ResetMission();
                    lastSequence = -1;
                    break;
            }

            // ResetMission synchronously regenerates the fleet and leaves the
            // runtime stopped. RESET is only an internal transition.
            missionState = message.type == VirtualFleetMessageTypes.MissionReset
                ? MissionState.Stopped
                : nextState;
            if (message.type == VirtualFleetMessageTypes.MissionReset)
                ResetPresentationTrails();
            else
                SetPresentationMissionState(StateName(missionState));
            Emit(new MissionStateResponse
            {
                type = "missionStateChanged",
                requestId = message.requestId,
                timestamp = Now(),
                payload = new MissionStatePayload
                {
                    success = true,
                    runId = currentRunId,
                    missionState = StateName(missionState)
                }
            });
        }

        private void ResetPresentationTrails()
        {
            if (!cameraBridge)
                cameraBridge = FindObjectOfType<WebCommandBridge>();
            if (cameraBridge)
                cameraBridge.ResetVirtualFleetTrails();
        }

        private void SetPresentationMissionState(string state)
        {
            if (!cameraBridge)
                cameraBridge = FindObjectOfType<WebCommandBridge>();
            if (cameraBridge)
                cameraBridge.SetVirtualFleetMissionState(state);
        }

        private void EmitScenarioReady(string requestId)
        {
            Emit(new ScenarioReadyResponse
            {
                type = "scenarioReady",
                requestId = requestId,
                timestamp = Now(),
                payload = new ScenarioReadyPayload
                {
                    success = true,
                    runtimeMode = VirtualFleetProtocol.RuntimeMode,
                    runId = currentRunId,
                    algorithmCode = currentConfig.algorithmCode,
                    uavCount = currentConfig.uavCount,
                    usvCount = currentConfig.usvCount,
                    targetCount = currentConfig.targetCount,
                    deviceCodes = BuildDeviceCodes(currentConfig),
                    initialPosesCoordinateFrame = "GLOBAL_ENU",
                    fleetOrigin = new FleetOriginPayload
                    {
                        eastM = -75.0,
                        northM = -310.0,
                        upM = 0.0
                    },
                    initialPoses = BuildInitialPoses(),
                    missionState = StateName(missionState)
                }
            });
        }

        private UavUsv.VirtualPose[] BuildInitialPoses()
        {
            UavUsv.VirtualFleetScenarioController controller =
                runtime as UavUsv.VirtualFleetScenarioController;
            UavUsv.VirtualFleetSnapshot snapshot =
                controller ? controller.GetSnapshot() : null;
            UavUsv.VirtualFleetDeviceState[] devices =
                snapshot != null && snapshot.devices != null
                    ? snapshot.devices
                    : new UavUsv.VirtualFleetDeviceState[0];
            var poses = new List<UavUsv.VirtualPose>(devices.Length);
            for (int i = 0; i < devices.Length; i++)
            {
                UavUsv.VirtualFleetDeviceState device = devices[i];
                if (device == null || string.IsNullOrWhiteSpace(device.deviceCode))
                    continue;
                Vector3 enu =
                    UavUsv.Coordinates.PresentationToEnu(device.position);
                poses.Add(new UavUsv.VirtualPose
                {
                    deviceCode = device.deviceCode,
                    deviceType = device.deviceType,
                    eastM = enu.x,
                    northM = enu.y,
                    upM = enu.z,
                    headingDeg = NormalizeHeading(-device.rotation.eulerAngles.y),
                    speedMps = 0f,
                    state = device.status,
                    valid = true
                });
            }
            return poses.ToArray();
        }

        private static string[] BuildDeviceCodes(UavUsv.VirtualFleetConfig config)
        {
            var codes = new string[config.uavCount + config.usvCount + config.targetCount];
            int index = 0;
            for (int i = 1; i <= config.uavCount; i++)
                codes[index++] = "UAV-" + i.ToString("D3");
            for (int i = 1; i <= config.usvCount; i++)
                codes[index++] = "USV-" + i.ToString("D3");
            for (int i = 1; i <= config.targetCount; i++)
                codes[index++] = "TARGET-" + i.ToString("D3");
            return codes;
        }

        private static UavUsv.VirtualFleetConfig ToRuntimeConfig(
            VirtualFleetConfigPayload payload)
        {
            return new UavUsv.VirtualFleetConfig
            {
                runtimeMode = payload.runtimeMode,
                algorithmCode = payload.algorithmCode,
                runId = payload.runId,
                uavCount = payload.uavCount,
                usvCount = payload.usvCount,
                targetCount = payload.targetCount,
                formationType = AutomaticFormation(payload.algorithmCode),
                initialSpeedMps = payload.initialSpeedMps,
                initialHeadingDeg = payload.initialHeadingDeg,
                seed = payload.seed,
                initialPosesCoordinateFrame = string.IsNullOrWhiteSpace(
                    payload.initialPosesCoordinateFrame
                )
                    ? VirtualFleetProtocol.GlobalCoordinateFrame
                    : payload.initialPosesCoordinateFrame,
                initialPoses = payload.initialPoses
            };
        }

        private static UavUsv.VirtualPoseBatch ToRuntimePoseBatch(
            VirtualPoseBatchPayload payload)
        {
            return new UavUsv.VirtualPoseBatch
            {
                runtimeMode = payload.runtimeMode,
                runId = payload.runId,
                sequence = payload.sequence,
                sampleTime = payload.sampleTime > 0 ? payload.sampleTime : Now(),
                vehicles = payload.vehicles ?? ConvertInputs(payload.poses),
                targets = payload.targets
            };
        }

        private static UavUsv.VirtualPose[] ConvertInputs(VirtualPoseInput[] inputs)
        {
            if (inputs == null)
                return null;
            var poses = new UavUsv.VirtualPose[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                VirtualPoseInput input = inputs[i] ?? new VirtualPoseInput();
                string code = input.deviceCode;
                if (string.IsNullOrWhiteSpace(code)) code = input.deviceId;
                if (string.IsNullOrWhiteSpace(code)) code = input.code;
                if (string.IsNullOrWhiteSpace(code)) code = input.id;
                float east = input.eastM;
                float north = input.northM;
                float up = input.upM;
                if (input.position != null && input.position.Length >= 3)
                {
                    east = input.position[0];
                    north = input.position[1];
                    up = input.position[2];
                }
                poses[i] = new UavUsv.VirtualPose
                {
                    deviceCode = VirtualFleetMessageValidator.NormalizeDeviceCode(code),
                    deviceType = ResolveDeviceType(code, input.deviceType, input.type),
                    eastM = east,
                    northM = north,
                    upM = up,
                    headingDeg = input.yawDegrees != 0f ? input.yawDegrees : input.yaw,
                    speedMps = input.speedMps,
                    state = input.state,
                    valid = input.valid
                };
            }
            return poses;
        }

        private static string ResolveDeviceType(string code, string deviceType, string type)
        {
            if (!string.IsNullOrWhiteSpace(deviceType))
                return deviceType.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(type))
                return type.Trim().ToUpperInvariant();
            string normalized = VirtualFleetMessageValidator.NormalizeDeviceCode(code);
            return normalized.StartsWith("USV-", StringComparison.Ordinal) ? "USV" : "UAV";
        }

        private void TryFindRuntime()
        {
            if (runtime != null)
                return;
            VirtualFleetScenarioController controller =
                FindObjectOfType<VirtualFleetScenarioController>();
            if (controller)
                runtime = controller;
        }

        private bool EnsureRuntime(string requestId)
        {
            TryFindRuntime();
            if (runtime != null)
            {
                DisableLegacyCollisionSafety();
                EnsurePoseOwnershipGuard();
                return true;
            }
            EmitError(requestId, RuntimeNotReadyCode, "Virtual fleet runtime is not registered");
            return false;
        }

        private void EnsurePoseOwnershipGuard()
        {
            VirtualFleetScenarioController controller =
                runtime as VirtualFleetScenarioController;
            if (!controller)
                return;

            if (!poseOwnershipGuard)
                poseOwnershipGuard = GetComponent<VirtualFleetPoseOwnershipGuard>();
            if (!poseOwnershipGuard)
                poseOwnershipGuard =
                    gameObject.AddComponent<VirtualFleetPoseOwnershipGuard>();
            poseOwnershipGuard.Initialize(controller);
        }

        private static void DisableLegacyCollisionSafety()
        {
            RuntimeCollisionSafety[] safetySystems =
                FindObjectsOfType<RuntimeCollisionSafety>(true);
            for (int i = 0; i < safetySystems.Length; i++)
            {
                RuntimeCollisionSafety safety = safetySystems[i];
                if (!safety || !safety.enabled)
                    continue;

                safety.enabled = false;
                Debug.Log(
                    "[VirtualFleetPlatformBridge] Disabled RuntimeCollisionSafety " +
                    "for VIRTUAL_SIMULATION pose ownership."
                );
            }
        }

        private bool EnsureCameraBridge(string requestId)
        {
            if (!cameraBridge)
                cameraBridge = FindObjectOfType<WebCommandBridge>();
            if (cameraBridge)
                return true;

            EmitError(requestId, "camera_bridge_not_ready", "WebCommandBridge is not registered");
            return false;
        }

        private static CameraCommandPayload ParseCameraCommand(string json)
        {
            CameraCommandPayload payload = JsonUtility.FromJson<CameraCommandPayload>(json);
            return payload ?? new CameraCommandPayload();
        }

        private static string FirstNonEmpty(string primary, string fallback)
        {
            return string.IsNullOrWhiteSpace(primary) ? fallback : primary;
        }

        private static string ResolveAlgorithm(string scenarioId)
        {
            return string.Equals(scenarioId, VirtualFleetAlgorithms.Escort, StringComparison.OrdinalIgnoreCase)
                ? VirtualFleetAlgorithms.Escort
                : VirtualFleetAlgorithms.Capture;
        }

        private static long ParseLong(string value, long fallback)
        {
            return long.TryParse(value, out long result) && result > 0 ? result : fallback;
        }

        private static string NewRequestId(string type)
        {
            return type + ":" + Now();
        }

        [Serializable]
        private sealed class FrontendScenarioPayload
        {
            public string requestId;
            public string runtimeMode;
            public string runId;
            public string scenarioId;
            public string algorithmCode;
            public string sceneName;
            public string coordinateSystem;
            public int uavCount;
            public int usvCount;
            public int targetCount;
            public float initialSpeedMps;
            public float initialHeadingDeg;
            public int seed;
        }

        [Serializable]
        private sealed class FrontendMissionPayload
        {
            public string requestId;
            public string runId;
            public long sequence;
            public string state;
            public string phase;
            public string status;
            public string message;
        }

        private static VirtualFleetFormationType AutomaticFormation(string algorithmCode)
        {
            return string.Equals(
                algorithmCode,
                VirtualFleetAlgorithms.Escort,
                StringComparison.OrdinalIgnoreCase
            )
                ? VirtualFleetFormationType.Escort
                : VirtualFleetFormationType.Encirclement;
        }

        private static string RequestId(VirtualFleetMessage message)
        {
            return message == null ? string.Empty : message.requestId;
        }

        private static string StateName(MissionState state)
        {
            return state.ToString().ToUpperInvariant();
        }

        private static long Now()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        [Serializable]
        private sealed class CameraCommandPayload
        {
            public string requestId;
            public string deviceCode;
            public string deviceId;
            public string mode;
        }

        private static void EmitError(string requestId, string code, string message)
        {
            Emit(new CommandAckResponse
            {
                type = "commandAck",
                requestId = requestId ?? string.Empty,
                timestamp = Now(),
                payload = new CommandAckPayload
                {
                    success = false,
                    code = code,
                    message = message,
                    runId = 0
                }
            });
        }

        private static void Emit(object response)
        {
            string json = JsonUtility.ToJson(response);
#if UNITY_WEBGL && !UNITY_EDITOR
            VueWebGlPostMessage(json);
#else
            Debug.Log("[VirtualFleetPlatformBridge] " + json);
#endif
        }

        [Serializable]
        private sealed class PlatformReadyResponse
        {
            public string type;
            public string requestId;
            public long timestamp;
            public PlatformReadyPayload payload;
        }

        [Serializable]
        private sealed class PlatformReadyPayload
        {
            public bool ready;
            public bool controlsReady;
            public bool cameraReady;
            public bool algorithmReady;
            public bool visualSensorReady;
            public string runtimeMode;
            public string protocolVersion;
            public string buildId;
            public int maxUavCount;
            public int maxUsvCount;
            public string[] capabilities;
        }

        [Serializable]
        private sealed class ScenarioReadyResponse
        {
            public string type;
            public string requestId;
            public long timestamp;
            public ScenarioReadyPayload payload;
        }

        [Serializable]
        private sealed class ScenarioReadyPayload
        {
            public bool success;
            public string runtimeMode;
            public long runId;
            public string algorithmCode;
            public int uavCount;
            public int usvCount;
            public int targetCount;
            public string[] deviceCodes;
            public string initialPosesCoordinateFrame;
            public FleetOriginPayload fleetOrigin;
            public UavUsv.VirtualPose[] initialPoses;
            public string missionState;
        }

        [Serializable]
        private sealed class FleetOriginPayload
        {
            public double eastM;
            public double northM;
            public double upM;
        }

        [Serializable]
        private sealed class PoseAppliedResponse
        {
            public string type;
            public string requestId;
            public long timestamp;
            public PoseAppliedPayload payload;
        }

        [Serializable]
        private sealed class PoseAppliedPayload
        {
            public bool success;
            public string code;
            public string message;
            public long runId;
            public long sequence;
            public int appliedCount;
            public string[] missingDeviceCodes;
            public string[] unknownDeviceCodes;
            public string trackedDeviceCode;
            public float unityPositionX;
            public float unityPositionY;
            public float unityPositionZ;
            public float unityHeadingDeg;
            public float transformPositionX;
            public float transformPositionY;
            public float transformPositionZ;
            public float transformHeadingDeg;
        }

        [Serializable]
        private sealed class MissionStateResponse
        {
            public string type;
            public string requestId;
            public long timestamp;
            public MissionStatePayload payload;
        }

        [Serializable]
        private sealed class MissionStatePayload
        {
            public bool success;
            public long runId;
            public string missionState;
        }

        [Serializable]
        private sealed class CommandAckResponse
        {
            public string type;
            public string requestId;
            public long timestamp;
            public CommandAckPayload payload;
        }

        [Serializable]
        private sealed class CommandAckPayload
        {
            public bool success;
            public string code;
            public string message;
            public long runId;
        }
    }
}
