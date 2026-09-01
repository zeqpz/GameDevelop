// CameraRig — Unity twin of Roblox CameraController, now with the full juice
// pass ported over:
//   • Alt cycles Free / Shoulder / FirstPerson; RMB in Free = aim-strafe.
//   • TWEEN SMOOTHING everywhere: the pivot lags the player (SmoothDamp),
//     mode changes BLEND their framing instead of snapping, and wall
//     collision snaps IN (never clip) but relaxes OUT on an ease.
//   • HEAD BOB: two sinusoids at stride frequency, amplitude scales with
//     pace, fades in/out with speed, stronger in first person.
//   • FLICK TILT: throwing the camera fast rolls it a few degrees into the
//     swing (yaw-rate driven), springing back upright.
//   • FOV KICK: sprint widens the lens a touch.
// The body's humanized follow of the camera (dead zone + random hesitation)
// lives in PlayerMotor.AimFollowYaw — the rig just publishes yaw + mode.
using UnityEngine;
using Game.Core;
using Game.Movement;

namespace Game.CameraSystem
{
    public enum CameraMode { Free, Shoulder, FirstPerson }

    public class CameraRig : MonoBehaviour
    {
        [Header("Wiring")]
        public Transform target;          // player root
        public Camera cam;                // child camera
        public PlayerMotor motor;         // for bob / FOV pace (optional but wanted)

        InputService _inputSvc;
        InputService InputSvc =>
            _inputSvc ??= (Services.TryGet(out InputService s) ? s : null);

        [Header("Look")]
        public float lookSensitivity = 0.14f;   // deg per mouse px
        public float pitchMinDeg = -75f;
        public float pitchMaxDeg = 75f;

        [Header("Framing")]
        public float pivotHeight = 1.55f;
        public float crouchPivotHeight = 1.0f;
        public float freeDistance = 5.5f;
        public Vector3 shoulderOffset = new Vector3(0.55f, 0.15f, -2.4f);
        [Tooltip("Shoulder boom while ADS — the AIM_ZOOM pull-in")]
        public Vector3 adsShoulderOffset = new Vector3(0.5f, 0.12f, -1.6f);
        public float collisionRadius = 0.25f;
        public LayerMask collisionMask = ~0;

        [Header("Tween smoothing")]
        [Tooltip("Pivot lags the player by this smoothing time (seconds)")]
        public float pivotSmoothTime = 0.055f;
        [Tooltip("Mode-change framing blend (per second)")]
        public float modeBlendResponse = 9f;
        [Tooltip("How fast the camera relaxes back OUT after a wall pull-in")]
        public float collisionRelaxResponse = 6f;

        [Header("Head bob")]
        public float bobWalkHz = 1.25f;
        public float bobSprintHz = 2.0f;
        public float bobVertAmp = 0.02f;
        public float bobSideAmp = 0.016f;
        public float bobFirstPersonMult = 1.5f;

        [Header("Flick tilt")]
        [Tooltip("Degrees of roll per (deg/sec) of yaw swing")]
        public float tiltPerYawRate = 0.011f;
        public float tiltMaxDeg = 4.5f;
        public float tiltResponse = 8f;

        [Header("FOV")]
        public float baseFov = 60f;
        public float sprintFov = 66f;
        public float fovResponse = 5f;

        public CameraMode Mode { get; private set; } = CameraMode.Free;
        public bool AimHeld { get; private set; }
        public bool BodyFollowsCamera => Mode != CameraMode.Free || AimHeld;
        public float YawDegrees => _yaw;
        public Quaternion YawRotation => Quaternion.Euler(0f, _yaw, 0f);

