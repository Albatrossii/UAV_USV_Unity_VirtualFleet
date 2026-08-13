using UnityEngine;

namespace UavUsv
{
    public sealed class OrbitCamera : MonoBehaviour
    {
        public Transform target;
        public float distance = 18f;
        private float yaw = -35f;
        private float pitch = 7.5f;

        private void LateUpdate()
        {
            if (!target) return;
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * 4f;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3f, 8f, 75f);
            }
            distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * 2f, 6f, 80f);
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
            transform.position = target.position - rotation * Vector3.forward * distance;
            transform.rotation = rotation;
        }
    }
}
