// RagdollEngine — the Roblox kinematic Verlet ragdoll (RS.Modules.Shared.
// RagdollEngine), ported with its hard-won rules intact, plus pose-matching
// MUSCLES for GTA-style active ragdolls:
//   • Verlet particles at the humanoid joint pivots (15 here vs R15's 20),
//     Jakobsen stick constraints + structural braces + fold-limit
//     inequality sticks. Fixed 60 Hz.
//   • Swept motion ray per particle (no tunneling) + rest ray; RESTITUTION
//     0.05, GROUND_FRICTION 0.4 — the "dead thud, no slide" tune.
//   • THE DRIFT-PUMP FIX (2026-07-08): collision response uses v0 — the
//     kinetic velocity captured at integration, BEFORE the solver — and the
//     end-of-step cleanup collide pass is POSITION-ONLY. Reconstructing
//     velocity after solving folds solver relaxation into velocity = a
//     deterministic ground crawl.
//   • Per-particle sleep under JITTER_EPS while contacting; ground-body
//     damping 0.72/step only when slow; self-collision pairs at 0.7 bind
//     distance corrected at HALF strength (full strength fights the sticks
//     in crumpled poses = perpetual limb quiver).
//   • Settle/freeze: SETTLE_SPEED for SETTLE_TIME with a CORE-DOWN contact
//     (hips/chest/head — a body can never lock standing), FREEZE_MIN_TIME
//     floor, FORCE_FREEZE_AFTER backstop.
//   • applyImpulse: contact-falloff velocities + up-bias (hit legs → flip);
//     trip = feet braked → faceplant. ConsumeImpact() reports the real slam
//     for the body-drop sound + fall damage.
//   • MUSCLES (the active-ragdoll extension): every step each particle is
//     pulled toward its bone's CURRENT ANIMATED position, scaled by a
//     per-part weight × the 0..1 Muscle dial. Muscle 1 = follows whatever
//     the Animator plays; 0 = limp; the get-up IS the ramp back to 1.
using System.Collections.Generic;
using UnityEngine;

namespace Game.Ragdoll
{
    public class RagdollEngine
    {
        // RagdollConfig, studs → meters where dimensional
        const float Gravity = 25.2f;                 // 90 st/s²
        const float Restitution = 0.05f;
        const float GroundFriction = 0.4f;
        const float AirDamp = 0.996f;
        const float GroundBodyDamp = 0.72f;          // per step, slow bodies only
        const float GroundDampSpeedGate = 10f * 0.28f;
        const float JitterEps = 2.0f * 0.28f;        // per-particle sleep (fix round 2)
        const float SettleSpeed = 1.8f * 0.28f;
        const float SettleTime = 2.0f;
        const float FreezeMinTime = 0.9f;
        const float ForceFreezeAfter = 5.0f;
        const float SelfCollideScale = 0.7f;
        const float SelfCollideStiff = 0.5f;
        const int SolveIterations = 4;
        public const float FixedStep = 1f / 60f;
        // Anti-jitter contact model (standard Verlet practice: never project
        // 100% of penetration — partial bias + a permitted slop stops the
        // snap-out/sink-in oscillation that reads as ground shiver).
        const float ContactSlop = 0.008f;
        const float ContactBias = 0.85f;
        // Knockdown impulses arrive 80% weaker (user tune) — one choke point
        // so every source (shots, falls, X debug) scales together.
        public const float ImpulseScale = 0.2f;
        // ── TOPPLE: the falling-tree phase (smoothness pass 3) ─────────────
        // A body going limp the instant it's hit crumples straight down —
        // spine folds, knees buckle, no direction. Real falls pivot: the
        // body stays semi-rigid, tips over its feet in the direction it
        // leans, and only goes limp when the torso lands. Temporary BRACE
        // sticks hold the knockdown-moment shape (spine firm, legs at half
        // give), a gentle lean assist commits the fall toward wherever the
        // body is actually tipping, and feet grip hard so they pivot
        // instead of sliding out. All of it fades over BRACE_FADE once the
        // core touches down — the tuned limp settle takes over from there.
        const float BraceFade = 0.25f;
        const float LeanAssistAccel = 3.0f;
        const float ToppleFriction = 0.85f;
        const float BraceStiff = 0.5f;

