using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Factory boundary for virtual vehicles. The first implementation reuses
    /// the existing runtime-built meshes from SimulationBootstrap.
    /// </summary>
    public sealed class VirtualVehicleFactory
    {
        private readonly Transform[] uavPads;
        private readonly Vector3[] usvPositions;
        private readonly float[] usvYaws;

        public VirtualVehicleFactory(
            Transform[] uavPads,
            Vector3[] usvPositions,
            float[] usvYaws)
        {
            this.uavPads = uavPads ?? new Transform[0];
            this.usvPositions = usvPositions ?? new Vector3[0];
            this.usvYaws = usvYaws ?? new float[0];
        }

        public Transform Create(VirtualFleetDeviceType type, string deviceCode, int index)
        {
            if (type == VirtualFleetDeviceType.Uav)
            {
                Transform uav = SimulationBootstrap.BuildVirtualUav(deviceCode);
                Transform pad = index < uavPads.Length ? uavPads[index] : null;
                if (pad)
                    SimulationBootstrap.PlaceVirtualUavOnPad(uav, pad, 0f);
                else
                    uav.position = ExpansionPosition(index - uavPads.Length, true);
                return uav;
            }

            Transform usv = SimulationBootstrap.BuildVirtualUsv(
                deviceCode,
                new Color(.86f, .035f, .025f)
            );
            if (index < usvPositions.Length)
                usv.position = usvPositions[index % usvPositions.Length];
            else
                usv.position = ExpansionPosition(index - usvPositions.Length, false);
            float yaw = index < usvYaws.Length ? usvYaws[index] : 0f;
            usv.rotation = Quaternion.Euler(0f, -yaw * Mathf.Rad2Deg, 0f);
            return usv;
        }

        private static Vector3 ExpansionPosition(int index, bool uav)
        {
            int column = index % 10;
            int row = index / 10;
            if (uav)
                return new Vector3(-4.5f + column * 1.05f, 8f, -3.5f - row * 1.05f);
            return new Vector3(-7.5f + column * 1.8f, .03f, -12f - row * 1.8f);
        }
    }
}
