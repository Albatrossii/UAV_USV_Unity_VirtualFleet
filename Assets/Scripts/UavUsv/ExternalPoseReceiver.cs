using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Receives authoritative Gazebo ENU poses from a ROS-side UDP adapter.
    /// Unity is a visualizer in this mode: it does not generate its own motion.
    /// </summary>
    public sealed class ExternalPoseReceiver : MonoBehaviour
    {
        [Serializable]
        private sealed class PoseData
        {
            public float[] position;
            public float[] orientation;
        }

        [Serializable]
        private sealed class PoseFrame
        {
            public long timestamp_ms;
            public uint sequence;
            public PoseData boat;
            public PoseData drone;
        }

        public Transform boat;
        public Transform drone;
        [Tooltip("Unity visual offset from the Gazebo x500 model origin.")]
        public float droneHeightOffset = .28f;
        public int listenPort = 14582;
        public float smoothing = 14f;

        public string connectionStatus { get; private set; } = "Waiting for ROS/Gazebo pose";

        private UdpClient receiver;
        private Thread thread;
        private volatile string pendingJson;
        private Vector3 boatTarget;
        private Vector3 droneTarget;
        private Quaternion boatRotation = Quaternion.identity;
        private Quaternion droneRotation = Quaternion.identity;
        private bool hasTargets;
        private bool droneDetached;
        private float lastPacketTime = -100f;
        private uint lastSequence;

        private void Start()
        {
            try
            {
                receiver = new UdpClient(listenPort);
                receiver.Client.ReceiveTimeout = 500;
                thread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "ROS pose receiver"
                };
                thread.Start();
                connectionStatus = "Listening UDP :" + listenPort;
            }
            catch (Exception ex)
            {
                connectionStatus = "Receiver failed: " + ex.Message;
            }
        }

        private void ReceiveLoop()
        {
            var source = new IPEndPoint(IPAddress.Any, 0);

            while (receiver != null)
            {
                try
                {
                    pendingJson = Encoding.UTF8.GetString(receiver.Receive(ref source));
                }
                catch (SocketException)
                {
                    // Timeout is expected while waiting for ROS/Gazebo.
                }
                catch (Exception ex)
                {
                    connectionStatus = ex.Message;
                }
            }
        }

        private void Update()
        {
            string json = pendingJson;
            pendingJson = null;

            if (!string.IsNullOrEmpty(json))
                ParseFrame(json);

            if (hasTargets)
            {
                float alpha = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
                boat.position = Vector3.Lerp(boat.position, boatTarget, alpha);
                boat.rotation = Quaternion.Slerp(boat.rotation, boatRotation, alpha);
                drone.position = Vector3.Lerp(drone.position, droneTarget, alpha);
                drone.rotation = Quaternion.Slerp(drone.rotation, droneRotation, alpha);
            }

            if (Time.realtimeSinceStartup - lastPacketTime > 1f)
                connectionStatus = "Waiting for ROS/Gazebo pose on UDP :" + listenPort;
        }

        private void ParseFrame(string json)
        {
            try
            {
                PoseFrame frame = JsonUtility.FromJson<PoseFrame>(json);
                if (frame == null || !Valid(frame.boat) || !Valid(frame.drone))
                {
                    connectionStatus = "Invalid pose packet";
                    return;
                }

                if (hasTargets && frame.sequence != 0 && frame.sequence <= lastSequence)
                    return;

                lastSequence = frame.sequence;
                boatTarget = EnuPosition(frame.boat.position);
                droneTarget = EnuPosition(frame.drone.position) + Vector3.up * droneHeightOffset;
                boatRotation = EnuRotation(frame.boat.orientation);
                droneRotation = EnuRotation(frame.drone.orientation);

                if (!droneDetached)
                {
                    drone.SetParent(null, true);
                    droneDetached = true;
                }

                hasTargets = true;
                lastPacketTime = Time.realtimeSinceStartup;
                connectionStatus = "Connected, seq " + frame.sequence;
            }
            catch (Exception ex)
            {
                connectionStatus = "Parse failed: " + ex.Message;
            }
        }

        private static bool Valid(PoseData pose)
        {
            return pose != null &&
                   pose.position != null &&
                   pose.position.Length >= 3 &&
                   pose.orientation != null &&
                   pose.orientation.Length >= 4;
        }

        private static Vector3 EnuPosition(float[] p)
        {
            return Coordinates.ToPresentation(p[0], p[1], p[2]);
        }

        private static Quaternion EnuRotation(float[] q)
        {
            // Basis change ENU(x-east,y-north,z-up) -> Unity(x-east,y-up,z-north).
            var result = new Quaternion(-q[0], -q[2], -q[1], q[3]);
            return result.normalized;
        }

        private void OnDestroy()
        {
            receiver?.Close();
            receiver = null;

            if (thread != null && thread.IsAlive)
                thread.Join(800);
        }
    }
}
