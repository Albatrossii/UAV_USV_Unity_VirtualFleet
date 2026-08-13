using System;
using UnityEngine;

namespace UavUsv
{
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
        public Transform transform;
    }

    [Serializable]
    public sealed class VirtualFleetSnapshot
    {
        public string mode = "VIRTUAL_SIMULATION";
        public string missionState;
        public VirtualFleetDeviceState[] devices;
    }
}
