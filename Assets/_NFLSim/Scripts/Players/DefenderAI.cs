using UnityEngine;

namespace NFLSim
{
    /// <summary>
    /// Simple defender: man coverage (mirrors an assignment after a reaction
    /// delay) or pass rush (runs at the QB — reach him before the throw and
    /// it's a sack). After a catch, the nearest defenders switch to pursuit.
    /// </summary>
    public class DefenderAI : MonoBehaviour
    {
        public enum Role { Man, Rush }

        public Role role = Role.Man;
        public RouteRunner assignment;
        public Transform InterceptPoint;
        public bool CanIntercept => live && !hasPick;

        bool live;
        bool hasPick;
        float reactAt;
        Transform pursueOverride;

        public void LineUp(Vector3 pos, Role r, RouteRunner a)
        {
            role = r;
            assignment = a;
            transform.position = pos;
            transform.rotation = Quaternion.identity;
            live = false;
            hasPick = false;
            pursueOverride = null;
        }

        public void OnSnap()
        {
            live = true;
            reactAt = Time.time + SimConfig.DefenderReactionS;
        }

        public void Pursue(Transform t) => pursueOverride = t;

        public void OnPick()
        {
            hasPick = true;
            live = false;
        }

        public void Stop()
        {
            live = false;
            pursueOverride = null;
        }

        void Update()
        {
            if (!live || Time.time < reactAt) return;

            float speed = (role == Role.Rush ? SimConfig.RusherSpeedYps : SimConfig.DefenderSpeedYps)
                * SimConfig.UnitsPerYard;

            Vector3 target;
            if (pursueOverride != null) target = pursueOverride.position;
            else if (role == Role.Rush) target = ExperienceDirector.Instance.qb.HeadPosition;
            else if (assignment != null) target = assignment.transform.position;
            else return;

            Vector3 to = target - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0004f) return;
            transform.position += to.normalized * Mathf.Min(speed * Time.deltaTime, to.magnitude);
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(to.normalized), 8f * Time.deltaTime);
        }
    }
}
