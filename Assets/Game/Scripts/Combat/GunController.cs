// GunController — the client half of the Roblox gun rework, ported:
//   • LOWERED by default: LMB dead. T toggles READY (needs a pistol in the
//     inventory grid — weapons live in the grid, per the Roblox rule).
//   • READY = hip fire: spread ×HIP.spreadMul +spreadAdd, recoil ×recoilMul.
//   • ADS = hold RMB while ready: raw spread/recoil, walk capped ×0.55.
//     Sprinting past 8.5 st/s cancels the aim; slowing with RMB held
//     re-arms it (aim state is recomputed live, same as the Heartbeat).
//   • PerformCast twin: clean probe ray measures target distance, the WHOLE
//     deviation stack scales by the CLOSE_RANGE curve, then one hitscan ray.
//     Trigger hitboxes are the only trigger-query in the game; the dummy's
//     body blocker (any collider under a Health) is passed through so the
//     capsule can't eat the hitbox behind it.
//   • Feedback: tracer/flash/shell/impact/blood via VfxService, camera kick
//     via CameraRig.AddRecoil, hitmarker + ammo on GunHud, synthesized
//     gunshot/dry/reload sounds. Publishes ShotFired (NPC hearing seam).
// The held gun is a primitive stand-in aimed with the camera each LateUpdate,
// with a light procedural arm pose (FollowMouse's spiritual twin). Real gun
// models + mag/chamber sim arrive with the gun-item port later.
using System.Collections.Generic;
using UnityEngine;
using Game.Core;
using Game.Audio;
using Game.CameraSystem;
using Game.Inventory;
using Game.Movement;
using Game.Vfx;

namespace Game.Combat
{
    public readonly struct ShotFired
    {
        public readonly Vector3 Position;
        public readonly float Loudness;
        public ShotFired(Vector3 position, float loudness)
        {
            Position = position;
            Loudness = loudness;
        }
    }

    public class GunController : MonoBehaviour
    {
        GunDef _def;
        PlayerMotor _motor;
        CameraRig _rig;
        Animator _animator;
        Transform _handBone, _spineBone, _upperArm, _lowerArm, _hand;
        GameObject _gun;
        Transform _muzzle;
        GunHud _hud;

        // Gun drawn (T) — PlayerAnimator swaps the pistol locomotion family
        // on this, and the shoulder camera locks while it's true.
        public bool IsReady => _ready;

        static readonly int PistolHash = Animator.StringToHash("Pistol");
        bool _pistolClipsDriving;   // animator has the pistol param (pack built)

        bool _ready;
        bool _aiming;
        bool _reloading;
        float _reloadT;
        float _nextFire;
        float _liveSpread;   // state.spread — the shared live cone (degrees)
        int _mag;
        float _poseWeight;
        float _targetAccum;
        bool _targetLast;

        InputService _input;
        InventoryService _inv;
        VfxService _vfx;
        AudioService _audio;
        Game.Stats.StatsService _stats;

        void Start()
        {
            _motor = GetComponent<PlayerMotor>();
            _rig = _motor != null ? _motor.cameraRig : null;
            _def = GunCatalog.Get("pistol");
            _mag = _def.magSize;
            _liveSpread = _def.spreadMin;
            _hud = new GunHud(transform);
            BuildGunVisual();
        }

        // Deagle model normalization (Shell9mm-style: measure, don't trust
        // FBX units/axes). Longest mesh axis = barrel → local +Z, middle
        // axis = slide/grip height → +Y, longest dimension scaled to a real
        // Desert Eagle's 0.27 m. Grip fractions seat the trigger area at
        // the HeldGun origin (where the old primitive grip sat), and the
        // muzzle is derived from the actual scaled barrel tip.
        const float DeagleLength = 0.27f;
        const float GripUpFrac = 0.35f;     // grip point: this far up the body
        const float GripFwdFrac = 0.28f;    // …and this far along the length

        void BuildGunVisual()
        {
            _gun = new GameObject("HeldGun");
            _gun.transform.SetParent(transform, false);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color32(52, 52, 58, 255));
            mat.SetFloat("_Metallic", 0.75f);
            mat.SetFloat("_Smoothness", 0.6f);

