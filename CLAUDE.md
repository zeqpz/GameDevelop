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
  + 17-clip locomotion pack in Assets/Game/Resources/Locomotion, plus the
  20-clip pistol pack in Locomotion/Pistol: a full-body armed state family
  ("Pistol" bool ← GunController.IsReady, i.e. gun DRAWN via T) — 2D blend
  with REAL backpedal clips, strafe handedness + jump variants auto-sorted
  by clip.averageSpeed root motion, pistol jumps, kneel = armed crouch
  (idle only, no kneel-walk clip yet; arcs imported unused). While pistol
  clips drive, GunController's procedural arm aim drops to ×0.35 pitch
  assist so it doesn't fight the authored hold.
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
  icon rig. UiFx = the Roblox UIFx module ported (SCALE-only pops — never
  size, so drag math stays honest; Back/Out 0.14 in, Quad/In 0.10 out,
  generation-guarded; menu 0.90→1→0.92, ghost 0.78→1→0.62 + one-way fade,
  Snap for screen-close paths) + the verbatim drag-ghost dangle spring
  (stiffness 180, decay 12 ≈ ζ0.45, ±14° lean, dt clamped 1/30) + UI sounds:
  zip open/close (pitch 1.05/0.88), pickup tick, category-pitched thud on
  rotate/place (weapon 0.85 · material 0.80 · consumable 1.1 · container
  0.95, InventoryClient's PlaybackSpeed offsets).
- **Vfx/** — Pool (generic pooler) + VfxService: tracer/flash/impact/decal
  ring (64 cap, parented to surfaces)/blood mist/shell brass. Shells are the
  Roblox system: ANCHORED kinematic parabola, per-step raycast bounce,
  ShellConfig.LAUNCH tune via GameUnits; brass = a PRELOADED 24-shell
  per-player ring reused oldest-first — settled casings stay on the ground
  (no timers) until their slot recycles. Casing model =
  Resources/Vfx/Shell9mm.obj (Studio export), long axis normalized → Z.
  BLOOD (spawnBloodSplatter/Mist port): layered cylinder splats — big
  centre 0.9–1.5 st in a tight cone along the shot, medium ring, outer
  dots — each disc rolling ITS OWN red (95–145,0,0 — the coloring effect),
  popping open 0.10 s Quad-Out, 400-cap oldest-evicted. Placement rules:
  rays that miss place NOTHING (no sky blood), Rigidbody/Health hits
  reject + re-roll on a count×3 attempt budget (no splats on bodies that
  later move), fit-to-face clamp on BoxColliders (never overhangs crate
  lips; <0.1 st slivers re-roll), light mode = ground-only bleed pattern.
  Mist is damage-scaled (dmg/28, clamp 0.55–1.8, ±18% jitter) with the
  fast-in/slow-out alpha curve. COLLISION POLICY: FX carry no
  colliders; body hitboxes are TRIGGERS (only gun casts query triggers —
  camera/footsteps/interaction/crouch all pass Ignore).
- **Combat/** — GunData (ported numbers: HIP_FIRE ×3 +1° / recoil ×1.15,
  CLOSE_RANGE 4/6/14 st curve, ADS walk ×0.55, sprint-cancel 8.5 st/s,
  pistol 28 dmg, head ×2) + GunController (T ready / RMB ADS / LMB semi;
  T locks the shoulder camera — Alt skips Free while the gun's up, RMB
  pulls the AIM_ZOOM boom, lowering restores the prior mode —
  clean-probe distance scaling, the verbatim live-spread machine (0.3°
  floor · +1.5°/shot cap 8° · 5°/s decay · movement tiers +1.5° walking /
  +3° past 14 st/s, snap-up/ease-down — the crosshair breathes with it),
  camera recoil, held-gun primitive
  aimed per-frame + light procedural arm pose, GunHud crosshair-by-cone +
  hitmarker + ammo, ShotFired bus event) + Health/BodyHitbox +
  DamageableDummy range targets (bone trigger hitboxes, tip-over death,
  3 s respawn). Weapons equip to Hand while KEEPING their grid cell (green
  stroke + E badge; ctx menu toggles Equip/Unequip) — T draws only the
  equipped Hand weapon; clothing equips still detach from the grid.
- **Stats/** — the StatService/SurvivalService port. Five skills 0–100
  (MAX_*=100): Agility ← meters sprinted, Accuracy ← the gunAccuracy shot
  accumulator (hits ×4 vs misses), Strength ← moving under load,
  Intelligence ← AddIntelligence (crafting later), Reputation ← kills route
  here (+1 per kill; earnings later). Effects: Accuracy trims gun spread up
  to −15%, Strength eases carry penalties, Agility trims sprint drain up to
  −25%. Vitals: hunger 0.05/s / thirst 0.07/s (shipped Roblox rates) with
  the ≤30 SELF-double-drain rule (the old cross-double was a port error);
  stamina drains on sprint ×LoadConfig.StaminaDrainMult, regens after
  1.2 s, gates sprint via PlayerMotor.SprintBlocked (block ≤4, free ≥18).
  Starvation (SurvivalService rule): at 0 hunger/thirst the player's
  Health (WorldBuilder adds one) takes 2/3 dmg per second; death respawns
  at spawn with vitals maxed. Player deaths never award Reputation.
  LoadConfig = the shared carry curve (0.4%/lb speed, floor 35%,
  +0.6%/lb drain cap 2.5×, Strength halves both) — StatsService applies it
  to ExternalSpeedMult every tick (replaced the interim inventory curve).
  Persisted in profile v3. UI/StatsScreen = the /stats panel on P (SKILLS
  blue bars / VITALS green / MONEY / RECORD / LICENSES; read-only, blocks
  gameplay). No 'aim' stat — v2 accuracy supersedes it, per the Roblox note.
- **World/TimeService.cs** — the Roblox TimeService port: TIME_SCALE 36
  (40 real min = 1 game day), 6 AM start, date rolls at 6 AM (kept quirk),
  compressed 3-day months / 36-day years from Sep 1 2026, seasons picking
  sunrise/sunset, and the shipped hour-by-hour lighting curve driving a
  code-owned sun (which doubles as the moon on the night arc) + flat
  ambient, eased ~2 s per hour mark. Elapsed game time persists (profile
  v4) — unlike Roblox's reboot-to-day-one. ForceTime(hour) = debug jump.
  Weather/wind/rain/lightning/season VISUALS are a later port.
- **UI/SurvivalHud.cs** — the Roblox SurvivalHUD transcribed 1:1 from the
  StarterGui.HUD.SurvivalHUD template: bottom-left 232×132 SurvivalBars
  panel (HP / FOOD / H₂O / STA rows — 12px color chip, GothamBold tag,
  13px bar with gloss strip, right value) + DateTimeFrame 232×24 above it
  ("6:00 AM  |  Sep 1, 2026" from TimeService). SurvivalClient juice
  ported: 0.35s quad fill tweens, <25% low-color swap, number
  flash/pop/shake per displayed-int change, tip particle bursts (0.3s
  throttle). Hides while Gameplay is blocked (the Roblox modal convention).
  Replaced StatsScreen's interim bottom-center stamina bar.
- **Ragdoll/** — the Roblox kinematic Verlet RagdollEngine ported (15
  particles + Jakobsen sticks + fold-limit inequality sticks, swept
  raycasts, RESTITUTION 0.05 / FRICTION 0.4, the drift-pump fix — collide
  from v0, position-only cleanup pass — per-particle sleep JITTER_EPS 2.0,
  sphere self-collide 0.7/0.5 + CAPSULE_COLLIDE segment pairs at half
  strength (limbs never pass through flesh), core-down settle/freeze
  rules, fixed 60 Hz WITH render interpolation between steps — and Muscle
  stays 0 while down: any idle-pose tug reads as twitch) + ACTIVE-ragdoll
  muscles: particles chase the live Animator pose ×Muscle (1 = animated,
  0 = limp; the get-up IS the ramp). RagdollController drives bones in
  world space post-animator, root follows hips (camera tracks the flop),
  colliders/motor/gun disabled while down, slam damage (cap 40, 0.8 s cd)
  + body-drop thud from ConsumeImpact. Triggers: sky-fall (≥3.5 st below
  takeoff, mid-air), X debug shove (24 st/s at head height — the
  zero-momentum statue fix), dummy deaths (launched along the killing
  shot, StayDown till respawn). Deferred: death topple, trip-from-sprint,
  alive-mode behaviors, corpse persistence.
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
T draw gun · LMB fire · R reload · RMB ADS (also aim-strafe) ·
P stats panel · X debug ragdoll · Alt camera mode · Esc toggles cursor lock.

## Engine roadmap

ENGINE.md (repo root) is the living base-engine checklist: what's done, the
open decisions (scenes, ragdolls, LFS — uGUI and Photon Fusion 2 are locked),
and the tiered build order. Update it as pieces land.

## Next planned ports

Footstep system (feet-crossing detector), the raycast car chassis, then a
networking decision (NGO vs Photon Fusion) before any multiplayer code.
