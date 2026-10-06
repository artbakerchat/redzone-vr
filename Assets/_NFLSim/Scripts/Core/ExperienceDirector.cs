using System.Collections.Generic;
using UnityEngine;

namespace NFLSim
{
    public enum MomentState { Broadcast, Wipe, PreSnap, Live, Dead, Celebration }

    /// <summary>
    /// Owns the experience flow: Broadcast → Wipe → PreSnap → Live → Celebration.
    /// No playbook, no downs, no score — every moment is a redzone touchdown
    /// waiting to happen. Red scenarios are the user's throw, first-person;
    /// blue scenarios are a broadcast-style run cinematic (the run is destiny).
    /// Yard lines are 0 (own goal) .. 100 (opp goal).
    /// </summary>
    public class ExperienceDirector : MonoBehaviour
    {
        public static ExperienceDirector Instance { get; private set; }

        [Header("Scene refs (wired by NFLSim/Build Experience Scene)")]
        public FieldBuilder field;
        public BroadcastPresenter presenter;
        public QBController qb;
        public Football ball;
        public List<RouteRunner> receivers = new List<RouteRunner>();
        public List<DefenderAI> defenders = new List<DefenderAI>();

        public MomentState State { get; private set; } = MomentState.Broadcast;
        public RedzoneScenario CurrentScenario { get; private set; }
        public int ScenarioIndex { get; private set; }

        RouteRunner carrier;
        RouteRunner runner; // blue scenarios: takes the handoff
        float carrierTime;

        void Awake()
        {
            Instance = this;
            Physics.gravity = new Vector3(0f, -SimConfig.GravityY, 0f);
        }

        void Start()
        {
            ScenarioIndex = 0;
            presenter.BuildRig();
            ShowBroadcast();
        }

        // ---------- coordinates ----------
        public Vector3 YardToWorld(float xYards, float yardLine, float height = 0f) =>
            new Vector3(SimConfig.Y(xYards), height, SimConfig.Y(yardLine));

        public float WorldToYardLine(Vector3 world) => world.z / SimConfig.UnitsPerYard;

        public static float XZDist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ---------- flow ----------
        void ShowBroadcast()
        {
            State = MomentState.Broadcast;
            CurrentScenario = ScenarioSet.Scenarios[ScenarioIndex];
            presenter.HideTouchdown();
            presenter.ShowBroadcast(CurrentScenario);
        }

        /// <summary>Broadcast card is up — the viewer takes the field.</summary>
        public void TakeTheField()
        {
            if (State != MomentState.Broadcast) return;
            State = MomentState.Wipe;
            presenter.PlayWipe(CurrentScenario, SetupMoment);
        }

        void SetupMoment()
        {
            State = MomentState.PreSnap;
            carrier = null;
            carrierTime = 0f;
            runner = null;

            float ballOn = CurrentScenario.ballOn;

            qb.SetSpot(YardToWorld(0f, Mathf.Max(ballOn - 5f, 1f)));
            ball.Place(YardToWorld(0f, ballOn - 5f, 1.2f));

            int n = Mathf.Min(receivers.Count, CurrentScenario.receivers.Length);
            for (int i = 0; i < n; i++)
            {
                var s = CurrentScenario.receivers[i];
                receivers[i].LineUp(YardToWorld(s.lineX, ballOn + s.lineZ), s);
            }
            if (CurrentScenario.color == ScenarioColor.Blue)
                runner = receivers[Mathf.Clamp(CurrentScenario.runnerIndex, 0, n - 1)];

            for (int i = 0; i < defenders.Count; i++)
            {
                bool isRusher = i >= n;
                var role = isRusher ? DefenderAI.Role.Rush : DefenderAI.Role.Man;
                var assignment = isRusher ? null : receivers[i];
                Vector3 pos = isRusher
                    ? YardToWorld(0f, ballOn + 3f)
                    : YardToWorld(CurrentScenario.receivers[i].lineX, ballOn + 1.5f);
                defenders[i].LineUp(pos, role, assignment);
            }

            presenter.ShowMomentPrompt(CurrentScenario);
        }

