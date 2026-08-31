// MovementSettings — the Unity twin of Roblox MovementModule.Config.
// One ScriptableObject holds every locomotion knob; defaults are the exact
// values we shipped on Roblox, converted studs→meters (see GameUnits).
// Create via: Assets → Create → Game → Movement Settings (the bootstrap menu
// makes one automatically).
using UnityEngine;

namespace Game.Core
{
    [CreateAssetMenu(fileName = "MovementSettings", menuName = "Game/Movement Settings")]
    public class MovementSettings : ScriptableObject
    {
        [Header("Base speeds (m/s) — Roblox: walk 7, sprint 16 st/s")]
        public float walkSpeed = 1.96f;
        public float sprintSpeed = 4.48f;

        [Header("Speed ramp (m/s²) — Roblox ACCEL 14 / DECEL 20 st/s²")]
        public float accelRate = 3.92f;
        public float decelRate = 5.6f;
        [Tooltip("Seconds to reach full sprint / drop back to walk")]
        public float sprintRampTime = 0.85f;
        public float sprintDownTime = 0.55f;

        [Header("Momentum (GTA overstep) — direction is a smoothed vector, not raw input")]
        [Tooltip("How fast momentum chases fresh input (per second)")]
        public float momentumAccel = 7f;
        [Tooltip("Momentum kept through a hard (>120°) reversal — the pivot beat")]
        public float momentumTurnDrag = 0.45f;
        [Tooltip("Momentum decay/sec on release from a walk (quick stop)")]
        public float overrunWalkDecay = 11f;
        [Tooltip("Decay/sec on release from full sprint (long glide)")]
        public float overrunSprintDecay = 3.2f;
        public float overrunMin = 0.06f;
        [Tooltip("Speed decel while overrunning a sprint (m/s²) — Roblox 9 st/s²")]
        public float sprintStopDecel = 2.52f;

        [Header("Gait wander — running is never laser-straight")]
        public float wanderDirDegrees = 2.5f;
        public float wanderSpeedFrac = 0.04f;
        public float wanderFrequency = 0.7f;

        [Header("Turn penalty — sharp input flicks scrub speed")]
        public float turnThresholdDeg = 35f;
        public float turnPenaltyMax = 0.28f;
        public float turnPenaltyDecay = 5f;

        [Header("Delayed body turn toward travel (free-look, no aim)")]
        [Tooltip("Exponential ease toward the travel heading (per second)")]
        public float moveTurnResponse = 7f;
        [Tooltip("Turn rate cap at walking pace (rad/s)")]
        public float moveTurnMaxRate = 7.5f;
        [Tooltip("Turn rate cap at full sprint (rad/s) — wide arcs at speed")]
        public float moveTurnSprintRate = 3.6f;
        [Header("Over-shoulder body follow — humanized, never robotic")]
        [Tooltip("Standing still, camera drift inside this cone doesn't turn the body")]
        public float aimDeadZoneDeg = 7f;
        [Tooltip("Swings past this angle engage instantly (no hesitation)")]
        public float aimSnapAngleDeg = 65f;
        [Tooltip("Random hesitation before the body starts chasing the camera (seconds)")]
        public float aimHesitationMin = 0.05f;
        public float aimHesitationMax = 0.17f;
        [Tooltip("Follow ease speed is re-rolled in this range each engagement (per second)")]
        public float aimResponseMin = 10f;
        public float aimResponseMax = 16f;

        [Header("Jump & air — Roblox JUMP_HEIGHT 5.2 st, air speed ×0.72")]
        public float jumpHeight = 1.46f;
        public float gravity = 25f;
        public float airSpeedMult = 0.72f;

        [Header("Landing stagger — Roblox LAND_PENALTY 0.70 over 1.0 s")]
        public float landPenaltyStart = 0.70f;
        public float landPenaltyTime = 1.0f;
        [Tooltip("Seconds airborne before a landing counts (stairs don't stagger)")]
        public float landMinAirtime = 0.18f;

        [Header("Backpedal — backwards is far slower than forwards")]
        [Tooltip("Speed multiplier moving straight backward (blends by angle)")]
        public float backwardSpeedMult = 0.45f;

        [Header("Crouch — Ctrl toggles (Roblox stance-system port, crouch only)")]
        public float crouchSpeedMult = 0.5f;
        [Tooltip("Capsule height while crouched (standing 1.8)")]
        public float crouchHeight = 1.2f;
        [Tooltip("Stance blend response (per second)")]
        public float crouchResponse = 9f;

        [Header("Carry weight — Roblox item-weight system (lbs)")]
        public float maxCarryLbs = 80f;
        [Tooltip("Speed multiplier at/over max carry (eases in from 40% load)")]
        public float overweightSpeedMult = 0.6f;
    }
}
