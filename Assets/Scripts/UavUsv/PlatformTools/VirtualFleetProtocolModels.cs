using System;

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
    }

    public static class VirtualFleetProtocol
    {
        public const string Version = "1.0";
        public const string RuntimeMode = "VIRTUAL_SIMULATION";
        public const int MaxUavCount = 100;
        public const int MaxUsvCount = 100;
        public const int FixedTargetCount = 1;
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

    public static class VirtualFleetFormations
    {
        public const string Random = "RANDOM";
        public const string Circle = "CIRCLE";
        public const string Encirclement = "ENCIRCLEMENT";
        public const string Escort = "ESCORT";

        public static bool IsSupported(string formationType)
        {
            return string.Equals(formationType, Random, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(formationType, Circle, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(formationType, Encirclement, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(formationType, Escort, StringComparison.OrdinalIgnoreCase);
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
        public string runtimeMode;
        public string protocolVersion;
        public string buildId;
    }

    [Serializable]
    public sealed class LoadScenarioMessage : VirtualFleetMessage
    {
        public VirtualFleetConfig payload;
    }

    [Serializable]
    public sealed class RegenerateScenarioMessage : VirtualFleetMessage
    {
        public RegenerateScenarioPayload payload;
    }

    [Serializable]
    public sealed class VirtualFleetConfig
    {
        public string runtimeMode;
        public string algorithmCode;
        public int runId;
        public int uavCount;
        public int usvCount;
        public int targetCount;
        public string formationType;
        public float initialSpeedMps;
        public float initialHeadingDeg;
        public int seed;
    }

    [Serializable]
    public sealed class RegenerateScenarioPayload
    {
        public int runId;
        public int uavCount;
        public int usvCount;
        public string formationType;
        public int seed;
    }

    [Serializable]
    public sealed class ApplyPoseBatchMessage : VirtualFleetMessage
    {
        public VirtualPoseBatch payload;
    }

    [Serializable]
    public sealed class VirtualPoseBatch
    {
        public string runtimeMode;
        public int runId;
        public long sequence;
        public long sampleTime;
        public VirtualPose[] vehicles;
        public VirtualTargetPose[] targets;
    }

    [Serializable]
    public sealed class VirtualPose
    {
        public string deviceCode;
        public string deviceType;
        public float eastM;
        public float northM;
        public float upM;
        public float headingDeg;
        public float speedMps;
        public string state;
        public bool valid;
    }

    [Serializable]
    public sealed class VirtualTargetPose
    {
        public string deviceCode;
        public string targetType;
        public float eastM;
        public float northM;
        public float upM;
        public float headingDeg;
        public bool valid;
    }

    [Serializable]
    public sealed class MissionCommandMessage : VirtualFleetMessage
    {
        public MissionCommandPayload payload;
    }

    [Serializable]
    public sealed class MissionCommandPayload
    {
        public int runId;
    }
}
