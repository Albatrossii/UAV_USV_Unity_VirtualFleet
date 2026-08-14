using System;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Small integration facade for PlatformBridge and future front-end code.
    /// Transport code should call this facade instead of touching transforms.
    /// </summary>
    public sealed class VirtualFleetScenarioController :
        MonoBehaviour,
        IVirtualFleetRuntime
    {
        [SerializeField] private VirtualFleetManager fleetManager;

        public VirtualFleetManager FleetManager => fleetManager;
        public bool CanModifyFleet => fleetManager && fleetManager.CanModifyFleet;
        public VirtualFleetConfig CurrentConfig { get; private set; }
        public long CurrentRunId => CurrentConfig != null ? CurrentConfig.runId : 0;
        public long LastAppliedSequence { get; private set; } = -1;

        private void Awake()
        {
            if (!fleetManager)
                fleetManager = GetComponent<VirtualFleetManager>();
        }

        public void Bind(VirtualFleetManager manager)
        {
            fleetManager = manager;
        }

        public void Configure(VirtualFleetConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            EnsureRuntime();
            if (!fleetManager.CanModifyFleet)
                throw new InvalidOperationException("scenario_locked");
            ValidateConfig(config);
            CurrentConfig = CloneConfig(config);
            LastAppliedSequence = -1;
        }

        public void Regenerate()
        {
            EnsureRuntime();
            if (!fleetManager.CanModifyFleet)
                throw new InvalidOperationException("scenario_locked");
            if (CurrentConfig == null)
                CurrentConfig = new VirtualFleetConfig();

            fleetManager.Initialize(
                CurrentConfig.uavCount,
                CurrentConfig.usvCount,
                CurrentConfig.seed
            );
            LastAppliedSequence = -1;
        }

        public VirtualPoseBatchApplyResult ApplyPoseBatch(VirtualPoseBatch batch)
        {
            EnsureRuntime();
            VirtualPoseBatchApplyResult result = new VirtualPoseBatchApplyResult
            {
                success = false,
                runId = batch != null ? batch.runId : 0,
                sequence = batch != null ? batch.sequence : 0,
                missingDeviceCodes = new string[0],
                unknownDeviceCodes = new string[0]
            };

            if (batch == null)
                return Failure(result, "invalid_payload", "位姿批次为空");
            if (!string.Equals(batch.runtimeMode, "VIRTUAL_SIMULATION", StringComparison.OrdinalIgnoreCase))
                return Failure(result, "invalid_runtime_mode", "仅支持 VIRTUAL_SIMULATION");
            if (CurrentConfig == null || CurrentConfig.runId <= 0)
                return Failure(result, "run_not_loaded", "尚未加载场景");
            if (batch.runId != CurrentConfig.runId)
                return Failure(result, "run_mismatch", "runId 与当前场景不一致");
            if (batch.sequence <= LastAppliedSequence)
                return Failure(result, "sequence_rewind", "sequence 未严格递增");

            var missing = new System.Collections.Generic.List<string>();
            var unknown = new System.Collections.Generic.List<string>();
            int applied = 0;
            VirtualPose[] poses = batch.vehicles ?? new VirtualPose[0];
            var seen = new System.Collections.Generic.HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );
            for (int i = 0; i < poses.Length; i++)
            {
                VirtualPose pose = poses[i];
                if (pose == null || !pose.valid || string.IsNullOrWhiteSpace(pose.deviceCode))
                    continue;
                string code = pose.deviceCode.Trim();
                if (!seen.Add(code))
                    continue;
                VirtualFleetDeviceState known = FindState(code);
                if (known == null)
                {
                    unknown.Add(code);
                    continue;
                }

                Vector3 position = Coordinates.ToPresentation(
                    pose.eastM,
                    pose.northM,
                    pose.upM
                );
                Quaternion rotation = Quaternion.Euler(
                    0f,
                    -NormalizeHeading(pose.headingDeg),
                    0f
                );
                if (fleetManager.TryApplyPose(code, position, rotation, pose.state))
                    applied++;
            }

            AddMissingCodes(missing, fleetManager.Uavs, seen);
            AddMissingCodes(missing, fleetManager.Usvs, seen);
            LastAppliedSequence = batch.sequence;
            result.success = true;
            result.code = "ok";
            result.message = "位姿批次已应用";
            result.appliedCount = applied;
            result.missingDeviceCodes = missing.ToArray();
            result.unknownDeviceCodes = unknown.ToArray();
            return result;
        }

        public bool AddUav()
        {
            return fleetManager && fleetManager.AddUav();
        }

        public bool AddUsv()
        {
            return fleetManager && fleetManager.AddUsv();
        }

        public bool RemoveUav(string deviceCode)
        {
            return fleetManager && fleetManager.RemoveUav(deviceCode);
        }

        public bool RemoveUsv(string deviceCode)
        {
            return fleetManager && fleetManager.RemoveUsv(deviceCode);
        }

        public void StartMission()
        {
            if (fleetManager) fleetManager.StartMission();
        }

        public void PauseMission()
        {
            if (fleetManager) fleetManager.PauseMission();
        }

        public void ResumeMission()
        {
            if (fleetManager) fleetManager.ResumeMission();
        }

        public void StopMission()
        {
            if (fleetManager) fleetManager.StopMission();
        }

        public void ResetMission()
        {
            if (fleetManager) fleetManager.ResetMission();
        }

        public VirtualFleetSnapshot GetSnapshot()
        {
            return fleetManager ? fleetManager.GetSnapshot() : new VirtualFleetSnapshot();
        }

        private void EnsureRuntime()
        {
            if (!fleetManager)
                fleetManager = GetComponent<VirtualFleetManager>();
            if (!fleetManager)
                throw new InvalidOperationException("VirtualFleetManager 未绑定");
        }

        private static void ValidateConfig(VirtualFleetConfig config)
        {
            if (!string.Equals(config.runtimeMode, "VIRTUAL_SIMULATION", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("invalid_runtime_mode");
            if (config.runId <= 0)
                throw new ArgumentException("invalid_run_id");
            if (config.algorithmCode != "GB_SFLA_CS" &&
                config.algorithmCode != "ESCORT_GUARD")
                throw new ArgumentException("invalid_algorithm");
            if (config.uavCount < 1 || config.uavCount > VirtualFleetManager.MaximumUavCount ||
                config.usvCount < 1 || config.usvCount > VirtualFleetManager.MaximumUsvCount)
                throw new ArgumentException("invalid_count");
            if (config.targetCount != 1)
                throw new ArgumentException("invalid_target_count");
            if (config.initialSpeedMps < 0f)
                throw new ArgumentException("invalid_speed");
        }

        private static VirtualFleetConfig CloneConfig(VirtualFleetConfig config)
        {
            return new VirtualFleetConfig
            {
                runtimeMode = "VIRTUAL_SIMULATION",
                algorithmCode = config.algorithmCode,
                runId = config.runId,
                uavCount = config.uavCount,
                usvCount = config.usvCount,
                targetCount = config.targetCount,
                formationType = config.formationType,
                initialSpeedMps = config.initialSpeedMps,
                initialHeadingDeg = NormalizeHeading(config.initialHeadingDeg),
                seed = config.seed
            };
        }

        private VirtualFleetDeviceState FindState(string deviceCode)
        {
            for (int i = 0; i < fleetManager.Uavs.Count; i++)
                if (string.Equals(fleetManager.Uavs[i].deviceCode, deviceCode, StringComparison.OrdinalIgnoreCase))
                    return fleetManager.Uavs[i];
            for (int i = 0; i < fleetManager.Usvs.Count; i++)
                if (string.Equals(fleetManager.Usvs[i].deviceCode, deviceCode, StringComparison.OrdinalIgnoreCase))
                    return fleetManager.Usvs[i];
            return null;
        }

        private static void AddMissingCodes(
            System.Collections.Generic.List<string> missing,
            System.Collections.Generic.IReadOnlyList<VirtualFleetDeviceState> devices,
            System.Collections.Generic.HashSet<string> seen)
        {
            for (int i = 0; i < devices.Count; i++)
                if (devices[i] != null && !string.IsNullOrEmpty(devices[i].deviceCode))
                {
                    if (!seen.Contains(devices[i].deviceCode))
                        missing.Add(devices[i].deviceCode);
                }
        }

        private static float NormalizeHeading(float heading)
        {
            float normalized = heading % 360f;
            return normalized < 0f ? normalized + 360f : normalized;
        }

        private static VirtualPoseBatchApplyResult Failure(
            VirtualPoseBatchApplyResult result,
            string code,
            string message)
        {
            result.code = code;
            result.message = message;
            return result;
        }
    }
}
