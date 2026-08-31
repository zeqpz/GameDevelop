// FootstepEmitter — the feet-crossing detector from the roadmap. Reads the
// humanoid's actual foot bones each LateUpdate (post-animator): when the
// feet swap front/back in body space while grounded and moving, that's a
// stride beat — play a step at the leading foot, voiced by the surface
// underneath (FootstepSurface tag), volume scaled by pace and hushed by
// crouch (stealth reads honest). Landings after real airtime thump harder.
// Every step also goes on the EventBus as FootstepSounded — the hook NPC
// hearing/perception will use later.
using UnityEngine;
using Game.Core;
using Game.Movement;

namespace Game.Audio
{
    public readonly struct FootstepSounded
    {
        public readonly Vector3 Position;
        public readonly float Loudness;
        public FootstepSounded(Vector3 position, float loudness)
        {
            Position = position;
            Loudness = loudness;
        }
    }

    public class FootstepEmitter : MonoBehaviour
    {
        const float MinStepInterval = 0.16f;
        const float MinSpeed = 0.35f;

        Animator _anim;
        PlayerMotor _motor;
        Transform _leftFoot, _rightFoot;
        int _lastSign;
        float _lastStepTime;
        float _spawnTime;
        bool _wasGrounded = true;

        public void Init(Animator anim, PlayerMotor motor)
        {
            _anim = anim;
            _motor = motor;
            _leftFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rightFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot);
            _spawnTime = Time.time;
        }

        void LateUpdate()   // foot bones are final only after the animator ran
        {
            if (_motor == null || _leftFoot == null || _rightFoot == null) return;

            bool grounded = _motor.IsGrounded;
            if (grounded && !_wasGrounded && Time.time - _spawnTime > 0.5f)
                Step(_rightFoot, landing: true);
            _wasGrounded = grounded;

            if (!grounded || _motor.CurrentSpeed < MinSpeed) { _lastSign = 0; return; }

            float leftZ = transform.InverseTransformPoint(_leftFoot.position).z;
            float rightZ = transform.InverseTransformPoint(_rightFoot.position).z;
            int sign = leftZ > rightZ ? 1 : -1;
            if (_lastSign == 0) { _lastSign = sign; return; }
            if (sign == _lastSign || Time.time - _lastStepTime < MinStepInterval) return;

            _lastSign = sign;
            Step(sign > 0 ? _leftFoot : _rightFoot, landing: false);
        }

        void Step(Transform foot, bool landing)
        {
            _lastStepTime = Time.time;

            var surface = SurfaceType.Concrete;
            if (Physics.Raycast(foot.position + Vector3.up * 0.3f, Vector3.down,
                    out RaycastHit hit, 1.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                var tag = hit.collider.GetComponentInParent<FootstepSurface>();
                if (tag != null) surface = tag.surface;
            }

            float pace = _motor.PaceFrac;
            float vol = landing
                ? 0.9f
                : Mathf.Lerp(0.25f, 0.8f, pace) * Mathf.Lerp(1f, 0.35f, _motor.CrouchT);
            float pitch = (landing ? 0.8f : 1f) * Random.Range(0.92f, 1.08f);

            if (Services.TryGet(out AudioService audio))
                audio.PlayAt(ProceduralAudio.RandomStep(surface), foot.position, vol, pitch);
            EventBus.Publish(new FootstepSounded(foot.position, vol));
        }
    }
}
