# Robloxia → Unity Port

Unity 6 (6000.5.10f1, URP) rebuild of the core systems from our Roblox RP game
("Robloxia", placeId 94968917760097). This file is the cross-device context:
any Claude Code session cloning this repo should read it and self-orient.

## What this project is

A greenfield vertical slice porting the *feel* of the Roblox build — not the
Lua code. Reference docs: the "Raycast Chassis Manual" artifact on claude.ai
and the Roblox project's Claude memory (car system, movement, camera, etc.).
All tuned Roblox numbers convert studs→meters at the edge via
`GameUnits.StudsToMeters = 0.28` and only ever exist in meters at runtime.

## Current systems (Assets/Game/Scripts/)

- **Core/MovementSettings.cs** — ScriptableObject twin of Roblox
  MovementModule.Config; defaults ARE the shipped Roblox tuning, converted.
- **Movement/PlayerMotor.cs** — kinematic CharacterController locomotion:
  momentum with pivot-shed reversals, sprint-scaled overstep glide, gait
  wander, turn penalty, landing stagger, delayed pace-capped body turn toward
  travel; humanized over-shoulder camera follow (dead zone + random
  hesitation + re-rolled ease per engagement).
- **CameraSystem/CameraRig.cs** — Alt cycles Free / Shoulder / FirstPerson;
  RMB in Free = aim-strafe (body follows camera, A/D sidestep). SmoothDamp'd
  pivot, mode-blend tweening, snap-in/ease-out wall collision, stride-locked
  head bob, yaw-rate flick roll, sprint FOV kick.
- **Data/SaveService.cs** — ProfileService-lite: versioned JSON profile in
  persistentDataPath, 30s autosave, quit flush, migration hook. Swap the
  backend here only.
- **Core/WorldBuilder.cs + RuntimeBootstrap.cs** — the world is built from
  code. Pressing Play in any scene with no `[GAME]` root constructs the
  baseplate test world (ground, ramp, crates, wall, player, camera, save).
  `Game ▸ Build Movement Test Scene` makes a persistent edit-time copy and
  the tunable `Assets/Game/Config/MovementSettings.asset`.

## Conventions

- **Physics stance is kinematic** — we drive the player (and later, cars)
  explicitly, same philosophy as the Roblox build. No rigidbody forces
  steering gameplay-critical motion.
- Feel numbers live in settings objects, never inline. When porting a Roblox
  system, carry its tuned values through GameUnits and name the Roblox
  source in a header comment.
- Input is the new Input System, polled via `Keyboard.current` /
  `Mouse.current` — no .inputactions asset yet.
- MCP for Unity (`com.coplaydev.unity-mcp`, in Packages/manifest.json) gives
  Claude Code editor access — on a fresh machine: install `uv`
  (https://astral.sh/uv), open the project, Window ▸ MCP for Unity ▸
  Configure All Detected Clients, restart the Claude session.

## Controls (test world)

WASD move · Shift sprint · Space jump · RMB aim-strafe · Alt camera mode ·
Esc toggles cursor lock.

## Next planned ports

Footstep system (feet-crossing detector), the raycast car chassis, then a
networking decision (NGO vs Photon Fusion) before any multiplayer code.
