// FootstepSurface — tag world geometry with what it sounds like underfoot.
// FootstepEmitter raycasts down and asks the hit collider (or any parent)
// for one of these; no tag = Concrete. WorldBuilder tags the test world.
using UnityEngine;

namespace Game.Audio
{
    public enum SurfaceType { Grass, Concrete, Wood, Metal }

    public class FootstepSurface : MonoBehaviour
    {
        public SurfaceType surface = SurfaceType.Concrete;
    }
}
