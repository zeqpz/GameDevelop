// LoadConfig — the shared carry-load curve, ported verbatim from
// ReplicatedStorage.Modules.Shared.LoadConfig:
//   • Speed: ~0.4%/lb penalty (45 lb ≈ 82%, 100 lb = 60%), floor 35%.
//   • Stamina drain: +0.6%/lb, capped at 2.5×.
//   • Strength (0..MAX_STRENGTH=100) halves both penalties at max.
// Pure math, shared by movement and vitals so they always agree — replaces
// the interim maxCarryLbs/overweightSpeedMult knobs.
using UnityEngine;

namespace Game.Stats
{
    public static class LoadConfig
    {
        public const float MaxStrength = 100f;

        static float StrengthEase(float strength) =>
            1f - 0.5f * Mathf.Clamp01(strength / MaxStrength);

        public static float SpeedMult(float weightLbs, float strength) =>
            Mathf.Max(0.35f, 1f - 0.004f * weightLbs * StrengthEase(strength));

        public static float StaminaDrainMult(float weightLbs, float strength) =>
            Mathf.Min(2.5f, 1f + 0.006f * weightLbs * StrengthEase(strength));
    }
}
