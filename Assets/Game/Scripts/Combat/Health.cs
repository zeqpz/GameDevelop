// Health + BodyHitbox — the explicit hitbox contract. Region colliders are
// TRIGGERS carrying a BodyHitbox that points at the owner's Health; only gun
// casts query triggers, so nothing else in the game can trip on them (the
// answer to the Roblox invisible-hitbox breakage). Damage multipliers follow
// the ported map: head ×2 (a 28-dmg pistol headshot ≈ zeroes the Roblox 60
// head pool), torso ×1, limbs ×0.75.
using System;
using UnityEngine;
using Game.Core;

namespace Game.Combat
{
    public enum BodyRegion { Head, Torso, Arm, Leg }

    public readonly struct EntityDamaged
    {
        public readonly GameObject Target;
        public readonly BodyRegion Region;
        public readonly float Amount;
        public readonly bool Died;
        public readonly Vector3 Point;
        public EntityDamaged(GameObject target, BodyRegion region, float amount, bool died, Vector3 point)
        {
            Target = target;
            Region = region;
            Amount = amount;
            Died = died;
            Point = point;
        }
    }

    public class BodyHitbox : MonoBehaviour
    {
        public BodyRegion region = BodyRegion.Torso;
        public Health health;

        public static float RegionMult(BodyRegion r) => r switch
        {
            BodyRegion.Head => 2f,
            BodyRegion.Arm => 0.75f,
            BodyRegion.Leg => 0.75f,
            _ => 1f,
        };
    }

    public class Health : MonoBehaviour
    {
        public float maxHealth = 100f;
        public float Current { get; private set; }
        public bool IsDead { get; private set; }
        public event Action<Health> Died;

        void Awake() => Current = maxHealth;

        public void ApplyDamage(BodyRegion region, float amount, Vector3 point)
        {
            if (IsDead) return;
            Current = Mathf.Max(0f, Current - amount);
            bool died = Current <= 0f;
            if (died) IsDead = true;
            EventBus.Publish(new EntityDamaged(gameObject, region, amount, died, point));
            if (died) Died?.Invoke(this);
        }

        public void ResetHealth()
        {
            Current = maxHealth;
            IsDead = false;
        }
    }
}
