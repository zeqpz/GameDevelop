// RagdollController — RagdollService/RagdollSimClient in one component:
// binds the humanoid to a RagdollEngine, owns the state machine, and drives
// the skeleton from the sim AFTER the Animator evaluates each frame — so
// the animation keeps playing underneath and the muscles chase it (active
// ragdolls: Muscle 1 = animated, 0 = limp, the get-up = the ramp between).
//
//   • Knockdown(point, dir, speed): the applyImpulse flow — snap particles
//     to the pose, shove with contact falloff, colliders off, control off.
//   • TripKnockdown(): feet braked → momentum faceplants over them.
//   • Player fall trigger (PhysicsDamageService sky-fall rule): ragdolls
//     MID-AIR once fallen ≥3.5 studs below takeoff height — flat jumps
//     never trigger; slam damage = engine impact, capped 40, 0.8 s
//     cooldown (the shipped cap).
//   • Get-up: after minDown + settle, Muscle ramps 0→1 over ~0.9 s pulling
//     the body toward the playing idle — when the pose converges, control
//     returns and the capsule teleports under the hips. StayDown corpses
//     skip it (dummies ragdoll on death, stand back up on respawn).
//   • X = debug self-knockdown using the zero-momentum shove rule (head
//     height, 24 st/s along facing — lower speeds pendulum back upright).
// The root follows the hips (camera tracks the flop); bones are written in
// WORLD space after the root moves, parents before children.
using UnityEngine;
using Game.Audio;
using Game.Combat;
using Game.Core;
using Game.Movement;

namespace Game.Ragdoll
{
    public enum RagdollState { None, Down, GetUp }

    public class RagdollController : MonoBehaviour
    {
        const float FallRagdollDrop = 3.5f * 0.28f;    // FALL.RAGDOLL_DROP
        const float SlamSafeSpeed = 6f;
        const float SlamDamagePerMs = 3f;
        const float SlamDamageCap = 40f;               // the shipped cap
        const float SlamDamageCooldown = 0.8f;
        const float GetUpTime = 0.9f;
        const float GetUpConverged = 0.16f;
        const float DebugShoveSpeed = 24f * 0.28f;     // 6.72 — the statue fix

        public RagdollState State { get; private set; }

        readonly RagdollEngine _engine = new RagdollEngine();
        bool _bound;
        Animator _anim;
        PlayerMotor _motor;
        CharacterController _cc;
        Health _health;
        GunController _gun;
        InputService _input;

        float _acc;
        float _minDownT;
        bool _stayDown;
        float _nextSlamDmg;
        bool _wasAirborne;
        float _takeoffY;

        class BoneDrive
        {
            public Transform Bone;
            public int Pa, Pb;             // primary axis particles
            public int Sa = -1, Sb = -1;   // secondary (basis) particles
            public Quaternion BindRot;
            public Vector3 BindP, BindS;
        }

        readonly System.Collections.Generic.List<BoneDrive> _drives =
            new System.Collections.Generic.List<BoneDrive>();
        int _headIdx = -1, _headTipIdx = -1;
        Vector3 _headTipLocal;

        void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _cc = GetComponent<CharacterController>();
            _health = GetComponent<Health>();
            _gun = GetComponent<GunController>();
        }

