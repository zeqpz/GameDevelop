// PlayerMotor — Unity twin of Roblox MovementController + the momentum
// override render-step, ported system-for-system with the shipped tuning:
//
//   • MOMENTUM: the character runs on a smoothed vector, not raw input.
//     Direction flicks swing through the arc, hard reversals shed momentum
//     (the pivot beat), and releasing input keeps the body gliding while it
//     decays — a couple of steps from a walk, a real GTA overstep out of a
//     sprint (speed decel softens to match during the glide).
//   • GAIT WANDER: a slow sinusoid bends the heading and breathes the pace,
//     so a full sprint never tracks a laser line.
//   • TURN PENALTY: sharp input changes scrub speed, decaying back.
//   • DELAYED BODY TURN: in free-look the body eases onto the travel heading,
//     rate-capped and lazier at pace (sprint reversals arc wide). When the
//     camera says BodyFollowsCamera (shoulder / first-person / RMB aim) the
//     body tracks camera yaw instead and A/D become sidesteps.
//   • JUMP/LANDING: reduced jump, air-speed multiplier, landing stagger after
//     real drops only.
//   • CROUCH: Ctrl toggles the stance — capsule shrinks (feet pinned), speed
//     halves, sprint disarms; Space or Ctrl stands up (clearance-checked).
//   • BACKPEDAL: target speed falls away as travel points behind the body —
//     walking or running backwards is far slower than forwards.
//
// Physics stance matches the Roblox build: KINEMATIC. A CharacterController
// capsule we drive explicitly — no rigidbody forces steering the player.
using UnityEngine;
using Game.Core;
using Game.CameraSystem;

