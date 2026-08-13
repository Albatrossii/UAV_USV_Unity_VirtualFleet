using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Scripting;

namespace UavUsv.PlatformTools
{
    public interface IVirtualFleetRuntime
    {
        void Configure(VirtualFleetConfig config);
        void Regenerate();
        void ApplyPoseBatch(VirtualPoseBatch batch);
        void StartMission();
        void PauseMission();
        void ResumeMission();
        void StopMission();
        void ResetMission();
    }

    [Preserve]
    public sealed class VirtualFleetPlatformBridge : MonoBehaviour
    {
        private const string RuntimeNotReadyCode = "runtime_not_ready";
        private IVirtualFleetRuntime runtime;
        private VirtualFleetConfig currentConfig;
        private int currentRunId;
        private long lastSequence = -1;
        private MissionState missionState = MissionState.Stopped;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void VueWebGlPostMessage(string message);
#endif

        public void Initialize(IVirtualFleetRuntime fleetRuntime)
        {
            runtime = fleetRuntime;
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
                requestId = string.Empty,
                timestamp = Now(),
                payload = new PlatformReadyPayload
                {
                    ready = runtime != null,
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

            currentConfig = message.payload;
            currentRunId = currentConfig.runId;
            lastSequence = -1;
            missionState = MissionState.Stopped;
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
                currentConfig = new VirtualFleetConfig();
            currentConfig.runId = message.payload.runId;
            currentConfig.uavCount = message.payload.uavCount;
            currentConfig.usvCount = message.payload.usvCount;
            currentConfig.targetCount = VirtualFleetProtocol.FixedTargetCount;
            currentConfig.formationType = message.payload.formationType;
            currentRunId = currentConfig.runId;
            lastSequence = -1;
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
            runtime.ApplyPoseBatch(message.payload);
            Emit(new PoseAppliedResponse
            {
                type = "poseFrameApplied",
                requestId = message.requestId,
                timestamp = Now(),
                payload = new PoseAppliedPayload
                {
                    success = true,
                    runId = currentRunId,
                    sequence = lastSequence,
                    appliedCount = CountPoses(message.payload),
                    missingDeviceCodes = new string[0],
                    unknownDeviceCodes = new string[0]
                }
            });
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
                    runtime.ResetMission();
                    lastSequence = -1;
                    break;
            }

            missionState = nextState;
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

        private bool EnsureRuntime(string requestId)
        {
            if (runtime != null)
                return true;
            EmitError(requestId, RuntimeNotReadyCode, "Virtual fleet runtime is not registered");
            return false;
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
                    missionState = StateName(missionState)
                }
            });
        }

        private static string[] BuildDeviceCodes(VirtualFleetConfig config)
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

        private static int CountPoses(VirtualPoseBatch batch)
        {
            return (batch.vehicles == null ? 0 : batch.vehicles.Length) +
                (batch.targets == null ? 0 : batch.targets.Length);
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
            public int runId;
            public string algorithmCode;
            public int uavCount;
            public int usvCount;
            public int targetCount;
            public string[] deviceCodes;
            public string missionState;
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
            public int runId;
            public long sequence;
            public int appliedCount;
            public string[] missingDeviceCodes;
            public string[] unknownDeviceCodes;
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
            public int runId;
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
            public int runId;
        }
    }
}