            var model = Resources.Load<GameObject>("Weapons/Deagle");
            if (model != null) BuildDeagleVisual(model, mat);
            else BuildPrimitiveVisual(mat);
            _gun.SetActive(false);
        }

        void BuildDeagleVisual(GameObject model, Material mat)
        {
            var m = Instantiate(model, _gun.transform);
            m.name = "Deagle";
            m.transform.localPosition = Vector3.zero;
            m.transform.localRotation = Quaternion.identity;
            m.transform.localScale = Vector3.one;
            foreach (var c in m.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var a in m.GetComponentsInChildren<Animator>(true)) Destroy(a);
            foreach (var r in m.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;   // gunmetal everywhere — never
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;   // magenta
                r.sharedMaterials = mats;
            }

            Bounds b = LocalMeshBounds(m.transform);
            Vector3 sz = b.size;
            Vector3 fwd, up;
            if (sz.x >= sz.y && sz.x >= sz.z)
            { fwd = Vector3.right; up = sz.y >= sz.z ? Vector3.up : Vector3.forward; }
            else if (sz.y >= sz.x && sz.y >= sz.z)
            { fwd = Vector3.up; up = sz.x >= sz.z ? Vector3.right : Vector3.forward; }
            else
            { fwd = Vector3.forward; up = sz.y >= sz.x ? Vector3.up : Vector3.right; }

            Quaternion rot = Quaternion.Inverse(Quaternion.LookRotation(fwd, up));
            float longest = Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z));
            float scale = DeagleLength / Mathf.Max(0.0001f, longest);
            m.transform.localRotation = rot;
            m.transform.localScale = Vector3.one * scale;

            Bounds gb = TransformBounds(b, rot, scale);
            Vector3 grip = new Vector3(gb.center.x,
                gb.min.y + gb.size.y * GripUpFrac,
                gb.min.z + gb.size.z * GripFwdFrac);
            m.transform.localPosition = -grip;

            _muzzle = new GameObject("Muzzle").transform;
            _muzzle.SetParent(_gun.transform, false);
            _muzzle.localPosition = new Vector3(0f,
                gb.center.y - grip.y + gb.extents.y * 0.3f,
                gb.max.z - grip.z - 0.005f);
        }

        // The original code-built stand-in — kept as the no-FBX fallback.
        void BuildPrimitiveVisual(Material mat)
        {
            var slide = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(slide.GetComponent<Collider>());
            slide.name = "Slide";
            slide.transform.SetParent(_gun.transform, false);
            slide.transform.localPosition = new Vector3(0f, 0.015f, 0.05f);
            slide.transform.localScale = new Vector3(0.034f, 0.042f, 0.19f);
            slide.GetComponent<Renderer>().sharedMaterial = mat;

            var grip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(grip.GetComponent<Collider>());
            grip.name = "Grip";
            grip.transform.SetParent(_gun.transform, false);
            grip.transform.localPosition = new Vector3(0f, -0.05f, -0.02f);
            grip.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            grip.transform.localScale = new Vector3(0.032f, 0.1f, 0.042f);
            grip.GetComponent<Renderer>().sharedMaterial = mat;

            _muzzle = new GameObject("Muzzle").transform;
            _muzzle.SetParent(_gun.transform, false);
            _muzzle.localPosition = new Vector3(0f, 0.02f, 0.15f);
        }

        // Mesh-data bounds in `root` space — works while inactive, immune
        // to world pose, covers static and skinned meshes alike.
        static Bounds LocalMeshBounds(Transform root)
        {
            bool has = false;
            Bounds acc = default;
            void Add(Mesh mesh, Transform t)
            {
                if (mesh == null) return;
                Bounds mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, Corner(i));
                    Vector3 p = root.InverseTransformPoint(t.TransformPoint(c));
                    if (!has) { acc = new Bounds(p, Vector3.zero); has = true; }
                    else acc.Encapsulate(p);
                }
            }
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                Add(mf.sharedMesh, mf.transform);
            foreach (var sk in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Add(sk.sharedMesh, sk.transform);
            return acc;
        }

        static Bounds TransformBounds(Bounds b, Quaternion rot, float scale)
        {
            Bounds acc = default;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, Corner(i));
                Vector3 p = rot * c * scale;
                if (i == 0) acc = new Bounds(p, Vector3.zero);
                else acc.Encapsulate(p);
            }
            return acc;
        }

        static Vector3 Corner(int i) => new Vector3(
            (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);

        // Ragdoll (or anything else) disabling us must holster cleanly.
        void OnDisable()
        {
            if (_ready) SetReady(false);
            _hud?.SetState(false, false);
            _hud?.SetTargeting(false);
            if (_motor != null) _motor.AimSpeedMult = 1f;
        }

        void FindBones()
        {
            if (_animator != null || Time.frameCount % 10 != 0) return;
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null || !_animator.isHuman) { _animator = null; return; }
            _handBone = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            _spineBone = _animator.GetBoneTransform(HumanBodyBones.Chest);
            _upperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _lowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _hand = _handBone;
            foreach (var p in _animator.parameters)
                if (p.nameHash == PistolHash) { _pistolClipsDriving = true; break; }
        }

        void Update()
        {
            if (_input == null && !Services.TryGet(out _input)) return;
            if (_inv == null) Services.TryGet(out _inv);
            if (_vfx == null) Services.TryGet(out _vfx);
            if (_audio == null) Services.TryGet(out _audio);
            if (_stats == null) Services.TryGet(out _stats);
            if (_def == null || _motor == null) return;
            float dt = Time.deltaTime;
            FindBones();

            // ── Ready toggle (T): needs the EQUIPPED Hand weapon ───────────
            if (_input.ReadyPressed)
            {
                if (_ready) SetReady(false);
                else
                {
                    var held = HeldWeapon();
                    var def = held != null ? GunCatalog.ForItem(held.Def.id) : null;
                    if (def != null) { _def = def; SetReady(true); }
                    else Debug.Log("[Gun] No weapon equipped — Tab → right-click the pistol → Equip");
                }
            }
            if (_ready && HeldWeapon() == null) SetReady(false);   // unequipped mid-wield

            // ── Live aim state: RMB while ready, sprint cancels, re-arms ───
            bool wasAiming = _aiming;
            _aiming = _ready && _rig != null && _rig.AimHeld
                && _motor.CurrentSpeed <= _def.sprintCancelSpeed;
            if (_aiming != wasAiming || !_ready)
                _motor.AimSpeedMult = _aiming ? _def.adsWalkMult : 1f;
            _rig?.SetGunAiming(_aiming);   // AIM_ZOOM boom pull-in

            // ── Live spread (the Heartbeat block, verbatim): movement sets
            // the target floor — above 14 st/s the run tier, above 1 st/s the
            // walk tier — penalties apply INSTANTLY (snap up to target) while
            // recovery decays at SPREAD_DECAY. Shots kick it in Fire().
            float moveSpread = 0f;
            if (_motor.CurrentSpeed > _def.moveRunSpeed) moveSpread = _def.spreadRun;
            else if (_motor.CurrentSpeed > _def.moveWalkSpeed) moveSpread = _def.spreadWalk;
            float targetSpread = _def.spreadMin + moveSpread;
            _liveSpread = _liveSpread > targetSpread
                ? Mathf.Max(targetSpread, _liveSpread - _def.spreadDecay * dt)
                : targetSpread;

            if (_reloading)
            {
                _reloadT -= dt;
                if (_reloadT <= 0f)
                {
                    _reloading = false;
                    _mag = _def.magSize;
                    PlayLocal(ProceduralAudio.ReloadClack(), 0.35f, 1.15f);
                }
            }
            else if (_ready && _input.ReloadPressed && _mag < _def.magSize)
            {
                _reloading = true;
                _reloadT = _def.reloadTime;
                PlayLocal(ProceduralAudio.ReloadClack(), 0.4f, 1f);
            }

            if (_ready && !_reloading && _input.FirePressed)
            {
                if (_mag <= 0) PlayLocal(ProceduralAudio.DryClick(), 0.35f, 1f);
                else if (Time.time >= _nextFire) Fire();
            }

            // ── HUD (Roblox visibility rules: modal UI hides everything) ───
            _hud.Tick(dt);
            bool uiBlocked = _input.GameplayBlocked;
            bool equipped = HeldWeapon() != null;
            _hud.SetState(_ready && !uiBlocked, equipped && !_ready && !uiBlocked);
            if (_ready && !uiBlocked)
            {
                _hud.SetAmmo(_mag, _def.magSize, _reloading);
                _hud.SetSpreadDeg(EffectiveSpreadDeg());
                _targetAccum += dt;                    // CROSS_TARGET_INTERVAL
                if (_targetAccum >= 0.04f)
                {
                    _targetAccum = 0f;
                    _targetLast = IsTargetAtCenter();
                }
                _hud.SetTargeting(_targetLast);
            }
            else
            {
                _targetAccum = 0f;
                _targetLast = false;
                _hud.SetTargeting(false);   // never leave the tint stuck on
            }
        }

        void SetReady(bool ready)
        {
            _ready = ready;
            _gun.SetActive(ready);
            _rig?.SetGunUp(ready);   // lock/release the shoulder camera
            if (!ready)
            {
                _aiming = false;
                _motor.AimSpeedMult = 1f;
            }
        }

        // Full cone (crosshair-facing): hip multipliers, no distance scale.
        float EffectiveSpreadDeg()
        {
            float spread = _liveSpread;   // ADS shows the raw live cone
            if (!_aiming) spread = spread * _def.hipSpreadMul + _def.hipSpreadAdd;
            if (_stats != null)                       // Accuracy skill: up to −15%
                spread *= 1f - 0.15f * _stats.Accuracy / 100f;
            return spread;
        }

        // isTargetUnderCursor, verbatim: true when the center ray rests on a
        // LIVING target (corpses fail the health check and don't light it).
        bool IsTargetAtCenter()
        {
            var cam = Camera.main;
            if (cam == null) return false;
            if (!CastSkippingSelf(cam.transform.position, cam.transform.forward, out var hit))
                return false;
            var hb = hit.collider.GetComponent<BodyHitbox>();
            return hb != null && hb.health != null && !hb.health.IsDead;
        }

        void Fire()
        {
            _nextFire = Time.time + _def.fireInterval;
            _mag--;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 origin = cam.transform.position;
            Vector3 fwd = cam.transform.forward;

            // Clean probe measures the distance the CLOSE_RANGE curve needs.
            float dist = _def.closeFull + 1f;
            if (CastSkippingSelf(origin, fwd, out RaycastHit probe)) dist = probe.distance;

            float spread = EffectiveSpreadDeg() * _def.CloseRangeScale(dist);
            var dir = Quaternion.AngleAxis(Random.Range(0f, 360f), fwd)
                * Quaternion.AngleAxis(Random.Range(0f, spread), Vector3.Cross(fwd, Vector3.up).normalized == Vector3.zero ? Vector3.right : Vector3.Cross(fwd, Vector3.up).normalized)
                * fwd;

            Vector3 muzzlePos = _muzzle != null ? _muzzle.position : origin;
            Vector3 hitPoint = origin + dir * _def.range;

            if (CastSkippingSelf(origin, dir, out RaycastHit hit))
            {
                hitPoint = hit.point;
                var hitbox = hit.collider.GetComponent<BodyHitbox>();
                if (hitbox != null && hitbox.health != null)
                {
                    float dmg = _def.damage * BodyHitbox.RegionMult(hitbox.region);
                    bool wasDead = hitbox.health.IsDead;
                    hitbox.health.ApplyDamage(hitbox.region, dmg, hit.point);
                    _vfx?.BloodMist(hit.point, hit.normal, dmg / 28f);  // auto caliber scale
                    _vfx?.BloodSplatter(hit.point, hit.normal);         // layered cartoony splats
                    if (!wasDead) _hud.Hitmarker(hitbox.health.IsDead);
                    _stats?.OnShot(true);    // gunAccuracy XP: hits weigh full
                }
                else
                {
                    _vfx?.Impact(hit.point, hit.normal, hit.collider.transform);
                    _stats?.OnShot(false);
                }
            }
            else
            {
                _stats?.OnShot(false);
            }

            if (_vfx != null)
            {
                _vfx.Tracer(muzzlePos, hitPoint);
                _vfx.MuzzleFlash(muzzlePos, dir);
                _vfx.EjectShell(muzzlePos - dir * 0.06f, _gun.transform.right, dir);
            }
            _audio?.PlayAt(ProceduralAudio.Gunshot(), muzzlePos, 0.85f,
                Random.Range(0.95f, 1.05f), 70f);
            EventBus.Publish(new ShotFired(muzzlePos, 1f));

            _liveSpread = Mathf.Min(_def.spreadMax, _liveSpread + _def.spreadPerShot);
            float recoilMul = _aiming ? 1f : _def.hipRecoilMul;
            _rig?.AddRecoil(_def.recoilPitch * recoilMul,
                Random.Range(-_def.recoilYaw, _def.recoilYaw) * recoilMul);
        }

        // First hit that isn't us and isn't a body BLOCKER (non-trigger
        // collider under a Health — the hitboxes behind it are the target).
        bool CastSkippingSelf(Vector3 origin, Vector3 dir, out RaycastHit best)
        {
            var hits = Physics.RaycastAll(origin, dir, _def.range, ~0,
                QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.transform.IsChildOf(transform)) continue;
                bool isHitbox = h.collider.GetComponent<BodyHitbox>() != null;
                if (!isHitbox && h.collider.GetComponentInParent<Health>() != null) continue;
                best = h;
                return true;
            }
            best = default;
            return false;
        }

        ItemStack HeldWeapon() =>
            _inv != null && _inv.Player.Equipped.TryGetValue(EquipSlot.Hand, out var s)
                ? s : null;

        void PlayLocal(AudioClip clip, float vol, float pitch) =>
            _audio?.PlayAt(clip, transform.position + Vector3.up * 1.3f, vol, pitch, 12f);

        // ── Held-gun placement + light aim pose (post-animator) ────────────
        void LateUpdate()
        {
            float target = _ready ? 1f : 0f;
            _poseWeight = Mathf.Lerp(_poseWeight, target, 1f - Mathf.Exp(-Time.deltaTime * 8f));
            if (!_ready && _poseWeight < 0.02f) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 fwd = cam.transform.forward;

            if (_animator != null && _upperArm != null && _lowerArm != null && _hand != null)
            {
                if (_spineBone != null)
                {
                    float pitch = -Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
                    _spineBone.rotation = Quaternion.Slerp(Quaternion.identity,
                        Quaternion.AngleAxis(pitch * 0.25f, _spineBone.right), _poseWeight)
                        * _spineBone.rotation;
                }
                // With the pistol pack driving, the clips already hold the
                // gun — the procedural aim drops to a pitch assist instead
                // of wrenching the authored arms toward the camera.
                float armW = _pistolClipsDriving ? 0.35f : 1f;
                AimBone(_upperArm, _lowerArm, fwd, 0.85f * _poseWeight * armW);
                AimBone(_lowerArm, _hand, fwd, 0.9f * _poseWeight * armW);
            }

            if (_gun.activeSelf)
            {
                Vector3 handPos = _hand != null
                    ? _hand.position
                    : transform.position + Vector3.up * 1.35f + transform.forward * 0.3f;
                _gun.transform.SetPositionAndRotation(handPos + fwd * 0.07f,
                    Quaternion.LookRotation(fwd));
            }
        }

        static void AimBone(Transform bone, Transform tip, Vector3 targetDir, float weight)
        {
            Vector3 boneDir = (tip.position - bone.position).normalized;
            if (boneDir.sqrMagnitude < 0.001f) return;
            var correction = Quaternion.FromToRotation(boneDir, targetDir);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, correction, weight)
                * bone.rotation;
        }
    }
}
