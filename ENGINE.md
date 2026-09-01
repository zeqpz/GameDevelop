# Base Engine Checklist — Robloxia → Unity

What the port needs UNDER the game. Everything here is engine: services and
foundations that many Robloxia systems lean on. The 90-odd gameplay systems
from the Roblox build (economy, housing, rice, phone, minigames…) are the
payload — they ride on this and are deliberately not listed.

Convention holds everywhere: tuned Roblox numbers cross through GameUnits at
the edge, feel knobs live in ScriptableObjects, physics stance is kinematic,
worlds/assets build from code where possible.

## Done ✓

- [x] **Units + tuning bridge** — GameUnits (0.28 m/stud), SO-per-system
      settings pattern (MovementSettings carries shipped Roblox values)
- [x] **Kinematic player motor** — momentum/pivot/glide/wander/turn penalty,
      jump + landing stagger, Ctrl crouch stance, angle-based backpedal
- [x] **Camera rig** — Free/Shoulder/FirstPerson, tween smoothing, head bob,
      flick tilt, FOV kick, wall collision, crouch pivot drop
- [x] **Character + animation pipeline** — Mixamo auto-import
      (LocomotionSetup), code-built controller, 17 clips, velocity-driven
      blends, turn-in-place, crouch, gait-blended jumps; + the 20-clip
      pistol pack as a full-body armed family (Pistol bool ← gun drawn):
      real backpedal, auto-sorted strafes/jumps, kneel-as-armed-crouch,
      procedural arm aim reduced to a pitch assist under authored clips
- [x] **Save layer v0** — versioned JSON profile, autosave, migration hook
      (ProfileService-lite; backend swaps in one place)
- [x] **Code-built world bootstrap** — WorldBuilder/RuntimeBootstrap + edit
      menu; Play in any scene produces the test world
- [x] **World clock + day/night** — TimeService: the Roblox port (40-min
      days, compressed 3-day-month calendar from Sep 1 2026, seasonal
      sunrise/sunset, shipped hourly lighting curve → code-owned sun/moon +
      flat ambient), elapsed time persisted in profile v4. Weather/rain/
      lightning visuals are the later half of the port.
- [x] **Survival HUD** — the Roblox SurvivalHUD 1:1 (HP/FOOD/H₂O/STA bars
      + DateTimeFrame clock) with the SurvivalClient juice (fill tweens,
      low-color swap, number flash/shake, tip particles); player Health +
      starvation damage (2/3 HP·s) + spawn-reset respawn close the vitals
      loop. Hunger/thirst drains corrected to shipped rates (≤30
      self-double rule).

## Decisions to lock (they get expensive to reverse)

- [x] **UI stack — LOCKED: uGUI** (2026-08-31). Mature drag-drop for the
      grid inventory, world-space support, no TMP/UITK migration risk. The
      interaction prompt is the first uGUI surface.
- [x] **Netcode — LOCKED: Photon Fusion 2** (2026-08-31). The deciders:
      built-in tick simulation with client prediction, LAG-COMPENSATED
      hitboxes (the gun game needs rewind), and area-of-interest — all
      things NGO would make us hand-roll. Fusion's network-authority model
      also maps onto the Roblox ownership mental model we already know.
      Integration stays Tier 3; revisit only if CCU pricing bites.
- [ ] **Scenes: code-built vs authored.** Test world stays code-built; real
      maps (Maplewood streets, stacked interiors) will want authored
      geometry. Likely hybrid: authored geometry scenes + code-built systems.
- [x] **Ragdolls — LOCKED: the Verlet RagdollEngine, ported + extended
      with pose-matching muscles** (active ragdolls that chase the playing
      animation — GTA-style knockdowns and get-ups). Unity physics ragdolls
      rejected as off-philosophy.
- [ ] **Git LFS** before big art/audio lands (FBXs are fine today).

## Tier 1 — core services (next; nearly everything consumes these)

- [x] **InputService** — code-built actions (no asset), Gameplay + System
      maps, typing gate (`SetGameplayBlocked`), rebind-ready. Motor and
      camera no longer touch Keyboard/Mouse directly — convention now.
- [x] **Service layer + composition root** — `Services` locator +
      `ServiceHost` on [GAME]/Services; explicit construction and tick
      order. (SaveService still standalone; fold in when it grows.)
- [x] **EventBus** — typed struct-only pub/sub with isolated handlers;
      documented as the future Fusion mirror seam. First event:
      InteractionPerformed.
- [x] **InteractionService** — LOS-gated view-cone [E] prompts with
      per-object ignoreLOS, code-built uGUI prompt, Interactable component;
      Crate1 in the test world demos the full pipeline.
