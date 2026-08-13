using UnityEngine;

namespace UavUsv
{
    public sealed class CooperativeMission : MonoBehaviour
    {
        public enum Phase { OnDeck, TakingOff, Outbound, WaitingForBoat, DeckApproach, Landing, Complete }

        public Transform boat;
        public Transform drone;
        public Transform deck;
        public DroneVisual droneVisual;
        public Phase phase { get; private set; } = Phase.OnDeck;
        public float takeoffAltitude = 8f;
        public float targetRadius = 5f;
        public float droneDepartureDelay = 8f;
        public float boatSpeed = 3.2f;
        public float droneCruiseSpeed = 6f;
        public Vector3 targetEnu = new Vector3(35, 18, 0);
        public bool automatic = true;
        public bool cruiseWithDroneOnDeck = false;

        private float phaseStarted;
        private Vector3 safePoint;
        private bool boatArrived;

        public string Status => phase.ToString();

        private void Start() => ResetMission();

        public void ResetMission()
        {
            boat.position = Coordinates.ToUnity(0, 0, .42f);
            FaceBoatToTarget();
            drone.SetParent(deck, false);
            drone.localPosition = new Vector3(0, .28f, 0);
            drone.localRotation = Quaternion.identity;
            phase = Phase.OnDeck;
            phaseStarted = Time.time;
            boatArrived = false;
            if (droneVisual) droneVisual.spinning = false;
        }

        public void StartMission()
        {
            if (phase != Phase.OnDeck && phase != Phase.Complete) return;
            if (phase == Phase.Complete) ResetMission();
            drone.SetParent(null, true);
            phase = Phase.TakingOff;
            phaseStarted = Time.time;
            if (droneVisual) droneVisual.spinning = true;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space)) StartMission();
            if (Input.GetKeyDown(KeyCode.R)) ResetMission();
            if (automatic && phase == Phase.OnDeck && cruiseWithDroneOnDeck)
            {
                MoveBoat();
                return;
            }
            if (automatic && phase == Phase.OnDeck && Time.time - phaseStarted > 1.5f) StartMission();
            if (phase != Phase.OnDeck && phase != Phase.Complete) MoveBoat();

            Vector3 targetUnity = Coordinates.ToUnity(targetEnu.x, targetEnu.y, targetEnu.z);
            Vector3 deckPosition = deck.position;
            switch (phase)
            {
                case Phase.OnDeck:
                    break;
                case Phase.TakingOff:
                    MoveDrone(new Vector3(drone.position.x, takeoffAltitude, drone.position.z), 1.8f);
                    if (drone.position.y >= takeoffAltitude - .15f)
                    {
                        Vector3 away = (Coordinates.ToUnity(0, 0, 0) - targetUnity).normalized;
                        safePoint = targetUnity + away * targetRadius + Vector3.up * takeoffAltitude;
                        phase = Phase.Outbound; phaseStarted = Time.time;
                    }
                    break;
                case Phase.Outbound:
                    if (Time.time - phaseStarted >= droneDepartureDelay) MoveDrone(safePoint, droneCruiseSpeed);
                    if (Vector3.Distance(drone.position, safePoint) < .25f) phase = Phase.WaitingForBoat;
                    break;
                case Phase.WaitingForBoat:
                    HoverFace(deckPosition);
                    if (boatArrived) { phase = Phase.DeckApproach; phaseStarted = Time.time; }
                    break;
                case Phase.DeckApproach:
                    MoveDrone(deckPosition + Vector3.up * 3f, 3.2f);
                    if (Vector3.Distance(drone.position, deckPosition + Vector3.up * 3f) < .18f && Time.time - phaseStarted > 1f)
                    { phase = Phase.Landing; phaseStarted = Time.time; }
                    break;
                case Phase.Landing:
                    MoveDrone(deckPosition + Vector3.up * .28f, .65f);
                    if (Vector3.Distance(drone.position, deckPosition + Vector3.up * .28f) < .08f)
                    {
                        drone.SetParent(deck, true);
                        phase = Phase.Complete;
                        if (droneVisual) droneVisual.spinning = false;
                    }
                    break;
            }
        }

        private void MoveBoat()
        {
            Vector3 target = Coordinates.ToUnity(targetEnu.x, targetEnu.y, .42f);
            Vector3 delta = target - boat.position; delta.y = 0;
            if (delta.magnitude <= targetRadius) { boatArrived = true; return; }
            Quaternion facing = Quaternion.LookRotation(delta.normalized, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
            boat.rotation = Quaternion.RotateTowards(boat.rotation, facing, 55f * Time.deltaTime);
            Vector3 bowForward = boat.right;
            float alignment = Mathf.Clamp01(Vector3.Dot(bowForward, delta.normalized));
            boat.position += bowForward * (boatSpeed * Mathf.Lerp(.25f, 1f, alignment) * Time.deltaTime);
        }

        private void FaceBoatToTarget()
        {
            Vector3 target = Coordinates.ToUnity(targetEnu.x, targetEnu.y, .42f);
            Vector3 delta = target - boat.position; delta.y = 0;
            boat.rotation = delta.sqrMagnitude > .001f ? Quaternion.LookRotation(delta.normalized, Vector3.up) * Quaternion.Euler(0f, -90f, 0f) : Quaternion.identity;
        }

        private void MoveDrone(Vector3 target, float speed)
        {
            Vector3 previous = drone.position;
            drone.position = Vector3.MoveTowards(previous, target, speed * Time.deltaTime);
            Vector3 velocity = drone.position - previous;
            if (velocity.sqrMagnitude > .00001f)
                drone.rotation = Quaternion.Slerp(drone.rotation, Quaternion.LookRotation(new Vector3(velocity.x, 0, velocity.z) + Vector3.forward * .001f), 3f * Time.deltaTime);
        }

        private void HoverFace(Vector3 target)
        {
            Vector3 delta = target - drone.position; delta.y = 0;
            if (delta.sqrMagnitude > .01f) drone.rotation = Quaternion.Slerp(drone.rotation, Quaternion.LookRotation(delta), 2f * Time.deltaTime);
        }
    }
}
