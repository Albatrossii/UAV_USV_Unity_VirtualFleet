using System;
using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Owns virtual UAV/USV instances and exposes the stable A/B boundary.
    /// It deliberately contains no ROS or WebGL transport code.
    /// </summary>
    public sealed class VirtualFleetManager : MonoBehaviour
    {
        public const int DefaultUavCount = 3;
        public const int DefaultUsvCount = 3;
        public const int MaximumUavCount = 100;
        public const int MaximumUsvCount = 100;

        private readonly List<VirtualFleetDeviceState> uavs = new List<VirtualFleetDeviceState>();
        private readonly List<VirtualFleetDeviceState> usvs = new List<VirtualFleetDeviceState>();
        private int nextUavNumber = 1;
        private int nextUsvNumber = 1;
        private VirtualVehicleFactory factory;
        private VirtualFleetMissionState missionState = VirtualFleetMissionState.Stopped;

        public VirtualFleetMissionState MissionState => missionState;
        public int UavCount => uavs.Count;
        public int UsvCount => usvs.Count;
        public bool CanModifyFleet =>
            missionState == VirtualFleetMissionState.Stopped ||
            missionState == VirtualFleetMissionState.Reset;

        public event Action FleetChanged;
        public event Action<VirtualFleetMissionState> MissionStateChanged;

        public void ConfigureSpawnPoints(
            Transform[] uavPads,
            Vector3[] usvPositions,
            float[] usvYaws)
        {
            factory = new VirtualVehicleFactory(uavPads, usvPositions, usvYaws);
        }

        public void Initialize(int uavCount = DefaultUavCount, int usvCount = DefaultUsvCount)
        {
            if (!CanModifyFleet)
                return;

            ClearFleet();
            if (factory == null)
                factory = new VirtualVehicleFactory(null, null, null);
            nextUavNumber = 1;
            nextUsvNumber = 1;
            for (int i = 0; i < Mathf.Clamp(uavCount, 1, MaximumUavCount); i++)
                AddUavInternal();
            for (int i = 0; i < Mathf.Clamp(usvCount, 1, MaximumUsvCount); i++)
                AddUsvInternal();
            NotifyFleetChanged();
        }

        public Transform[] GetUavTransforms()
        {
            return ToTransformArray(uavs);
        }

        public Transform[] GetUsvTransforms()
        {
            return ToTransformArray(usvs);
        }

        public bool AddUav()
        {
            if (!CanModifyFleet || uavs.Count >= MaximumUavCount)
                return false;
            AddUavInternal();
            NotifyFleetChanged();
            return true;
        }

        public bool AddUsv()
        {
            if (!CanModifyFleet || usvs.Count >= MaximumUsvCount)
                return false;
            AddUsvInternal();
            NotifyFleetChanged();
            return true;
        }

        public bool RemoveUav(string deviceCode)
        {
            return Remove(uavs, deviceCode);
        }

        public bool RemoveUsv(string deviceCode)
        {
            return Remove(usvs, deviceCode);
        }

        public void StartMission()
        {
            SetMissionState(VirtualFleetMissionState.Running);
        }

        public void PauseMission()
        {
            if (missionState == VirtualFleetMissionState.Running)
                SetMissionState(VirtualFleetMissionState.Paused);
        }

        public void ResumeMission()
        {
            if (missionState == VirtualFleetMissionState.Paused)
                SetMissionState(VirtualFleetMissionState.Running);
        }

        public void StopMission()
        {
            SetMissionState(VirtualFleetMissionState.Stopped);
        }

        public void ResetMission()
        {
            SetMissionState(VirtualFleetMissionState.Reset);
            Initialize(DefaultUavCount, DefaultUsvCount);
            SetMissionState(VirtualFleetMissionState.Stopped);
        }

        public VirtualFleetSnapshot GetSnapshot()
        {
            var all = new List<VirtualFleetDeviceState>(uavs.Count + usvs.Count);
            all.AddRange(uavs);
            all.AddRange(usvs);
            return new VirtualFleetSnapshot
            {
                missionState = missionState.ToString().ToUpperInvariant(),
                devices = all.ToArray()
            };
        }

        private void AddUavInternal()
        {
            string code = "UAV-" + nextUavNumber.ToString("000");
            nextUavNumber++;
            Transform model = factory.Create(VirtualFleetDeviceType.Uav, code, uavs.Count);
            uavs.Add(CreateState(code, "UAV", model));
        }

        private void AddUsvInternal()
        {
            string code = "USV-" + nextUsvNumber.ToString("000");
            nextUsvNumber++;
            Transform model = factory.Create(VirtualFleetDeviceType.Usv, code, usvs.Count);
            usvs.Add(CreateState(code, "USV", model));
        }

        private static VirtualFleetDeviceState CreateState(
            string code,
            string type,
            Transform model)
        {
            return new VirtualFleetDeviceState
            {
                deviceCode = code,
                deviceType = type,
                status = "STOPPED",
                position = model.position,
                rotation = model.rotation,
                transform = model
            };
        }

        private bool Remove(List<VirtualFleetDeviceState> devices, string deviceCode)
        {
            if (!CanModifyFleet || string.IsNullOrWhiteSpace(deviceCode))
                return false;
            int index = devices.FindIndex(item =>
                string.Equals(item.deviceCode, deviceCode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;
            VirtualFleetDeviceState state = devices[index];
            if (state.transform)
                Destroy(state.transform.gameObject);
            devices.RemoveAt(index);
            NotifyFleetChanged();
            return true;
        }

        private void ClearFleet()
        {
            for (int i = 0; i < uavs.Count; i++)
                if (uavs[i].transform) Destroy(uavs[i].transform.gameObject);
            for (int i = 0; i < usvs.Count; i++)
                if (usvs[i].transform) Destroy(usvs[i].transform.gameObject);
            uavs.Clear();
            usvs.Clear();
        }

        private void SetMissionState(VirtualFleetMissionState nextState)
        {
            if (missionState == nextState)
                return;
            missionState = nextState;
            for (int i = 0; i < uavs.Count; i++)
                uavs[i].status = nextState.ToString().ToUpperInvariant();
            for (int i = 0; i < usvs.Count; i++)
                usvs[i].status = nextState.ToString().ToUpperInvariant();
            MissionStateChanged?.Invoke(missionState);
        }

        private void NotifyFleetChanged()
        {
            FleetChanged?.Invoke();
        }

        private static Transform[] ToTransformArray(
            List<VirtualFleetDeviceState> devices)
        {
            var result = new Transform[devices.Count];
            for (int i = 0; i < devices.Count; i++)
                result[i] = devices[i].transform;
            return result;
        }
    }
}
