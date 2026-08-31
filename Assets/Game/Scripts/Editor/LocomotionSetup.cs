// LocomotionSetup (Editor) — makes the Mixamo locomotion pack a zero-click
// install, same philosophy as the code-built world. Drop the FBXs in
// Assets/Game/Resources/Locomotion and this does the rest (auto-runs after
// script reloads, or Game ▸ Rebuild Locomotion to force):
//
//   1. IMPORT: every FBX becomes a Humanoid rig sharing X Bot's avatar; each
//      clip is renamed after its file, looped (jump excepted) and left
//      IN-PLACE — root rotation/XZ go to root motion, which the Animator
//      never applies: PlayerMotor owns all movement, animation only paints it.
//      Walk-cycle height bob stays in the pose; the jump's rise is baked OUT
//      because the capsule really jumps.
//   2. CONTROLLER: PlayerLocomotion.controller is built in code — a 2D
//      velocity blend in gait units (walk=1, sprint=2; reversed-clip
//      backpedal), a 1D turn-in-place blend over all four turn clips, and an
//      airborne state — driven by PlayerAnimator's MoveX / MoveY / TurnDir /
//      Gait / Grounded.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.EditorTools
{
    [InitializeOnLoad]
    public static class LocomotionSetup
    {
        const string Folder = "Assets/Game/Resources/Locomotion";
        const string CharacterFbx = Folder + "/X Bot.fbx";
        const string ControllerPath = Folder + "/PlayerLocomotion.controller";
        const string ImportTag = "locomotion-import-v3";   // bump → full reimport + controller rebuild

        static readonly string[] LoopedClips =
        {
            "idle", "walking", "running",
            "left strafe walking", "right strafe walking",
            "left strafe", "right strafe",
            "left turn", "right turn", "left turn 90", "right turn 90",
            "crouching idle", "crouched walking",
        };

        static LocomotionSetup()
        {
            EditorApplication.delayCall += AutoRun;
        }

        static void AutoRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(CharacterFbx)) return;   // pack not dropped in yet

            // The FBXs can land in the same refresh that compiled us — wait
            // until the import pipeline actually knows the character file.
            if (AssetImporter.GetAtPath(CharacterFbx) as ModelImporter == null)
            {
                EditorApplication.delayCall += AutoRun;
                return;
            }

            bool needsImport = AllFbxPaths().Any(p =>
                (AssetImporter.GetAtPath(p) as ModelImporter)?.userData != ImportTag);
            bool needsController =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null;
            if (!needsImport && !needsController) return;
            Rebuild();
        }

        [MenuItem("Game/Rebuild Locomotion (Import + Controller)")]
        public static void Rebuild()
        {
            ConfigureModel(CharacterFbx, null);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(CharacterFbx)
                .OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman)
            {
                Debug.LogError("[LocomotionSetup] X Bot did not import as a Humanoid avatar.");
                return;
            }

            foreach (string path in AllFbxPaths().Where(p => p != CharacterFbx))
                ConfigureModel(path, avatar);

            BuildController();
            AssetDatabase.SaveAssets();
            Debug.Log("[LocomotionSetup] Locomotion pack imported + PlayerLocomotion.controller built.");
        }

        static IEnumerable<string> AllFbxPaths() =>
            Directory.GetFiles(Folder, "*.fbx").Select(p => p.Replace('\\', '/'));

        static void ConfigureModel(string path, Avatar sourceAvatar)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[LocomotionSetup] No importer for {path}");
                return;
            }
            if (importer.userData == ImportTag) return;   // already configured

            importer.animationType = ModelImporterAnimationType.Human;
            importer.materialImportMode = ModelImporterMaterialImportMode.None; // URP-safe default gray

            if (sourceAvatar == null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
            else
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = sourceAvatar;

                string clipName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var clips = importer.defaultClipAnimations;
                foreach (var clip in clips)
                {
                    clip.name = clipName;
                    clip.loopTime = LoopedClips.Contains(clipName);
                    bool isJumpClip = clipName == "jump" || clipName == "jumping";
                    clip.lockRootRotation = false;      // rotation → root motion (discarded)
                    clip.lockRootPositionXZ = false;    // travel → root motion (discarded)
                    clip.lockRootHeightY = !isJumpClip; // bob in pose; jump rises are real
                    clip.keepOriginalOrientation = true;
                    clip.keepOriginalPositionY = true;
                    clip.keepOriginalPositionXZ = true;
                }
                importer.clipAnimations = clips;
            }

            importer.userData = ImportTag;
            importer.SaveAndReimport();
        }

        static AnimationClip Clip(string file)
        {
            string path = $"{Folder}/{file}.fbx";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (clip == null) Debug.LogError($"[LocomotionSetup] No animation clip in {path}");
            return clip;
        }

        static void BuildController()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            controller.AddParameter("Gait", AnimatorControllerParameterType.Float);
            controller.AddParameter("TurnDir", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Crouch", AnimatorControllerParameterType.Bool);

            var sm = controller.layers[0].stateMachine;

            // ── Locomotion: 2D local-velocity blend, gait units (walk=1, sprint=2)
            var moveTree = new BlendTree
            {
                name = "LocomotionTree",
                blendType = BlendTreeType.FreeformDirectional2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY",
            };
            AssetDatabase.AddObjectToAsset(moveTree, controller);
            moveTree.AddChild(Clip("idle"), Vector2.zero);
            moveTree.AddChild(Clip("walking"), new Vector2(0f, 1f));
            moveTree.AddChild(Clip("running"), new Vector2(0f, 2f));
            moveTree.AddChild(Clip("left strafe walking"), new Vector2(-1f, 0f));
            moveTree.AddChild(Clip("right strafe walking"), new Vector2(1f, 0f));
            moveTree.AddChild(Clip("left strafe"), new Vector2(-2f, 0f));
            moveTree.AddChild(Clip("right strafe"), new Vector2(2f, 0f));
            moveTree.AddChild(Clip("walking"), new Vector2(0f, -1f));
            moveTree.AddChild(Clip("running"), new Vector2(0f, -2f));
            var kids = moveTree.children;                 // no backpedal clips in the
            kids[7].timeScale = -1f;                      // pack — walk/run reversed
            kids[8].timeScale = -1f;
            moveTree.children = kids;

            var locomotion = sm.AddState("Locomotion");
            locomotion.motion = moveTree;
            sm.defaultState = locomotion;

            // ── Turn-in-place: 1D over all four turn clips, idle at center
            var turnTree = new BlendTree
            {
                name = "TurnTree",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "TurnDir",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(turnTree, controller);
            turnTree.AddChild(Clip("left turn 90"), -1f);
            turnTree.AddChild(Clip("left turn"), -0.45f);
            turnTree.AddChild(Clip("idle"), 0f);
            turnTree.AddChild(Clip("right turn"), 0.45f);
            turnTree.AddChild(Clip("right turn 90"), 1f);

            var turn = sm.AddState("TurnInPlace");
            turn.motion = turnTree;

            // ── Airborne: standing hop ↔ running leap picked by gait; real
            // height always comes from the capsule
            var airTree = new BlendTree
            {
                name = "AirborneTree",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Gait",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(airTree, controller);
            airTree.AddChild(Clip("jump"), 0.4f);
            airTree.AddChild(Clip("jumping"), 1.3f);

            var air = sm.AddState("Airborne");
            air.motion = airTree;

            // Airborne transitions first so they outrank the ground swaps.
            AddTransition(locomotion, air, 0.06f,
                (AnimatorConditionMode.IfNot, 0f, "Grounded"));
            AddTransition(turn, air, 0.06f,
                (AnimatorConditionMode.IfNot, 0f, "Grounded"));
            AddTransition(air, locomotion, 0.18f,
                (AnimatorConditionMode.If, 0f, "Grounded"));

            // ── Crouch: one-shot enter/exit clips bracket a 1D crouch-gait
            // blend (crouch speed caps gait ≈ 0.5, right at the walk child).
            var crouchTree = new BlendTree
            {
                name = "CrouchTree",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Gait",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(crouchTree, controller);
            crouchTree.AddChild(Clip("crouching idle"), 0f);
            crouchTree.AddChild(Clip("crouched walking"), 0.5f);

            var crouchMove = sm.AddState("CrouchLocomotion");
            crouchMove.motion = crouchTree;
            var toCrouch = sm.AddState("StandToCrouch");
            toCrouch.motion = Clip("standing to crouched");
            var toStand = sm.AddState("CrouchToStand");
            toStand.motion = Clip("crouched to standing");

            AddTransition(locomotion, toCrouch, 0.15f,
                (AnimatorConditionMode.If, 0f, "Crouch"));
            AddTransition(turn, toCrouch, 0.15f,
                (AnimatorConditionMode.If, 0f, "Crouch"));
            AddTransition(crouchMove, toStand, 0.15f,
                (AnimatorConditionMode.IfNot, 0f, "Crouch"));
            AddTransition(crouchMove, air, 0.06f,
                (AnimatorConditionMode.IfNot, 0f, "Grounded"));
            // Quick double-Ctrl must never trap a one-shot mid-clip.
            AddTransition(toCrouch, toStand, 0.15f,
                (AnimatorConditionMode.IfNot, 0f, "Crouch"));
            AddTransition(toStand, toCrouch, 0.15f,
                (AnimatorConditionMode.If, 0f, "Crouch"));
            // One-shots hand off on exit time.
            var settle = toCrouch.AddTransition(crouchMove);
            settle.hasExitTime = true;
            settle.exitTime = 0.75f;
            settle.hasFixedDuration = true;
            settle.duration = 0.25f;
            var rise = toStand.AddTransition(locomotion);
            rise.hasExitTime = true;
            rise.exitTime = 0.7f;
            rise.hasFixedDuration = true;
            rise.duration = 0.25f;

            // Standing + turning → step in place (hysteresis on both sides).
            AddTransition(locomotion, turn, 0.25f,
                (AnimatorConditionMode.Less, 0.15f, "Gait"),
                (AnimatorConditionMode.Greater, 0.22f, "TurnDir"));
            AddTransition(locomotion, turn, 0.25f,
                (AnimatorConditionMode.Less, 0.15f, "Gait"),
                (AnimatorConditionMode.Less, -0.22f, "TurnDir"));
            AddTransition(turn, locomotion, 0.25f,
                (AnimatorConditionMode.Greater, 0.25f, "Gait"));
            AddTransition(turn, locomotion, 0.3f,
                (AnimatorConditionMode.Less, 0.1f, "TurnDir"),
                (AnimatorConditionMode.Greater, -0.1f, "TurnDir"));
        }

        static void AddTransition(AnimatorState from, AnimatorState to, float duration,
            params (AnimatorConditionMode mode, float threshold, string param)[] conditions)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = duration;
            foreach (var c in conditions)
                t.AddCondition(c.mode, c.threshold, c.param);
        }
    }
}