        public class Particle
        {
            public Transform Bone;       // null for synthetic tips
            public Vector3 Pos, Prev, V0, Target;
            public Vector3 PrevStep;     // last fixed-step pos — render interpolation
            public float Radius;
            public float MuscleWeight;
            public bool Grounded;
            public bool Core;            // hips/chest/head — the core-down set
        }

        class Stick { public int A, B; public float Len; public bool MinOnly; public bool Body; }
        class CapsulePair { public int A1, A2, B1, B2; public float RSum; }
        class Brace { public int A, B; public float Len; public float Stiff; }

        public readonly List<Particle> Particles = new List<Particle>();
        readonly List<Stick> _sticks = new List<Stick>();
        readonly List<(int a, int b, float min)> _selfPairs = new List<(int, int, float)>();
        readonly List<CapsulePair> _capsulePairs = new List<CapsulePair>();
        readonly List<Brace> _braces = new List<Brace>();
        float _braceStrength;
        bool _toppling;
        Vector3 _leanFallback = Vector3.forward;

        public float Muscle;             // 0 limp … 1 follows the animation
        public bool Settled { get; private set; }
        public float Age { get; private set; }
        float _settleClock;
        float _maxImpact;
        Vector3 _maxImpactPos;

        public Vector3 HipsPos => Particles.Count > 0 ? Particles[0].Pos : Vector3.zero;

        public int AddParticle(Transform bone, float radius, float muscleWeight, bool core)
        {
            Particles.Add(new Particle
            {
                Bone = bone,
                Pos = bone != null ? bone.position : Vector3.zero,
                Prev = bone != null ? bone.position : Vector3.zero,
                Radius = radius,
                MuscleWeight = muscleWeight,
                Core = core,
            });
            return Particles.Count - 1;
        }

        // body=true marks REAL segments (bone chains) that capsule-collide;
        // structural braces pass false — they're virtual, not flesh.
        public void Link(int a, int b, bool body = true)
        {
            _sticks.Add(new Stick
            {
                A = a,
                B = b,
                Len = Vector3.Distance(Particles[a].Pos, Particles[b].Pos),
                Body = body,
            });
        }

        // Fold-limit: only pushes APART when closer than the bind distance ×
        // scale — the inequality sticks that stop knees/elbows folding through.
        public void LinkMin(int a, int b, float scale)
        {
            _sticks.Add(new Stick
            {
                A = a,
                B = b,
                Len = Vector3.Distance(Particles[a].Pos, Particles[b].Pos) * scale,
                MinOnly = true,
            });
        }

        // Sphere min-distance pairs between everything NOT stick-connected
        // and close at bind (limbs) — half-strength corrections.
        public void BuildSelfCollision()
        {
            var linked = new HashSet<(int, int)>();
            foreach (var s in _sticks)
            {
                linked.Add((s.A, s.B));
                linked.Add((s.B, s.A));
            }
            for (int i = 0; i < Particles.Count; i++)
                for (int j = i + 1; j < Particles.Count; j++)
                {
                    if (linked.Contains((i, j))) continue;
                    float d = Vector3.Distance(Particles[i].Pos, Particles[j].Pos);
                    if (d < 0.65f)
                        _selfPairs.Add((i, j, d * SelfCollideScale));
                }
        }