        // ── Binding ────────────────────────────────────────────────────────
        bool Bind()
        {
            if (_bound) return true;
            _anim = GetComponentInChildren<Animator>();
            if (_anim == null || !_anim.isHuman) return false;

            Transform B(HumanBodyBones b) => _anim.GetBoneTransform(b);
            var hips = B(HumanBodyBones.Hips);
            var chest = B(HumanBodyBones.Chest);
            var head = B(HumanBodyBones.Head);
            if (hips == null || chest == null || head == null) return false;

            int pHips = _engine.AddParticle(hips, 0.14f, 1.0f, true);
            int pChest = _engine.AddParticle(chest, 0.13f, 1.0f, true);
            int pHead = _engine.AddParticle(head, 0.11f, 0.9f, true);
            _headIdx = pHead;
            _headTipIdx = _engine.AddParticle(null, 0.05f, 0.9f, false);
            _headTipLocal = head.InverseTransformPoint(head.position + head.up * 0.2f);
            _engine.Particles[_headTipIdx].Pos = head.position + head.up * 0.2f;
            _engine.Particles[_headTipIdx].Prev = _engine.Particles[_headTipIdx].Pos;

            int pUAL = _engine.AddParticle(B(HumanBodyBones.LeftUpperArm), 0.07f, 0.55f, false);
            int pUAR = _engine.AddParticle(B(HumanBodyBones.RightUpperArm), 0.07f, 0.55f, false);
            int pLAL = _engine.AddParticle(B(HumanBodyBones.LeftLowerArm), 0.06f, 0.5f, false);
            int pLAR = _engine.AddParticle(B(HumanBodyBones.RightLowerArm), 0.06f, 0.5f, false);
            int pHandL = _engine.AddParticle(B(HumanBodyBones.LeftHand), 0.055f, 0.45f, false);
            int pHandR = _engine.AddParticle(B(HumanBodyBones.RightHand), 0.055f, 0.45f, false);
            int pULL = _engine.AddParticle(B(HumanBodyBones.LeftUpperLeg), 0.09f, 0.7f, false);
            int pULR = _engine.AddParticle(B(HumanBodyBones.RightUpperLeg), 0.09f, 0.7f, false);
            int pLLL = _engine.AddParticle(B(HumanBodyBones.LeftLowerLeg), 0.08f, 0.6f, false);
            int pLLR = _engine.AddParticle(B(HumanBodyBones.RightLowerLeg), 0.08f, 0.6f, false);
            int pFootL = _engine.AddParticle(B(HumanBodyBones.LeftFoot), 0.06f, 0.35f, false);
            int pFootR = _engine.AddParticle(B(HumanBodyBones.RightFoot), 0.06f, 0.35f, false);

            // Chains
            _engine.Link(pHips, pChest);
            _engine.Link(pChest, pHead);
            _engine.Link(pHead, _headTipIdx);
            _engine.Link(pChest, pUAL); _engine.Link(pUAL, pLAL); _engine.Link(pLAL, pHandL);
            _engine.Link(pChest, pUAR); _engine.Link(pUAR, pLAR); _engine.Link(pLAR, pHandR);
            _engine.Link(pHips, pULL); _engine.Link(pULL, pLLL); _engine.Link(pLLL, pFootL);
            _engine.Link(pHips, pULR); _engine.Link(pULR, pLLR); _engine.Link(pLLR, pFootR);
            // Structural braces — virtual, never capsule-collide
            _engine.Link(pUAL, pUAR, false);
            _engine.Link(pULL, pULR, false);
            _engine.Link(pHips, pUAL, false); _engine.Link(pHips, pUAR, false);
            _engine.Link(pChest, pULL, false); _engine.Link(pChest, pULR, false);
            // Fold limits (inequality sticks)
            _engine.LinkMin(pHips, pLLL, 0.55f);
            _engine.LinkMin(pHips, pLLR, 0.55f);
            _engine.LinkMin(pChest, pHandL, 0.5f);
            _engine.LinkMin(pChest, pHandR, 0.5f);
            _engine.LinkMin(pHips, pHead, 0.7f);
            _engine.BuildSelfCollision();
            _engine.BuildCapsuleCollision();   // limbs can't pass through flesh

            // Bone drives (parents first — children overwrite after)
            AddDrive(hips, pHips, pChest, pULL, pULR);
            AddDrive(chest, pChest, pHead, pUAL, pUAR);
            AddDrive(head, pHead, _headTipIdx, pUAL, pUAR);
            AddDrive(B(HumanBodyBones.LeftUpperArm), pUAL, pLAL);
            AddDrive(B(HumanBodyBones.LeftLowerArm), pLAL, pHandL);
            AddDrive(B(HumanBodyBones.RightUpperArm), pUAR, pLAR);
            AddDrive(B(HumanBodyBones.RightLowerArm), pLAR, pHandR);
            AddDrive(B(HumanBodyBones.LeftUpperLeg), pULL, pLLL);
            AddDrive(B(HumanBodyBones.LeftLowerLeg), pLLL, pFootL);
            AddDrive(B(HumanBodyBones.RightUpperLeg), pULR, pLLR);
            AddDrive(B(HumanBodyBones.RightLowerLeg), pLLR, pFootR);

            _bound = true;
            return true;
        }

