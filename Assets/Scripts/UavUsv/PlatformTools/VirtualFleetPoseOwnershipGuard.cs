using UnityEngine;

namespace UavUsv.PlatformTools
{
    /// <summary>
    /// Keeps virtual-fleet transforms owned by the protocol runtime.
    /// Legacy scene scripts may still write transforms during LateUpdate.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class VirtualFleetPoseOwnershipGuard : MonoBehaviour
    {
        private UavUsv.VirtualFleetScenarioController runtime;
        private bool driftLogged;

        public void Initialize(UavUsv.VirtualFleetScenarioController controller)
        {
            runtime = controller;
        }

        private void LateUpdate()
        {
            if (!runtime)
                return;

            UavUsv.VirtualFleetSnapshot snapshot = runtime.GetSnapshot();
            if (snapshot == null || snapshot.devices == null)
                return;

            for (int i = 0; i < snapshot.devices.Length; i++)
            {
                UavUsv.VirtualFleetDeviceState device = snapshot.devices[i];
                if (device == null || !device.transform)
                    continue;

                Transform model = device.transform;
                bool positionDrifted =
                    Vector3.Distance(model.position, device.position) > 0.001f;
                bool rotationDrifted =
                    Quaternion.Angle(model.rotation, device.rotation) > 0.1f;
                if (!positionDrifted && !rotationDrifted)
                    continue;

                if (!driftLogged)
                {
                    driftLogged = true;
                    Debug.Log(
                        "[VirtualFleetPoseOwnershipGuard] Restored overwritten " +
                        "virtual pose for " + device.deviceCode +
                        " positionDrift=" + positionDrifted +
                        " rotationDrift=" + rotationDrifted
                    );
                }

                model.SetPositionAndRotation(device.position, device.rotation);
            }
        }
    }
}
