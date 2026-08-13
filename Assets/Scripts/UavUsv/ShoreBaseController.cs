using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Shore base station: search → contact report → order → capture/defense.
    /// Holds between report and order so the demo audience can see each step.
    /// </summary>
    public sealed class ShoreBaseController : MonoBehaviour
    {
        public enum MissionPhase
        {
            Standby,
            Searching,
            TargetReported,
            Ordering,
            Capture,
            DefenseHold,
            Complete
        }

        public Transform shoreBase;
        public Transform[] boats;
        public Transform[] drones;
        public Transform targetPoint;
        public MultiAgentCaptureDefenseScenario scenario;

        public MissionPhase phase { get; private set; } = MissionPhase.Standby;
        public string status { get; private set; } = "岸基站在线 — 无人机待命";
        public bool automatic = true;
        [Tooltip("How long to show 'target reported' before the base issues the capture order.")]
        public float reportHoldSeconds = 3.2f;
        [Tooltip("How long to show the capture order before agents start closing.")]
        public float orderHoldSeconds = 2.4f;

        private float phaseStarted;
        private Vector3[] boatSlots = new Vector3[3];
        private Vector3[] droneSlots = new Vector3[3];
        private bool targetContact;
        private string lastReporter = "-";
        private bool orderSent;

        public bool HasTargetContact => targetContact;
        public bool CaptureOrdered =>
            phase == MissionPhase.Capture ||
            phase == MissionPhase.DefenseHold ||
            phase == MissionPhase.Complete;

        public bool ShouldLaunchDrones => CaptureOrdered;
        public bool ShouldCloseCapture => CaptureOrdered;

        public Vector3 GetBoatSlot(int index) =>
            boatSlots[Mathf.Clamp(index, 0, boatSlots.Length - 1)];

        public Vector3 GetDroneSlot(int index) =>
            droneSlots[Mathf.Clamp(index, 0, droneSlots.Length - 1)];

        public void BeginSearch()
        {
            phase = MissionPhase.Searching;
            phaseStarted = Time.time;
            targetContact = false;
            orderSent = false;
            lastReporter = "-";
            status = "① 搜索中 — 三艘 USV 三向接近探测，UAV 待命";
            RecomputeSlots();
        }

        public void NotifyTargetContact(string reporter)
        {
            if (targetContact && phase != MissionPhase.Searching && phase != MissionPhase.Standby)
                return;

            targetContact = true;
            orderSent = false;
            lastReporter = string.IsNullOrEmpty(reporter) ? "USV sensor" : reporter;
            phase = MissionPhase.TargetReported;
            phaseStarted = Time.time;
            status = "② 发现目标 — " + lastReporter + " 上报岸基站";
            RecomputeSlots();
            // Do NOT dispatch yet — hold so the report step is visible.
        }

        public void BeginMission()
        {
            // Manual force (B): jump to reported, then still show a short order beat.
            NotifyTargetContact("manual base order");
        }

        public void NotifyCaptureComplete()
        {
            phase = MissionPhase.DefenseHold;
            phaseStarted = Time.time;
            status = "⑤ 围捕成功 — 准备转入护航防卫";
        }

        public void NotifyDefenseStarted()
        {
            phase = MissionPhase.DefenseHold;
            phaseStarted = Time.time;
            status = "⑥ 护航防卫 — 敌船朝岸基站接近，阻断点展开";
        }

        public void NotifyDefenseComplete()
        {
            phase = MissionPhase.Complete;
            status = "⑥ 护航防卫成功 — 阻断+守卫弧+护航弧锁定";
        }

        public void ResetMission()
        {
            phase = MissionPhase.Standby;
            phaseStarted = Time.time;
            targetContact = false;
            orderSent = false;
            lastReporter = "-";
            status = "岸基站待命 — 无人机在停机坪";
            RecomputeSlots();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.B))
                BeginMission();

            RecomputeSlots();

            if (!automatic)
                return;

            switch (phase)
            {
                case MissionPhase.Standby:
                    if (Time.time - phaseStarted > .6f)
                        BeginSearch();
                    break;

                case MissionPhase.Searching:
                    status = "① 搜索中 — 分区巡逻，等待激光/雷达发现目标";
                    break;

                case MissionPhase.TargetReported:
                    status = "② 发现目标 — " + lastReporter + " 上报中（其余船继续搜索）";
                    if (Time.time - phaseStarted >= reportHoldSeconds)
                    {
                        phase = MissionPhase.Ordering;
                        phaseStarted = Time.time;
                        status = "③ 岸基站下令 — 执行围捕与起飞";
                    }
                    break;

                case MissionPhase.Ordering:
                    status = "③ 岸基站下令 — 执行围捕与起飞";
                    if (Time.time - phaseStarted >= orderHoldSeconds)
                    {
                        phase = MissionPhase.Capture;
                        phaseStarted = Time.time;
                        status = "④ 围捕执行 — USV 缩圈，UAV 起飞";
                        if (!orderSent && scenario)
                        {
                            orderSent = true;
                            scenario.NotifyBaseDispatch();
                        }
                    }
                    break;

                case MissionPhase.Capture:
                    if (scenario && scenario.CaptureReady)
                    {
                        phase = MissionPhase.DefenseHold;
                        phaseStarted = Time.time;
                        status = "④ 围捕到位 — 准备转入护航防卫";
                    }
                    else
                    {
                        status = "④ 围捕执行 — USV 缩圈，UAV 护航起飞";
                    }
                    break;

                case MissionPhase.DefenseHold:
                    if (scenario && scenario.DefenseComplete)
                    {
                        phase = MissionPhase.Complete;
                        status = "⑥ 护航防卫成功 — 阻断+护航弧锁定";
                    }
                    else if (scenario && scenario.DefenseEscortActive)
                    {
                        status = "⑥ 护航防卫 — 敌船朝岸基站接近，阻断/护航展开";
                    }
                    else if (scenario && scenario.FormationHolding)
                    {
                        status = "⑤ 围捕成功 — 等待转入护航防卫";
                    }
                    else
                    {
                        status = "④ 护航编队保持中";
                    }
                    break;
            }
        }

        private void RecomputeSlots()
        {
            if (!targetPoint)
                return;

            float captureRadius = scenario ? scenario.captureRadius : 28f;
            float defenseRadius = scenario ? scenario.defenseRadius : 48f;
            float droneAltitude = scenario ? scenario.droneAltitude : 8f;

            float[] boatAngles = { 0f, 120f, 240f };
            float[] droneAngles = { 60f, 180f, 300f };

            for (int i = 0; i < boatSlots.Length; i++)
            {
                float angle = boatAngles[i] * Mathf.Deg2Rad;
                boatSlots[i] = new Vector3(
                    targetPoint.position.x + Mathf.Cos(angle) * captureRadius,
                    .42f,
                    targetPoint.position.z + Mathf.Sin(angle) * captureRadius
                );
            }

            for (int i = 0; i < droneSlots.Length; i++)
            {
                float angle = droneAngles[i] * Mathf.Deg2Rad;
                float alt = scenario ? scenario.droneAltitude + (i - 1) * 1.6f : droneAltitude;
                droneSlots[i] = new Vector3(
                    targetPoint.position.x + Mathf.Cos(angle) * defenseRadius,
                    alt,
                    targetPoint.position.z + Mathf.Sin(angle) * defenseRadius
                );
            }
        }
    }
}
