// GameUnits — the bridge between our Roblox tuning and Unity's meters.
//
// Every feel number in this project was tuned for months in the Roblox build
// (see the Raycast Chassis Manual + MovementModule). Roblox works in studs;
// Unity works in meters. Rather than re-tune blind, we convert:
//
//   1 stud = 0.28 m  (a 5-stud character ≈ 1.4 m of torso+legs; capsule 1.8 m)
//
// which lands our speeds on believable human numbers:
//   walk   7 st/s → 1.96 m/s      sprint 16 st/s → 4.48 m/s
//
// Convert AT THE EDGE (settings defaults), never mid-simulation — sim code
// only ever sees meters.
namespace Game.Core
{
    public static class GameUnits
    {
        public const float StudsToMeters = 0.28f;

        public static float Studs(float studs) => studs * StudsToMeters;
    }
}
