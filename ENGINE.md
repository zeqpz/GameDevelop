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
      blends, turn-in-place, crouch, gait-blended jumps
- [x] **Save layer v0** — versioned JSON profile, autosave, migration hook
      (ProfileService-lite; backend swaps in one place)
- [x] **Code-built world bootstrap** — WorldBuilder/RuntimeBootstrap + edit
      menu; Play in any scene produces the test world

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
- [ ] **Ragdolls: port the kinematic Verlet RagdollEngine or use Unity
      physics ragdolls.** House stance says kinematic; Unity's ragdolls are
      cheap but off-philosophy.
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

- [ ] **VFX/pooling foundation** — generic pooler, decal system, and the
      layer/collision matrix policy (FX ignore characters; explicit
      hitbox layers — no invisible-part hacks this time).
- [ ] **Gun core** — raycast PerformCast twin (client+server seam kept),
      spread model with the close-range curve, ready/ADS states, body-region
      hitbox colliders + damage map (pools per region), damage feedback,
      shells/tracers on the pooler.
- [ ] **NPC foundation** — NavMesh (package already in manifest) + patrol
      node graphs, perception stub, and the sim-tick vs render-interpolation
      split (the server-rendered-NPCs-are-choppy lesson, applied local).
      Traffic lane-graph port sits on top.
- [ ] **Raycast car chassis** — kinematic spring chassis from the manual;
      seat-as-marker + hidden driver pattern; ChassisSimState-style
      sim/render separation; engine/fuel/locks are game-layer later.
- [ ] **Ragdoll + fall damage** — per the ragdoll decision; plausibility
      caps on physics damage (anti-spike guard lesson).
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
