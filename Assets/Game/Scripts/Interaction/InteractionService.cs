// InteractionService — LOS-gated [E] prompts, straight port of the Roblox
// interaction rules: candidates within THEIR OWN reach of the player, inside
// the camera's view cone, and not hidden behind opaque geometry (per-object
// ignoreLOS opt-out). Best candidate gets a code-built uGUI prompt pinned
// over it; E fires the Interactable and publishes InteractionPerformed on
// the EventBus (the seam bounties/audit/UI — and later the server — hook).
// Pumped by ServiceHost.Update; owns no scene refs beyond a host transform
// for its UI, and finds the player lazily so menu-built worlds heal.
using UnityEngine;
using UnityEngine.UI;
using Game.Core;
using Game.Movement;

namespace Game.Interaction
{
    public readonly struct InteractionPerformed
    {
        public readonly GameObject Target;
        public readonly GameObject User;
        public InteractionPerformed(GameObject target, GameObject user)
        {
            Target = target;
            User = user;
        }
    }

    public class InteractionService
    {
        const float ViewConeDeg = 40f;

        readonly Transform _host;   // UI parents here, dies with [GAME]
        Canvas _canvas;
        RectTransform _promptRect;
        Image _promptBg;
        Text _promptText;

        public Transform User { get; set; }
        public Interactable Current { get; private set; }

        public InteractionService(Transform host) { _host = host; }

        public void Tick()
        {
            if (User == null)
                User = Object.FindAnyObjectByType<PlayerMotor>()?.transform;
            var cam = Camera.main;
            if (User == null || cam == null) { ShowPrompt(null, cam); return; }

            Current = PickBest(cam);
            ShowPrompt(Current, cam);

            if (Current != null && Services.TryGet(out InputService input)
                && input.InteractPressed)
            {
                Current.Fire(User.gameObject);
                EventBus.Publish(new InteractionPerformed(Current.gameObject, User.gameObject));
            }
        }

        static Vector3 CenterOf(Interactable it) =>
            it.TryGetComponent(out Collider col) ? col.bounds.center : it.transform.position;

        Interactable PickBest(Camera cam)
        {
            Interactable best = null;
            float bestScore = float.MaxValue;
            Vector3 camPos = cam.transform.position;
            Vector3 camFwd = cam.transform.forward;

            foreach (var it in Interactable.Registry)
            {
                if (it == null || !it.isActiveAndEnabled) continue;
                Vector3 center = CenterOf(it);
                float dist = Vector3.Distance(User.position, center);
                if (dist > it.maxDistance) continue;

                float angle = Vector3.Angle(camFwd, center - camPos);
                if (angle > ViewConeDeg) continue;

                // Opaque geometry blocks the prompt (Roblox LOS rule); hitting
                // the target itself or the player counts as clear.
                if (!it.ignoreLOS
                    && Physics.Linecast(camPos, center, out RaycastHit hit,
                        ~0, QueryTriggerInteraction.Ignore)
                    && !hit.transform.IsChildOf(it.transform)
                    && !hit.transform.IsChildOf(User))
                    continue;

                float score = angle + dist * 2f;   // aim wins ties, closeness helps
                if (score < bestScore) { bestScore = score; best = it; }
            }
            return best;
        }

        // ── Prompt UI (code-built uGUI, per the locked UI decision) ────────
        void EnsureUi()
        {
            if (_canvas != null) return;
            var go = new GameObject("InteractionPrompt");
            go.transform.SetParent(_host, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 500;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(go.transform, false);
            _promptBg = bgGo.AddComponent<Image>();
            _promptBg.color = new Color(0.07f, 0.08f, 0.07f, 0.62f);
            _promptRect = bgGo.GetComponent<RectTransform>();

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(bgGo.transform, false);
            _promptText = textGo.AddComponent<Text>();
            _promptText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _promptText.fontSize = 24;
            _promptText.alignment = TextAnchor.MiddleCenter;
            _promptText.color = new Color(0.95f, 0.94f, 0.90f);
            var tr = textGo.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
        }

        void ShowPrompt(Interactable target, Camera cam)
        {
            if (target == null || cam == null)
            {
                if (_canvas != null) _canvas.enabled = false;
                return;
            }
            EnsureUi();
            Vector3 screen = cam.WorldToScreenPoint(CenterOf(target) + Vector3.up * 0.35f);
            if (screen.z < 0f) { _canvas.enabled = false; return; }

            _canvas.enabled = true;
            _promptText.text = $"[E] {target.prompt}";
            _promptRect.sizeDelta = new Vector2(_promptText.preferredWidth + 30f, 40f);
            _promptRect.position = screen;
        }
    }
}
