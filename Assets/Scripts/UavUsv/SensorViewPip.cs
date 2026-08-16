using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Picture-in-picture sensor window. Prefer Gazebo/ROS uplink frames from the
    /// WebSocket bridge; fall back to a local virtual camera when needed
    /// (e.g. tactical top-down, or while waiting for the selected uplink).
    /// </summary>
    public sealed class SensorViewPip : MonoBehaviour
    {
        public enum SensorView
        {
            Usv01Forward = 0,
            Usv02Forward = 1,
            Usv03Forward = 2,
            Uav01Down = 3,
            Uav02Down = 4,
            Uav03Down = 5,
            TacticalTop = 6
        }

        public ExternalPoseWebSocketClient poseClient;
        public Transform[] usvs = new Transform[0];
        public Transform[] uavs = new Transform[0];
        public Transform lookAt;
        public bool visible = true;
        public SensorView activeView = SensorView.Usv01Forward;
        public Rect normalizedRect = new Rect(.56f, .02f, .42f, .48f);
        public int textureWidth = 720;
        public int textureHeight = 405;
        // Latest Gazebo sensor TFs: USV camera_link=(6.48, 0, 3.10),
        // UAV camera_link=(0, 0, 0.90), relative to base_link.
        public float usvCameraHeight = 3.10f;
        public float usvCameraForward = 6.48f;
        public float uavCameraHeight = .90f;
        public float tacticalHeight = 220f;
        public bool preferGazeboStream = true;
        public Vector2 minimumWindowSize = new Vector2(430f, 330f);

        private Camera pipCamera;
        private RenderTexture renderTexture;
        private Texture2D panelBackground;
        private Texture2D buttonNormal;
        private Texture2D buttonActive;
        private GUIStyle titleStyle;
        private GUIStyle buttonStyle;
        private GUIStyle activeButtonStyle;
        private GUIStyle closeStyle;
        private GUIStyle statusStyle;
        private GUIStyle badgeStyle;
        private float styleScale = -1f;
        private SensorView lastRequestedView = (SensorView)(-1);
        private Rect windowRect;
        private Rect restoredRect;
        private bool windowInitialized;
        private bool minimized;
        private bool maximized;
        private bool dragging;
        private bool resizing;
        private float expandedHeight;
        private Vector2 pointerStart;
        private Rect rectStart;
        private static readonly string[] ViewLabels =
        {
            "我方船1 前视",
            "我方船2 前视",
            "我方船3 前视",
            "我方机1 下视",
            "我方机2 下视",
            "我方机3 下视",
            "全局俯视"
        };

        private void Awake()
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return;

            EnsureResources();
        }

        private void OnDestroy()
        {
            if (pipCamera)
                Destroy(pipCamera.gameObject);
            if (renderTexture)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }
            if (panelBackground)
                Destroy(panelBackground);
            if (buttonNormal)
                Destroy(buttonNormal);
            if (buttonActive)
                Destroy(buttonActive);
        }

        private void LateUpdate()
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return;

            HandleHotkeys();
            if (!visible)
                return;

            EnsureResources();
            SyncGazeboCameraSelection();

            if (ShouldUseVirtualCamera())
                UpdatePipCamera();
        }

        private void OnGUI()
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return;

            float uiScale = RuntimeGuiScale();
            if (!visible)
            {
                EnsureStyles();
                if (GUI.Button(
                        new Rect(
                            Screen.width - 176f * uiScale,
                            14f * uiScale,
                            158f * uiScale,
                            30f * uiScale
                        ),
                        "V  打开传感器",
                        buttonStyle))
                    visible = true;
                return;
            }

            EnsureStyles();
            EnsureResources();
            EnsureWindowRect();
            HandleWindowInput();
            Rect panel = windowRect;
            GUI.DrawTexture(panel, panelBackground);

            float header = 34f * uiScale;
            float footer = minimized ? 0f : 148f * uiScale;
            Rect imageRect = new Rect(
                panel.x + 8f * uiScale,
                panel.y + header,
                panel.width - 16f * uiScale,
                Mathf.Max(
                    48f * uiScale,
                    panel.height - header - footer - 8f * uiScale
                )
            );

            if (!minimized)
            {
                Texture image = ResolveDisplayTexture(out string sourceLabel);
                if (image)
                    GUI.DrawTexture(imageRect, image, ScaleMode.ScaleToFit);
                else
                    GUI.Label(imageRect, "等待画面…", statusStyle);

                GUI.Label(
                    new Rect(
                        panel.x + 12f * uiScale,
                        imageRect.yMax - 22f * uiScale,
                        panel.width - 24f * uiScale,
                        20f * uiScale
                    ),
                    sourceLabel,
                    statusStyle
                );

                DrawTelemetryBadges(imageRect);
                DrawSelector(panel, footer);
                GUI.Box(
                    new Rect(
                        panel.xMax - 16f * uiScale,
                        panel.yMax - 16f * uiScale,
                        12f * uiScale,
                        12f * uiScale
                    ),
                    "↘",
                    statusStyle
                );
            }

            GUI.Label(
                new Rect(
                    panel.x + 12f * uiScale,
                    panel.y + 6f * uiScale,
                    panel.width - 172f * uiScale,
                    26f * uiScale
                ),
                "传感器预览  ·  " + ViewLabels[(int)activeView],
                titleStyle
            );

            if (GUI.Button(
                    new Rect(
                        panel.xMax - 112f * uiScale,
                        panel.y + 5f * uiScale,
                        30f * uiScale,
                        26f * uiScale
                    ),
                    minimized ? "▣" : "—",
                    buttonStyle))
                ToggleMinimize();

            if (GUI.Button(
                    new Rect(
                        panel.xMax - 78f * uiScale,
                        panel.y + 5f * uiScale,
                        30f * uiScale,
                        26f * uiScale
                    ),
                    maximized ? "❐" : "□",
                    buttonStyle))
                ToggleMaximize();

            if (GUI.Button(
                    new Rect(
                        panel.xMax - 44f * uiScale,
                        panel.y + 5f * uiScale,
                        32f * uiScale,
                        26f * uiScale
                    ),
                    "×",
                    closeStyle))
                visible = false;
        }

        private void DrawSelector(Rect panel, float footer)
        {
            float uiScale = RuntimeGuiScale();
            float buttonWidth = (panel.width - 28f * uiScale) * .5f;
            float buttonHeight = 30f * uiScale;
            float rowY = panel.yMax - footer + 6f * uiScale;
            for (int i = 0; i < ViewLabels.Length; i++)
            {
                int row = i / 2;
                int column = i % 2;
                Rect buttonRect = new Rect(
                    panel.x + 10f * uiScale +
                    (buttonWidth + 8f * uiScale) * column,
                    rowY + row * (buttonHeight + 4f * uiScale),
                    buttonWidth,
                    buttonHeight
                );
                DrawViewButton(buttonRect, (SensorView)i);
            }
        }

        private void DrawTelemetryBadges(Rect imageRect)
        {
            float uiScale = RuntimeGuiScale();
            bool fresh = poseClient != null &&
                         poseClient.HasFreshCamera(CameraIdForView(activeView));
            string source = activeView == SensorView.TacticalTop || !preferGazeboStream
                ? "UNITY"
                : fresh ? "GAZEBO LIVE" : "GAZEBO WAIT";
            Color oldColor = GUI.color;
            GUI.color = fresh || source == "UNITY"
                ? new Color(.68f, 1f, .72f)
                : new Color(1f, .78f, .35f);
            GUI.Label(
                new Rect(
                    imageRect.x + 8f * uiScale,
                    imageRect.y + 7f * uiScale,
                    112f * uiScale,
                    22f * uiScale
                ),
                "● " + source,
                badgeStyle
            );
            GUI.color = oldColor;

            if (poseClient == null || !fresh)
                return;

            string telemetry =
                poseClient.cameraFps.ToString("0.0") + " FPS  ·  " +
                Mathf.Max(0f, poseClient.latestCameraAgeSeconds * 1000f).ToString("0") +
                " ms";
            GUI.Label(
                new Rect(
                    imageRect.xMax - 176f * uiScale,
                    imageRect.y + 7f * uiScale,
                    168f * uiScale,
                    22f * uiScale
                ),
                telemetry,
                badgeStyle
            );
        }

        private Texture ResolveDisplayTexture(out string sourceLabel)
        {
            string cameraId = CameraIdForView(activeView);
            if (preferGazeboStream &&
                !string.IsNullOrEmpty(cameraId) &&
                poseClient != null &&
                poseClient.HasFreshCamera(cameraId))
            {
                sourceLabel = poseClient.cameraStatus;
                return poseClient.latestCameraTexture;
            }

            if (ShouldUseVirtualCamera() && renderTexture)
            {
                sourceLabel = string.IsNullOrEmpty(cameraId)
                    ? "Unity 虚拟俯视"
                    : "Unity 临时视角 · 等待 Gazebo " + cameraId;
                return renderTexture;
            }

            sourceLabel = poseClient != null
                ? poseClient.cameraStatus
                : "未连接 ROS 桥";
            return null;
        }

        private bool ShouldUseVirtualCamera()
        {
            if (activeView == SensorView.TacticalTop)
                return true;

            if (!preferGazeboStream)
                return true;

            string cameraId = CameraIdForView(activeView);
            if (string.IsNullOrEmpty(cameraId) || poseClient == null)
                return true;

            return !poseClient.HasFreshCamera(cameraId);
        }

        private void SyncGazeboCameraSelection()
        {
            if (poseClient == null || !preferGazeboStream)
                return;

            if (!poseClient.isConnected)
            {
                lastRequestedView = (SensorView)(-1);
                return;
            }

            string cameraId = CameraIdForView(activeView);
            if (string.IsNullOrEmpty(cameraId))
                return;

            if (activeView == lastRequestedView &&
                poseClient.selectedCameraId == cameraId &&
                poseClient.HasFreshCamera(cameraId, 5f))
                return;

            // Re-request while waiting so reconnect / observe-mode bridges stay in sync.
            if (activeView == lastRequestedView &&
                poseClient.selectedCameraId == cameraId &&
                Time.frameCount % 90 != 0)
                return;

            lastRequestedView = activeView;
            poseClient.SelectCamera(cameraId);
        }

        public static string CameraIdForView(SensorView view)
        {
            switch (view)
            {
                case SensorView.Usv01Forward:
                    return "usv_01";
                case SensorView.Usv02Forward:
                    return "usv_02";
                case SensorView.Usv03Forward:
                    return "usv_03";
                case SensorView.Uav01Down:
                    return "uav_01";
                case SensorView.Uav02Down:
                    return "uav_02";
                case SensorView.Uav03Down:
                    return "uav_03";
                default:
                    return null;
            }
        }

        private void DrawViewButton(Rect rect, SensorView view)
        {
            GUIStyle style = activeView == view ? activeButtonStyle : buttonStyle;
            if (GUI.Button(rect, ViewLabels[(int)view], style))
                activeView = view;
        }

        private void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.V))
                visible = !visible;
            if (!visible)
                return;

            if (Input.GetKeyDown(KeyCode.LeftBracket) || Input.GetKeyDown(KeyCode.Comma))
                Cycle(-1);
            if (Input.GetKeyDown(KeyCode.RightBracket) || Input.GetKeyDown(KeyCode.Period))
                Cycle(1);

            // Shift+1..7 avoids clashing with ChaseCamera's 1..4 view modes.
            bool shifted = Input.GetKey(KeyCode.LeftShift) ||
                           Input.GetKey(KeyCode.RightShift);
            if (shifted && Input.GetKeyDown(KeyCode.Alpha1))
                activeView = SensorView.Usv01Forward;
            else if (shifted && Input.GetKeyDown(KeyCode.Alpha2))
                activeView = SensorView.Usv02Forward;
            else if (shifted && Input.GetKeyDown(KeyCode.Alpha3))
                activeView = SensorView.Usv03Forward;
            else if (shifted && Input.GetKeyDown(KeyCode.Alpha4))
                activeView = SensorView.Uav01Down;
            else if (shifted && Input.GetKeyDown(KeyCode.Alpha5))
                activeView = SensorView.Uav02Down;
            else if (shifted && Input.GetKeyDown(KeyCode.Alpha6))
                activeView = SensorView.Uav03Down;
            else if (shifted && Input.GetKeyDown(KeyCode.Alpha7))
                activeView = SensorView.TacticalTop;
        }

        private void Cycle(int step)
        {
            int count = ViewLabels.Length;
            int next = ((int)activeView + step) % count;
            if (next < 0)
                next += count;
            activeView = (SensorView)next;
        }

        private void EnsureResources()
        {
            if (!renderTexture ||
                renderTexture.width != textureWidth ||
                renderTexture.height != textureHeight)
            {
                if (renderTexture)
                {
                    renderTexture.Release();
                    Destroy(renderTexture);
                }

                renderTexture = new RenderTexture(
                    textureWidth,
                    textureHeight,
                    16,
                    RenderTextureFormat.ARGB32
                )
                {
                    name = "Sensor View PiP",
                    antiAliasing = 2
                };
            }

            if (!pipCamera)
            {
                GameObject go = new GameObject("Sensor PiP Camera");
                go.transform.SetParent(transform, false);
                pipCamera = go.AddComponent<Camera>();
                pipCamera.enabled = false;
                pipCamera.fieldOfView = 55f;
                pipCamera.nearClipPlane = .15f;
                pipCamera.farClipPlane = 5500f;
                pipCamera.clearFlags = CameraClearFlags.Skybox;
                pipCamera.allowHDR = true;
                pipCamera.allowMSAA = true;
            }

            pipCamera.targetTexture = renderTexture;
        }

        private void UpdatePipCamera()
        {
            if (!pipCamera)
                return;

            if (!TryGetViewPose(out Vector3 position, out Quaternion rotation, out float fov))
            {
                pipCamera.enabled = false;
                return;
            }

            pipCamera.transform.SetPositionAndRotation(position, rotation);
            pipCamera.fieldOfView = fov;
            pipCamera.Render();
        }

        private bool TryGetViewPose(
            out Vector3 position,
            out Quaternion rotation,
            out float fov)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            fov = 55f;

            switch (activeView)
            {
                case SensorView.Usv01Forward:
                    return TryUsvForward(0, out position, out rotation, out fov);
                case SensorView.Usv02Forward:
                    return TryUsvForward(1, out position, out rotation, out fov);
                case SensorView.Usv03Forward:
                    return TryUsvForward(2, out position, out rotation, out fov);
                case SensorView.Uav01Down:
                    return TryUavDown(0, out position, out rotation, out fov);
                case SensorView.Uav02Down:
                    return TryUavDown(1, out position, out rotation, out fov);
                case SensorView.Uav03Down:
                    return TryUavDown(2, out position, out rotation, out fov);
                case SensorView.TacticalTop:
                    return TryTacticalTop(out position, out rotation, out fov);
                default:
                    return false;
            }
        }

        private bool TryUsvForward(
            int index,
            out Vector3 position,
            out Quaternion rotation,
            out float fov)
        {
            fov = 58f;
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (usvs == null || index < 0 || index >= usvs.Length || !usvs[index])
                return false;

            Transform usv = usvs[index];
            Vector3 forward = usv.right;
            Vector3 up = Vector3.up;
            position = usv.position + up * usvCameraHeight + forward * usvCameraForward;
            Vector3 lookPoint = position + forward * 28f + up * .2f;
            if (lookAt)
                lookPoint = Vector3.Lerp(lookPoint, lookAt.position + Vector3.up * 1.5f, .18f);
            rotation = Quaternion.LookRotation((lookPoint - position).normalized, up);
            return true;
        }

        private bool TryUavDown(
            int index,
            out Vector3 position,
            out Quaternion rotation,
            out float fov)
        {
            fov = 70f;
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (uavs == null || index < 0 || index >= uavs.Length || !uavs[index])
                return false;

            Transform uav = uavs[index];
            position = uav.position + Vector3.up * uavCameraHeight;
            Vector3 forward = Vector3.ProjectOnPlane(uav.forward, Vector3.up);
            if (forward.sqrMagnitude < .001f)
                forward = Vector3.ProjectOnPlane(uav.right, Vector3.up);
            if (forward.sqrMagnitude < .001f)
                forward = Vector3.forward;
            rotation = Quaternion.LookRotation(Vector3.down, forward.normalized);
            return true;
        }

        private bool TryTacticalTop(
            out Vector3 position,
            out Quaternion rotation,
            out float fov)
        {
            fov = 48f;
            Vector3 center = Vector3.zero;
            int count = 0;
            Accumulate(usvs, ref center, ref count);
            Accumulate(uavs, ref center, ref count);
            if (lookAt)
            {
                center += lookAt.position;
                count++;
            }

            if (count == 0)
            {
                position = new Vector3(-70f, tacticalHeight, -280f);
                rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                return true;
            }

            center /= count;
            position = center + Vector3.up * tacticalHeight;
            rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            return true;
        }

        private static void Accumulate(
            Transform[] subjects,
            ref Vector3 center,
            ref int count)
        {
            if (subjects == null)
                return;
            for (int i = 0; i < subjects.Length; i++)
            {
                if (!subjects[i])
                    continue;
                center += subjects[i].position;
                count++;
            }
        }

        private Rect GuiRect()
        {
            return new Rect(
                Screen.width * normalizedRect.x,
                Screen.height * normalizedRect.y,
                Screen.width * normalizedRect.width,
                Screen.height * normalizedRect.height
            );
        }

        private void EnsureWindowRect()
        {
            if (windowInitialized)
                return;
            windowRect = GuiRect();
            restoredRect = windowRect;
            windowInitialized = true;
        }

        private void HandleWindowInput()
        {
            float uiScale = RuntimeGuiScale();
            Event current = Event.current;
            Vector2 mouse = current.mousePosition;
            Rect header = new Rect(
                windowRect.x,
                windowRect.y,
                windowRect.width - 124f * uiScale,
                34f * uiScale
            );
            Rect resize = new Rect(
                windowRect.xMax - 24f * uiScale,
                windowRect.yMax - 24f * uiScale,
                24f * uiScale,
                24f * uiScale
            );

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                if (!maximized && !minimized && resize.Contains(mouse))
                {
                    resizing = true;
                    pointerStart = mouse;
                    rectStart = windowRect;
                    current.Use();
                }
                else if (!maximized && header.Contains(mouse))
                {
                    dragging = true;
                    pointerStart = mouse;
                    rectStart = windowRect;
                    current.Use();
                }
            }
            else if (current.type == EventType.MouseDrag && current.button == 0)
            {
                Vector2 delta = mouse - pointerStart;
                if (dragging)
                {
                    windowRect.position = rectStart.position + delta;
                    ClampWindow();
                    current.Use();
                }
                else if (resizing)
                {
                    windowRect.width = Mathf.Max(
                        minimumWindowSize.x * uiScale,
                        rectStart.width + delta.x
                    );
                    windowRect.height = Mathf.Max(
                        minimumWindowSize.y * uiScale,
                        rectStart.height + delta.y
                    );
                    ClampWindow();
                    current.Use();
                }
            }
            else if (current.rawType == EventType.MouseUp)
            {
                dragging = false;
                resizing = false;
            }
        }

        private void ClampWindow()
        {
            float uiScale = RuntimeGuiScale();
            windowRect.width = Mathf.Min(windowRect.width, Screen.width);
            windowRect.height = Mathf.Min(windowRect.height, Screen.height);
            windowRect.x = Mathf.Clamp(windowRect.x, 0f, Screen.width - windowRect.width);
            windowRect.y = Mathf.Clamp(
                windowRect.y,
                0f,
                Screen.height - 34f * uiScale
            );
        }

        private void ToggleMaximize()
        {
            if (maximized)
            {
                windowRect = restoredRect;
                maximized = false;
            }
            else
            {
                float uiScale = RuntimeGuiScale();
                if (minimized)
                {
                    windowRect.height = Mathf.Max(
                        minimumWindowSize.y * uiScale,
                        expandedHeight
                    );
                    minimized = false;
                }
                restoredRect = windowRect;
                windowRect = new Rect(
                    12f * uiScale,
                    12f * uiScale,
                    Screen.width - 24f * uiScale,
                    Screen.height - 24f * uiScale
                );
                maximized = true;
            }
        }

        private void ToggleMinimize()
        {
            if (minimized)
            {
                windowRect.height = Mathf.Max(
                    minimumWindowSize.y * RuntimeGuiScale(),
                    expandedHeight
                );
                minimized = false;
                ClampWindow();
            }
            else
            {
                expandedHeight = windowRect.height;
                windowRect.height = 36f * RuntimeGuiScale();
                minimized = true;
                maximized = false;
            }
        }

        private void EnsureStyles()
        {
            float uiScale = RuntimeGuiScale();
            if (titleStyle != null && Mathf.Approximately(styleScale, uiScale))
                return;

            if (!panelBackground)
            {
                panelBackground = MakeColorTex(new Color(0.05f, 0.07f, 0.1f, 0.88f));
                buttonNormal = MakeColorTex(new Color(0.16f, 0.2f, 0.26f, 0.95f));
                buttonActive = MakeColorTex(new Color(0.42f, 0.5f, 0.18f, 0.98f));
            }

            styleScale = uiScale;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(16f * uiScale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(12f * uiScale),
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.75f, 0.9f, 0.78f, 0.95f) }
            };
            badgeStyle = new GUIStyle(statusStyle)
            {
                fontSize = Mathf.RoundToInt(11f * uiScale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal =
                {
                    background = panelBackground,
                    textColor = Color.white
                }
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(13f * uiScale),
                alignment = TextAnchor.MiddleCenter,
                normal =
                {
                    background = buttonNormal,
                    textColor = new Color(0.92f, 0.95f, 1f)
                },
                hover =
                {
                    background = buttonNormal,
                    textColor = Color.white
                },
                active =
                {
                    background = buttonActive,
                    textColor = Color.white
                }
            };
            activeButtonStyle = new GUIStyle(buttonStyle)
            {
                fontStyle = FontStyle.Bold,
                normal =
                {
                    background = buttonActive,
                    textColor = Color.white
                }
            };
            closeStyle = new GUIStyle(buttonStyle)
            {
                fontSize = Mathf.RoundToInt(12f * uiScale),
                normal =
                {
                    background = buttonNormal,
                    textColor = new Color(1f, 0.85f, 0.75f)
                }
            };
        }

        private static float RuntimeGuiScale()
        {
            return 1f;
        }

        private static Texture2D MakeColorTex(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