        void AddDrive(Transform bone, int pa, int pb, int sa = -1, int sb = -1)
        {
            if (bone == null) return;
            var d = new BoneDrive
            {
                Bone = bone,
                Pa = pa,
                Pb = pb,
                Sa = sa,
                Sb = sb,
                BindRot = bone.rotation,
                BindP = (_engine.Particles[pb].Pos - _engine.Particles[pa].Pos).normalized,
            };
            if (sa >= 0)
                d.BindS = (_engine.Particles[sb].Pos - _engine.Particles[sa].Pos).normalized;
            _drives.Add(d);
        }

        // ── Public API ─────────────────────────────────────────────────────
        public void Knockdown(Vector3 point, Vector3 dir, float speed,
            float minDown = 1.4f, bool stayDown = false)
        {
            if (!Bind()) return;
            if (State != RagdollState.None)
            {
                _engine.ApplyImpulse(point, dir, speed);   // re-hit while down
                _engine.WakeUp();
                return;
            }
            EnterDown(minDown, stayDown, _cc != null && _cc.enabled ? _cc.velocity : Vector3.zero);
            _engine.ApplyImpulse(point, dir, speed);
        }

        public void TripKnockdown(float minDown = 1.2f)
        {
            if (!Bind() || State != RagdollState.None) return;
            EnterDown(minDown, false, _cc != null && _cc.enabled ? _cc.velocity : Vector3.zero);
            _engine.BrakeFeet();
        }

        public void RestoreInstant()
        {
            if (State == RagdollState.None) return;
            State = RagdollState.None;
            _engine.Muscle = 0f;
            SetColliders(true);
            SetControl(true);
        }

        void EnterDown(float minDown, bool stayDown, Vector3 rootVel)
        {
            State = RagdollState.Down;
            _minDownT = minDown;
            _stayDown = stayDown;
            _engine.Muscle = 0f;   // truly limp — any idle-pose tug reads as twitch
            _engine.SnapToBones(rootVel);
            SetColliders(false);
            SetControl(false);
        }

        void SetControl(bool on)
        {
            if (_motor != null) _motor.enabled = on;
            if (_gun != null) _gun.enabled = on;
            if (_cc != null && !on) _cc.enabled = false;   // re-enabled by Restore
        }

