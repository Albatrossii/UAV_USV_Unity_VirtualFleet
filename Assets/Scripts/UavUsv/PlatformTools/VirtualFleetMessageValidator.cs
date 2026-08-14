using System;
using System.Collections.Generic;
using UavUsv;

namespace UavUsv.PlatformTools
{
    public sealed class VirtualFleetValidationResult
    {
        public bool IsValid { get; private set; }
        public string Code { get; private set; }
        public string Message { get; private set; }

        private VirtualFleetValidationResult(bool isValid, string code, string message)
        {
            IsValid = isValid;
            Code = code;
            Message = message;
        }

        public static VirtualFleetValidationResult Valid()
        {
            return new VirtualFleetValidationResult(true, string.Empty, string.Empty);
        }

        public static VirtualFleetValidationResult Invalid(string code, string message)
        {
            return new VirtualFleetValidationResult(false, code, message);
        }
    }

    public static class VirtualFleetMessageValidator
    {
        private static readonly HashSet<string> SupportedDeviceTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UAV", "USV" };

        public static VirtualFleetValidationResult ValidateEnvelope(VirtualFleetMessage message)
        {
            if (message == null)
                return Invalid("invalid_payload", "Message is null");
            if (string.IsNullOrWhiteSpace(message.type))
                return Invalid("invalid_payload", "Message type is required");
            if (!IsSupportedMessage(message.type.Trim()))
                return Invalid("unsupported_message", "Unsupported message type");
            if (message.timestamp <= 0)
                return Invalid("invalid_payload", "Timestamp must be positive");
            if (string.IsNullOrWhiteSpace(message.requestId))
                return Invalid("invalid_payload", "RequestId is required");
            return VirtualFleetValidationResult.Valid();
        }

        public static VirtualFleetValidationResult ValidateInitialize(InitializePlatformMessage message)
        {
            VirtualFleetValidationResult result = ValidateHeader(
                message, VirtualFleetMessageTypes.InitializePlatform);
            if (!result.IsValid)
                return result;
            if (message.payload == null)
                return Invalid("invalid_payload", "Payload is required");
            if (!IsRuntimeMode(message.payload.runtimeMode))
                return Invalid("invalid_runtime_mode", "runtimeMode must be VIRTUAL_SIMULATION");
            if (!string.Equals(message.payload.protocolVersion, VirtualFleetProtocol.Version, StringComparison.Ordinal))
                return Invalid("invalid_payload", "Unsupported protocol version");
            return VirtualFleetValidationResult.Valid();
        }

        public static VirtualFleetValidationResult ValidateLoadScenario(LoadScenarioMessage message)
        {
            VirtualFleetValidationResult result = ValidateHeader(
                message, VirtualFleetMessageTypes.LoadScenario);
            if (!result.IsValid)
                return result;
            if (message.payload == null)
                return Invalid("invalid_payload", "Payload is required");

            VirtualFleetConfigPayload config = message.payload;
            result = ValidateRuntimeMode(config.runtimeMode);
            if (!result.IsValid)
                return result;
            if (!VirtualFleetAlgorithms.IsSupported(config.algorithmCode))
                return Invalid("invalid_algorithm", "Unsupported algorithmCode");
            if (config.runId <= 0)
                return Invalid("invalid_payload", "runId must be positive");
            if (config.uavCount < 1 || config.uavCount > VirtualFleetProtocol.MaxUavCount ||
                config.usvCount < 1 || config.usvCount > VirtualFleetProtocol.MaxUsvCount)
                return Invalid("invalid_count", "UAV and USV counts must be in range 1..100");
            if (config.targetCount != VirtualFleetProtocol.FixedTargetCount)
                return Invalid("invalid_count", "v1 requires targetCount to be 1");
            if (!VirtualFleetFormations.IsSupported(config.formationType))
                return Invalid("invalid_payload", "Unsupported formationType");
            if (config.initialSpeedMps < 0f)
                return Invalid("invalid_payload", "initialSpeedMps cannot be negative");

            config.algorithmCode = config.algorithmCode.Trim().ToUpperInvariant();
            config.formationType = config.formationType.Trim().ToUpperInvariant();
            config.initialHeadingDeg = NormalizeHeading(config.initialHeadingDeg);
            return VirtualFleetValidationResult.Valid();
        }

