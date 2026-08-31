// PlayerAnimator — skins the code-built player with the Mixamo X Bot and
// drives the locomotion blends off PlayerMotor's REAL state, so every piece
// of the momentum feel reads in the animation for free:
//
//   • MoveX/MoveY = the capsule's actual local velocity in gait units
//     (walk=1, sprint=2). The smoothed momentum vector, pivot sheds, turn
//     penalty, overstep glide and gait wander all pass straight through —
//     free-look rides the forward lane (the body chases travel), aim-strafe
//     lights up the strafe clips, backpedal plays walk/run reversed.
//   • TurnDir = the body's smoothed yaw RATE, so the delayed body turn and
//     the humanized aim-follow produce real turn-in-place footsteps.
//   • Grounded gates the airborne state; the jump clip's rise was baked out
//     on import because the capsule genuinely jumps.
//
// Loads everything from Resources/Locomotion (imported + built by
// LocomotionSetup). If the pack isn't there yet it keeps the capsule visual
// and bows out quietly. The Animator never applies root motion.
using UnityEngine;
using Game.Audio;

namespace Game.Movement
{
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerAnimator : MonoBehaviour
    {
        const string ModelResource = "Locomotion/X Bot";
        const string ControllerResource = "Locomotion/PlayerLocomotion";

        static readonly int MoveXHash = Animator.StringToHash("MoveX");
        static readonly int MoveYHash = Animator.StringToHash("MoveY");
        static readonly int GaitHash = Animator.StringToHash("Gait");
        static readonly int TurnDirHash = Animator.StringToHash("TurnDir");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int CrouchHash = Animator.StringToHash("Crouch");

        [Tooltip("Body yaw rate (deg/s) that reads as a full-speed in-place turn")]
        public float fullTurnYawRate = 120f;

        PlayerMotor _motor;
        CharacterController _cc;
        Animator _animator;
        float _prevYaw;
        float _turnDir;   // smoothed yaw rate, -1..1

        void Start()
        {
            _motor = GetComponent<PlayerMotor>();
            _cc = GetComponent<CharacterController>();

            var model = Resources.Load<GameObject>(ModelResource);
            var controller = Resources.Load<RuntimeAnimatorController>(ControllerResource);
            if (model == null || controller == null)
            {
                Debug.LogWarning("[PlayerAnimator] Locomotion pack not in Resources/Locomotion " +
                    "yet — keeping the capsule visual.");
                enabled = false;
                return;
            }

            var visual = Instantiate(model, transform);
            visual.name = "CharacterVisual";
            float feetY = _cc.center.y - _cc.height * 0.5f;   // X Bot pivots at the feet
            visual.transform.localPosition = new Vector3(0f, feetY, 0f);
            visual.transform.localRotation = Quaternion.identity;
            FitToCapsule(visual);

            _animator = visual.GetComponent<Animator>();
            if (_animator == null) _animator = visual.AddComponent<Animator>();
            _animator.runtimeAnimatorController = controller;
            _animator.applyRootMotion = false;   // PlayerMotor owns ALL motion
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Retire the placeholder capsule + facing nose.
            SetInactive("Visual");
            SetInactive("Nose");

            // First-person hiding caches renderers at camera Start — re-cache
            // now that the real character exists.
            if (_motor.cameraRig != null) _motor.cameraRig.RefreshTargetRenderers();

            // Feet-crossing footsteps read the humanoid foot bones.
            gameObject.AddComponent<FootstepEmitter>().Init(_animator, _motor);

            _prevYaw = transform.eulerAngles.y;
        }

        void SetInactive(string childName)
        {
            var t = transform.Find(childName);
            if (t != null) t.gameObject.SetActive(false);
        }

        // X Bot stands ~1.87 m — match him to whatever the capsule says.
        void FitToCapsule(GameObject visual)
        {
            var rends = visual.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (rends.Length == 0) return;
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            if (b.size.y < 0.5f || b.size.y > 5f) return;   // bounds not trustworthy
            float scale = Mathf.Clamp(_cc.height / b.size.y, 0.5f, 1.5f);
            visual.transform.localScale = Vector3.one * scale;
        }

        void Update()
        {
            if (_animator == null || _motor.settings == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var s = _motor.settings;

            // ── Local horizontal velocity → gait-normalized blend coords ───
            Vector3 v = _cc.velocity;
            v.y = 0f;
            Vector3 local = transform.InverseTransformDirection(v);
            float speed = local.magnitude;
            float gait = speed <= s.walkSpeed
                ? speed / Mathf.Max(0.01f, s.walkSpeed)
                : 1f + (speed - s.walkSpeed)
                    / Mathf.Max(0.01f, s.sprintSpeed - s.walkSpeed);
            gait = Mathf.Min(gait, 2.3f);   // wander breathe can peek past sprint
            Vector3 dir = speed > 0.05f ? local / speed : Vector3.zero;

            _animator.SetFloat(MoveXHash, dir.x * gait, 0.08f, dt);
            _animator.SetFloat(MoveYHash, dir.z * gait, 0.08f, dt);
            _animator.SetFloat(GaitHash, gait, 0.1f, dt);

            // ── Body yaw rate → turn-in-place direction ────────────────────
            float yaw = transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(_prevYaw, yaw) / dt;
            _prevYaw = yaw;
            float target = Mathf.Clamp(yawRate / fullTurnYawRate, -1f, 1f);
            _turnDir = Mathf.Lerp(_turnDir, target, 1f - Mathf.Exp(-dt * 8f));
            _animator.SetFloat(TurnDirHash, _turnDir);

            _animator.SetBool(GroundedHash, _motor.IsGrounded);
            _animator.SetBool(CrouchHash, _motor.IsCrouched);
        }
    }
}
