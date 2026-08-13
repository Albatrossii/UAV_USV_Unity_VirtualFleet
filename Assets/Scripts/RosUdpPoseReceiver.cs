using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class RosUdpPoseReceiver : MonoBehaviour
{
    [Header("UDP Settings")]
    public int listenPort = 14582;

    [Header("Unity Targets")]
    public Transform boatTarget;
    public Transform droneTarget;

    [Header("Auto Find Names")]
    public string[] boatNames = { "LandingBoat", "landing_boat", "Boat", "boat", "simple_boat" };
    public string[] droneNames = { "X500Drone", "x500_0", "Drone", "drone", "mini-drone" };

    [Header("Coordinate Convert")]
    public bool convertGazeboToUnity = true;
    public float positionScale = 1.0f;

    [Header("Rotation")]
    public bool useYawRotation = false;

    private UdpClient udpClient;
    private Thread receiveThread;
    private bool running = false;

    private readonly object dataLock = new object();

    private bool hasBoatPose = false;
    private bool hasDronePose = false;

    private Vector3 latestBoatPosition;
    private Vector3 latestDronePosition;

    private float latestBoatYawDeg;
    private float latestDroneYawDeg;

    private int latestSeq = 0;
    private float nextFindTime = 0f;

    [Serializable]
    public class PoseData
    {
        public string name;
        public float[] position;
        public float[] orientation;
    }

    [Serializable]
    public class PacketData
    {
        public int seq;
        public PoseData boat;
        public PoseData drone;
    }

    void Start()
    {
        StartReceiver();
    }

    void Update()
    {
        AutoFindTargets();

        lock (dataLock)
        {
            if (hasBoatPose && boatTarget != null)
            {
                boatTarget.position = latestBoatPosition;

                if (useYawRotation)
                {
                    boatTarget.rotation = Quaternion.Euler(0f, latestBoatYawDeg, 0f);
                }
            }

            if (hasDronePose && droneTarget != null)
            {
                droneTarget.position = latestDronePosition;

                if (useYawRotation)
                {
                    droneTarget.rotation = Quaternion.Euler(0f, latestDroneYawDeg, 0f);
                }
            }
        }
    }

    void OnGUI()
    {
        string boatName = boatTarget == null ? "None" : boatTarget.name;
        string droneName = droneTarget == null ? "None" : droneTarget.name;

        GUI.Label(
            new Rect(10, 10, 1200, 30),
            $"ROS UDP listening {listenPort} | seq={latestSeq} | boatTarget={boatName} | droneTarget={droneName} | boatPose={hasBoatPose} | dronePose={hasDronePose}"
        );
    }

    void OnDestroy()
    {
        StopReceiver();
    }

    private void AutoFindTargets()
    {
        if (Time.time < nextFindTime) return;
        nextFindTime = Time.time + 0.5f;

        if (boatTarget == null)
        {
            boatTarget = FindTransformByNames(boatNames);
            if (boatTarget != null)
            {
                Debug.Log("Auto found boat target: " + boatTarget.name);
            }
        }

        if (droneTarget == null)
        {
            droneTarget = FindTransformByNames(droneNames);
            if (droneTarget != null)
            {
                Debug.Log("Auto found drone target: " + droneTarget.name);
            }
        }
    }

    private Transform FindTransformByNames(string[] names)
    {
        Transform[] allTransforms = Resources.FindObjectsOfTypeAll<Transform>();

        foreach (string targetName in names)
        {
            foreach (Transform t in allTransforms)
            {
                if (t == null) continue;
                if (!t.gameObject.scene.IsValid()) continue;
                if (t.name == targetName)
                {
                    return t;
                }
            }
        }

        return null;
    }

    private void StartReceiver()
    {
        try
        {
            udpClient = new UdpClient(listenPort);
            running = true;

            receiveThread = new Thread(ReceiveLoop);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            Debug.Log("ROS UDP receiver started on port " + listenPort);
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to start UDP receiver: " + e.Message);
        }
    }

    private void StopReceiver()
    {
        running = false;

        try
        {
            udpClient?.Close();
            udpClient = null;
        }
        catch {}

        try
        {
            if (receiveThread != null && receiveThread.IsAlive)
            {
                receiveThread.Join(200);
            }
        }
        catch {}
    }

    private void ReceiveLoop()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (running)
        {
            try
            {
                byte[] data = udpClient.Receive(ref remoteEndPoint);
                string json = Encoding.UTF8.GetString(data);

                PacketData packet = JsonUtility.FromJson<PacketData>(json);
                if (packet == null) continue;

                lock (dataLock)
                {
                    latestSeq = packet.seq;

                    if (packet.boat != null && packet.boat.position != null && packet.boat.position.Length >= 3)
                    {
                        latestBoatPosition = ConvertPosition(packet.boat.position);
                        latestBoatYawDeg = ExtractYawDeg(packet.boat.orientation);
                        hasBoatPose = true;
                    }

                    if (packet.drone != null && packet.drone.position != null && packet.drone.position.Length >= 3)
                    {
                        latestDronePosition = ConvertPosition(packet.drone.position);
                        latestDroneYawDeg = ExtractYawDeg(packet.drone.orientation);
                        hasDronePose = true;
                    }
                }
            }
            catch
            {
                // Unity 退出或者 UDP 关闭时会进入这里，忽略即可
            }
        }
    }

    private Vector3 ConvertPosition(float[] p)
    {
        float gx = p[0];
        float gy = p[1];
        float gz = p[2];

        if (convertGazeboToUnity)
        {
            // Gazebo: X/Y 是水平面，Z 是高度
            // Unity: X/Z 是水平面，Y 是高度
            return new Vector3(gx, gz, gy) * positionScale;
        }

        return new Vector3(gx, gy, gz) * positionScale;
    }

    private float ExtractYawDeg(float[] q)
    {
        if (q == null || q.Length < 4) return 0f;

        float x = q[0];
        float y = q[1];
        float z = q[2];
        float w = q[3];

        float sinyCosp = 2f * (w * z + x * y);
        float cosyCosp = 1f - 2f * (y * y + z * z);
        float yawRad = Mathf.Atan2(sinyCosp, cosyCosp);

        return -yawRad * Mathf.Rad2Deg;
    }
}