namespace Game.Movement
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        public MovementSettings settings;
        public CameraRig cameraRig;

        InputService _inputSvc;
        InputService InputSvc =>
            _inputSvc ??= (Services.TryGet(out InputService s) ? s : null);

        public float CurrentSpeed => _speed;
        public float SprintT => _sprintT;
        public Vector3 Momentum => _momentum;
        public bool IsGrounded => _grounded;
        public bool IsCrouched => _crouched;
        public float CrouchT => _crouchT;
        // Systems (carry weight, status effects…) scale target speed here.
        public float ExternalSpeedMult { get; set; } = 1f;
        public float PaceFrac => settings != null
            ? Mathf.Clamp01(_speed / settings.sprintSpeed) : 0f;

        CharacterController _cc;
        Vector3 _momentum;           // smoothed world-space move vector, magnitude 0..1
        Vector3 _prevInputDir;
        float _speed;                // ramped horizontal speed (m/s)
        float _sprintT;              // 0..1 sprint ramp
        float _overrunT;             // sprint ramp captured at release — scales the glide
        float _turnPenalty;
        float _wanderClock;
        float _verticalVel;
        float _landTimer;
        float _airborneSince = -1f;
        bool _grounded;
        bool _crouched;
        float _crouchT;              // 0..1 smoothed stance blend
        float _standHeight;
        float _standCenterY;

        // Over-shoulder body-follow state machine (idle → hesitating → following)
        enum AimFollow { Idle, Hesitating, Following }
        AimFollow _aimState = AimFollow.Idle;
        float _aimTimer;         // hesitation countdown
        float _aimResponse;      // this engagement's rolled ease speed

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _standHeight = _cc.height;
            _standCenterY = _cc.center.y;
            if (settings == null)
                Debug.LogError("[PlayerMotor] No MovementSettings assigned.");
            _wanderClock = Random.value * 10f;
        }

        void Update()
        {
            if (settings == null) return;
            float dt = Time.deltaTime;

            // ── Raw input (InputService; camera-relative like Roblox) ──────
            var input = InputSvc;
            Vector2 wasd = input != null ? input.Move : Vector2.zero;
            bool wantSprint = input != null && input.SprintHeld;
            bool wantJump = input != null && input.JumpPressed;
            Vector3 inputDir = Vector3.zero;
            if (wasd.sqrMagnitude > 0.01f)
            {
                Quaternion camYaw = cameraRig != null ? cameraRig.YawRotation : Quaternion.identity;
                inputDir = (camYaw * new Vector3(wasd.x, 0f, wasd.y)).normalized;
            }
            bool hasInput = inputDir.sqrMagnitude > 0.01f;

            // ── Crouch stance (Ctrl toggles; Space stands up instead of jumping)
            bool toggleCrouch = input != null && input.CrouchPressed;
            if (toggleCrouch && _grounded)
            {
                if (!_crouched) _crouched = true;
                else if (CanStand()) _crouched = false;
            }
            if (wantJump && _crouched)
            {
                if (CanStand()) _crouched = false;
                wantJump = false;
            }
            _crouchT = Mathf.Lerp(_crouchT, _crouched ? 1f : 0f,
                1f - Mathf.Exp(-dt * settings.crouchResponse));
            if (_crouchT < 0.001f) _crouchT = 0f;
            float capsuleH = Mathf.Lerp(_standHeight, settings.crouchHeight, _crouchT);
            _cc.height = capsuleH;
            _cc.center = new Vector3(0f,
                _standCenterY - (_standHeight - capsuleH) * 0.5f, 0f); // feet stay put

            // ── Turn penalty (sharp flicks scrub speed) ────────────────────
            if (hasInput && _prevInputDir.sqrMagnitude > 0.01f)
            {
                float turnDeg = Vector3.Angle(_prevInputDir, inputDir);
                if (turnDeg > settings.turnThresholdDeg)
                {
                    float spike = Mathf.Clamp01((turnDeg - settings.turnThresholdDeg)
                        / (180f - settings.turnThresholdDeg)) * settings.turnPenaltyMax;
                    _turnPenalty = Mathf.Max(_turnPenalty, spike);
                }
            }
            if (hasInput) _prevInputDir = inputDir;
            _turnPenalty = Mathf.Max(0f, _turnPenalty - dt * settings.turnPenaltyDecay);

            // ── Sprint ramp ────────────────────────────────────────────────
            bool sprinting = wantSprint && hasInput && _grounded && !_crouched;
            _sprintT = Mathf.Clamp01(_sprintT + (sprinting
                ? dt / settings.sprintRampTime
                : -dt / settings.sprintDownTime));

            // ── Momentum (the GTA core) ────────────────────────────────────
            if (hasInput)
            {
                // Hard reversal sheds momentum: brake through the pivot.
                if (_momentum.sqrMagnitude > 0.0025f
                    && Vector3.Dot(_momentum.normalized, inputDir) < -0.5f)
                    _momentum *= settings.momentumTurnDrag;
                float a = 1f - Mathf.Exp(-dt * settings.momentumAccel);
                _momentum = Vector3.Lerp(_momentum, inputDir, a);
                _overrunT = _sprintT;
            }
            else if (_momentum.sqrMagnitude > 0f)
            {
                // OVERRUN: glide on while the vector decays — sprint earns the
                // slow decay (long overstep), a stroll stops on the spot.
                float decay = Mathf.Lerp(settings.overrunWalkDecay,
                    settings.overrunSprintDecay, _overrunT);
                _momentum *= Mathf.Exp(-dt * decay);
                if (_momentum.magnitude < settings.overrunMin) _momentum = Vector3.zero;
            }

            // ── Target speed + ramp ────────────────────────────────────────
            float baseTarget = 0f;
            if (hasInput || _momentum.sqrMagnitude > 0.001f)
            {
                baseTarget = Mathf.Lerp(settings.walkSpeed, settings.sprintSpeed, _sprintT);
                baseTarget *= 1f - _turnPenalty;
                if (!_grounded) baseTarget *= settings.airSpeedMult;
                if (_landTimer > 0f)
                {
                    float t = 1f - Mathf.Clamp01(_landTimer / settings.landPenaltyTime);
                    baseTarget *= Mathf.Lerp(settings.landPenaltyStart, 1f, t);
                }
                baseTarget *= ExternalSpeedMult;
                baseTarget *= Mathf.Lerp(1f, settings.crouchSpeedMult, _crouchT);
                // Backpedal honesty: speed falls away the further travel
                // points behind the body (aim-mode backpedal; free-look
                // pivots dip through it, deepening the pivot beat).
                if (_momentum.sqrMagnitude > 0.01f)
                {
                    float back = Mathf.Clamp01(
                        -Vector3.Dot(_momentum.normalized, transform.forward));
                    baseTarget *= Mathf.Lerp(1f, settings.backwardSpeedMult, back);
                }
                if (!hasInput) baseTarget = 0f; // gliding: momentum carries, speed bleeds
            }
            float rate;
            if (baseTarget > _speed) rate = settings.accelRate;
            else
            {
                rate = settings.decelRate;
                // Overrunning a sprint: brake gently so the run-out reads as
                // decelerating steps, not a sudden stand-still.
                if (!hasInput && _momentum.magnitude > settings.overrunMin)
                    rate = Mathf.Lerp(settings.decelRate, settings.sprintStopDecel, _overrunT);
            }
            _speed = Mathf.Max(0f, Mathf.MoveTowards(_speed, baseTarget, rate * dt));
            if (_landTimer > 0f) _landTimer -= dt;

            // ── Gait wander ────────────────────────────────────────────────
            Vector3 moveVec = _momentum;
            float paceFrac = Mathf.Clamp01(_speed / settings.sprintSpeed);
            if (moveVec.sqrMagnitude > 0.0025f && paceFrac > 0.2f)
            {
                _wanderClock += dt * settings.wanderFrequency * Mathf.PI * 2f;
                float bend = settings.wanderDirDegrees * paceFrac * Mathf.Sin(_wanderClock);
                float breathe = 1f + settings.wanderSpeedFrac * paceFrac
                    * Mathf.Sin(_wanderClock * 1.7f + 1f);
                moveVec = Quaternion.Euler(0f, bend, 0f) * moveVec * breathe;
            }

            // ── Vertical: gravity + jump ───────────────────────────────────
            if (_grounded && _verticalVel < 0f) _verticalVel = -2f; // stick to ground
            if (wantJump && _grounded)
                _verticalVel = Mathf.Sqrt(2f * settings.gravity * settings.jumpHeight);
            _verticalVel -= settings.gravity * dt;

            // ── Move the capsule ───────────────────────────────────────────
            Vector3 velocity = moveVec * _speed + Vector3.up * _verticalVel;
            _cc.Move(velocity * dt);

            bool wasGrounded = _grounded;
            _grounded = _cc.isGrounded;
            if (!_grounded && wasGrounded) _airborneSince = Time.time;
            if (_grounded && !wasGrounded)
            {
                float airtime = _airborneSince >= 0f ? Time.time - _airborneSince : 0f;
                if (airtime >= settings.landMinAirtime) _landTimer = settings.landPenaltyTime;
                _airborneSince = -1f;
            }

            UpdateBodyYaw(dt, paceFrac);
        }

        // Headroom check before standing from crouch: cast the capsule's top
        // sphere upward by the missing height (casts skip colliders they
        // start overlapped with, so our own capsule never blocks it).
        bool CanStand()
        {
            float need = _standHeight - _cc.height;
            if (need <= 0.01f) return true;
            Vector3 top = transform.position + transform.rotation
                * (_cc.center + Vector3.up * (_cc.height * 0.5f - _cc.radius));
            return !Physics.SphereCast(top, _cc.radius * 0.95f, Vector3.up, out _,
                need + 0.05f, ~0, QueryTriggerInteraction.Ignore);
        }

        // Delayed body turn: toward TRAVEL in free-look, toward CAMERA when
        // aiming / shouldered — same split as the Roblox CameraController.
        void UpdateBodyYaw(float dt, float paceFrac)
        {
            float bodyYaw = transform.eulerAngles.y;

            if (cameraRig != null && cameraRig.BodyFollowsCamera)
            {
                AimFollowYaw(dt, bodyYaw);
                return;
            }
            _aimState = AimFollow.Idle;

            if (_momentum.sqrMagnitude <= 0.014f) return; // idle: body stays put
            float targetYaw = Mathf.Atan2(_momentum.x, _momentum.z) * Mathf.Rad2Deg;
            float maxRate = Mathf.Lerp(settings.moveTurnMaxRate,
                settings.moveTurnSprintRate, paceFrac) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(bodyYaw, targetYaw);
            float step = delta * (1f - Mathf.Exp(-dt * settings.moveTurnResponse));
            step = Mathf.Clamp(step, -maxRate * dt, maxRate * dt);
            transform.rotation = Quaternion.Euler(0f, bodyYaw + step, 0f);
        }

        // Over-shoulder follow, humanized: a person doesn't servo-track the
        // camera. Small drift sits in a dead zone (standing), a real swing is
        // chased only after a short RANDOM hesitation, and the chase speed
        // itself is re-rolled per engagement — so no two follows are quite
        // the same. Throwing the camera hard (past aimSnapAngleDeg) skips the
        // hesitation: big turns must never feel laggy.
        void AimFollowYaw(float dt, float bodyYaw)
        {
            float delta = Mathf.DeltaAngle(bodyYaw, cameraRig.YawDegrees);
            float absDelta = Mathf.Abs(delta);
            bool moving = _momentum.sqrMagnitude > 0.01f;
            float deadZone = moving ? 2f : settings.aimDeadZoneDeg;

            switch (_aimState)
            {
                case AimFollow.Idle:
                    if (absDelta > settings.aimSnapAngleDeg)
                    {
                        EngageAimFollow();                      // hard throw: instant
                    }
                    else if (absDelta > deadZone)
                    {
                        _aimState = AimFollow.Hesitating;       // noticed the drift...
                        _aimTimer = Random.Range(settings.aimHesitationMin,
                            settings.aimHesitationMax);
                    }
                    return;

                case AimFollow.Hesitating:
                    if (absDelta > settings.aimSnapAngleDeg) { EngageAimFollow(); break; }
                    if (absDelta < deadZone * 0.5f) { _aimState = AimFollow.Idle; return; }
                    _aimTimer -= dt;
                    if (_aimTimer > 0f) return;                 // ...beat passes...
                    EngageAimFollow();                          // ...then commit
                    break;

                case AimFollow.Following:
                    break;
            }

            // Following: tween-ease onto the camera yaw, disengage when settled.
            float step = delta * (1f - Mathf.Exp(-dt * _aimResponse));
            transform.rotation = Quaternion.Euler(0f, bodyYaw + step, 0f);
            if (absDelta < 1.2f) _aimState = AimFollow.Idle;
        }

        void EngageAimFollow()
        {
            _aimState = AimFollow.Following;
            _aimResponse = Random.Range(settings.aimResponseMin, settings.aimResponseMax);
        }
    }
}
