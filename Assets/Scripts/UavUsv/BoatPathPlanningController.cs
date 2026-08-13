using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    public sealed class BoatPathPlanningController : MonoBehaviour
    {
        public Transform boat;
        public Transform lighthouse;
        public Transform buoyWest;
        public Transform buoySouth;
        public Transform buoyEast;
        public Transform targetVessel;
        public Transform coastlineCollisionRoot;
        public ExternalPoseWebSocketClient webSocket;

        [Header("A* Grid")]
        public float worldMinX = -180f;
        public float worldMinY = -180f;
        public float worldMaxX = 180f;
        public float worldMaxY = 180f;
        public float cellSize = 2f;
        public float safetyMargin = 2.5f;

        public string status { get; private set; } = "Ready";
        public bool selectingGoal { get; private set; }

        private GridAStarPlanner planner;
        private readonly List<Vector2> plannedPath = new List<Vector2>();
        private LineRenderer pathRenderer;
        private Transform goalMarker;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private Rect panelRect;
        private float guiScale = 1f;
        private int expandedNodes;
        private long activePathId;
        private Collider[] coastlineColliders;

        private void Awake()
        {
            planner = new GridAStarPlanner(worldMinX, worldMinY, worldMaxX, worldMaxY, cellSize);
            BuildPathVisuals();
        }

        private void Update()
        {
            if (!selectingGoal || !Input.GetMouseButtonDown(0))
                return;

            Vector2 guiMouse = new Vector2(
                Input.mousePosition.x / guiScale,
                (Screen.height - Input.mousePosition.y) / guiScale
            );
            if (panelRect.Contains(guiMouse))
                return;

            SelectGoalAtScreenPosition(new Vector2(
                Input.mousePosition.x,
                Input.mousePosition.y
            ));
        }

        private void SelectGoalAtScreenPosition(Vector2 screenPosition)
        {
            Camera camera = Camera.main;
            if (!camera)
            {
                status = "Main camera unavailable";
                selectingGoal = false;
                return;
            }

            Ray ray = camera.ScreenPointToRay(screenPosition);
            var waterPlane = new Plane(Vector3.up, Vector3.zero);
            if (!waterPlane.Raycast(ray, out float distance))
            {
                status = "Selected point does not intersect the sea";
                selectingGoal = false;
                return;
            }

            Vector3 point = ray.GetPoint(distance);
            PlanTo(Coordinates.PresentationToEnu(point));
            selectingGoal = false;
        }

        private void PlanTo(Vector3 goalEnu)
        {
            if (!boat)
            {
                status = "Boat pose unavailable";
                return;
            }

            Vector3 startEnu = Coordinates.PresentationToEnu(boat.position);
            var start = new Vector2(startEnu.x, startEnu.y);
            var goal = new Vector2(goalEnu.x, goalEnu.y);
            List<GridAStarPlanner.CircularObstacle> obstacles = CollectObstacles();
            Physics.SyncTransforms();

            if (!planner.TryPlan(
                    start,
                    goal,
                    obstacles,
                    IsCoastBlocked,
                    out List<Vector2> path,
                    out expandedNodes))
            {
                plannedPath.Clear();
                RenderPath();
                status = "No collision-free path";
                return;
            }

            plannedPath.Clear();
            plannedPath.AddRange(path);
            RenderPath();
            goalMarker.position = Coordinates.ToPresentation(goal.x, goal.y, .35f);
            goalMarker.gameObject.SetActive(true);
            status = $"A* ready: {plannedPath.Count} waypoints";
        }

        public void SetCoastlineCollisionRoot(Transform root)
        {
            coastlineCollisionRoot = root;
            coastlineColliders = root
                ? root.GetComponentsInChildren<Collider>(true)
                : null;
        }

        private bool IsCoastBlocked(Vector2 enu)
        {
            if (coastlineColliders == null || coastlineColliders.Length == 0)
                return false;

            Ray ray = new Ray(
                Coordinates.ToPresentation(enu.x, enu.y, 80f),
                Vector3.down
            );
            for (int i = 0; i < coastlineColliders.Length; i++)
            {
                Collider coast = coastlineColliders[i];
                if (coast && coast.Raycast(ray, out _, 160f))
                    return true;
            }
            return false;
        }

        private List<GridAStarPlanner.CircularObstacle> CollectObstacles()
        {
            var obstacles = new List<GridAStarPlanner.CircularObstacle>();
            AddObstacle(obstacles, lighthouse, 5.5f + safetyMargin);
            AddObstacle(obstacles, buoyWest, 1.6f + safetyMargin);
            AddObstacle(obstacles, buoySouth, 1.6f + safetyMargin);
            AddObstacle(obstacles, buoyEast, 1.6f + safetyMargin);
            if (targetVessel && targetVessel.gameObject.activeInHierarchy)
                AddObstacle(obstacles, targetVessel, 4.5f + safetyMargin);
            return obstacles;
        }

        private static void AddObstacle(
            ICollection<GridAStarPlanner.CircularObstacle> obstacles,
            Transform target,
            float radius)
        {
            if (!target)
                return;

            Vector3 enu = Coordinates.PresentationToEnu(target.position);
            obstacles.Add(new GridAStarPlanner.CircularObstacle(new Vector2(enu.x, enu.y), radius));
        }

        private void ExecutePath()
        {
            if (plannedPath.Count < 2)
            {
                status = "Select a valid goal first";
                return;
            }

            if (!webSocket || !webSocket.isConnected)
            {
                status = "ROS WebSocket is not connected";
                return;
            }

            activePathId = webSocket.SendBoatPath(plannedPath);
            status = activePathId > 0 ? "Path sent to ROS/Gazebo" : "Failed to queue path";
        }

        private void StopBoat()
        {
            if (webSocket)
                webSocket.SendBoatStop();
            status = "Stop command sent";
        }

        private void ClearPath()
        {
            plannedPath.Clear();
            activePathId = 0;
            selectingGoal = false;
            RenderPath();
            if (goalMarker)
                goalMarker.gameObject.SetActive(false);
            status = "Ready";
        }

        private void BuildPathVisuals()
        {
            GameObject pathObject = new GameObject("Unity AStar Planned Path");
            pathRenderer = pathObject.AddComponent<LineRenderer>();
            pathRenderer.sharedMaterial = SceneFactory.Material(
                "AStar Path",
                new Color(.08f, .92f, 1f, .94f),
                0f,
                .45f
            );
            pathRenderer.widthMultiplier = .32f;
            pathRenderer.numCornerVertices = 3;
            pathRenderer.numCapVertices = 3;
            pathRenderer.useWorldSpace = true;
            pathRenderer.positionCount = 0;

            goalMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            goalMarker.name = "Unity AStar Goal";
            goalMarker.localScale = new Vector3(1.5f, .12f, 1.5f);
            goalMarker.GetComponent<Renderer>().sharedMaterial = SceneFactory.Material(
                "AStar Goal",
                new Color(1f, .22f, .05f, .92f),
                0f,
                .4f
            );
            Collider collider = goalMarker.GetComponent<Collider>();
            if (collider)
                Destroy(collider);
            goalMarker.gameObject.SetActive(false);
        }

        private void RenderPath()
        {
            if (!pathRenderer)
                return;

            pathRenderer.positionCount = plannedPath.Count;
            for (int i = 0; i < plannedPath.Count; i++)
                pathRenderer.SetPosition(i, Coordinates.ToPresentation(plannedPath[i].x, plannedPath[i].y, .55f));
        }

        private void OnGUI()
        {
            guiScale = Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            float logicalWidth = Screen.width / guiScale;
            panelRect = new Rect(Mathf.Max(16f, logicalWidth - 356f), 16f, 340f, 200f);

            Event currentEvent = Event.current;
            if (selectingGoal &&
                currentEvent.type == EventType.MouseDown &&
                currentEvent.button == 0)
            {
                Rect physicalPanel = new Rect(
                    panelRect.x * guiScale,
                    panelRect.y * guiScale,
                    panelRect.width * guiScale,
                    panelRect.height * guiScale
                );
                if (!physicalPanel.Contains(currentEvent.mousePosition))
                {
                    SelectGoalAtScreenPosition(new Vector2(
                        currentEvent.mousePosition.x,
                        Screen.height - currentEvent.mousePosition.y
                    ));
                    currentEvent.Use();
                }
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(guiScale, guiScale, 1f));
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(.9f, .96f, 1f) }
            };

            GUI.Box(panelRect, "");
            GUI.Label(new Rect(panelRect.x + 14f, panelRect.y + 10f, 312f, 28f), "Boat Path Planning", titleStyle);

            string bridgeState = webSocket ? webSocket.controlStatus : "WebSocket unavailable";
            GUI.Label(
                new Rect(panelRect.x + 14f, panelRect.y + 42f, 312f, 58f),
                status + "\nROS: " + bridgeState +
                (plannedPath.Count > 0 ? $"\nA*: {expandedNodes} expanded nodes" : ""),
                bodyStyle
            );

            float buttonY = panelRect.y + 112f;
            if (GUI.Button(new Rect(panelRect.x + 14f, buttonY, 98f, 32f),
                    selectingGoal ? "Selecting..." : "Select Goal"))
            {
                selectingGoal = true;
                status = "Select a point on the water";
            }

            GUI.enabled =
                plannedPath.Count >= 2 &&
                webSocket &&
                webSocket.isConnected &&
                webSocket.acceptsCommands;
            if (GUI.Button(new Rect(panelRect.x + 121f, buttonY, 98f, 32f), "Execute"))
                ExecutePath();
            GUI.enabled = true;

            if (GUI.Button(new Rect(panelRect.x + 228f, buttonY, 98f, 32f), "STOP"))
                StopBoat();

            if (GUI.Button(new Rect(panelRect.x + 14f, buttonY + 40f, 98f, 30f), "Clear"))
                ClearPath();

            GUI.Label(
                new Rect(panelRect.x + 121f, buttonY + 45f, 205f, 24f),
                activePathId > 0 ? "Path ID: " + activePathId : $"Grid: {cellSize:0.0} m",
                bodyStyle
            );

            GUI.matrix = previousMatrix;
        }

        private void OnDestroy()
        {
            if (pathRenderer && pathRenderer.sharedMaterial)
                Destroy(pathRenderer.sharedMaterial);
            if (pathRenderer)
                Destroy(pathRenderer.gameObject);
            if (goalMarker)
                Destroy(goalMarker.gameObject);
        }
    }
}
