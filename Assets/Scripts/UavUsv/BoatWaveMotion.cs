using UnityEngine;

namespace UavUsv
{
    public sealed class BoatWaveMotion : MonoBehaviour
    {
        public float meanHeight = .01f;
        public float heaveAmplitude = .035f;
        public float rollAmplitude = .025f;
        public float pitchAmplitude = .02f;
        public float windDirectionDegrees = 28f;

        private void LateUpdate()
        {
            float t = Time.time;
            Vector3 enu = Coordinates.PresentationToEnu(transform.position);
            float wind = windDirectionDegrees * Mathf.Deg2Rad;
            float alongWind = Mathf.Cos(wind) * enu.x + Mathf.Sin(wind) * enu.y;
            float crossWind = -Mathf.Sin(wind) * enu.x + Mathf.Cos(wind) * enu.y;
            float a = 1.1f * t + .42f * alongWind + .05f * crossWind;
            float b = 1.7f * t + .18f * alongWind - .31f * crossWind + 1.2f;
            float heave = heaveAmplitude * (.7f * Mathf.Sin(a) + .3f * Mathf.Sin(b));
            float roll = rollAmplitude * (.65f * Mathf.Sin(b) + .35f * Mathf.Sin(a + .8f));
            float pitch = pitchAmplitude * (.7f * Mathf.Cos(a) + .3f * Mathf.Sin(b - .4f));
            var p = transform.position; p.y = meanHeight + heave; transform.position = p;
            float yaw = transform.eulerAngles.y;
            transform.rotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, yaw, -roll * Mathf.Rad2Deg);
        }
    }
}
