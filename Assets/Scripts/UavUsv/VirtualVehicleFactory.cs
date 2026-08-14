using System;
using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Factory boundary for virtual vehicles. The first implementation reuses
    /// the existing runtime-built meshes from SimulationBootstrap.
    /// </summary>
    public sealed class VirtualVehicleFactory
    {
        private const float WaterSpawnHalfWidth = 28f;
        private const float WaterSpawnHalfDepth = 22f;
        private const float UavMinAltitude = 7f;
        private const float UavMaxAltitude = 12f;
        private const float SeaLevel = .03f;
        private const float UavMinimumSpacing = 1.35f;
        private const float UsvMinimumSpacing = 1.75f;
        private const int RandomPositionAttempts = 256;

        private readonly Transform[] uavPads;
        private readonly Vector3[] usvPositions;
        private readonly List<Vector3> uavSpawnPositions = new List<Vector3>();
        private readonly List<Vector3> usvSpawnPositions = new List<Vector3>();
        private System.Random random;
        private Vector3 waterSpawnCenter;

        public VirtualVehicleFactory(
            Transform[] uavPads,
            Vector3[] usvPositions,
            float[] usvYaws)
        {
            this.uavPads = uavPads ?? new Transform[0];
            this.usvPositions = usvPositions ?? new Vector3[0];
            waterSpawnCenter = CalculateWaterSpawnCenter();
            ResetRandom(VirtualFleetManager.DefaultRandomSeed);
        }

        public void ResetRandom(int seed)
        {
            random = new System.Random(seed);
            uavSpawnPositions.Clear();
            usvSpawnPositions.Clear();
        }

        public Transform Create(VirtualFleetDeviceType type, string deviceCode, int index)
        {
            if (type == VirtualFleetDeviceType.Uav)
            {
                Transform uav = SimulationBootstrap.BuildVirtualUav(deviceCode);
                Vector3 position = CreateRandomPosition(true, uavSpawnPositions);
                uav.SetPositionAndRotation(
                    position,
                    RandomRotation()
                );
                uavSpawnPositions.Add(position);
                return uav;
            }

            Transform usv = SimulationBootstrap.BuildVirtualUsv(
                deviceCode,
                new Color(.86f, .035f, .025f)
            );
            Vector3 usvPosition = CreateRandomPosition(false, usvSpawnPositions);
            usv.SetPositionAndRotation(
                usvPosition,
                RandomRotation()
            );
            usvSpawnPositions.Add(usvPosition);
            return usv;
        }

        private Vector3 CreateRandomPosition(
            bool uav,
            List<Vector3> occupiedPositions)
        {
            float minimumSpacing = uav
                ? UavMinimumSpacing
                : UsvMinimumSpacing;

            for (int attempt = 0; attempt < RandomPositionAttempts; attempt++)
            {
                Vector3 candidate = new Vector3(
                    waterSpawnCenter.x + NextRange(-WaterSpawnHalfWidth, WaterSpawnHalfWidth),
                    uav
                        ? NextRange(UavMinAltitude, UavMaxAltitude)
                        : SeaLevel,
                    waterSpawnCenter.z + NextRange(-WaterSpawnHalfDepth, WaterSpawnHalfDepth)
                );

                if (IsFarEnough(candidate, occupiedPositions, minimumSpacing))
                    return candidate;
            }

            // The default operating area is deliberately oversized for 100
            // devices. This fallback keeps generation total even if a custom
            // scene or future density setting leaves no perfect sample.
            Vector3 fallback = new Vector3(
                waterSpawnCenter.x + NextRange(-WaterSpawnHalfWidth, WaterSpawnHalfWidth),
                uav ? UavMinAltitude : SeaLevel,
                waterSpawnCenter.z + NextRange(-WaterSpawnHalfDepth, WaterSpawnHalfDepth)
            );
            return fallback;
        }

        private Quaternion RandomRotation()
        {
            return Quaternion.Euler(
                0f,
                -NextRange(0f, 360f),
                0f
            );
        }

        private bool IsFarEnough(
            Vector3 candidate,
            List<Vector3> occupiedPositions,
            float minimumSpacing)
        {
            for (int i = 0; i < occupiedPositions.Count; i++)
            {
                Vector3 other = occupiedPositions[i];
                float dx = candidate.x - other.x;
                float dz = candidate.z - other.z;
                if (dx * dx + dz * dz < minimumSpacing * minimumSpacing)
                    return false;
            }
            return true;
        }

        private Vector3 CalculateWaterSpawnCenter()
        {
            if (usvPositions.Length > 0)
            {
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < usvPositions.Length; i++)
                    sum += usvPositions[i];
                return sum / usvPositions.Length;
            }

            if (uavPads.Length > 0 && uavPads[0])
                return uavPads[0].position;

            return Vector3.zero;
        }

        private float NextRange(float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }
    }
}
