// RuntimeBootstrap — press Play, get a game. No menus, no scene assembly:
// after any scene loads, if there's no [GAME] root and no PlayerMotor in it,
// the baseplate test world builds itself. Delete this file (or add a [GAME]
// object to the scene) once we move to authored scenes.
using UnityEngine;
using Game.Movement;

namespace Game.Core
{
    public static class RuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBuild()
        {
            if (GameObject.Find(WorldBuilder.RootName) != null) return;
            if (Object.FindAnyObjectByType<PlayerMotor>() != null) return;
            WorldBuilder.Build();
        }
    }
}