        // CAPSULE_COLLIDE: segment-vs-segment between body sticks that share
        // no particle — a forearm can't pass through a thigh or the torso.
        // Corrected at HALF strength (the anti-quiver rule).
        public void BuildCapsuleCollision()
        {
            for (int i = 0; i < _sticks.Count; i++)
            {
                var s1 = _sticks[i];
                if (!s1.Body || s1.MinOnly) continue;
                for (int j = i + 1; j < _sticks.Count; j++)
                {
                    var s2 = _sticks[j];
                    if (!s2.Body || s2.MinOnly) continue;
                    if (s1.A == s2.A || s1.A == s2.B || s1.B == s2.A || s1.B == s2.B)
                        continue;
                    float r = Mathf.Min(Particles[s1.A].Radius, Particles[s1.B].Radius) * 0.9f
                        + Mathf.Min(Particles[s2.A].Radius, Particles[s2.B].Radius) * 0.9f;
                    _capsulePairs.Add(new CapsulePair
                    { A1 = s1.A, A2 = s1.B, B1 = s2.A, B2 = s2.B, RSum = r });
                }
            }
        }

        // Seed the sim at the current bone pose with a starting velocity.
        public void SnapToBones(Vector3 rootVelocity)
        {
            foreach (var p in Particles)
            {
                if (p.Bone != null) p.Pos = p.Bone.position;
                p.Prev = p.Pos - rootVelocity * FixedStep;
                p.PrevStep = p.Pos;
                p.Grounded = false;
            }
            Age = 0f;
            _settleClock = 0f;
            Settled = false;
            _maxImpact = 0f;
            _braces.Clear();
            _braceStrength = 0f;
            _toppling = false;
        }

        // Freeze the CURRENT shape into temporary braces and start the
        // falling-tree phase. firm pairs (spine/neck) hold at full brace
        // stiffness; anything touching soft (legs) gets half — a little
        // knee give. Pairs already held by an equality stick are skipped.
        public void BeginTopple(Vector3 fallbackDir, int[] firm, int[] soft)
        {
            _braces.Clear();
            var linked = new HashSet<(int, int)>();
            foreach (var s in _sticks)
                if (!s.MinOnly)
                {
                    linked.Add((s.A, s.B));
                    linked.Add((s.B, s.A));
                }
            int nf = firm.Length;
            int[] all = new int[nf + soft.Length];
            firm.CopyTo(all, 0);
            soft.CopyTo(all, nf);
            for (int i = 0; i < all.Length; i++)
                for (int j = i + 1; j < all.Length; j++)
                {
                    if (linked.Contains((all[i], all[j]))) continue;
                    _braces.Add(new Brace
                    {
                        A = all[i],
                        B = all[j],
                        Len = Vector3.Distance(Particles[all[i]].Pos, Particles[all[j]].Pos),
                        Stiff = (i < nf && j < nf) ? 1f : 0.5f,
                    });
                }
            fallbackDir.y = 0f;
            if (fallbackDir.sqrMagnitude > 0.001f) _leanFallback = fallbackDir.normalized;
            _braceStrength = 1f;
            _toppling = true;
        }

        // applyImpulse: contact falloff + up-bias. Hit legs → the body flips.
        public void ApplyImpulse(Vector3 point, Vector3 dir, float speed)
        {
            speed *= ImpulseScale;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
            foreach (var p in Particles)
            {
                float falloff = Mathf.Clamp01(1f - Vector3.Distance(p.Pos, point) / 1.4f);
                Vector3 v = dir * speed * (0.55f + 0.45f * falloff)
                    + Vector3.up * speed * 0.25f * falloff;
                p.Prev = p.Pos - v * FixedStep;
            }
        }

        // Trip: brake the feet so momentum pitches the body over them.
        public void BrakeFeet()
        {
            foreach (var p in Particles)
                if (!p.Core && p.MuscleWeight <= 0.35f && p.Radius <= 0.07f)
                    p.Prev = p.Pos;
        }

        // Slam reporting (body-drop sound + fall damage) — consume-once.
        public float ConsumeImpact(out Vector3 at)
        {
            at = _maxImpactPos;
            float v = _maxImpact;
            _maxImpact = 0f;
            return v;
        }

