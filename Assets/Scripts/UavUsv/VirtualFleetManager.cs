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
        public const int MaximumUavCount = 128;
        public const int MaximumUsvCount = 128;
        public const int MaximumTargetCount = 12;
        public const int DefaultRandomSeed = 20260814;

        private readonly List<VirtualFleetDeviceState> uavs = new List<VirtualFleetDeviceState>();
        private readonly List<VirtualFleetDeviceState> usvs = new List<VirtualFleetDeviceState>();
        private readonly List<VirtualFleetDeviceState> targets = new List<VirtualFleetDeviceState>();
        private readonly HashSet<string> receivedRuntimePose =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector3> renderVelocities =
            new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        private int nextUavNumber = 1;
        private int nextUsvNumber = 1;
        private int currentRandomSeed = DefaultRandomSeed;
        private VirtualVehicleFactory factory;
        private Transform externalTargetTransform;
        private VirtualFleetMissionState missionState = VirtualFleetMissionState.Stopped;

        public VirtualFleetMissionState MissionState => missionState;
        public int UavCount => uavs.Count;
        public int UsvCount => usvs.Count;
        public int TargetCount => targets.Count;
        public int CurrentRandomSeed => currentRandomSeed;
        public IReadOnlyList<VirtualFleetDeviceState> Uavs => uavs;
        public IReadOnlyList<VirtualFleetDeviceState> Usvs => usvs;
        public IReadOnlyList<VirtualFleetDeviceState> Targets => targets;
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

        public void ConfigureTargetTransform(Transform targetTransform)
        {
            externalTargetTransform = targetTransform;
            if (factory == null)
                factory = new VirtualVehicleFactory(null, null, null);
            factory.SetTargetTransform(targetTransform);
        }

        public void Initialize(
            int uavCount = DefaultUavCount,
            int usvCount = DefaultUsvCount,
            int seed = DefaultRandomSeed,
            int targetCount = 1,
            VirtualPose[] initialPoses = null)
        {
            if (!CanModifyFleet)
                return;

            ClearFleet();
            if (factory == null)
                factory = new VirtualVehicleFactory(null, null, null);
            currentRandomSeed = seed == 0 ? DefaultRandomSeed : seed;
            factory.ResetRandom(currentRandomSeed);
            nextUavNumber = 1;
            nextUsvNumber = 1;
            for (int i = 0; i < Mathf.Clamp(uavCount, 1, MaximumUavCount); i++)
                AddUavInternal();
            for (int i = 0; i < Mathf.Clamp(usvCount, 1, MaximumUsvCount); i++)
                AddUsvInternal();
            int safeTargetCount = Mathf.Clamp(targetCount, 1, MaximumTargetCount);
            for (int i = 0; i < safeTargetCount; i++)
                AddTargetInternal(TargetTypeFor(i, initialPoses));
            NotifyFleetChanged();
        }

        public bool TryApplyPose(
            string deviceCode,
            Vector3 position,
            Quaternion rotation,
            string status)
        {
            VirtualFleetDeviceState state = FindDevice(deviceCode);
            if (state == null || !state.transform)
                return false;

            // The first authoritative pose establishes the scene immediately.
            // Later 10 Hz algorithm samples are render-interpolated in Update,
            // avoiding the visible stop/start motion caused by transform snaps.
            if (receivedRuntimePose.Add(state.deviceCode))
            {
                state.transform.SetPositionAndRotation(position, rotation);
                renderVelocities[state.deviceCode] = Vector3.zero;
            }
            state.position = position;
            state.rotation = rotation;
            if (!string.IsNullOrWhiteSpace(status))
                state.status = status.Trim().ToUpperInvariant();
            return true;
        }

        private void Update()
        {
            Interpolate(uavs);
            Interpolate(usvs);
            Interpolate(targets);
        }

        private void Interpolate(List<VirtualFleetDeviceState> devices)
        {
            for (int i = 0; i < devices.Count; i++)
            {
                VirtualFleetDeviceState state = devices[i];
                if (state == null || !state.transform)
                    continue;
                bool aircraft = string.Equals(
                    state.deviceType,
                    "UAV",
                    StringComparison.OrdinalIgnoreCase
                );
                float smoothTime = aircraft ? .065f : .105f;
                float physicalMaximum = aircraft ? 3.15f : .90f;
                if (!renderVelocities.TryGetValue(state.deviceCode, out Vector3 velocity))
                    velocity = Vector3.zero;
                state.transform.position = Vector3.SmoothDamp(
                    state.transform.position,
                    state.position,
                    ref velocity,
                    smoothTime,
                    physicalMaximum,
                    Time.deltaTime
                );
                renderVelocities[state.deviceCode] = velocity;
                float turnRate = aircraft ? 220f : 115f;
                state.transform.rotation = Quaternion.RotateTowards(
                    state.transform.rotation,
                    state.rotation,
                    turnRate * Time.deltaTime
                );
            }
        }

        public bool TryApplyTargetPose(
            string deviceCode,
            Vector3 position,
            Quaternion rotation,
            string status)
        {
            if (string.IsNullOrWhiteSpace(deviceCode) ||
                !deviceCode.Trim().StartsWith("TARGET-", StringComparison.OrdinalIgnoreCase))
                return false;
            return TryApplyPose(deviceCode, position, rotation, status);
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
            Initialize(
                Mathf.Max(1, uavs.Count),
                Mathf.Max(1, usvs.Count),
                currentRandomSeed,
                Mathf.Max(1, targets.Count)
            );
            SetMissionState(VirtualFleetMissionState.Stopped);
        }

        public VirtualFleetSnapshot GetSnapshot()
        {
            var all = new List<VirtualFleetDeviceState>(
                uavs.Count + usvs.Count + targets.Count
            );
            all.AddRange(uavs);
            all.AddRange(usvs);
            all.AddRange(targets);
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

        private void AddTargetInternal(string targetType)
        {
            string code = "TARGET-" + (targets.Count + 1).ToString("000");
            Transform model = factory.Create(
                VirtualFleetDeviceType.Target,
                code,
                targets.Count,
                targetType
            );
            targets.Add(CreateState(code, "TARGET", model));
        }

        public Transform[] GetTargetTransforms()
        {
            return ToTransformArray(targets);
        }

        private static string TargetTypeFor(int index, VirtualPose[] initialPoses)
        {
            string code = "TARGET-" + (index + 1).ToString("000");
            if (initialPoses != null)
                for (int i = 0; i < initialPoses.Length; i++)
                    if (initialPoses[i] != null &&
                        string.Equals(initialPoses[i].deviceCode, code, StringComparison.OrdinalIgnoreCase))
                        return initialPoses[i].targetType ?? initialPoses[i].state;
            return "THREAT_TARGET";
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
            receivedRuntimePose.Remove(state.deviceCode);
            renderVelocities.Remove(state.deviceCode);
            NotifyFleetChanged();
            return true;
        }

        private void ClearFleet()
        {
            for (int i = 0; i < uavs.Count; i++)
                if (uavs[i].transform) Destroy(uavs[i].transform.gameObject);
            for (int i = 0; i < usvs.Count; i++)
                if (usvs[i].transform) Destroy(usvs[i].transform.gameObject);
            for (int i = 0; i < targets.Count; i++)
                if (targets[i].transform &&
                    targets[i].transform != externalTargetTransform)
                    Destroy(targets[i].transform.gameObject);
            uavs.Clear();
            usvs.Clear();
            targets.Clear();
            receivedRuntimePose.Clear();
            renderVelocities.Clear();
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
            for (int i = 0; i < targets.Count; i++)
                targets[i].status = nextState.ToString().ToUpperInvariant();
            MissionStateChanged?.Invoke(missionState);
        }

        private void NotifyFleetChanged()
        {
            FleetChanged?.Invoke();
        }

        private VirtualFleetDeviceState FindDevice(string deviceCode)
        {
            if (string.IsNullOrWhiteSpace(deviceCode))
                return null;
            string normalized = deviceCode.Trim();
            for (int i = 0; i < uavs.Count; i++)
                if (string.Equals(uavs[i].deviceCode, normalized, StringComparison.OrdinalIgnoreCase))
                    return uavs[i];
            for (int i = 0; i < usvs.Count; i++)
                if (string.Equals(usvs[i].deviceCode, normalized, StringComparison.OrdinalIgnoreCase))
                    return usvs[i];
            for (int i = 0; i < targets.Count; i++)
                if (string.Equals(targets[i].deviceCode, normalized, StringComparison.OrdinalIgnoreCase))
                    return targets[i];
            return null;
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
