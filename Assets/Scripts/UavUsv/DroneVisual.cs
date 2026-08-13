using UnityEngine;

namespace UavUsv
{
    public sealed class DroneVisual : MonoBehaviour
    {
        public Transform[] rotors;
        public bool spinning;
        private void Update()
        {
            if (!spinning || rotors == null) return;
            foreach (var rotor in rotors) rotor.Rotate(Vector3.up, 1450f * Time.deltaTime, Space.Self);
        }
    }
}
