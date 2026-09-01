// StatsService — the StatService + SurvivalService pair, ported. Five
// skills, all 0..100 (Constants MAX_* = 100), the XP-derived ones recomputed
// from accumulators exactly like the Roblox statXP/gunAccuracy pattern:
//   • Agility  ← meters sprinted.        • Accuracy ← shots (hits weigh 4×).
//   • Strength ← moving under load.      • Intelligence ← AddIntelligence
//     (crafting later; +1/craft).        • Reputation ← AddReputation
//     (kills route here, like GunService/FistCombat did; +1 per dummy kill).
// Legacy 'aim' stat intentionally does not exist (v2 accuracy supersedes).
//
// Vitals (0..100): hunger/thirst decay at the shipped Roblox rates
// (HUNGER_DRAIN_RATE 0.05/s, THIRST 0.07/s) with the ≤30 self-double rule;
// stamina drains on sprint scaled by LoadConfig.StaminaDrainMult (carry
// weight, Strength-eased) and Agility (up to −25% drain), regens after a
// beat, and gates sprint with hysteresis (blocked ≤4, released ≥18) via
// PlayerMotor.SprintBlocked.
//
// Effects owned here every tick: ExternalSpeedMult = LoadConfig.SpeedMult
// (weight, Strength) — the item-weight system's real curve. Persisted in
// profile v3 via the OnBeforeSave hook. Starvation is the SurvivalService
// rule: at 0 hunger/thirst the player Health takes 2/3 dmg per second;
// death respawns at the spawn point with vitals reset to max.
using UnityEngine;
using Game.Combat;
using Game.Core;
using Game.Data;
using Game.Inventory;
using Game.Movement;

namespace Game.Stats
{
    public class StatsService
    {
        const float MaxSkill = 100f;
        const float HungerPerSec = 0.05f;   // Roblox HUNGER_DRAIN_RATE (~33 min empty)
        const float ThirstPerSec = 0.07f;   // Roblox THIRST_DRAIN_RATE (~24 min empty)
        const float DoubleDrainBelow = 30f; // HUNGER_DOUBLE_DRAIN_THRESHOLD
        const float StarveDmgPerSec = 2f;   // SurvivalService starving damage / tick
        const float DehydrateDmgPerSec = 3f;   // dehydration damage / tick
        const float SprintDrainPerSec = 6f;
        const float StaminaRegenPerSec = 9f;
        const float RegenDelay = 1.2f;
        const float SprintBlockAt = 4f;
        const float SprintFreeAt = 18f;

        // Skills
        public float Strength { get; private set; }
        public float Agility { get; private set; }
        public float Accuracy { get; private set; }
        public float Intelligence { get; private set; }
        public float Reputation { get; private set; }

        // Vitals
        public float Hunger { get; private set; } = 100f;
        public float Thirst { get; private set; } = 100f;
        public float Stamina { get; private set; } = 100f;

        // XP accumulators (Roblox statXP / gunAccuracy)
        float _sprintXp, _gunXp, _strengthXp;

        bool _loaded;
        bool _blocked;
        float _regenWait;
        float _survivalTickT;      // 1 Hz starvation cadence (SURVIVAL_TICK)
        bool _respawnQueued;
        Vector3 _spawnPos;
        PlayerMotor _motor;
        Health _playerHealth;
        InventoryService _inv;

        public StatsService()
        {
            EventBus.Subscribe<EntityDamaged>(OnEntityDamaged);
            SaveService.OnBeforeSave += WriteProfile;
        }

        public void Shutdown()
        {
            EventBus.Unsubscribe<EntityDamaged>(OnEntityDamaged);
            SaveService.OnBeforeSave -= WriteProfile;
            if (_playerHealth != null) _playerHealth.Died -= OnPlayerDied;
        }

        void OnEntityDamaged(EntityDamaged e)
        {
            // Kills route reputation, as on Roblox — but never the player's
            // own death (starvation would otherwise farm rep).
            if (e.Died && (_motor == null || e.Target != _motor.gameObject))
                AddReputation(1f);
        }

        void OnPlayerDied(Health h) => _respawnQueued = true;

        public void AddReputation(float delta) =>
            Reputation = Mathf.Clamp(Reputation + delta, 0f, MaxSkill);

        public void AddIntelligence(float delta) =>
            Intelligence = Mathf.Clamp(Intelligence + delta, 0f, MaxSkill);

        // GunController reports every shot; hits earn full XP, misses a taste.
        public void OnShot(bool hitFlesh)
        {
            _gunXp += hitFlesh ? 1f : 0.25f;
            Accuracy = Mathf.Min(MaxSkill, _gunXp * 0.5f);
        }