        float _yaw;
        float _pitch = 8f;
        float _prevYaw;
        float _yawVelSmooth;      // deg/sec, smoothed — drives the flick roll
        float _roll;
        float _recoilPitch;
        float _recoilYaw;
        bool _gunUp;
        bool _gunAiming;
        CameraMode _preGunMode = CameraMode.Free;
        Vector3 _pivotPos;
        Vector3 _pivotVel;
        bool _pivotInit;
        Vector3 _localOffset;     // current blended framing offset
        float _curDist = -1f;     // eased collision distance along the boom
        float _bobClock;
        float _bobAmp;            // smoothed 0..1 bob intensity
        Renderer[] _targetRenderers;

        void Start()
        {
            if (cam == null) cam = GetComponentInChildren<Camera>();
            if (target != null)
            {
                _targetRenderers = target.GetComponentsInChildren<Renderer>();
                _yaw = target.eulerAngles.y;
                _prevYaw = _yaw;
                if (motor == null) motor = target.GetComponent<PlayerMotor>();
            }
            _localOffset = OffsetForMode(Mode);
            if (cam != null) cam.fieldOfView = baseFov;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Update()
        {
            var input = InputSvc;
            if (input == null) return;
            if (input.CameraCyclePressed)
            {
                var next = (CameraMode)(((int)Mode + 1) % 3);
                if (_gunUp && next == CameraMode.Free)   // gun up: Shoulder ↔ FP only
                    next = (CameraMode)(((int)next + 1) % 3);
                SetMode(next);
            }
            if (input.EscapePressed && !input.GameplayBlocked)   // UI owns Esc while open
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = locked;
            }

            AimHeld = input.AimHeld;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = input.LookDelta;
                _yaw += delta.x * lookSensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * lookSensitivity, pitchMinDeg, pitchMaxDeg);
            }
        }

        void LateUpdate()
        {
            if (target == null || cam == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // ── Flick tilt: roll into fast yaw swings, spring back ─────────
            float yawRate = Mathf.DeltaAngle(_prevYaw, _yaw) / dt;
            _prevYaw = _yaw;
            _yawVelSmooth = Mathf.Lerp(_yawVelSmooth, yawRate, 1f - Mathf.Exp(-dt * 10f));
            float targetRoll = Mathf.Clamp(-_yawVelSmooth * tiltPerYawRate, -tiltMaxDeg, tiltMaxDeg);
            _roll = Mathf.Lerp(_roll, targetRoll, 1f - Mathf.Exp(-dt * tiltResponse));

            _recoilPitch = Mathf.Lerp(_recoilPitch, 0f, 1f - Mathf.Exp(-dt * 9f));
            _recoilYaw = Mathf.Lerp(_recoilYaw, 0f, 1f - Mathf.Exp(-dt * 9f));
            Quaternion look = Quaternion.Euler(_pitch - _recoilPitch, _yaw + _recoilYaw, _roll);

            // ── Smoothed pivot (the camera breathes behind the player) ─────
            float pivotH = motor != null
                ? Mathf.Lerp(pivotHeight, crouchPivotHeight, motor.CrouchT)
                : pivotHeight;
            Vector3 pivotTarget = target.position + Vector3.up * pivotH;
            if (!_pivotInit) { _pivotPos = pivotTarget; _pivotInit = true; }
            _pivotPos = Vector3.SmoothDamp(_pivotPos, pivotTarget, ref _pivotVel, pivotSmoothTime);

            // ── Mode framing blends, never snaps ───────────────────────────
            _localOffset = Vector3.Lerp(_localOffset, OffsetForMode(Mode),
                1f - Mathf.Exp(-dt * modeBlendResponse));

            // ── Boom + wall collision (snap in, ease out) ──────────────────
            Vector3 pos;
            Vector3 boom = look * _localOffset;
            float boomLen = boom.magnitude;
            if (boomLen > 0.05f)
            {
                Vector3 dir = boom / boomLen;
                float allowed = boomLen;
                if (Physics.SphereCast(_pivotPos, collisionRadius, dir,
                        out RaycastHit hit, boomLen, collisionMask, QueryTriggerInteraction.Ignore)
                    && !hit.transform.IsChildOf(target))
                    allowed = Mathf.Max(hit.distance - 0.05f, 0.3f);

                if (_curDist < 0f) _curDist = allowed;
                _curDist = allowed < _curDist
                    ? allowed                                            // wall: snap in
                    : Mathf.Lerp(_curDist, allowed,                      // clear: ease out
                        1f - Mathf.Exp(-dt * collisionRelaxResponse));
                pos = _pivotPos + dir * _curDist;
            }
            else
            {
                pos = _pivotPos + boom;   // first person: no boom, no collision
                _curDist = -1f;
            }

            // ── Head bob: stride-frequency sway, pace-scaled, speed-faded ──
            float pace = motor != null ? motor.PaceFrac : 0f;
            bool stepping = motor != null && motor.IsGrounded && motor.CurrentSpeed > 0.3f;
            _bobAmp = Mathf.Lerp(_bobAmp, stepping ? pace : 0f, 1f - Mathf.Exp(-dt * 4.5f));
            if (_bobAmp > 0.01f)
            {
                _bobClock += dt * Mathf.Lerp(bobWalkHz, bobSprintHz, pace) * Mathf.PI * 2f;
                float mult = Mode == CameraMode.FirstPerson ? bobFirstPersonMult : 1f;
                float sway = _bobAmp * mult;
                pos += look * new Vector3(
                    Mathf.Sin(_bobClock) * bobSideAmp * sway,
                    Mathf.Sin(_bobClock * 2f) * bobVertAmp * sway,
                    0f);
            }

            cam.transform.SetPositionAndRotation(pos, look);

            // ── Sprint FOV kick ────────────────────────────────────────────
            float targetFov = Mathf.Lerp(baseFov, sprintFov,
                motor != null ? motor.SprintT * pace : 0f);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov,
                1f - Mathf.Exp(-dt * fovResponse));
        }

        Vector3 OffsetForMode(CameraMode mode) => mode switch
        {
            CameraMode.Shoulder => _gunAiming ? adsShoulderOffset : shoulderOffset,
            CameraMode.FirstPerson => Vector3.zero,
            _ => new Vector3(0f, 0f, -freeDistance),
        };

        // Roblox CameraAim contract: raising the gun ("ready") locks the
        // shoulder frame — remember where the player was, force Shoulder;
        // lowering restores Free only if they didn't re-frame while up.
        // ADS ("aim") pulls the boom to adsShoulderOffset via the usual
        // mode-blend tween.
        public void SetGunUp(bool up)
        {
            if (up == _gunUp) return;
            _gunUp = up;
            if (up)
            {
                _preGunMode = Mode;
                if (Mode == CameraMode.Free) SetMode(CameraMode.Shoulder);
            }
            else
            {
                _gunAiming = false;
                if (Mode == CameraMode.Shoulder && _preGunMode == CameraMode.Free)
                    SetMode(CameraMode.Free);
            }
        }

        public void SetGunAiming(bool aiming) => _gunAiming = aiming;

        // Gun kick: pitch lifts, yaw jitters, both spring back in LateUpdate.
        public void AddRecoil(float pitch, float yaw)
        {
            _recoilPitch += pitch;
            _recoilYaw += yaw;
        }

        // The player's visual hierarchy can change after Start (the animated
        // character spawns in) — re-cache so first-person hiding covers every
        // renderer.
        public void RefreshTargetRenderers()
        {
            if (target == null) return;
            _targetRenderers = target.GetComponentsInChildren<Renderer>(true);
            bool show = Mode != CameraMode.FirstPerson;
            foreach (var r in _targetRenderers)
                if (r != null) r.enabled = show;
        }

        void SetMode(CameraMode mode)
        {
            Mode = mode;
            bool show = mode != CameraMode.FirstPerson;
            if (_targetRenderers != null)
                foreach (var r in _targetRenderers)
                    if (r != null) r.enabled = show;
        }
    }
}