        public static VirtualFleetValidationResult ValidateRegenerateScenario(
            RegenerateScenarioMessage message,
            MissionState currentState,
            long currentRunId)
        {
            VirtualFleetValidationResult result = ValidateHeader(
                message, VirtualFleetMessageTypes.RegenerateScenario);
            if (!result.IsValid)
                return result;
            if (message.payload == null)
                return Invalid("invalid_payload", "Payload is required");
            if (currentState != MissionState.Stopped && currentState != MissionState.Reset)
                return Invalid("scenario_locked", "Scenario can only regenerate while stopped or reset");
            if (currentRunId > 0 && message.payload.runId != currentRunId)
                return Invalid("run_mismatch", "Regenerate runId does not match current run");
            if (message.payload.runId <= 0)
                return Invalid("invalid_payload", "runId must be positive");
            if (message.payload.uavCount < 1 || message.payload.uavCount > VirtualFleetProtocol.MaxUavCount ||
                message.payload.usvCount < 1 || message.payload.usvCount > VirtualFleetProtocol.MaxUsvCount)
                return Invalid("invalid_count", "UAV and USV counts must be in range 1..100");
            if (!VirtualFleetFormations.IsSupported(message.payload.formationType))
                return Invalid("invalid_payload", "Unsupported formationType");
            message.payload.formationType = message.payload.formationType.Trim().ToUpperInvariant();
            return VirtualFleetValidationResult.Valid();
        }

        public static VirtualFleetValidationResult ValidateApplyPoseBatch(
            ApplyPoseBatchMessage message,
            long currentRunId,
            long lastSequence)
        {
            VirtualFleetValidationResult result = ValidateHeader(
                message, VirtualFleetMessageTypes.ApplyPoseBatch);
            if (!result.IsValid)
                return result;
            if (message.payload == null)
                return Invalid("invalid_payload", "Payload is required");

            VirtualPoseBatchPayload batch = message.payload;
            result = ValidateRuntimeMode(batch.runtimeMode);
            if (!result.IsValid)
                return result;
            if (currentRunId <= 0)
                return Invalid("run_not_loaded", "No scenario is loaded");
            if (batch.runId != currentRunId)
                return Invalid("run_mismatch", "Pose batch runId does not match current run");
            if (batch.sequence <= lastSequence)
                return Invalid("sequence_rewind", "Pose sequence must increase strictly");
            if (batch.vehicles == null && batch.targets == null && batch.poses == null)
                return Invalid("invalid_payload", "Pose batch has no devices");

            result = NormalizeAndValidatePoses(batch.vehicles);
            if (!result.IsValid)
                return result;
            result = NormalizeAndValidatePoses(batch.targets);
            if (!result.IsValid)
                return result;
            return NormalizeAndValidateInputs(batch.poses);
        }

        public static VirtualFleetValidationResult ValidateMissionCommand(
            MissionCommandMessage message,
            MissionState currentState,
            long currentRunId,
            out MissionState nextState)
        {
            nextState = currentState;
            VirtualFleetValidationResult result = ValidateHeader(
                message, message == null ? string.Empty : message.type);
            if (!result.IsValid)
                return result;
            if (!IsMissionCommand(message.type))
                return Invalid("unsupported_message", "Message is not a mission command");
            if (message.payload == null || message.payload.runId <= 0)
                return Invalid("invalid_payload", "Mission runId must be positive");
            if (currentRunId <= 0)
                return Invalid("run_not_loaded", "No scenario is loaded");
            if (message.payload.runId != currentRunId)
                return Invalid("run_mismatch", "Mission runId does not match current run");
            if (!TryGetNextState(message.type, currentState, out nextState))
                return Invalid("mission_state_conflict", "Invalid mission state transition");
            return VirtualFleetValidationResult.Valid();
        }

        public static string NormalizeDeviceCode(string deviceCode)
        {
            if (string.IsNullOrWhiteSpace(deviceCode))
                return string.Empty;
            string normalized = deviceCode.Trim().ToUpperInvariant().Replace('_', '-');
            string[] parts = normalized.Split('-');
            if (parts.Length != 2)
                return string.Empty;
            if (parts[0] != "UAV" && parts[0] != "USV" && parts[0] != "TARGET")
                return string.Empty;
            if (!int.TryParse(parts[1], out int number) || number < 1)
                return string.Empty;
            if (parts[0] == "TARGET" && number != 1)
                return string.Empty;
            if ((parts[0] == "UAV" || parts[0] == "USV") && number > 100)
                return string.Empty;
            return parts[0] + "-" + number.ToString("D3");
        }

        public static float NormalizeHeading(float headingDeg)
        {
            float normalized = headingDeg % 360f;
            return normalized < 0f ? normalized + 360f : normalized;
        }

