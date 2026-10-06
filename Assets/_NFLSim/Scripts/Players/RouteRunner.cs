using UnityEngine;

namespace NFLSim
{
    /// <summary>
    /// An eligible receiver. Lines up from the playbook data, runs his route
    /// waypoint list on the snap, settles and stays catch-eligible, and becomes
    /// a ball carrier upfield after the catch.
    /// </summary>
    public class RouteRunner : MonoBehaviour
    {
        public string receiverId = "X";
        public Transform CatchPoint;

        public bool RouteRunning { get; private set; }
        public bool HasBall { get; private set; }
        public bool CanCatch => (RouteRunning || settled) && !HasBall;

        ReceiverSpot spot;
        Vector3[] waypoints;
        int wp;
        float speedYps;
        bool settled;

        public void LineUp(Vector3 pos, ReceiverSpot s)
        {
            spot = s;
            speedYps = s.speedYps;
            transform.position = pos;
            transform.rotation = Quaternion.identity; // face downfield (+z)
            RouteRunning = false;
            HasBall = false;
            settled = false;
            wp = 0;
            waypoints = null;
        }

        public void RunRoute()
        {
            if (spot == null || spot.route == null || spot.route.Length == 0) return;
            waypoints = new Vector3[spot.route.Length];
            for (int i = 0; i < waypoints.Length; i++)
                waypoints[i] = transform.position + new Vector3(
                    SimConfig.Y(spot.route[i].x), 0f, SimConfig.Y(spot.route[i].y));
            wp = 0;
            settled = false;
            RouteRunning = true;
        }

        public void OnCaught()
        {
            HasBall = true;
            RouteRunning = false;
            settled = false;
        }

        public void Stop()
        {
            RouteRunning = false;
            HasBall = false;
            settled = false;
        }

        void Update()
        {
            if (HasBall)
            {
                // ball carrier: sprint straight upfield, the director whistles him down
                transform.position += Vector3.forward *
                    SimConfig.Y(SimConfig.BallCarrierSpeedYps) * Time.deltaTime;
                return;
            }
            if (!RouteRunning || waypoints == null) return;

            if (wp >= waypoints.Length)
            {
                // route stem done: settle, turn and face the quarterback
                RouteRunning = false;
                settled = true;
                var qb = ExperienceDirector.Instance != null ? ExperienceDirector.Instance.qb : null;
                if (qb != null)
                {
                    Vector3 toQb = qb.HeadPosition - transform.position;
                    toQb.y = 0f;
                    if (toQb.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.Slerp(transform.rotation,
                            Quaternion.LookRotation(-toQb), 6f * Time.deltaTime);
                }
                return;
            }

            Vector3 target = waypoints[wp];
            Vector3 to = target - transform.position;
            to.y = 0f;
            float step = speedYps * SimConfig.UnitsPerYard * Time.deltaTime;
            if (to.magnitude <= Mathf.Max(step, 0.05f))
            {
                transform.position = new Vector3(target.x, transform.position.y, target.z);
                wp++;
            }
            else
            {
                transform.position += to.normalized * step;
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(to.normalized), 10f * Time.deltaTime);
            }
        }
    }
}
