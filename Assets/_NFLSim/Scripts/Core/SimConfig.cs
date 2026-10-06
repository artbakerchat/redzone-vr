using UnityEngine;

namespace NFLSim
{
    /// <summary>
    /// Global tuning. The sim uses an arcade scale so VR throws stay fun:
    /// 1 Unity unit = 1 yard, drawn at UnitsPerYard meters.
    /// (A real 40-yard throw would be ~37 m — too far for a controller flick,
    /// so the world is drawn at 60% and throws get a small boost.)
    /// </summary>
    public static class SimConfig
    {
        public const float UnitsPerYard = 0.6f;

        public const float FieldLengthYards = 120f; // includes both end zones
        public const float FieldWidthYards = 53.3f;

        // Interaction radii
        public const float CatchRadiusYards = 1.6f;
        public const float DefenderCatchRadiusYards = 1.4f;
        public const float TackleRadiusYards = 1.2f;
        public const float SackRadiusYards = 1.5f;

        // Player speeds (yards/sec)
        public const float ReceiverSpeedYps = 8.0f;
        public const float TightEndSpeedYps = 7.2f;
        public const float DefenderSpeedYps = 7.8f;
        public const float RusherSpeedYps = 3.2f;
        public const float BallCarrierSpeedYps = 9.0f;
        public const float DefenderReactionS = 0.35f;

        // Rules / feel
        public const float CarrierWhistleS = 8f; // auto-whistle if nobody tackles
        public const float ThrowBoost = 1.12f;
        public const float MinThrowSpeed = 6f;   // m/s — anything slower is a lob that dies
        public const float MaxThrowSpeed = 30f;  // m/s — clamp for sanity

        // Broadcast design language: scenario identity colors
        public static readonly Color ScenarioBlue = new Color(0.2f, 0.5f, 1f);  // run (RB)
        public static readonly Color ScenarioRed = new Color(1f, 0.2f, 0.25f);   // pass (your throw)

        // Experience pacing
        public const float WipeDurationS = 1.1f;   // broadcast → moment transition
        public const float CelebrationS = 4f;      // touchdown splash hold
        public const float MomentRetryS = 2.2f;    // beat before a failed moment restages

        public static float Y(float yards) => yards * UnitsPerYard;
        public static float GravityY => 9.81f * UnitsPerYard;
    }
}