        public static bool TryParseMissionState(string value, out MissionState state)
        {
            switch ((value ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "STOPPED": state = MissionState.Stopped; return true;
                case "RUNNING": state = MissionState.Running; return true;
                case "PAUSED": state = MissionState.Paused; return true;
                case "RESET": state = MissionState.Reset; return true;
                default: state = MissionState.Unknown; return false;
            }
        }

        private static VirtualFleetValidationResult ValidateHeader(
            VirtualFleetMessage message, string expectedType)
        {
            if (message == null)
                return Invalid("invalid_payload", "Message is null");
            if (!string.Equals(message.type, expectedType, StringComparison.Ordinal))
                return Invalid("unsupported_message", "Unexpected message type");
            if (string.IsNullOrWhiteSpace(message.requestId))
                return Invalid("invalid_payload", "RequestId is required");
            if (message.timestamp <= 0)
                return Invalid("invalid_payload", "Timestamp must be positive");
            return VirtualFleetValidationResult.Valid();
        }

        private static VirtualFleetValidationResult ValidateRuntimeMode(string runtimeMode)
        {
            return IsRuntimeMode(runtimeMode)
                ? VirtualFleetValidationResult.Valid()
                : Invalid("invalid_runtime_mode", "runtimeMode must be VIRTUAL_SIMULATION");
        }

        private static bool IsRuntimeMode(string runtimeMode)
        {
            return string.Equals(runtimeMode, VirtualFleetProtocol.RuntimeMode, StringComparison.Ordinal);
        }

        private static bool IsSupportedMessage(string type)
        {
            return type == VirtualFleetMessageTypes.InitializePlatform ||
                type == VirtualFleetMessageTypes.LoadScenario ||
                type == VirtualFleetMessageTypes.RegenerateScenario ||
                type == VirtualFleetMessageTypes.ApplyPoseBatch ||
                IsMissionCommand(type);
        }

        private static bool IsMissionCommand(string type)
        {
            return type == VirtualFleetMessageTypes.MissionStart ||
                type == VirtualFleetMessageTypes.MissionPause ||
                type == VirtualFleetMessageTypes.MissionResume ||
                type == VirtualFleetMessageTypes.MissionStop ||
                type == VirtualFleetMessageTypes.MissionReset;
        }

        private static bool TryGetNextState(string command, MissionState current, out MissionState next)
        {
            next = current;
            switch (command)
            {
                case VirtualFleetMessageTypes.MissionStart:
                    if (current == MissionState.Stopped) next = MissionState.Running; else return false;
                    break;
                case VirtualFleetMessageTypes.MissionPause:
                    if (current == MissionState.Running) next = MissionState.Paused; else return false;
                    break;
                case VirtualFleetMessageTypes.MissionResume:
                    if (current == MissionState.Paused) next = MissionState.Running; else return false;
                    break;
                case VirtualFleetMessageTypes.MissionStop:
                    if (current == MissionState.Running || current == MissionState.Paused) next = MissionState.Stopped; else return false;
                    break;
                case VirtualFleetMessageTypes.MissionReset:
                    if (current == MissionState.Stopped) next = MissionState.Reset; else return false;
                    break;
                default:
                    return false;
            }
            return true;
        }

        private static VirtualFleetValidationResult NormalizeAndValidatePoses(VirtualPose[] poses)
        {
            if (poses == null)
                return VirtualFleetValidationResult.Valid();
            foreach (VirtualPose pose in poses)
            {
                if (pose == null)
                    return Invalid("invalid_payload", "Pose entry cannot be null");
                string normalizedCode = NormalizeDeviceCode(pose.deviceCode);
                if (normalizedCode.Length == 0)
                    return Invalid("invalid_device_code", "Invalid deviceCode");
                if (!SupportedDeviceTypes.Contains(pose.deviceType))
                    return Invalid("invalid_payload", "Invalid deviceType");
                if (!normalizedCode.StartsWith(pose.deviceType.Trim().ToUpperInvariant() + "-", StringComparison.Ordinal))
                    return Invalid("invalid_device_code", "deviceCode and deviceType do not match");
                pose.deviceCode = normalizedCode;
                pose.deviceType = pose.deviceType.Trim().ToUpperInvariant();
                pose.headingDeg = NormalizeHeading(pose.headingDeg);
            }
            return VirtualFleetValidationResult.Valid();
        }

        private static VirtualFleetValidationResult NormalizeAndValidateInputs(
            VirtualPoseInput[] poses)
        {
            if (poses == null)
                return VirtualFleetValidationResult.Valid();
            foreach (VirtualPoseInput pose in poses)
            {
                if (pose == null)
                    return Invalid("invalid_payload", "Pose entry cannot be null");
                string code = pose.deviceCode;
                if (string.IsNullOrWhiteSpace(code))
                    code = string.IsNullOrWhiteSpace(pose.deviceId) ? pose.code : pose.deviceId;
                if (string.IsNullOrWhiteSpace(code))
                    code = pose.id;
                if (NormalizeDeviceCode(code).Length == 0)
                    return Invalid("invalid_device_code", "Invalid deviceCode");
            }
            return VirtualFleetValidationResult.Valid();
        }

        private static VirtualFleetValidationResult Invalid(string code, string message)
        {
            return VirtualFleetValidationResult.Invalid(code, message);
        }
    }
}