- [x] **Footstep system** — feet-crossing detector on the humanoid foot
      bones (post-animator), FootstepSurface voices, pace-scaled volume,
      crouch hush, landing thumps, FootstepSounded on the bus (future NPC
      hearing hook).
- [x] **AudioService** — pooled voices under [GAME]/Services, code-side
      World/Ui/Music buses (real AudioMixer can slot behind the API later);
      placeholder sounds SYNTHESIZED in code (ProceduralAudio) since the
      repo ships no audio assets yet.
- [x] **Item + inventory data core** — ItemDef SOs built by a code
      ItemCatalog, ItemGrid (footprints, rotation, stacking, first-fit),
      Inventory (equip = membership in ONE dict — the three-flag gotcha,
      answered), nested containers, carry weight → motor speed, flat-list
      save in profile v2 (starter kit on fresh saves). Console ASCII dump
      until the UI kit lands.
- [ ] **Debug console + cheats** — "/" command twin (noclip, stats, give,
      teleport), tunable overlays, test toggles (the _G.Aimbot pattern).
      Pays for itself the moment guns/NPCs arrive.

## Tier 2 — simulation stacks

- [x] **VFX/pooling foundation** — generic Pool + VfxService: tracers,
      muzzle flash+light, impact bursts, capped decal ring (parented to hit
      surfaces), blood mist, and the Roblox shell system verbatim (anchored
      parabola, per-step raycast bounces, ShellConfig.LAUNCH numbers).
      Collision policy BY CONSTRUCTION: FX have no colliders; hitboxes are
      triggers; every non-combat query ignores triggers.
- [x] **Gun core** — GunController: Lowered/Ready(T)/ADS(RMB) state machine
      with the ported HIP_FIRE (×3 +1°, recoil ×1.15), CLOSE_RANGE curve
      (dead-on ≤4 st), ADS walk ×0.55 + sprint-cancel at 8.5 st/s, bloom,
      camera recoil, pistol 28 dmg with region mults (head ×2), trigger
      hitboxes + Health on range dummies, hitmarker/crosshair/ammo HUD,
      synthesized gunshot/dry/reload audio, ShotFired on the bus (NPC
      hearing seam). Later: real gun models, mag/chamber sim, ammo items,
      server validation seam.
- [ ] **NPC foundation** — NavMesh (package already in manifest) + patrol
      node graphs, perception stub, and the sim-tick vs render-interpolation
      split (the server-rendered-NPCs-are-choppy lesson, applied local).
      Traffic lane-graph port sits on top.
- [ ] **Raycast car chassis** — kinematic spring chassis from the manual;
      seat-as-marker + hidden driver pattern; ChassisSimState-style
      sim/render separation; engine/fuel/locks are game-layer later.
- [x] **Ragdoll + fall damage** — RagdollEngine (the Roblox Verlet sim,
      verbatim rules: drift-pump fix, position-only cleanup pass,
      per-particle sleep, core-down freeze, 0.05/0.4 rest/friction) +
      RagdollController (Animator-target muscles, mid-air sky-fall trigger
      ≥3.5 st below takeoff, slam damage capped 40 @ 0.8 s cooldown, get-up
      muscle ramp, X debug shove). Dummies die into real ragdolls. Later:
      topple ("dead man standing"), alive-mode hand-cradle/knee-tuck,
      corpse persistence, trip triggers from sprint collisions.
- [ ] **UI kit v0** — IN PROGRESS. Shipped: UiKit (procedural rounded-rect
      UICorner/UIStroke twins, Montserrat-as-Gotham, Roblox TL coordinates,
      hand-rolled hit tests) + InventoryScreen — the Roblox InventoryGui
      transcribed 1:1 from the live Studio template (Win 840×592, 20×20 grid
      at CELL 22 / STEP 23, equip rows, drag + R rotate with COL_VALID /
      COL_INVALID cell tints, right-click ctx menu, Tab toggle). Still to
      come: screen stack, 3D RenderTexture item icons, character preview,
      backpack overlay, tooltips.

## Tier 3 — after the netcode decision

- [ ] **Transport + replication model**, interest management; retrofit the
      sim/render splits as prediction boundaries.
- [ ] **Persistence backend swap** — SaveService seam → server/cloud profile
      store; session/identity plumbing.

## Sequencing note

Tier 1 is ordered roughly by leverage: InputService and the service layer
first (they touch every later file), then Interaction + Footsteps + Audio
(fast wins that make the slice feel like a game), then the inventory data
core and debug console before any Tier 2 stack starts.
