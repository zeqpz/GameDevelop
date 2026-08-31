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
  hesitation + re-rolled ease per engagement); Ctrl-toggle crouch stance
  (capsule shrink with feet pinned, clearance-checked stand, Space stands
  up, camera pivot drops); angle-based backpedal penalty — moving backwards
  is far slower than forwards (backwardSpeedMult).
- **CameraSystem/CameraRig.cs** — Alt cycles Free / Shoulder / FirstPerson;
  RMB in Free = aim-strafe (body follows camera, A/D sidestep). SmoothDamp'd
  pivot, mode-blend tweening, snap-in/ease-out wall collision, stride-locked
  head bob, yaw-rate flick roll, sprint FOV kick.
- **Data/SaveService.cs** — ProfileService-lite: versioned JSON profile in
  persistentDataPath, 30s autosave, quit flush, migration hook. Swap the
  backend here only.
- **Movement/PlayerAnimator.cs + Editor/LocomotionSetup.cs** — Mixamo X Bot
  + 17-clip locomotion pack in Assets/Game/Resources/Locomotion.
  LocomotionSetup auto-runs after reloads (or Game ▸ Rebuild Locomotion):
  imports every FBX as Humanoid on X Bot's avatar (in-place, looped, jump
  rise baked out) and code-builds PlayerLocomotion.controller — 2D velocity
  blend in gait units (walk=1, sprint=2; strafes; reversed-clip backpedal),
  1D turn-in-place blend (all four turn clips), gait-blended airborne
  (standing "jump" hop ↔ running "jumping" leap), and crouch — stand↔crouch
  one-shots bracketing a crouch idle/walk gait blend (Crouch bool).
  PlayerAnimator (added by WorldBuilder) instantiates the model over the
  capsule, feeds MoveX/MoveY/Gait from REAL capsule local velocity and
  TurnDir from body yaw rate — momentum/glide/aim-strafe read through
  automatically. Root motion is never applied; PlayerMotor owns movement.
- **Core/Services.cs + ServiceHost.cs + EventBus.cs + InputService.cs,
  Interaction/** — the service layer. ServiceHost on [GAME]/Services is the
  composition root (explicit construction + tick order); Services is the
  typed locator; EventBus is struct-only pub/sub and the future Fusion
  mirror seam (Remotes twin); InputService builds its actions in code
  (Gameplay + System maps, SetGameplayBlocked typing gate). Interaction:
  Interactable component + InteractionService — LOS-gated view-cone [E]
  prompts (ignoreLOS opt-out), code-built uGUI prompt, publishes
  InteractionPerformed. Crate1 in the test world demos the pipeline.
- **Audio/** — AudioService (pooled voices, code-side World/Ui/Music buses)
  + ProceduralAudio (synthesized placeholder sounds — repo has no audio
  assets) + FootstepEmitter (feet-crossing detector on the humanoid foot
  bones, FootstepSurface voices, crouch hush, landing thumps, publishes
  FootstepSounded for future NPC hearing).
- **Inventory/** — the data core, NO UI yet: ItemCatalog (code-built
  ItemDefs), ItemGrid (footprints/rotation/stacking/first-fit), Inventory
  (equip state = membership in one dict, THE single source of truth),
  nested containers (backpack), carry weight → PlayerMotor.ExternalSpeedMult
  (no penalty under 40% of maxCarryLbs), flat SavedStack list persisted in
  profile v2 via SaveService.OnBeforeSave. InventoryService.DumpToLog()
  prints the grid as ASCII. Starter kit grants on fresh profiles.
- **UI/** — UiKit (procedural rounded-rect sprites = UICorner/UIStroke
  twins, Montserrat = Gotham stand-in in Resources/UI/Fonts, Roblox
  top-left coordinate helpers, hand-rolled hit tests — no EventSystem) +
  InventoryScreen: the Roblox InventoryGui transcribed 1:1 from the live
  template (Win 840×592 · CELL 22 GAP 1 STEP 23 · 20×20 grid · six equip
  rows · drag with R-rotate + green/red cell tints · right-click context
  menu · Tab toggles, InventoryClient:2454). Opening blocks the Gameplay
  map and frees the cursor; Esc closes ctx → drag → screen. Flat def-color
  tiles stand in for the Roblox 3D viewport icons until the RenderTexture
  icon rig.
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
- Input goes through InputService (actions built in code, resolved via
  `Services.TryGet`) — gameplay code NEVER polls `Keyboard.current` /
  `Mouse.current` directly. UI/chat later blocks gameplay input with
  `SetGameplayBlocked`; Escape lives on the System map and always works.
- Locked decisions (2026-08-31): UI is uGUI; netcode is Photon Fusion 2
  (integration is a later tier — see ENGINE.md).
- MCP for Unity (`com.coplaydev.unity-mcp`, in Packages/manifest.json) gives
  Claude Code editor access — on a fresh machine: install `uv`
  (https://astral.sh/uv), open the project, Window ▸ MCP for Unity ▸
  Configure All Detected Clients, restart the Claude session.

## Controls (test world)

WASD move · Shift sprint · Space jump · Ctrl crouch (Space stands up) ·
E interact · Tab inventory (drag items, R rotates, right-click menu) ·
RMB aim-strafe · Alt camera mode · Esc toggles cursor lock.

## Engine roadmap

ENGINE.md (repo root) is the living base-engine checklist: what's done, the
open decisions (scenes, ragdolls, LFS — uGUI and Photon Fusion 2 are locked),
and the tiered build order. Update it as pieces land.

## Next planned ports

Footstep system (feet-crossing detector), the raycast car chassis, then a
networking decision (NGO vs Photon Fusion) before any multiplayer code.