        public void Snap()
        {
            if (State != MomentState.PreSnap) return;
            State = MomentState.Live;
            presenter.HideMomentPrompt();

            if (CurrentScenario.color == ScenarioColor.Blue && runner != null)
            {
                // The run is destiny: handoff on the snap. The defense chases
                // for the cameras — nobody stops this one.
                ball.HandTo(runner.CatchPoint);
                runner.OnCaught();
                carrier = runner;
            }
            else
            {
                ball.SnapToHands(qb.HandsPosition);
            }

            foreach (var r in receivers) r.RunRoute();
            foreach (var d in defenders) d.OnSnap();
        }

        void Update()
        {
            if (State != MomentState.Live) return;

            // Red moments only: the rush can still get home.
            if (!ball.Thrown && CurrentScenario.color == ScenarioColor.Red)
            {
                foreach (var d in defenders)
                {
                    if (d.role != DefenderAI.Role.Rush) continue;
                    if (XZDist(d.transform.position, qb.HeadPosition) < SimConfig.Y(SimConfig.SackRadiusYards))
                    {
                        RetryMoment("Sacked — run it back.");
                        return;
                    }
                }
            }

            if (carrier != null)
            {
                carrierTime += Time.deltaTime;
                float yard = Mathf.Clamp(WorldToYardLine(carrier.transform.position), 0f, 100f);

                if (yard >= 100f) { Touchdown(); return; }

                if (CurrentScenario.color == ScenarioColor.Red)
                {
                    float ax = Mathf.Abs(carrier.transform.position.x / SimConfig.UnitsPerYard);
                    if (ax > SimConfig.FieldWidthYards / 2f)
                    {
                        RetryMoment("Out of bounds — run it back.");
                        return;
                    }
                    foreach (var d in defenders)
                    {
                        if (XZDist(d.transform.position, carrier.transform.position) <
                            SimConfig.Y(SimConfig.TackleRadiusYards))
                        {
                            RetryMoment("Tackled short — run it back.");
                            return;
                        }
                    }
                    if (carrierTime > SimConfig.CarrierWhistleS)
                    {
                        RetryMoment("Whistled down — run it back.");
                        return;
                    }
                }
                // Blue moments: no tackles, no whistles. Destiny.
            }
        }

        // ---------- ball events (called by Football) ----------
        public void OnThrow() { /* routes keep running; defenders react on their own timers */ }

        public void OnCatch(RouteRunner r)
        {
            carrier = r;
            carrierTime = 0f;
            float yard = Mathf.Clamp(WorldToYardLine(r.transform.position), 0f, 100f);
            if (yard >= 100f) { Touchdown(); return; }

            var sorted = new List<DefenderAI>(defenders);
            sorted.Sort((a, b) => XZDist(a.transform.position, r.transform.position)
                .CompareTo(XZDist(b.transform.position, r.transform.position)));
            for (int i = 0; i < Mathf.Min(2, sorted.Count); i++) sorted[i].Pursue(r.transform);
        }

        public void OnInterception(DefenderAI d) => RetryMoment("Picked — run it back.");

        public void OnIncomplete(string why) => RetryMoment("Incomplete — run it back.");

        // ---------- resolution ----------
        void Touchdown()
        {
            Freeze();
            State = MomentState.Celebration;
            presenter.ShowTouchdown(CurrentScenario);
            Invoke(nameof(NextScenario), SimConfig.CelebrationS);
        }

        void RetryMoment(string message)
        {
            if (State != MomentState.Live) return;
            Freeze();
            presenter.ShowMomentPrompt(message);
            Invoke(nameof(SetupMoment), SimConfig.MomentRetryS);
        }

        void NextScenario()
        {
            ScenarioIndex = (ScenarioIndex + 1) % ScenarioSet.Scenarios.Length;
            ShowBroadcast();
        }

        void Freeze()
        {
            State = MomentState.Dead;
            carrier = null;
            ball.Kill();
            foreach (var r in receivers) r.Stop();
            foreach (var d in defenders) d.Stop();
        }
    }
}