        public void Tick(float dt)
        {
            if (!_loaded && SaveService.Profile != null) LoadFromProfile();
            if (_motor == null)
            {
                _motor = Object.FindAnyObjectByType<PlayerMotor>();
                if (_motor == null) return;
                _spawnPos = _motor.transform.position;   // respawn point
            }
            if (_playerHealth == null)
            {
                _playerHealth = _motor.GetComponent<Health>();
                if (_playerHealth != null) _playerHealth.Died += OnPlayerDied;
            }
            if (_inv == null) Services.TryGet(out _inv);

            float weight = _inv != null ? _inv.Player.TotalWeightLbs : 0f;
            float speed = _motor.CurrentSpeed;
            bool sprinting = _motor.SprintT > 0.25f && speed > 2.2f && _motor.IsGrounded;

            // ── Skill XP ───────────────────────────────────────────────────
            if (sprinting)
            {
                _sprintXp += speed * dt;                       // meters sprinted
                Agility = Mathf.Min(MaxSkill, _sprintXp / 50f);
            }
            if (speed > 0.5f && weight > 5f)
            {
                _strengthXp += (weight / 50f) * dt;            // time under load
                Strength = Mathf.Min(MaxSkill, _strengthXp / 12f);
            }

            // ── Vitals ─────────────────────────────────────────────────────
            // Roblox rule: a vital at/below 30 doubles ITS OWN drain (the
            // earlier port had this crossed between the two — corrected).
            float hungerRate = HungerPerSec * (Hunger <= DoubleDrainBelow ? 2f : 1f);
            float thirstRate = ThirstPerSec * (Thirst <= DoubleDrainBelow ? 2f : 1f);
            Hunger = Mathf.Max(0f, Hunger - hungerRate * dt);
            Thirst = Mathf.Max(0f, Thirst - thirstRate * dt);

            // ── Starvation / dehydration (SurvivalService, 1 Hz tick) ──────
            _survivalTickT += dt;
            if (_survivalTickT >= 1f)
            {
                _survivalTickT -= 1f;
                if (_playerHealth != null && !_playerHealth.IsDead)
                {
                    Vector3 at = _motor.transform.position;
                    if (Hunger <= 0f)
                        _playerHealth.ApplyDamage(BodyRegion.Torso, StarveDmgPerSec, at);
                    if (Thirst <= 0f && !_playerHealth.IsDead)
                        _playerHealth.ApplyDamage(BodyRegion.Torso, DehydrateDmgPerSec, at);
                }
            }

            // Death → respawn at the spawn point with ALL survival stats
            // reset to max (the Roblox CharacterAdded reset).
            if (_respawnQueued)
            {
                _respawnQueued = false;
                Hunger = 100f;
                Thirst = 100f;
                Stamina = 100f;
                _playerHealth.ResetHealth();
                var cc = _motor.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                _motor.transform.position = _spawnPos;
                if (cc != null) cc.enabled = true;
                Debug.Log("[Stats] You died — respawned, vitals reset");
            }

            if (sprinting)
            {
                float drain = SprintDrainPerSec
                    * LoadConfig.StaminaDrainMult(weight, Strength)
                    * (1f - 0.25f * Agility / MaxSkill);
                Stamina = Mathf.Max(0f, Stamina - drain * dt);
                _regenWait = RegenDelay;
            }
            else
            {
                _regenWait -= dt;
                if (_regenWait <= 0f)
                    Stamina = Mathf.Min(100f, Stamina + StaminaRegenPerSec * dt);
            }

            if (Stamina <= SprintBlockAt) _blocked = true;
            else if (Stamina >= SprintFreeAt) _blocked = false;
            _motor.SprintBlocked = _blocked;

            // ── The item-weight rule: load speed penalty, Strength-eased ───
            _motor.ExternalSpeedMult = LoadConfig.SpeedMult(weight, Strength);
        }

        // ── Persistence (profile v3) ───────────────────────────────────────
        void LoadFromProfile()
        {
            _loaded = true;
            var p = SaveService.Profile;
            if (!p.hasStats || p.stats == null) return;
            Strength = p.stats.strength;
            Agility = p.stats.agility;
            Accuracy = p.stats.accuracy;
            Intelligence = p.stats.intelligence;
            Reputation = p.stats.reputation;
            _sprintXp = p.stats.sprintXp;
            _gunXp = p.stats.gunXp;
            _strengthXp = p.stats.strengthXp;
            Hunger = p.stats.hunger;
            Thirst = p.stats.thirst;
            Stamina = p.stats.stamina;
        }

        void WriteProfile(PlayerProfile p)
        {
            if (!_loaded) return;
            p.hasStats = true;
            p.stats = new SavedStats
            {
                strength = Strength,
                agility = Agility,
                accuracy = Accuracy,
                intelligence = Intelligence,
                reputation = Reputation,
                sprintXp = _sprintXp,
                gunXp = _gunXp,
                strengthXp = _strengthXp,
                hunger = Hunger,
                thirst = Thirst,
                stamina = Stamina,
            };
        }
    }
}
