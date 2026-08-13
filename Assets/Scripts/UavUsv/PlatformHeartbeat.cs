using System;
using System.Collections;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace UavUsv
{
    public sealed class PlatformHeartbeat : MonoBehaviour
    {
        [Serializable]
        private sealed class HeartbeatPayload
        {
            public string componentCode;
            public string instanceId;
            public string state;
            public string detail;
            public string rosConnectionStatus;
        }

        public string endpoint = "http://127.0.0.1:8081/api/integration/heartbeat";
        public string token = "uav-usv-local-agent";
        public float intervalSeconds = 2f;

        private string instanceId;

        private void Awake()
        {
            instanceId = Environment.MachineName + ":unity:" + Process.GetCurrentProcess().Id;
        }

        private IEnumerator Start()
        {
            while (enabled)
            {
                yield return SendHeartbeat();
                yield return new WaitForSecondsRealtime(intervalSeconds);
            }
        }

        private IEnumerator SendHeartbeat()
        {
            ExternalPoseWebSocketClient rosClient = GetComponent<ExternalPoseWebSocketClient>();
            var payload = new HeartbeatPayload
            {
                componentCode = "unity-client-01",
                instanceId = instanceId,
                state = "RUNNING",
                detail = Application.isEditor ? "Unity Editor Play Mode" : "Unity Windows Player",
                rosConnectionStatus = rosClient ? rosClient.connectionStatus : "ROS pose client unavailable"
            };

            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            using var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 3
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("X-Platform-Token", token);
            yield return request.SendWebRequest();
        }
    }
}
