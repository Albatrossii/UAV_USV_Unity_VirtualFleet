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
        private readonly List<VirtualFleetDeviceState> renderedDevices =
            new List<VirtualFleetDeviceState>(MaximumUavCount + MaximumUsvCount + MaximumTargetCount);
        private readonly Dictionary<Vector2Int, List<int>> renderSafetyBuckets =
            new Dictionary<Vector2Int, List<int>>();
        private readonly List<Vector2Int> activeRenderSafetyBuckets =
            new List<Vector2Int>();
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
            ResolveRenderedSeparation();
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

        private void ResolveRenderedSeparation()
        {
            renderedDevices.Clear();
            renderedDevices.AddRange(uavs);
            renderedDevices.AddRange(usvs);
            renderedDevices.AddRange(targets);
            if (renderedDevices.Count < 2)
                return;

            // Algorithm samples are safe at 10 Hz, but independent SmoothDamp
            // curves can bow into each other between two samples. Resolve the
            // actual rendered transforms using model-sized horizontal
            // envelopes. A spatial hash keeps this near-linear for 128+128.
            const float cellSize = 3.2f;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                for (int i = 0; i < activeRenderSafetyBuckets.Count; i++)
                    renderSafetyBuckets[activeRenderSafetyBuckets[i]].Clear();
                activeRenderSafetyBuckets.Clear();

                for (int index = 0; index < renderedDevices.Count; index++)
                {
                    VirtualFleetDeviceState state = renderedDevices[index];
                    if (state == null || !state.transform)
                        continue;
                    Vector3 position = state.transform.position;
                    Vector2Int cell = new Vector2Int(
                        Mathf.FloorToInt(position.x / cellSize),
                        Mathf.FloorToInt(position.z / cellSize)
                    );
                    if (!renderSafetyBuckets.TryGetValue(cell, out List<int> members))
                    {
                        members = new List<int>(8);
                        renderSafetyBuckets[cell] = members;
                    }
                    if (members.Count == 0)
                        activeRenderSafetyBuckets.Add(cell);
                    members.Add(index);
                }

                bool corrected = false;
                for (int leftIndex = 0; leftIndex < renderedDevices.Count; leftIndex++)
                {
                    VirtualFleetDeviceState left = renderedDevices[leftIndex];
                    if (left == null || !left.transform)
                        continue;
                    Vector3 leftPosition = left.transform.position;
                    Vector2Int leftCell = new Vector2Int(
                        Mathf.FloorToInt(leftPosition.x / cellSize),
                        Mathf.FloorToInt(leftPosition.z / cellSize)
                    );
                    for (int cellX = -1; cellX <= 1; cellX++)
                    for (int cellY = -1; cellY <= 1; cellY++)
                    {
                        Vector2Int cell = leftCell + new Vector2Int(cellX, cellY);
                        if (!renderSafetyBuckets.TryGetValue(cell, out List<int> members))
                            continue;
                        for (int member = 0; member < members.Count; member++)
                        {
                            int rightIndex = members[member];
                            if (rightIndex <= leftIndex)
                                continue;
                            if (SeparateRenderedPair(left, renderedDevices[rightIndex], leftIndex, rightIndex))
                                corrected = true;
                        }
                    }
                }
                if (!corrected)
                    break;
            }
        }

        private bool SeparateRenderedPair(
            VirtualFleetDeviceState first,
            VirtualFleetDeviceState second,
            int firstIndex,
            int secondIndex)
        {
            if (second == null || !second.transform)
                return false;
            bool firstTarget = IsTarget(first);
            bool secondTarget = IsTarget(second);
            if (firstTarget && secondTarget)
                return false;

            Vector3 firstPosition = first.transform.position;
            Vector3 secondPosition = second.transform.position;
            Vector2 delta = new Vector2(
                firstPosition.x - secondPosition.x,
                firstPosition.z - secondPosition.z
            );
            float required = RenderRadius(first) + RenderRadius(second) + .25f;
            float distance = delta.magnitude;
            if (distance >= required)
                return false;
            if (distance < .0001f)
            {
                float angle = ((firstIndex * 37 + secondIndex * 53) % 360) * Mathf.Deg2Rad;
                delta = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                distance = 1f;
            }
            Vector2 direction = delta / distance;
            float overlap = required - distance;
            float firstShare = firstTarget ? 0f : secondTarget ? 1f : .5f;
            float secondShare = secondTarget ? 0f : firstTarget ? 1f : .5f;
            if (firstShare > 0f)
            {
                firstPosition.x += direction.x * overlap * firstShare;
                firstPosition.z += direction.y * overlap * firstShare;
                first.transform.position = firstPosition;
                renderVelocities[first.deviceCode] = Vector3.zero;
            }
            if (secondShare > 0f)
            {
                secondPosition.x -= direction.x * overlap * secondShare;
                secondPosition.z -= direction.y * overlap * secondShare;
                second.transform.position = secondPosition;
                renderVelocities[second.deviceCode] = Vector3.zero;
            }
            return true;
        }

        private static bool IsTarget(VirtualFleetDeviceState state)
        {
            return string.Equals(state.deviceType, "TARGET", StringComparison.OrdinalIgnoreCase);
        }

        private static float RenderRadius(VirtualFleetDeviceState state)
        {
            if (IsTarget(state))
                return 1.55f;
            return string.Equals(state.deviceType, "USV", StringComparison.OrdinalIgnoreCase)
                ? .96f
                : .58f;
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
