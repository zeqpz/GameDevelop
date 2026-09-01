// WorldBuilder — constructs the test world + player + camera + data service
// entirely from code, so there is nothing to hand-assemble and no scene asset
// to keep in sync. Called two ways:
//   • RuntimeBootstrap: automatically on Play when the scene has no [GAME]
//     root — press Play in ANY scene and the baseplate world just exists.
//   • The editor menu (Game ▸ Build Movement Test Scene) for a persistent
//     edit-time copy you can inspect and tweak in the hierarchy.
// Roblox kinship: this is our "Baseplate" — flat ground, a ramp for slopes,
// crates for collision feel, a wall for the camera pullback.
using UnityEngine;
using Game.Movement;
using Game.CameraSystem;
using Game.Data;
using Game.Interaction;
using Game.Audio;
using Game.Inventory;
using Game.Combat;
using Game.Ragdoll;

namespace Game.Core
{
    public static class WorldBuilder
    {
        public const string RootName = "[GAME]";

        public static GameObject Build(MovementSettings settings = null)
        {
            if (settings == null)
                settings = ScriptableObject.CreateInstance<MovementSettings>(); // ported defaults

            var existing = GameObject.Find(RootName);
            if (existing != null) Remove(existing);
            var root = new GameObject(RootName);

            // Park any template camera so ours is the only view + listener.
            var templateCam = Camera.main;
            if (templateCam != null && !templateCam.transform.root.name.Equals(RootName))
                templateCam.gameObject.SetActive(false);

            // ── Services FIRST (composition root: input, events, interaction)
            var servicesGo = new GameObject("Services");
            servicesGo.transform.SetParent(root.transform);
            servicesGo.AddComponent<ServiceHost>();

            // ── Baseplate world ────────────────────────────────────────────
            var baseplate = GameObject.CreatePrimitive(PrimitiveType.Plane);
            baseplate.name = "Baseplate";
            baseplate.transform.SetParent(root.transform);
            baseplate.transform.localScale = new Vector3(20f, 1f, 20f); // 200 × 200 m
            Tint(baseplate, new Color(0.42f, 0.47f, 0.40f));

            MakeBox(root.transform, "Ramp", new Vector3(8f, 0.25f, 0f),
                new Vector3(4f, 0.5f, 8f), Quaternion.Euler(-12f, 0f, 0f),
                new Color(0.55f, 0.50f, 0.45f));
            for (int i = 0; i < 4; i++)
                MakeBox(root.transform, $"Crate{i + 1}",
                    new Vector3(-6f - i * 2.2f, 0.6f, 4f + (i % 2) * 3f),
                    Vector3.one * 1.2f, Quaternion.Euler(0f, i * 25f, 0f),
                    new Color(0.50f, 0.42f, 0.32f));

            // Crate1 proves the pipeline end to end: prompt → E → Interactable
            // event (tint flip + scrap loot) → EventBus log → inventory dump.
            var pokeCrate = root.transform.Find("Crate1").gameObject;
            var poke = pokeCrate.AddComponent<Interactable>();
            poke.prompt = "Search crate";
            bool flipped = false;
            poke.Interacted += _ =>
            {
                flipped = !flipped;
                Tint(pokeCrate, flipped
                    ? new Color(0.75f, 0.55f, 0.30f)
                    : new Color(0.50f, 0.42f, 0.32f));
                if (Services.TryGet(out InventoryService inv))
                    inv.GrantPlayer("scrap_metal", 1);
            };
            MakeBox(root.transform, "Wall", new Vector3(0f, 1.5f, 14f),
                new Vector3(10f, 3f, 0.4f), Quaternion.identity,
                new Color(0.60f, 0.60f, 0.62f));

            // Footstep voices (FootstepEmitter raycasts for these tags).
            baseplate.AddComponent<FootstepSurface>().surface = SurfaceType.Grass;
            root.transform.Find("Ramp").gameObject
                .AddComponent<FootstepSurface>().surface = SurfaceType.Concrete;
            root.transform.Find("Wall").gameObject
                .AddComponent<FootstepSurface>().surface = SurfaceType.Concrete;
            for (int i = 1; i <= 4; i++)
                root.transform.Find($"Crate{i}").gameObject
                    .AddComponent<FootstepSurface>().surface = SurfaceType.Wood;

            // Shooting-range dummies (Health + trigger hitboxes on the bones).
            DamageableDummy.Spawn(root.transform, new Vector3(5f, 0f, 9f), 205f);
            DamageableDummy.Spawn(root.transform, new Vector3(-3f, 0f, 11f), 165f);

            // ── Player (kinematic capsule + PlayerMotor) ───────────────────
            var player = new GameObject("Player");
            player.transform.SetParent(root.transform);
            player.transform.position = new Vector3(0f, 1.1f, 0f);

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual";
            visual.transform.SetParent(player.transform, false);
            Remove(visual.GetComponent<Collider>());
            Tint(visual, new Color(0.75f, 0.55f, 0.30f));

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube); // facing cue
            nose.name = "Nose";
            nose.transform.SetParent(player.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.55f, 0.35f);
            nose.transform.localScale = new Vector3(0.18f, 0.18f, 0.35f);
            Remove(nose.GetComponent<Collider>());
            Tint(nose, new Color(0.85f, 0.70f, 0.45f));

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.slopeLimit = 48f;
            cc.stepOffset = 0.35f;

            var motor = player.AddComponent<PlayerMotor>();
            motor.settings = settings;
            player.AddComponent<Health>();           // HP bar + starvation target
            player.AddComponent<PlayerAnimator>();   // X Bot + locomotion blends (capsule fallback)
            player.AddComponent<GunController>();    // T draws (needs pistol in the grid)
            player.AddComponent<RagdollController>(); // falls + X debug knockdown
            if (Application.isPlaying && Services.TryGet(out InteractionService interaction))
                interaction.User = player.transform; // menu-built worlds self-heal in Tick

            // ── Camera rig ─────────────────────────────────────────────────
            var rigGo = new GameObject("CameraRig");
            rigGo.transform.SetParent(root.transform);
            var camGo = new GameObject("GameCamera");
            camGo.transform.SetParent(rigGo.transform);
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";
            var rig = rigGo.AddComponent<CameraRig>();
            rig.target = player.transform;
            rig.cam = cam;
            rig.motor = motor;   // bob + FOV read pace straight off the motor
            motor.cameraRig = rig;

            // ── Data layer ─────────────────────────────────────────────────
            if (SaveService.Instance == null)
            {
                var saveGo = new GameObject("SaveService");
                saveGo.transform.SetParent(root.transform);
                var save = saveGo.AddComponent<SaveService>();
                save.trackPlayer = player.transform;
            }
            else
            {
                SaveService.Instance.trackPlayer = player.transform;
            }

            Debug.Log("[WorldBuilder] World ready — WASD move · Shift sprint · Space jump · " +
                "Ctrl crouch · E interact · Tab inventory · T gun (LMB fire · R reload · " +
                "RMB ADS) · Alt camera · Esc cursor");
            return root;
        }

        // Destroy that works in BOTH modes: Play uses Destroy, the editor
        // menu path (edit mode) must use DestroyImmediate or Unity throws.
        static void Remove(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        static void MakeBox(Transform parent, string name, Vector3 pos, Vector3 size,
            Quaternion rot, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = size;
            Tint(go, color);
        }

        static void Tint(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var mat = new Material(r.sharedMaterial); // URP-safe base color
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            r.sharedMaterial = mat;
        }
    }
}
