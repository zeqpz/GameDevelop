// GunData — the tuned Roblox gun numbers, converted at the edge. Sources:
//   • HIP_FIRE {spreadMul 3.0, spreadAdd 1.0°, recoilMul 1.15} — T-ready hip
//     fire vs RMB ADS raw values (gun-ready-ads rework, 2026-08).
//   • CLOSE_RANGE {deadOn 4, reduced 6, full 14 studs, reducedScale 0.45} —
//     distance-scaled spread so point-blank never whiffs.
//   • ADS walk ×0.55 (AIM_WALK_MULT), sprint cancels ADS above 8.5 st/s.
//   • Pistol damage 28 (one glock body shot = 23% of the 120 torso pool;
//     ×2 head ≈ zeroes the 60 head pool).
using System.Collections.Generic;
using UnityEngine;
using Game.Core;

namespace Game.Combat
{
    public class GunDef
    {
        public string id;
        public string itemId;            // inventory item that grants this gun
        public float damage = 28f;
        public float fireInterval = 0.16f;   // semi-auto pistol cadence cap
        public int magSize = 15;
        public float reloadTime = 1.8f;
        public float range = 120f;

        // Live-spread machine (GunClient SPREAD_* constants, verbatim):
        // floor + movement tier, +perShot on fire (capped), decay-recovered.
        // Movement penalties snap UP instantly; recovery eases down.
        public float spreadMin = 0.3f;       // SPREAD_MIN — resting floor (deg)
        public float spreadMax = 8f;         // SPREAD_MAX — cap
        public float spreadPerShot = 1.5f;   // SPREAD_PER_SHOT
        public float spreadDecay = 5f;       // SPREAD_DECAY (deg/s)
        public float spreadWalk = 1.5f;      // SPREAD_WALK tier
        public float spreadRun = 3f;         // SPREAD_RUN tier
        public float moveWalkSpeed = 1f * GameUnits.StudsToMeters;   // > 1 st/s
        public float moveRunSpeed = 14f * GameUnits.StudsToMeters;   // > 14 st/s
        public float hipSpreadMul = 3f;      // HIP_FIRE.spreadMul
        public float hipSpreadAdd = 1f;      // HIP_FIRE.spreadAdd (degrees)

        public float recoilPitch = 1.15f;
        public float recoilYaw = 0.4f;
        public float hipRecoilMul = 1.15f;   // HIP_FIRE.recoilMul

        public float adsWalkMult = 0.55f;                                // AIM_WALK_MULT
        public float sprintCancelSpeed = 8.5f * GameUnits.StudsToMeters; // 2.38 m/s

        // CLOSE_RANGE curve (meters)
        public float closeDeadOn = 4f * GameUnits.StudsToMeters;    // 1.12
        public float closeReduced = 6f * GameUnits.StudsToMeters;   // 1.68
        public float closeFull = 14f * GameUnits.StudsToMeters;     // 3.92
        public float closeReducedScale = 0.45f;

        // 0..1 spread multiplier by target distance — shared client/server on
        // Roblox; keep it on the def so a future server twin agrees.
        public float CloseRangeScale(float dist)
        {
            if (dist <= closeDeadOn) return 0f;
            if (dist <= closeReduced)
                return Mathf.InverseLerp(closeDeadOn, closeReduced, dist) * closeReducedScale;
            if (dist <= closeFull)
                return Mathf.Lerp(closeReducedScale, 1f,
                    Mathf.InverseLerp(closeReduced, closeFull, dist));
            return 1f;
        }
    }

    public static class GunCatalog
    {
        static Dictionary<string, GunDef> _defs;

        public static GunDef ForItem(string itemId)
        {
            if (_defs == null) Build();
            foreach (var def in _defs.Values)
                if (def.itemId == itemId) return def;
            return null;
        }

        public static GunDef Get(string id)
        {
            if (_defs == null) Build();
            return _defs.TryGetValue(id, out var def) ? def : null;
        }

        static void Build()
        {
            _defs = new Dictionary<string, GunDef>
            {
                ["pistol"] = new GunDef { id = "pistol", itemId = "pistol" },
            };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _defs = null;
    }
}
