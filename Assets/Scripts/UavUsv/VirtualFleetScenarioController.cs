using System;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Small integration facade for PlatformBridge and future front-end code.
    /// Transport code should call this facade instead of touching transforms.
    /// </summary>
    public sealed class VirtualFleetScenarioController : MonoBehaviour
    {
        [SerializeField] private VirtualFleetManager fleetManager;

        public VirtualFleetManager FleetManager => fleetManager;
        public bool CanModifyFleet => fleetManager && fleetManager.CanModifyFleet;

        private void Awake()
        {
            if (!fleetManager)
                fleetManager = GetComponent<VirtualFleetManager>();
        }

        public void Bind(VirtualFleetManager manager)
        {
            fleetManager = manager;
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
    }
}
