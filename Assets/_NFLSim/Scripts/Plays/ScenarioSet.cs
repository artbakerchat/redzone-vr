using System;
using UnityEngine;

namespace NFLSim
{
    /// <summary>
    /// Scenario identity color — the broadcast design language.
    /// Blue = a run (RB) is the probable play: the moment plays as a
    /// broadcast-style cinematic. Red = passing: the probable play is
    /// YOUR throw, first-person.
    /// </summary>
    public enum ScenarioColor { Blue, Red }

    /// <summary>
    /// One eligible receiver: where he lines up (yards, +x = offense's right,
    /// lineZ relative to the line of scrimmage) and his route as waypoints
    /// in yards relative to his lineup spot (+z = downfield).
    /// A single-waypoint route means "run there and settle".
    /// </summary>
    [Serializable]
    public class ReceiverSpot
    {
        public string id;
        public float lineX;
        public float lineZ;
        public float speedYps = SimConfig.ReceiverSpeedYps;
        public Vector2[] route;
    }

    /// <summary>
    /// One redzone moment. ballOn is the yard line (0 = own goal .. 100 = opp
    /// goal); redzone scenarios sit 80..97. Blue scenarios name a runnerIndex
    /// into receivers[] — that player takes the handoff on the snap.
    /// </summary>
    [Serializable]
    public class RedzoneScenario
    {
        public string title;
        public string situation;
        public string broadcastLine;
        public ScenarioColor color;
        public float ballOn;
        public ReceiverSpot[] receivers;
        public int runnerIndex;
    }

    /// <summary>
    /// The moment list. Add a scenario by appending a RedzoneScenario —
    /// no new code needed.
    /// </summary>
    public static class ScenarioSet
    {
        static Vector2[] R(params Vector2[] pts) => pts;

        static ReceiverSpot X(float lx, float lz, Vector2[] route) =>
            new ReceiverSpot { id = "X", lineX = lx, lineZ = lz, route = route };
        static ReceiverSpot Z(float lx, float lz, Vector2[] route) =>
            new ReceiverSpot { id = "Z", lineX = lx, lineZ = lz, route = route };
        static ReceiverSpot S(float lx, float lz, Vector2[] route) =>
            new ReceiverSpot { id = "S", lineX = lx, lineZ = lz, route = route };
        static ReceiverSpot T(float lx, float lz, Vector2[] route) =>
            new ReceiverSpot { id = "T", lineX = lx, lineZ = lz, speedYps = SimConfig.TightEndSpeedYps, route = route };

        public static readonly RedzoneScenario[] Scenarios = new RedzoneScenario[]
        {
            new RedzoneScenario {
                title = "4TH & GOAL",
                situation = "4th quarter — down 4, 0:08 on the clock",
                broadcastLine = "The season comes down to one throw.",
                color = ScenarioColor.Red,
                ballOn = 92f,
                receivers = new ReceiverSpot[] {
                    X(-8, 0, R(new Vector2(0,4),  new Vector2(5,8))),   // slant
                    Z( 8, 0, R(new Vector2(0,8),  new Vector2(0,12))),  // fade
                    S(-3,-1, R(new Vector2(-4,2), new Vector2(-7,4))),  // flat
                    T( 4, 0, R(new Vector2(0,5),  new Vector2(2,8))),   // seam
                },
            },
            new RedzoneScenario {
                title = "3RD & 2",
                situation = "3rd quarter — tied, 4:12 left",
                broadcastLine = "The back has been unstoppable all night.",
                color = ScenarioColor.Blue,
                ballOn = 94f,
                runnerIndex = 0,
                receivers = new ReceiverSpot[] {
                    X( 0,-4, R(new Vector2(0,12))),  // RB dive — straight through
                    Z( 8, 0, R(new Vector2(0,2))),   // block & settle
                    S(-8, 0, R(new Vector2(0,2))),   // block & settle
                    T( 4, 0, R(new Vector2(0,2))),   // block & settle
                },
            },
            new RedzoneScenario {
                title = "2ND & GOAL",
                situation = "2nd quarter — down 3, 0:44 left",
                broadcastLine = "Twelve yards from the lead.",
                color = ScenarioColor.Red,
                ballOn = 88f,
                receivers = new ReceiverSpot[] {
                    X(-8, 0, R(new Vector2(0,9),  new Vector2(-6,13))), // corner
                    Z( 8, 0, R(new Vector2(0,11), new Vector2(2,15))),  // seam
                    S(-3,-1, R(new Vector2(-3,2), new Vector2(-6,3))),  // flat
                    T( 4, 0, R(new Vector2(0,4),  new Vector2(4,7))),   // slant
                },
            },
            new RedzoneScenario {
                title = "1ST & GOAL",
                situation = "Overtime — first possession",
                broadcastLine = "Punch it in and walk it off.",
                color = ScenarioColor.Red,
                ballOn = 97f,
                receivers = new ReceiverSpot[] {
                    X(-6, 0, R(new Vector2(0,3),  new Vector2(4,5))),  // quick slant
                    Z( 6, 0, R(new Vector2(0,3),  new Vector2(-4,5))),  // quick slant
                    S(-2,-1, R(new Vector2(0,2))),                     // settle
                    T( 3, 0, R(new Vector2(0,4),  new Vector2(0,7))),   // stick
                },
            },
        };
    }
}