        public void Step()
        {
            if (Settled) return;
            float dt = FixedStep;
            Age += dt;

            // ── Integrate (capture v0 FIRST — the drift-pump rule) ─────────
            float avgSpeed = 0f;
            bool coreDown = false;
            foreach (var p in Particles)
            {
                p.PrevStep = p.Pos;      // render interpolation baseline
                p.V0 = (p.Pos - p.Prev) / dt;
                avgSpeed += p.V0.magnitude;
                float damp = AirDamp;
                if (p.Grounded && p.V0.magnitude < GroundDampSpeedGate)
                    damp = GroundBodyDamp;      // slow grounded bodies bleed out
                // Resting particles get NO gravity — a grounded, near-still
                // particle otherwise sinks and pops every step forever.
                bool resting = p.Grounded && p.V0.magnitude < JitterEps;
                Vector3 g = resting ? Vector3.zero : Vector3.down * (Gravity * dt * dt);
                Vector3 next = p.Pos + p.V0 * damp * dt + g;
                p.Prev = p.Pos;
                p.Pos = next;
            }
            avgSpeed /= Mathf.Max(1, Particles.Count);

            // ── Lean assist (topple phase only): the body commits to the
            // direction it is ACTUALLY tipping — measured core-vs-support
            // each step, fallback = the knockdown push. Applied strongest
            // at the top of the body (a lever over the feet), zero at the
            // pivot, so it reads as falling weight, never a shove. Skipped
            // airborne (n == 0): gravity owns a sky fall.
            if (_toppling && _braceStrength > 0f)
            {
                Vector3 support = Vector3.zero, com = Vector3.zero;
                int nSup = 0, nCore = 0;
                float minY = float.MaxValue, maxY = float.MinValue;
                foreach (var p in Particles)
                {
                    minY = Mathf.Min(minY, p.Pos.y);
                    maxY = Mathf.Max(maxY, p.Pos.y);
                    if (p.Grounded) { support += p.Pos; nSup++; }
                    if (p.Core) { com += p.Pos; nCore++; }
                }
                float span = maxY - minY;
                if (nSup > 0 && nCore > 0 && span > 0.3f)
                {
                    Vector3 lean = com / nCore - support / nSup;
                    lean.y = 0f;
                    Vector3 dir = lean.magnitude > 0.02f ? lean.normalized : _leanFallback;
                    foreach (var p in Particles)
                    {
                        float hf = (p.Pos.y - minY) / span;
                        p.Pos += dir * (LeanAssistAccel * hf * dt * dt);
                    }
                }
            }

            // ── Muscles: chase the animated pose (moves pos AND prev so the
            // pull adds position, not much velocity — limp stays limp) ──────
            if (Muscle > 0.001f)
            {
                foreach (var p in Particles)
                {
                    float a = 1f - Mathf.Exp(-dt * 14f * Muscle * p.MuscleWeight);
                    Vector3 delta = (p.Target - p.Pos) * a;
                    p.Pos += delta;
                    p.Prev += delta * 0.85f;
                }
            }

            // ── Solve sticks (Jakobsen relaxation) ─────────────────────────
            for (int it = 0; it < SolveIterations; it++)
            {
                foreach (var s in _sticks)
                {
                    var a = Particles[s.A];
                    var b = Particles[s.B];
                    Vector3 d = b.Pos - a.Pos;
                    float len = d.magnitude;
                    if (len < 0.0001f) continue;
                    if (s.MinOnly && len >= s.Len) continue;
                    float diff = (len - s.Len) / len;
                    Vector3 corr = d * 0.5f * diff;
                    a.Pos += corr;
                    b.Pos -= corr;
                }
                // Topple braces: soft equality springs holding the knockdown
                // shape — the body tips as one piece instead of folding.
                if (_braceStrength > 0f)
                    foreach (var br in _braces)
                    {
                        var a = Particles[br.A];
                        var b = Particles[br.B];
                        Vector3 d = b.Pos - a.Pos;
                        float len = d.magnitude;
                        if (len < 0.0001f) continue;
                        Vector3 corr = d * (0.5f * (len - br.Len) / len)
                            * (BraceStiff * br.Stiff * _braceStrength);
                        a.Pos += corr;
                        b.Pos -= corr;
                    }
                foreach (var (ia, ib, min) in _selfPairs)
                {
                    var a = Particles[ia];
                    var b = Particles[ib];
                    Vector3 d = b.Pos - a.Pos;
                    float len = d.magnitude;
                    if (len < 0.0001f || len >= min) continue;
                    Vector3 corr = d * 0.5f * ((len - min) / len) * SelfCollideStiff;
                    a.Pos += corr;
                    b.Pos -= corr;
                }
                foreach (var cp in _capsulePairs)
                {
                    var a1 = Particles[cp.A1];
                    var a2 = Particles[cp.A2];
                    var b1 = Particles[cp.B1];
                    var b2 = Particles[cp.B2];
                    ClosestSegSeg(a1.Pos, a2.Pos, b1.Pos, b2.Pos,
                        out Vector3 ca, out Vector3 cb, out float s, out float t);
                    Vector3 d = cb - ca;
                    float dist = d.magnitude;
                    if (dist >= cp.RSum) continue;
                    Vector3 dir = dist > 0.0001f ? d / dist : Vector3.up;
                    float mag = (cp.RSum - dist) * 0.5f * SelfCollideStiff;
                    a1.Pos -= dir * mag * (1f - s);
                    a2.Pos -= dir * mag * s;
                    b1.Pos += dir * mag * (1f - t);
                    b2.Pos += dir * mag * t;
                }
            }

            // ── Collide (velocity response from v0) ────────────────────────
            foreach (var p in Particles)
            {
                p.Grounded = false;
                Vector3 motion = p.Pos - p.Prev;
                float mLen = motion.magnitude;
                RaycastHit hit;
                bool contact = false;
                if (mLen > 0.0001f && Physics.Raycast(p.Prev, motion / mLen, out hit,
                        mLen + p.Radius, ~0, QueryTriggerInteraction.Ignore))
                    contact = true;
                else if (Physics.Raycast(p.Pos + Vector3.up * 0.01f, Vector3.down, out hit,
                        p.Radius + 0.03f, ~0, QueryTriggerInteraction.Ignore))
                    contact = true;   // reach past the slop: contact hysteresis

                if (!contact) continue;
                float pen = p.Radius - Vector3.Dot(p.Pos - hit.point, hit.normal);
                if (pen > ContactSlop)
                    p.Pos += hit.normal * ((pen - ContactSlop) * ContactBias);
                p.Grounded = true;
                if (p.Core) coreDown = true;

                float vN = Vector3.Dot(p.V0, hit.normal);
                if (vN < 0f)
                {
                    if (-vN > _maxImpact && -vN > 2f)
                    {
                        _maxImpact = -vN;
                        _maxImpactPos = hit.point;
                    }
                    Vector3 vNorm = hit.normal * vN;
                    Vector3 vTan = p.V0 - vNorm;
                    // Topple phase: feet grip so the body PIVOTS over them —
                    // sliding support is exactly what turns a fall into a
                    // straight-down crumple.
                    float friction = _toppling ? ToppleFriction : GroundFriction;
                    Vector3 v = vTan * (1f - friction) - vNorm * Restitution;
                    if (p.Grounded && v.magnitude < JitterEps) v = Vector3.zero;  // sleep
                    p.Prev = p.Pos - v * dt;
                }
            }

            // ── End-of-step cleanup pass: POSITION-ONLY (never velocity) ───
            foreach (var p in Particles)
            {
                if (Physics.Raycast(p.Pos + Vector3.up * 0.05f, Vector3.down,
                        out RaycastHit hit, p.Radius + 0.05f, ~0,
                        QueryTriggerInteraction.Ignore))
                {
                    float pen = p.Radius - Vector3.Dot(p.Pos - hit.point, hit.normal);
                    if (pen > ContactSlop)
                        p.Pos += hit.normal * ((pen - ContactSlop) * ContactBias);
                }
            }

            // ── Grounded kill switch: the knockdown force must DIE on the
            // ground. Solver/capsule corrections fold into next step's
            // velocity — on a grounded, already-slow particle that residual
            // is pure jitter. Hard-sleep it (prev = pos) and measure the
            // TRUE end-of-step motion (Pos - PrevStep, solver included).
            float avgMotion = 0f;
            foreach (var p in Particles)
            {
                float motion = (p.Pos - p.PrevStep).magnitude / dt;
                avgMotion += motion;
                if (p.Grounded && motion < JitterEps)
                    p.Prev = p.Pos;
            }
            avgMotion /= Mathf.Max(1, Particles.Count);

            // WHOLE-BODY sleep: per-particle sleep alone lets the sticks wake
            // neighbours in a loop. Once the body as a whole is quiet on its
            // core, every particle's velocity dies together.
            if (coreDown && avgMotion < JitterEps)
                foreach (var p in Particles) p.Prev = p.Pos;

            // ── Topple lifecycle: torso landing ends the falling-tree phase;
            // braces fade out over BRACE_FADE (limp on impact, not board-
            // stiff bounce) and the tuned settle owns everything after.
            if (_toppling && coreDown) _toppling = false;
            if (!_toppling && _braceStrength > 0f)
            {
                _braceStrength = Mathf.MoveTowards(_braceStrength, 0f, dt / BraceFade);
                if (_braceStrength <= 0f) _braces.Clear();
            }

            // ── Settle / freeze (CORE contact required — never lock
            // standing; skipped while muscles are actively getting up).
            // Fast path: a near-still grounded body locks in ~0.5 s (the
            // heaviness-pass feel); the 2 s window covers slow slides.
            if (Age >= FreezeMinTime && coreDown && Muscle < 0.15f)
            {
                if (avgMotion < JitterEps * 0.8f) _settleClock += dt * 4f;
                else if (avgMotion < SettleSpeed) _settleClock += dt;
                else _settleClock = 0f;
            }
            else
            {
                _settleClock = 0f;
            }
            // Neither freeze path may fire while muscles are actively rising —
            // a mid-get-up freeze stops the sim and strands the body forever.
            if (Muscle < 0.15f
                && (_settleClock >= SettleTime || (Age >= ForceFreezeAfter && coreDown)))
            {
                Settled = true;
                foreach (var p in Particles) p.Prev = p.Pos;   // no junk on WakeUp
            }
        }

