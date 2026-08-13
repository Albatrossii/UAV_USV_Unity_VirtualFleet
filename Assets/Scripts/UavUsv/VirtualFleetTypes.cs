using System;
using UnityEngine;

namespace UavUsv
{
    public enum VirtualFleetFormationType
    {
        Random,
        Circle,
        Encirclement,
        Escort
    }

    public enum VirtualFleetMissionState
    {
        Stopped,
        Running,
        Paused,
        Reset
    }

    public enum VirtualFleetDeviceType
    {
        Uav,
        Usv
    }

    [Serializable]
    public sealed class VirtualFleetDeviceState
    {
        public string deviceCode;
        public string deviceType;
        public string status;
        public string mode = "VIRTUAL_SIMULATION";
        public Vector3 position;
        public Quaternion rotation;
        [NonSerialized] internal Transform transform;
    }

    [Serializable]
    public sealed class VirtualFleetConfig
    {
        public string runtimeMode = "VIRTUAL_SIMULATION";
        public string algorithmCode = "GB_SFLA_CS";
        public long runId;
        public int uavCount = 3;
        public int usvCount = 3;
        public int targetCount = 1;
        public VirtualFleetFormationType formationType =
            VirtualFleetFormationType.Encirclement;
        public float initialSpeedMps;
        public float initialHeadingDeg;
        public int seed;
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
        public bool valid = true;
    }

    [Serializable]
    public sealed class VirtualPoseBatch
    {
        public string runtimeMode = "VIRTUAL_SIMULATION";
        public long runId;
        public long sequence;
        public long sampleTime;
        public VirtualPose[] vehicles;
        public VirtualPose[] targets;
    }

    [Serializable]
    public sealed class VirtualPoseBatchApplyResult
    {
        public bool success;
        public string code;
        public string message;
        public long runId;
        public long sequence;
        public int appliedCount;
        public string[] missingDeviceCodes;
        public string[] unknownDeviceCodes;
    }

    public interface IVirtualFleetRuntime
    {
        void Configure(VirtualFleetConfig config);
        void Regenerate();
        VirtualPoseBatchApplyResult ApplyPoseBatch(VirtualPoseBatch batch);
        void StartMission();
        void PauseMission();
        void ResumeMission();
        void StopMission();
        void ResetMission();
    }

    [Serializable]
    public sealed class VirtualFleetSnapshot
    {
        public string mode = "VIRTUAL_SIMULATION";
        public string missionState;
        public VirtualFleetDeviceState[] devices;
    }
}
