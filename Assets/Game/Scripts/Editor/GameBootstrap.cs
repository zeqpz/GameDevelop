// GameBootstrap (Editor) — optional edit-time build of the same world the
// RuntimeBootstrap creates on Play, so you can inspect/tweak it in the
// hierarchy outside Play mode. Also owns creating the persistent
// MovementSettings asset (runtime builds use in-memory defaults; this asset
// is the tunable copy the edit-time world wires in).
//
//   Game ▸ Build Movement Test Scene      (top menu bar, next to Window)
using UnityEditor;
using UnityEngine;
using Game.Core;

namespace Game.EditorTools
{
    public static class GameBootstrap
    {
        const string SettingsPath = "Assets/Game/Config/MovementSettings.asset";

        [MenuItem("Game/Build Movement Test Scene")]
        public static void BuildTestScene()
        {
            var settings = AssetDatabase.LoadAssetAtPath<MovementSettings>(SettingsPath);
            if (settings == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Game"))
                    AssetDatabase.CreateFolder("Assets", "Game");
                if (!AssetDatabase.IsValidFolder("Assets/Game/Config"))
                    AssetDatabase.CreateFolder("Assets/Game", "Config");
                settings = ScriptableObject.CreateInstance<MovementSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
            }

            // Edit-time Destroy isn't allowed — clear the old root immediately.
            var old = GameObject.Find(WorldBuilder.RootName);
            if (old != null) Object.DestroyImmediate(old);

            var root = WorldBuilder.Build(settings);
            Selection.activeGameObject = root;
        }
    }
}