        void SetColliders(bool on)
        {
            foreach (var col in GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger || col is CharacterController) continue;
                col.enabled = on;
            }
        }

        // ── Triggers (player) ──────────────────────────────────────────────
        void Update()
        {
            if (State != RagdollState.None) return;
            if (_input == null) Services.TryGet(out _input);

            if (_motor != null && _motor.enabled)
            {
                // Sky-fall rule: ragdoll MID-AIR once ≥3.5 st below takeoff.
                bool airborne = !_motor.IsGrounded;
                if (airborne && !_wasAirborne) _takeoffY = transform.position.y;
                if (airborne && _takeoffY - transform.position.y > FallRagdollDrop)
                {
                    Vector3 v = _cc != null ? _cc.velocity : Vector3.zero;
                    Knockdown(transform.position + Vector3.up * 1.2f,
                        v.sqrMagnitude > 0.01f ? v.normalized : transform.forward,
                        Mathf.Max(2f, v.magnitude * 0.4f), 1.0f);
                }
                _wasAirborne = airborne;

                if (_input != null && _input.RagdollTestPressed)
                    Knockdown(transform.position + Vector3.up * 1.55f,
                        transform.forward, DebugShoveSpeed, 1.2f);
            }
        }

        // ── Sim + bone writing (post-animator) ─────────────────────────────
        void LateUpdate()
        {
            if (State == RagdollState.None) return;

            // Bones hold the FRESH animated pose right now — sample muscle
            // targets from it, then overwrite with the sim.
            foreach (var p in _engine.Particles)
                if (p.Bone != null) p.Target = p.Bone.position;
            if (_headTipIdx >= 0 && _headIdx >= 0)
                _engine.Particles[_headTipIdx].Target =
                    _engine.Particles[_headIdx].Bone.TransformPoint(_headTipLocal);

            _acc += Time.deltaTime;
            int steps = 0;
            while (_acc >= RagdollEngine.FixedStep && steps++ < 4)
            {
                _engine.Step();
                _acc -= RagdollEngine.FixedStep;
            }

            // Slam damage + body-drop thud (0.8 s cooldown, 40 cap).
            float slam = _engine.ConsumeImpact(out Vector3 at);
            if (slam > SlamSafeSpeed && Time.time >= _nextSlamDmg)
            {
                _nextSlamDmg = Time.time + SlamDamageCooldown;
                if (Services.TryGet(out AudioService audio))
                    audio.PlayAt(ProceduralAudio.UiThud(), at,
                        Mathf.Clamp01(slam / 15f) * 0.9f, 0.55f, 20f);
                if (!_stayDown && _health != null && !_health.IsDead)
                    _health.ApplyDamage(BodyRegion.Torso,
                        Mathf.Min(SlamDamageCap, (slam - SlamSafeSpeed) * SlamDamagePerMs), at);
            }

            // Render interpolation: frames sit between fixed steps.
            float alpha = _acc / RagdollEngine.FixedStep;

            // Root follows the hips so the camera tracks the flop.
            Vector3 hips = _engine.LerpedPos(0, alpha);
            float rootY = transform.position.y;
            if (Physics.Raycast(hips + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground,
                    3f, ~0, QueryTriggerInteraction.Ignore))
                rootY = ground.point.y;
            transform.position = new Vector3(hips.x, rootY, hips.z);

            WriteBones(alpha);

            switch (State)
            {
                case RagdollState.Down:
                    _minDownT -= Time.deltaTime;
                    if (!_stayDown && _minDownT <= 0f
                        && (_engine.Settled || _engine.Age > 4f))
                    {
                        State = RagdollState.GetUp;
                        _engine.WakeUp();
                    }
                    break;
                case RagdollState.GetUp:
                    _engine.Muscle = Mathf.MoveTowards(_engine.Muscle, 1f,
                        Time.deltaTime / GetUpTime);
                    if (_engine.Muscle >= 1f && _engine.AvgTargetDistance() < GetUpConverged)
                        Restore();
                    break;
            }
        }

        void WriteBones(float alpha)
        {
            // Hips carry position; everything else is rotation-only so the
            // hierarchy keeps limb lengths (particles just steer). All reads
            // are step-interpolated — raw steps stutter at any framerate.
            if (_drives.Count > 0)
                _drives[0].Bone.position = _engine.LerpedPos(_drives[0].Pa, alpha);

            foreach (var d in _drives)
            {
                Vector3 simP = (_engine.LerpedPos(d.Pb, alpha)
                    - _engine.LerpedPos(d.Pa, alpha)).normalized;
                if (simP.sqrMagnitude < 0.0001f) continue;
                Quaternion delta;
                if (d.Sa >= 0)
                {
                    Vector3 simS = (_engine.LerpedPos(d.Sb, alpha)
                        - _engine.LerpedPos(d.Sa, alpha)).normalized;
                    delta = BasisRot(simP, simS) * Quaternion.Inverse(BasisRot(d.BindP, d.BindS));
                }
                else
                {
                    delta = Quaternion.FromToRotation(d.BindP, simP);
                }
                d.Bone.rotation = delta * d.BindRot;
            }
        }

        static Quaternion BasisRot(Vector3 primary, Vector3 secondary)
        {
            Vector3 f = Vector3.Cross(primary, secondary);
            if (f.sqrMagnitude < 0.000001f) return Quaternion.LookRotation(primary);
            return Quaternion.LookRotation(f.normalized, primary.normalized);
        }

        void Restore()
        {
            State = RagdollState.None;
            _engine.Muscle = 0f;
            SetColliders(true);
            // Capsule under the hips, feet on the ground.
            if (_cc != null)
            {
                Vector3 hips = _engine.HipsPos;
                float y = transform.position.y + 1.0f;
                if (Physics.Raycast(hips + Vector3.up * 1f, Vector3.down, out RaycastHit hit,
                        4f, ~0, QueryTriggerInteraction.Ignore))
                    y = hit.point.y + 0.92f;
                transform.position = new Vector3(hips.x, y, hips.z);
                _cc.enabled = true;
            }
            SetControl(true);
        }
    }
}
