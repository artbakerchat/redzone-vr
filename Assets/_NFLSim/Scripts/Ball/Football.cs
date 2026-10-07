using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace NFLSim
{
    /// <summary>
    /// The football. Grab it with the controller and throw — release velocity is
    /// measured from your real hand motion. While flying it spiral-stabilizes,
    /// then resolves catches, picks, turf and bounds by radius checks.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(SphereCollider))]
    public class Football : MonoBehaviour
    {
        public enum BallState { Placed, ToHands, InHands, Held, Flying, Dead }
        public BallState State { get; private set; } = BallState.Placed;
        public bool Thrown { get; private set; }

        Rigidbody rb;
        XRGrabInteractable grab;
        TrailRenderer trail;

        readonly Vector3[] posBuf = new Vector3[12];
        readonly float[] timeBuf = new float[12];
        int bufCount;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            grab = GetComponent<XRGrabInteractable>();
            trail = GetComponent<TrailRenderer>();
            rb.isKinematic = true;
            grab.enabled = false;
            if (trail) trail.emitting = false;

            grab.selectEntered.AddListener(_ =>
            {
                if (State == BallState.InHands) State = BallState.Held;
            });
            grab.selectExited.AddListener(OnReleased);
        }

        public void Place(Vector3 spot)
        {
            StopAllCoroutines();
            transform.SetParent(null);
            transform.position = spot;
            transform.rotation = Quaternion.identity;
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            grab.enabled = false;
            Thrown = false;
            bufCount = 0;
            if (trail) { trail.emitting = false; trail.Clear(); }
            State = BallState.Placed;
        }

        public void SnapToHands(Vector3 hands) => StartCoroutine(SnapRoutine(hands));

        IEnumerator SnapRoutine(Vector3 hands)
        {
            State = BallState.ToHands;
            Vector3 from = transform.position;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.22f;
                transform.position = Vector3.Lerp(from, hands, Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f)));
                yield return null;
            }
            State = BallState.InHands;
            grab.enabled = true;
        }

        /// <summary>
        /// Blue-scenario handoff: parents the ball to a runner's anchor and
        /// kills its physics. The runner becomes the carrier via OnCaught().
        /// </summary>
        public void HandTo(Transform t)
        {
            StopAllCoroutines();
            State = BallState.Dead;
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            grab.enabled = false;
            Thrown = false;
            if (trail) { trail.emitting = false; trail.Clear(); }
            transform.SetParent(t);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        public void Kill()
        {
            StopAllCoroutines();
            State = BallState.Dead;
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            grab.enabled = false;
            if (trail) trail.emitting = false;
        }

        void Update()
        {
            if (State == BallState.Held && grab.isSelected && grab.interactorsSelecting.Count > 0)
            {
                var interactor = grab.interactorsSelecting[0];
                Vector3 p = interactor.GetAttachTransform(grab).position;
                if (bufCount < posBuf.Length) bufCount++;
                for (int i = posBuf.Length - 1; i > 0; i--)
                {
                    posBuf[i] = posBuf[i - 1];
                    timeBuf[i] = timeBuf[i - 1];
                }
                posBuf[0] = p;
                timeBuf[0] = Time.time;
            }
        }

        void OnReleased(SelectExitEventArgs _)
        {
            if (State != BallState.Held) return;

            Vector3 v;
            if (bufCount >= 2)
            {
                int last = Mathf.Min(bufCount - 1, posBuf.Length - 1);
                float dt = Mathf.Max(timeBuf[0] - timeBuf[last], 0.016f);
                v = (posBuf[0] - posBuf[last]) / dt;
            }
            else
            {
                v = transform.forward * 8f; // dropped without motion: gentle lob forward
            }

            float speed = Mathf.Clamp(v.magnitude * SimConfig.ThrowBoost,
                SimConfig.MinThrowSpeed, SimConfig.MaxThrowSpeed);
            Vector3 dir = v.magnitude > 0.5f
                ? v.normalized
                : (transform.forward + Vector3.up * 0.4f).normalized;

            rb.isKinematic = false;
            rb.linearVelocity = dir * speed;
            rb.angularVelocity = dir * 18f; // spiral
            transform.rotation = Quaternion.LookRotation(dir);
            if (trail) trail.emitting = true;

            State = BallState.Flying;
            Thrown = true;
            ExperienceDirector.Instance.OnThrow();
        }

        void FixedUpdate()
        {
            if (State != BallState.Flying) return;
            var dir = ExperienceDirector.Instance;
            if (dir == null || dir.State != MomentState.Live) return;

            Vector3 vel = rb.linearVelocity;
            if (vel.sqrMagnitude > 1f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(vel.normalized), 8f * Time.fixedDeltaTime);

            float rRad = SimConfig.Y(SimConfig.CatchRadiusYards);
            float dRad = SimConfig.Y(SimConfig.DefenderCatchRadiusYards);

            RouteRunner bestR = null; float bestRd = float.MaxValue;
            foreach (var r in dir.receivers)
            {
                if (!r.CanCatch) continue;
                float d = Vector3.Distance(transform.position, r.CatchPoint.position);
                if (d < bestRd) { bestRd = d; bestR = r; }
            }
            DefenderAI bestD = null; float bestDd = float.MaxValue;
            foreach (var d in dir.defenders)
            {
                if (!d.CanIntercept) continue;
                float ddist = Vector3.Distance(transform.position, d.InterceptPoint.position);
                if (ddist < bestDd) { bestDd = ddist; bestD = d; }
            }

            bool rIn = bestR != null && bestRd <= rRad;
            bool dIn = bestD != null && bestDd <= dRad;
            if (rIn && dIn)
            {
                if (bestRd <= bestDd) CompleteCatch(bestR); else Intercept(bestD);
                return;
            }
            if (rIn) { CompleteCatch(bestR); return; }
            if (dIn) { Intercept(bestD); return; }

            if (transform.position.y <= 0.09f)
            {
                dir.OnIncomplete("Incomplete — hit the turf.");
                return;
            }
            float ax = Mathf.Abs(transform.position.x);
            float z = transform.position.z;
            if (ax > SimConfig.Y(30f) || z > SimConfig.Y(125f) || z < SimConfig.Y(-5f))
            {
                dir.OnIncomplete("Incomplete — out of bounds.");
                return;
            }
            if (vel.magnitude < 0.6f)
            {
                dir.OnIncomplete("Incomplete — the ball died.");
            }
        }

        void CompleteCatch(RouteRunner r)
        {
            State = BallState.Dead;
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            if (trail) trail.emitting = false;
            transform.SetParent(r.CatchPoint);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            r.OnCaught();
            ExperienceDirector.Instance.OnCatch(r);
        }

        void Intercept(DefenderAI d)
        {
            State = BallState.Dead;
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            if (trail) trail.emitting = false;
            transform.SetParent(d.InterceptPoint);
            transform.localPosition = Vector3.zero;
            d.OnPick();
            ExperienceDirector.Instance.OnInterception(d);
        }
    }
}
