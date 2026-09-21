using System;
using UnityEngine;
using UavUsv;

namespace UavUsv.PlatformTools
{
    public static class VirtualFleetMessageTypes
    {
        public const string InitializePlatform = "initializePlatform";
        public const string LoadScenario = "loadScenario";
        public const string RegenerateScenario = "regenerateScenario";
        public const string ApplyPoseBatch = "applyPoseBatch";
        public const string MissionStart = "missionStart";
        public const string MissionPause = "missionPause";
        public const string MissionResume = "missionResume";
        public const string MissionStop = "missionStop";
        public const string MissionReset = "missionReset";
        public const string PresentationHello = "PRESENTATION_HELLO";
        public const string PresentationReady = "PRESENTATION_READY";
        public const string PresentationHeartbeat = "PRESENTATION_HEARTBEAT";
        public const string PresentationProbe = "PRESENTATION_PROBE";
        public const string PresentationReport = "PRESENTATION_REPORT";
    }

    public static class VirtualFleetProtocol
    {
        public const string Version = "2.0";
        public const string RuntimeMode = "VIRTUAL_SIMULATION";
        public const string GlobalCoordinateFrame = "GLOBAL_ENU";
        public const int MaxUavCount = 128;
        public const int MaxUsvCount = 128;
        public const int MaxTargetCount = 12;

        public static string SerializePresentationReport(PresentationReportResponse response)
        {
            if (response == null)
                return "null";

            string json = JsonUtility.ToJson(response);
            if (response.executionId == null)
            {
                const string emptyExecutionId = "\"executionId\":\"\"";
                const string nullExecutionId = "\"executionId\":null";
                json = json.Replace(emptyExecutionId, nullExecutionId);
                if (json.IndexOf("\"executionId\":", StringComparison.Ordinal) < 0)
                    json = json.Insert(1, nullExecutionId + ",");
            }
            return json;
        }
    }

    public static class VirtualFleetAlgorithms
    {
        public const string Capture = "GB_SFLA_CS";
        public const string Escort = "ESCORT_GUARD";

        public static bool IsSupported(string algorithmCode)
        {
            return string.Equals(algorithmCode, Capture, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(algorithmCode, Escort, StringComparison.OrdinalIgnoreCase);
        }
    }

    public enum MissionState
    {
        Unknown = 0,
        Stopped = 1,
        Running = 2,
        Paused = 3,
        Reset = 4
    }

    [Serializable]
    public class VirtualFleetMessage
    {
        public string type;
        public string requestId;
        public long timestamp;
    }

    [Serializable]
    public sealed class InitializePlatformMessage : VirtualFleetMessage
    {
        public InitializePlatformPayload payload;
    }

    [Serializable]
    public sealed class InitializePlatformPayload
    {
        public string requestId;
        public string runtimeMode;
        public string protocolVersion;
        public string buildId;
    }

    [Serializable]
    public sealed class LoadScenarioMessage : VirtualFleetMessage
    {
        public VirtualFleetConfigPayload payload;
    }

    [Serializable]
    public sealed class VirtualFleetConfigPayload
    {
        public string runtimeMode;
        public string algorithmCode;
        public long runId;
        public int uavCount;
        public int usvCount;
        public int targetCount;
        public float initialSpeedMps;
        public float initialHeadingDeg;
        public int seed;
        public string initialPosesCoordinateFrame;
        public UavUsv.VirtualPose[] initialPoses;
    }

    [Serializable]
    public sealed class RegenerateScenarioMessage : VirtualFleetMessage
    {
        public RegenerateScenarioPayload payload;
    }

    [Serializable]
    public sealed class RegenerateScenarioPayload
    {
        public string runtimeMode;
        public string algorithmCode;
        public long runId;
        public int uavCount;
        public int usvCount;
        public int targetCount;
        public float initialSpeedMps;
        public float initialHeadingDeg;
        public int seed;
    }

    [Serializable]
    public sealed class ApplyPoseBatchMessage : VirtualFleetMessage
    {
        public VirtualPoseBatchPayload payload;
    }

    [Serializable]
    public sealed class VirtualPoseBatchPayload
    {
        public string requestId;
        public string runtimeMode;
        public string coordinateFrame;
        public long runId;
        public long sequence;
        public long sampleTime;
        public UavUsv.VirtualPose[] vehicles;
        public UavUsv.VirtualPose[] targets;
        public VirtualPoseInput[] poses;
    }

    [Serializable]
    public sealed class VirtualPoseInput
    {
        public string deviceCode;
        public string deviceId;
        public string code;
        public string id;
        public string deviceType;
        public string type;
        public string state;
        public bool valid = true;
        public float eastM;
        public float northM;
        public float upM;
        public float headingDeg;
        public float speedMps;
        public float x;
        public float y;
        public float z;
        public float yaw;
        public float yawDegrees;
        public float[] position;
    }

    [Serializable]
    public sealed class MissionCommandMessage : VirtualFleetMessage
    {
        public MissionCommandPayload payload;
    }

    [Serializable]
    public sealed class MissionCommandPayload
    {
        public long runId;
        public string state;
    }

    [Serializable]
    public sealed class PresentationEnvelope : VirtualFleetMessage
    {
        public string protocolVersion;
        public string runtimeRef;
        public string runtimeGeneration;
        public string bindingId;
        public string unityInstanceId;
        public long sceneRevision;
        public bool scenarioReady;
        public long lastAppliedFrameSequence;
        public long sequence;
        public string kind;
        public string executionId;
        public bool applied;
        public long frameSequence;
    }

    [Serializable]
    public sealed class PresentationReadyResponse
    {
        public string type;
        public string protocolVersion;
        public string runtimeRef;
        public string runtimeGeneration;
        public string bindingId;
        public string unityInstanceId;
        public long sceneRevision;
        public bool scenarioReady;
        public long lastAppliedFrameSequence;
    }

    [Serializable]
    public sealed class PresentationReportResponse
    {
        public string type;
        public string protocolVersion;
        public string runtimeRef;
        public string runtimeGeneration;
        public string bindingId;
        public string unityInstanceId;
        public long sceneRevision;
        public string requestId;
        public long sequence;
        public string kind;
        public string executionId;
        public long frameSequence;
        public bool applied;
    }

    [Serializable]
    public sealed class PresentationHeartbeatResponse
    {
        public string type;
        public string protocolVersion;
        public string runtimeRef;
        public string runtimeGeneration;
        public string bindingId;
        public string unityInstanceId;
        public long sceneRevision;
        public bool scenarioReady;
        public long lastAppliedFrameSequence;
    }
}