        // Fixed steps are 60 Hz; frames aren't. Interpolate between the last
        // two steps by the accumulator fraction so bones never stutter.
        public Vector3 LerpedPos(int i, float alpha)
        {
            var p = Particles[i];
            return Vector3.Lerp(p.PrevStep, p.Pos, Mathf.Clamp01(alpha));
        }

        // Ericson closest-points-between-segments (the standard routine).
        static void ClosestSegSeg(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2,
            out Vector3 c1, out Vector3 c2, out float s, out float t)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            s = 0f;
            t = 0f;
            if (a <= 1e-6f && e <= 1e-6f) { c1 = p1; c2 = p2; return; }
            if (a <= 1e-6f)
            {
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-6f)
                {
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > 1e-6f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }

        // Average distance to the animated targets — the get-up "close enough".
        public float AvgTargetDistance()
        {
            float sum = 0f;
            foreach (var p in Particles) sum += Vector3.Distance(p.Pos, p.Target);
            return sum / Mathf.Max(1, Particles.Count);
        }

        public void WakeUp()
        {
            Settled = false;
            _settleClock = 0f;
            Age = Mathf.Min(Age, FreezeMinTime);
            // Get-up muscles must never wrestle leftover braces.
            _braces.Clear();
            _braceStrength = 0f;
            _toppling = false;
        }
    }
}
