using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Locks the Unity camera to the Gazebo MinimalScene camera contract.
    /// It runs after the chase director so the comparison view cannot drift.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [RequireComponent(typeof(Camera))]
    public sealed class GazeboComparisonCamera : MonoBehaviour
    {
        public bool comparisonActive = true;
        public Vector3 positionEnu = new Vector3(-430f, -560f, 420f);
        public float rollRadians;
        public float pitchRadians = .78f;
        public float yawRadians = .72f;
        public float horizontalFovDegrees = 90f;

        private Camera attachedCamera;

        private void Awake()
        {
            attachedCamera = GetComponent<Camera>();
            ApplyGazeboView();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.G))
                comparisonActive = true;

            if (comparisonActive &&
                (Input.GetKeyDown(KeyCode.C) ||
                 Input.GetKeyDown(KeyCode.Alpha1) ||
                 Input.GetKeyDown(KeyCode.Alpha2) ||
                 Input.GetKeyDown(KeyCode.Alpha3) ||
                 Input.GetKeyDown(KeyCode.Alpha4) ||
                 Input.GetKeyDown(KeyCode.Tab)))
                comparisonActive = false;
        }

        private void LateUpdate()
        {
            if (comparisonActive)
                ApplyGazeboView();
        }

        private void ApplyGazeboView()
        {
            if (!attachedCamera)
                attachedCamera = GetComponent<Camera>();

            transform.position = Coordinates.ToPresentationEnvironment(
                positionEnu.x,
                positionEnu.y,
                positionEnu.z
            );

            float cosPitch = Mathf.Cos(pitchRadians);
            Vector3 forwardEnu = new Vector3(
                cosPitch * Mathf.Cos(yawRadians),
                cosPitch * Mathf.Sin(yawRadians),
                -Mathf.Sin(pitchRadians)
            );
            Vector3 forwardUnity = Coordinates.ToUnity(
                forwardEnu.x,
                forwardEnu.y,
                forwardEnu.z
            );
            transform.rotation = Quaternion.LookRotation(
                forwardUnity,
                Vector3.up
            );
            if (!Mathf.Approximately(rollRadians, 0f))
                transform.Rotate(
                    Vector3.forward,
                    -rollRadians * Mathf.Rad2Deg,
                    Space.Self
                );

            float aspect = Mathf.Max(.1f, attachedCamera.aspect);
            attachedCamera.fieldOfView = 2f * Mathf.Atan(
                Mathf.Tan(horizontalFovDegrees * .5f * Mathf.Deg2Rad) / aspect
            ) * Mathf.Rad2Deg;
        }
    }
}
