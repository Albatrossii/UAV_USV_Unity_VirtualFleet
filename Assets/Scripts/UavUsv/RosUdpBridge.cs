using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace UavUsv
{
    // Lightweight adapter endpoint. A ROS 2 node can exchange JSON without a Unity package dependency.
    public sealed class RosUdpBridge : MonoBehaviour
    {
        [Serializable] private sealed class Command { public string command; }
        [Serializable] private sealed class Telemetry
        {
            public string phase;
            public float[] boat_enu;
            public float[] drone_enu;
            public long timestamp_ms;
        }

        public CooperativeMission mission;
        public int listenPort = 14580;
        public int telemetryPort = 14581;
        public bool enabledBridge;
        public string lastPacket = "disabled";
        private UdpClient receiver;
        private UdpClient sender;
        private Thread thread;
        private volatile string pendingCommand;

        private void Start()
        {
            if (!enabledBridge) return;
            try
            {
                receiver = new UdpClient(listenPort);
                receiver.Client.ReceiveTimeout = 500;
                sender = new UdpClient();
                thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "UAV-USV UDP bridge" };
                thread.Start();
                lastPacket = "listening :" + listenPort;
            }
            catch (Exception ex) { lastPacket = ex.Message; enabledBridge = false; }
        }

        private void ReceiveLoop()
        {
            var source = new IPEndPoint(IPAddress.Any, 0);
            while (receiver != null)
            {
                try
                {
                    string json = Encoding.UTF8.GetString(receiver.Receive(ref source));
                    var command = JsonUtility.FromJson<Command>(json);
                    pendingCommand = command.command;
                    lastPacket = json;
                }
                catch (SocketException) { }
                catch (Exception ex) { lastPacket = ex.Message; }
            }
        }

        private void Update()
        {
            if (!enabledBridge || !mission) return;
            string command = pendingCommand;
            pendingCommand = null;
            if (command == "start") mission.StartMission();
            else if (command == "reset") mission.ResetMission();

            if (Time.frameCount % 6 == 0)
            {
                Vector3 b = Coordinates.PresentationToEnu(mission.boat.position);
                Vector3 d = Coordinates.PresentationToEnu(mission.drone.position);
                var telemetry = new Telemetry
                {
                    phase = mission.Status,
                    boat_enu = new[] { b.x, b.y, b.z },
                    drone_enu = new[] { d.x, d.y, d.z },
                    timestamp_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(telemetry));
                try { sender.Send(bytes, bytes.Length, "127.0.0.1", telemetryPort); } catch (Exception) { }
            }
        }

        private void OnDestroy()
        {
            receiver?.Close(); receiver = null;
            sender?.Close(); sender = null;
            if (thread != null && thread.IsAlive) thread.Join(800);
        }
    }
}
