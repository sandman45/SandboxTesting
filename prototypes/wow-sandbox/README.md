# wow-sandbox

Prototype: pulling World of Warcraft models (M2/WMO) into Unity via `wow.export` → glTF (M2) / OBJ (WMO) → `wow.unity`, for personal, non-commercial sandbox use. Background/rationale/engine comparison lives in [`docs/wow-model-research.md`](../../docs/wow-model-research.md).

## Prerequisites

| Tool | Status | Notes |
|---|---|---|
| WoW retail client | ✅ Already installed | `C:\Program Files (x86)\World of Warcraft\_retail_` |
| wow.export (portable) | ✅ Already downloaded | `C:\Users\sandm\Tools\wow.export\wow.export.exe` — portable build, no install needed |
| Unity Hub + Editor | ✅ Installed | Unity 6000.5.7f1, URP 3D template |
| wow.unity package | ✅ Installed | `wow-export-unityifier.briochie` via git URL (`Packages/manifest.json`) |

## Setup checklist

1. **Install Unity Hub**, sign in, install **Unity 6000.x LTS** with the **URP (Universal Render Pipeline)** 3D core template.
2. In Unity Hub, **create a new 3D (URP) project at this exact path**: `prototypes/wow-sandbox/` (reuse this folder — don't create a new one elsewhere).
3. In the Unity Editor: **Window → Package Manager → `+` → "Install package from git URL"**, paste:
   ```
   https://github.com/briochie/wow.unity.git
   ```
4. Launch `C:\Users\sandm\Tools\wow.export\wow.export.exe`. Point it at the retail install (`_retail_`) and let it index CASC storage (first index takes a few minutes).
5. **First test export** — pick something simple to validate the pipeline before anything complex:
   - Browse Creature or Doodad models, choose a small single-piece model (avoid complex layered gear/armor sets for the first try).
   - Export selected model as **glTF**, with **textures** and **animation** included (glTF is correct for M2 — see the format section below).
   - Export destination: `prototypes/wow-sandbox/Assets/WowExports/` (inside `Assets/` so Unity/`wow.unity` picks it up automatically; already gitignored — raw WoW assets never get committed). This is configured in wow.export's own settings, so exports land here without a manual move.
   - Try a small WMO (e.g. a simple building interior) as a second test, to validate the WMO group/texture path separately from M2 — but export that one as **OBJ**, not glTF.
6. Back in Unity, bring the exported glTF + textures into the project (drag into `Assets/`, or use wow.unity's importer per its README). Confirm:
   - Mesh renders with correct materials/textures.
   - If the model had animation data, it plays correctly in a bare test scene.

## Status

✅ **Pipeline validated end-to-end**, both asset types:
- **M2 (creature)**: `chicken2` (white) — mesh, textures, skeleton, and animation all confirmed working in a bare scene.
- **M2 (humanoid)**: `humanmalewarriorlight` and `humanthief` — both imported and placed in a scene, textures and animations confirmed good. Humanoid rigs carry ~40 animation clips each.
- **WMO (static world object)**: `gnomehut` — mesh + multi-texture materials confirmed working (glTF, shell only).

⚙️ **wow.export's output directory is now set to `Assets/WowExports/`**, so exports land in the project automatically — no manual move step.

🎮 **Playable world:** a warrior you control on procedurally generated terrain, under raymarched clouds and fog; a gnome hut with collision you can walk inside; lakes you can swim in, with an underwater view; and wandering chickens, warriors and thieves routing around it all on a baked NavMesh. Every part is rebuildable from the `WoW Sandbox` menu, so the scene itself is disposable.

Since then, as of 2026-10-12:
- **HUD and D&D-style combat:** stats, inventory, loot and maps.
- **Sea creatures and a deadly open sea:** sharks, and the Ancient Leviathan, which rises at every storm's peak.
- **Storms:** recorded rain.
- **The pirate ship:** climb aboard by rope ladders and sail it from the helm.
- **Maps:** a zoomable world map of the whole sea.
- **Edge-of-world fog** in any weather.
- **A vendor** who sells gear and healing potions and buys your loot.

As of 2026-10-13:
- **Sea life:** fish schools in the shallows.
- **Fishing:** fishing spots with a cast bar, splashes and a fishing animation, and rarer fish further from shore.
- **Combat:** thieves that fight, Rare loot drops back on.
- **Scenery:** a shipwreck, mixed chicken colours, palms swaying in the wind.
- **Menus:** a title screen, a settings menu on Esc, and developer options.

As of 2026-10-15:
- **A WoW character as the player,** dressed in whatever's equipped: painted-on cloth, armour, helms, weapons and shields.
- **Fishing poles:** fishing needs one, and it shows in his hand.
- **Weapon-aware combat animations:** per-weapon swings, crits, fighting stances, parries, blocks and dodges.
- **Warrior abilities** on an action bar: Heroic Strike, Shield Bash, Whirlwind and Charge.
- **Action bar:** clickable, with cooldowns, tooltips and red when unusable; keys 1 Attack, 2–5 abilities, 6 Potion. `Tab` targets the nearest enemy.
- **More armour:** shoulders (Leather Spaulders, Iron Pauldrons) and legs (Leather Leggings, Iron Legguards), with built-in fits for oversized pieces.
- **Rope ladders** draped over the ship's hull in WoW's rope-ladder art, swaying at the foot.
- **The island dressed:** beach sand at the waterline, scattered palms, bushes, rocks and driftwood, and a dense grass carpet.
- **Looting kneels**, as in WoW.
- **Treasure chests and items on the ground** to open and pick up, with good odds of rare, epic and legendary loot; the Developer panel spawns any exported chest.

As of 2026-10-20:
- **A shipwreck opening:** you start washed up and asleep on the beach in the storm that wrecked you, with nothing, and find a map in the wreck.
- **Day and night:** a 24-minute day with sunrises, sunsets, stars, a moon and a clock on the minimap.
- **Lanterns and torches** that light up at night, and ones to carry in the off hand.

As of 2026-10-21:
- **Night life:** NPCs and Briggs sleep, thieves keep a night watch by the lanterns, chickens roost, and night fish bite.
- **The story begins:** the lore of Tidegrave Isle, a Journal (`J`) of pages found around the island, and a hut and bed for Briggs.
- **Dialogue:** talk to Briggs in a box with a live portrait, and hear the Captain's own thoughts as subtitles.
- **Darker nights**, and the wreck's storm held for two minutes after you start.
- **Weather:** raymarched clouds, sea fog and dawn mist, wind in the trees, grass and sea, and a weather system that changes the sky on its own and sometimes brings a storm.
- **Saving:** the whole game, autosaved, with Continue and New Game on the title screen.

As of 2026-10-22:
- **A survival start:** he washes up starving and too weak to leave the beach or swim. Gather driftwood and flint, build a campfire, cook a fish, eat, and sit by the fire (`X`) to get his strength back. Then a gentle hunger bar. A cooked meal heals him to full health. No eating in combat or while swimming.
- **Campfires and cooking** anywhere on open ground, with WoW's burning, cooking and cold campfire models.
- **Sharks hunt the fish schools**, and fishing spots have tooltips.
- **The old WoW sky dome is gone**, replaced by the raymarched clouds.

✅ **Doodad chain verified end-to-end** on `md_pirateship` exported as OBJ: wow.export writes `pirateship_ModelPlacementInformation.csv` plus each doodad's OBJ, and wow.unity rebuilds `pirateship.prefab` with all 36 doodads (cannons, lanterns, hammocks, anchor) in place. Drag the **`.prefab`**, not the `.obj`. The traps on the way are listed under "Export format" below.

## Features we can add

Ideas outside the story's build plan (that lives in [docs/LORE.md](docs/LORE.md), "Build plan by act"). 🎨 means it needs a model export first.

- **Friendly ship crew:** bearded pirates working the wheel, oars and cannon crank (`WABoatWheelStand`, rowing and cannon-crank clips). Overlaps Act IV's crew aboard.
- **Combat follow-ups:** a dazed animation for stunned enemies (`EmoteStunNoSheathe`), rage for the warrior abilities, and smarter `Tab` targeting that checks facing and line of sight.
- 🎨 **Ground models for every pickup:** body armour has none yet (sack, pouch or armour stand-ins).
- **Starting gear placed in the world**, now that he washes up with nothing.
- **A falling animation** when walking off a ledge (the export's `Fall` clip, ID 40).
- **Turning in place**, so he doesn't pivot on planted feet (the export has no turn clips).
- **Cloaks**, if a character model that can wear one turns up.
- **Save the rest:** breath, a lit torch, vendor stock and loot left on corpses aren't saved yet.
- **Journal polish:** a font per hand and parchment art (details in LORE.md).
- 🎨 **A bed aboard the ship:** a bunk or hammock below deck to rest at, opening the same rest window as a campfire (`RestPanel`) without needing a fire. A campfire can't be built on a ship.

### Sound effects and voice lines

All that plays now is the music (`MusicPlayer`) and the storm (`StormAudio`: rain, thunder, the leviathan). WoW's own sounds export from wow.export's Sounds tab and stay gitignored under `Assets/WowExports/sound/`, as the music does.

**Sound effects**
- [ ] **Wind** that follows the weather system's wind, not only the storm's peak
- [ ] **Sea and shore:** surf on the beach, waves against the hull, the ship's timbers creaking
- [ ] **Footsteps** by ground: sand, grass, stone, wooden deck, shallow water; landing from a jump
- [ ] **Swimming:** strokes, diving in, surfacing, the muffled sound underwater, gasping when out of breath
- [ ] **Combat:** swings by weapon (fists, dagger, one-hander, two-hander), hits, misses, crits, block, parry, dodge
- [ ] **Abilities:** each warrior ability's own sound
- [ ] **Weapons away and out:** sheathing and drawing (Z), by back, hip and shield
- [ ] **Creatures:** chickens, sharks, the island's wildlife; their hurt and death sounds
- [ ] **Fishing:** the cast, the bobber's splash, a bite, reeling in
- [ ] **Camp:** striking flint, the campfire's crackle, cooking, eating and drinking
- [ ] **World objects:** chests opening, picking things up, climbing ladders, the gate, doors
- [ ] **Interface:** opening and closing windows, equipping, moving items, coins, buying and selling, loot, the error buzz, level up, journal pages turning
- [ ] **Day and night:** birds by day, insects and frogs at night, lanterns lighting

**Voice lines**
- [ ] **The Captain in combat:** grunts as he swings, pain when hit, a death cry
- [ ] **The Captain's errors**, spoken as WoW's are: "I can't carry any more", "I don't have a weapon", "I can't do that here", "Not yet"
- [ ] **His thoughts** spoken aloud: waking on the beach, starving, finding a journal page
- [ ] **NPC greetings and goodbyes:** the vendor, Briggs and anyone else with dialogue
- [ ] **Dialogue lines** spoken alongside the dialogue window's text
- [ ] **Enemies:** aggro shouts, and lines from the Thief Leader and Act II's other named foes

## Playable sandbox

Two editor menu items rebuild the whole playable scene from scratch, so nothing needs hand-wiring in the Inspector:

| Menu item | What it does |
|---|---|
| **WoW Sandbox → Spawn Warrior Player** | Builds an AnimatorController from the clips of the player's model (`character/thinhuman/base/thinhumanmale.gltf`, a human male character export with nothing equipped), then assembles the rig: capsule sized from the model's real bounds, Animator + Avatar wired, camera hooked up, movement speeds scaled to the model's height, and the model child rotated +90° (see the facing gotcha below). Re-running replaces the player already in the scene, on the same spot. |
| **WoW Sandbox → Harvest Gear** | Builds the player's gear looks from the character exports under `character/thinhuman/` (see *Gear on the player*) into `_Generated/Gear/GearLibrary.asset`, and hands the library to the player in the scene. Re-run after adding a piece export. |
| **WoW Sandbox → Generate Terrain** | Procedural Unity Terrain with layered Perlin noise. Flattens a pad at the origin and drops the player onto it. Self-contained — the ground texture is generated in code, so there are no external asset dependencies. |
| **WoW Sandbox → Fix Terrain Gloss** | Zeroes the albedo alpha on terrain layers in place. Fixes glass-looking ground without regenerating (which would orphan the baked NavMesh). |
| **WoW Sandbox → Paint Beach Sand** | Paints sand onto the terrain in place: everything under the water and a band above it (*Beach height*, 1.2), fading into the ground above over *Blend* with a ragged edge (*Raggedness*). Higher ground keeps its own paint. Generated sand to match the seabed, or an exported WoW ground texture (copied with its alpha cleared, so it isn't glossy). Re-run to change the band; Ctrl+Z undoes it. |
| **WoW Sandbox → Scatter Vegetation** | Scatters every exported plant and rock it recognises by name (see *Vegetation*), and paints a grass carpet. Re-running replaces only what it placed. Re-bake the NavMesh after. |
| **WoW Sandbox → Add Mesh Colliders to Selection** | Adds MeshColliders across a whole hierarchy — a WMO is many group meshes, not one. Non-convex, so you can walk *into* buildings. Skips skinned meshes, and skips foliage geosets so trees collide on the trunk rather than on their leaf cards. |
| **WoW Sandbox → Add Colliders to All Scenery** | Same, swept across the whole scene instead of the selection — the step that actually gets missed after placing a batch of doodads. Idempotent. Skips the water surface and anything belonging to a character. |
| **WoW Sandbox → Bake NavMesh** | Creates/updates a NavMeshSurface and bakes. Re-run after moving buildings or regenerating terrain. **This does not stop the player** — see below. |
| **WoW Sandbox → Populate Chickens** | Scatters wandering chickens around the selection, snapped to the NavMesh, in a random mix of every `chicken2_*` colour exported. |
| **WoW Sandbox → Spawn Wandering NPCs** | Same, for any M2 glTF you drag in — sized from the model's own bounds. |
| **WoW Sandbox → Setup Sky and Sun** | Procedural sky plus a matching directional light; ambient comes from the sky. Also forces the camera's Clear Flags to Skybox, without which the sky never draws at all. |
| **WoW Sandbox → Set View Distance** | Sets far clip and fog range together — they're coupled and can't be set independently. Opens on the scene's current values. ~900 works well: `StormWeather`'s edge fog closes in only as you near the edge of the sea (*Edge Fog Reach*), so the water's edge and the seabed never show, and a clear-weather horizon haze blends the sea into the sky. |
| **WoW Sandbox → Setup Water** | Floods the terrain to a sea level given as a fraction of its height. Builds the wave mesh, generates the ripple normal maps in code, adds the `WaterVolume` the swim code reads, puts `UnderwaterEffect` on the camera, adds `WaterWaves`, and marks the submerged terrain unwalkable. Re-bake the NavMesh afterwards. Opens on the existing water's sea level and overhang, and a rebuild keeps the old `WaterWaves` settings — so it can resize the sea (*Overhang*: area grows with its square) without moving the water out from under the ship or resetting tuned waves. Anything else parented under `WaterSurface` is moved out before the old water is deleted — a rebuild once took StormWeather down with it. |
| **WoW Sandbox → Add Tree Sway** | Gives every standing palm in the scene a `TreeSway` (it skips the fallen trunk and the log). Re-run after placing more palms. |
| **WoW Sandbox → Add Storm Weather** | Adds a `StormWeather` that drives the waves, darkening cloud, failing light, closing fog, rain, wind and lightning from one intensity. Finds the sun, sky, water and camera itself at Play time, so there's nothing to wire. |
| **WoW Sandbox → Fix OBJ Textures in Selection** | Assigns the textures Unity's OBJ importer missed on WMO exports, by reading the `.mtl` itself. Run it once on any building or ship exported as OBJ that comes in untextured. **Not yet tried** — the pirate ship was fixed by writing the texture references into its `.mat` files directly, before anyone ran the tool. |
| **WoW Sandbox → Add Water Dry Zone to Selection** | Slices the selected ship's hull at the waterline and cuts that shape out of the water, so it doesn't show inside the hull and you don't swim in the hold. Re-run it (on the ship) after moving the ship up or down. |
| **WoW Sandbox → Add Music Player** | Fills a `MusicPlayer` playlist from `Assets/WowExports/sound/music/` (gitignored — WoW music is Blizzard content like the models) and sets each track to stream. It plays shuffled in Play mode, each track fading in and out over *Crossfade Seconds*. Re-run after adding tracks. |
| **WoW Sandbox → Spawn Sea Creatures** | Places Reef, Hunter or Giant sharks (`SwimmingCreature`) in a ring offshore. They roam a depth band, keep clear of the seabed and shore, and hostile ones hunt swimmers. |
| **WoW Sandbox → Generate Seabed** | Builds the sea floor beyond the terrain's edge (`Seabed.HeightAt` is the sea-floor query everything uses). |
| **WoW Sandbox → Setup Deep Sea Danger** | Gives the player `DeepSeaDanger` and places the hidden Ancient Leviathan, with its cast and submerge animations. Re-run after pulling changes to the Leviathan's animator. |
| **WoW Sandbox → Add Ship Ladders** | Hangs four rope ladders (`ShipLadder`) on the floating ship, just forward of midship (clear of the open gun port) and aft, on both sides. Raycasts find the rail, the deck and the hull's shape, and each ladder is draped over that shape (see *Rope ladders*). Needs the `ropeladder01` export for the real look; without it, it falls back to plain primitive ropes and rungs. **Re-running replaces every ladder, including hand-moved ones.** |
| **WoW Sandbox → Add Ship Helm** | Adds `ShipSailing` to the ship and an invisible click box (`ShipHelm`) to drag over the wheel, which is part of the hull mesh. The box's forward (yellow line) must point to the bow. Its `Stand` child is where you stand to steer. |
| **WoW Sandbox → Add Sea Life** | Fills the shallows with fish schools (`fishbasic01`, `AmbientSwimmer`) and fishing spots (`fishfrenzy`, `FishingSpot`), from the beach out to *Max distance offshore* (45, inside the Dangerous Waters line). Re-running replaces the batch; tick **Keep existing** to add that many more, spaced away from the ones already there. The sharks hunt the schools every 3–6 game hours (a school bolts as one closes in, and reforms 3–6 game hours after it's eaten). |
| **WoW Sandbox → Scatter Driftwood** | A window. **Scatter Driftwood** lays however many pieces you ask for (palm logs and lumber-mill planks) on the painted beach sand within reach of where the player wakes, mostly down by the tide line, with a share (*Round shipwrecks*) washed up round anything named like a shipwreck. Nothing goes into a rock, house or hull: colliders, and every solid model's bounds (not bushes or flowers; palms and wrecks by their colliders). Each piece washes up again a game day after it's taken. **Scatter Flint** lays a few `12rocks01` stones round the wreckage, each giving Flint & Steel to a castaway without one. |
| **WoW Sandbox → Rebuild Campfire** | Rebuilds the campfire prefab (WoW's `6fx_campfire01`, `_cooking01` and `_cold01`) that's otherwise built into `_Generated/Resources/Campfire/` the first time Play is pressed. |
| **WoW Sandbox → Update Warrior Animations** | Adds the fishing states (cast, wait, cheer), the hit flinch (`CombatWound`), the combat states (per-weapon swings, crits, stances, parry/block/dodge), the ability states and the looting kneel to the warrior controller already in use, in place, and gives the player in the scene `WarriorAbilities`. Use this rather than *Build Warrior Animator*, which recreates the controller and leaves the player in the scene without one. Safe to re-run; save the scene after. |
| **WoW Sandbox → Make NPCs Fight** | Gives the wandering humanoids already in the scene `NpcCombat`, and adds Run and Attack to their animator controllers in place, so nothing needs respawning. New spawns from *Spawn Wandering NPCs* get it automatically. Skips chickens and friendlies. |
| **WoW Sandbox → Add Shipwreck** | Lays the `md_shipwreck` hull and stern half-buried at the waterline, opposite the pirate ship by default (*Bearing*), scaled down (the hull is ~97 units at full size). It adds colliders and re-bakes the NavMesh. Re-running replaces the wreck. |
| **WoW Sandbox → Place Player at Shipwreck** | Stands the player on the island side of the shipwreck, clear of it and facing it. That's where the game begins: the title screen circles them there, and it's the respawn point. Save the scene after. |
| **WoW Sandbox → Add Vendor** | Places Quartermaster Briggs (the bearded-pirate export) on the beach nearest the ship and gives the player a `VendorPanel`. |
| **WoW Sandbox → Place Treasure Chest** | A window: pick a chest model (everything in `world/skillactivated/containers`, plus `creatures/TreasureTracker`) and its height (0.9), and place it where the Scene view is looking (see *Treasure chests and ground items*). |
| **WoW Sandbox → Place Ground Item** | A window: an item (or coin) to lie on the ground where the Scene view is looking, with a model per kind of item. |
| **WoW Sandbox → Rebuild Dev Chests** | Rebuilds the chests the Developer panel spawns. They're built by themselves on Play when missing; this is for after changing how chests are set up. |

**Floating ships:** add **Ship Buoyancy** to a ship (after its dry zone). In Play it rises, falls, pitches and rolls with `WaterWaves`, eased by *Response Time*, and the dry zone travels with it. The waterline is wherever you placed it in the editor. Standing on the deck carries you with it — `WowCharacterController` rides anything with a kinematic Rigidbody under its feet, which Ship Buoyancy adds.

**Boarding and sailing:**
- **Ladders:** swim up facing a ladder and hold `W` to climb; `S` climbs down and `Space` lets go. At the top you clamber over the rail onto the deck. From the deck, face the sea at a ladder and press `W` to climb down. Climbing plays the walk animation, since no export has a climb clip.
- **Rope ladders:** each ladder is a strip of WoW's rope-ladder art draped down the hull.
  - **The art:** `ropeladder01` (Models tab: `world/generic/human/passivedoodads/ropeladders/`) is a few flat panels with the ropes and rungs painted on a see-through texture. *Add Ship Ladders* builds its own strip with that texture (`_Generated/ShipLadder/RopeLadder.mat`, cut-out and two-sided) at the model's rung spacing, rather than placing the rigid model, which couldn't follow the hull's curve.
  - **The drape:** the raycasts that find the side give the hull's surface every 0.2 units from the waterline up. The ladder follows it 0.08 units off the planks, bridging dips shorter than about 0.8 units (gun ports, seams) as a rope would, hooks over the rail, and hangs straight down below the water. `ShipLadder.profile` holds that line, so climbing follows it too.
  - **Sway:** `LadderSway` swings the lower 70% gently along the hull and a little away from it (never into it), growing toward the foot, each ladder out of step with the others.
  - **Landing:** the spot on deck is checked for room to stand, moving further inboard past crates, masts or the cabin.
  - **Placement:** `ShipLadderSetup.Stations` sets where along the ship each pair hangs, as a fraction of half the ship's length from the middle (negative is toward the bow): the forward pair at −0.02, just off an open gun port at dead centre, and the aft pair at 0.45. In the current scene some ladders were then nudged outward by hand where they clipped the hull's woodwork, and saved there; re-running *Add Ship Ladders* would undo that.
- **Taking the helm:** click the wheel from close by. You're held behind it, and the camera pulls back around the ship; right-drag orbits.
- **Sailing:** `W`/`S` let out and take in sail, and `A`/`D` steer (`ShipSailing`). The ship reaches about 1.5× run speed, gathers way over ~10 s, coasts to a stop, and can't turn when stopped.
- **Helm panel:** shows sails and speed, with anchor and leave buttons. The ship starts at anchor. Leaving the helm keeps the sails set.
- **Limits:** the ship stops before the shallows (by `Seabed.HeightAt` against its keel) and the edge of the sea; `S` backs it off.

**The open sea:**
- **Dangerous Waters:** swim more than 60 units past the island and a "Dangerous Waters" bar fills over 15 s. When it's full, `DeepSeaDanger` sends a pack of sharks, or 5% of the time the Ancient Leviathan, which breaches, lunges and drags you under. Being aboard the ship is safe, because the danger only counts while you're swimming.
- **Storm peaks:** at every storm's peak (`StormLeviathan`), the Leviathan rises out at sea seaward of you. It sways and roars or casts, dipping into the water each time, then submerges as the storm clears. Lightning favours its spot, and each flash briefly pushes the fog back to show it. It has a pulsing map marker.

**Maps:**
- **Snapshot:** `WorldMap` takes one top-down snapshot of the whole sea. It leaves out the moving ship, and the hull's dry-zone hole, which would otherwise show as land.
- **No map to start:** the minimap is hidden and `M` says "You don't have a map." until the player finds one (`WorldMap.Found`): a map lying in the world (`MapPickup`) — click it to learn it, no bag space, a "You found a map" message. Place it with **Place Ground Item**: set a model in its **Map** slot and pick **Map** from the Item list. The Developer panel's **Show Minimap** and **Enable Map** toggles (saved, like Skip Title) stand in for it.
- **World map (`M`):** opens on the island. The mouse wheel zooms out to the full sea, and the map centres on you once you're off the island.
- **Ship marker:** the ship is drawn on both maps as a to-scale silhouette.

**Vendor:**
- **Opening the shop:** click Quartermaster Briggs from close by. The shop opens beside your bags.
- **Buying:** the Merchant tab has every mundane piece of gear at its D&D price, plus healing potions. Click an item, or drag it into the bags.
- **Selling:** right-click a bag item while the shop is open, or drag it onto the shop. You get half its value, and Rare+ items ask first.
- **Buyback:** the Buyback tab has your last 12 sales. **Sell Junk** sells every Common trade good, so prize fish aren't sold with the junk.
- **Potions:** right-click one in the bags, or press `6`, to drink it.
- **Friendly NPCs** can't be attacked.

**Sea life and fishing:**
- **Fish schools:** each `fishbasic01` is already a small school of fish in one model. `AmbientSwimmer` cruises it between random points near home, 1–4 units down, above the seabed and inside the shallows. They're scenery: they can't be targeted or hurt.
- **Fishing spots:** a `fishfrenzy` swirl rides just under the waves. Left-click it from within 15 units (from the beach, the deck or the water) to cast. You need a fishing pole in your bags: the Twig Fishing Pole (2 sp) or the Fishing Pole (1 gp), both sold by the vendor. Without one: "You need a fishing pole."
  - **The cast:** a small splash where the line lands and a 3 s **Fishing** cast bar (`CastBarHud`). Moving, leaving reach or dying interrupts it.
  - **The result:** when the bar fills there's a 60% chance of a bite, shown by a big splash (`WaterSplash`), and the fish goes into your bags.
  - **Scattering:** after 3–5 catches the school scatters, and it comes back after 90 s.
  - **Tooltip:** hovering a spot says what it is, whether you've a pole, whether you're close enough, and when the night fish are biting.
- **Prize fish:** the further a spot is past the island's edge, the better the odds, blending from *Near Odds* at the shore to *Far Odds* 45 units out.

| Rarity | Fish | Shore | 45+ units out |
|---|---|---|---|
| Common | Raw Fish | 75% | 35% |
| Uncommon | Silverscale Snapper | 20% | 33% |
| Rare | Tidegrave Grouper | 4.5% | 20% |
| Very Rare | Moonfin Eel | 0.5% | 9% |
| Legendary | Gilded Kingfish | 0% | 3% |

- **The fishing animation:** he faces the water, casts (`FishingCast`), holds the fishing pose while waiting (`FishingLoop`) and cheers for a Rare or better fish. Not while swimming. The best pole in your bags appears in his right hand while he fishes (`EquipmentVisuals.Hold`), and the main-hand weapon is put away until he's done. A model without fishing clips falls back to an overhand throw and the thrown-weapon ready pose.

**Fighting NPCs:** `NpcCombat`, on the wandering humanoids.
- **Aggro:** a Hostile one that sees you within 12 units on land runs at you and swings every 2 s (d20 against your AC). Anything you hit fights back, whatever its reaction.
- **Leashing:** it gives up and runs home, healing, once you die, take to the water, get 40 units from its home, or stand where it can't path (the ship's deck).
- **Taking hits:** a blow that lands makes you flinch (`CombatWound`, not while swimming) and breaks a fishing cast.
- **Loot:** humanoids drop Rare items about 1 time in 20 and Very Rare about 1 in 100, and a Pirate Hat about 1 in 25 (`LootDrop.HumanoidDrops`).
- **Looting:** clicking a corpse turns you to face it. With items, you kneel (`KneelStart`, then `KneelLoop`) while the loot window is open and you stand still, and get up (`KneelEnd`) when it closes or you move. A corpse with only coin gets a quick reach (`Loot`). *Update Warrior Animations* adds these states.

**Treasure chests and ground items:** anything with an `Interactable` can be used by clicking it within reach (2.5 units from its nearest edge). Hovering shows its name and "Click to open" or "Click to pick up"; from further away it says "You are too far away." The player's `Interactor` (added at Play) does the hovering and clicking.
- **Treasure chests (`TreasureChest`):**
  - **Opening:** clicking swings the lid open on the model's own `Open` clip, then holds `Opened`. The loot window opens on its contents, as for a corpse (`LootPanel.OpenFor`), and you kneel. What you leave stays inside; an empty chest stops offering itself.
  - **Contents:** hand-picked in the Inspector by catalog name, with counts and coin. Unknown names are listed in the Console at Play. **Bonus Treasure** adds that many random rolls on top, re-rolled each Play.
  - **Treasure odds (`ItemCatalog.RandomTreasure`):** never mundane. Uncommon 40%, Rare 35%, Very Rare (epic) 18%, Legendary 7%.
  - **Models:** WoW's chests (`treasurechest01`–`05`) carry Open, Opened, Close and Closed clips. Chests 02, 03 and 05 draw a pile of gold coins as a second layer whose texture is mostly see-through; it gets a cut-out material, or it would cover the chest as a solid sheet. Light shafts, smoke and similar effects get a glowing, see-through material.
  - **Placing:** *Place Treasure Chest* scales the model to the height you give, adds a solid collider, an empty `LootDrop` and a `TreasureChest` holding a potion, 5 gold and one bonus roll.
- **Ground items (`GroundItem`):** an item, a count and/or coin, lying in the world. Clicking reaches down (`Loot`), puts it in the bags and removes it; a full bag leaves it there. *Place Ground Item* picks the model by the item's kind (Weapon, Armor, Shield, Clothing, Jewelry, Potion, Trade Good, Tool, Coin): set an exported WoW model for each in its window (remembered per machine). A kind without one gets a placeholder lump. **Dropping:** drag a bag item off every window and confirm, and `GroundItem.Drop` lays that very item (enchantments, stack and all) just in front of him: its own gear model laid flat if it has one, else a copy of a scene ground item's model for that item or its kind, else a lump. Not in the water, on a deck or on a ladder; equipped gear has to come off first. Dropped items are saved where they lie.

**Your own combat:** the warrior's swings follow what's in hand. *Update Warrior Animations* builds these states.
- **Swing timer:** one swing a second (`swingInterval` on `WowCharacterController`); pressing `1` sooner does nothing. Abilities share that timer, like WoW's global cooldown.
- **Swings per weapon:** fists, a one-hander, a two-hander and a dagger (stabbed) each have their own attack clips. Swings alternate between the clip's two variations, and a critical hit plays `CombatCritical`. The attack is rolled before the swing plays, so the crit gets the crit swing.
- **Fighting stance:** for 6 s after you swing or are swung at (`combatLinger`), you stand in the ready pose for your weapon (`Ready1H`, `Ready2H`, `ReadyUnarmed`).
- **Parry, block, dodge:** a blow that misses you plays a reaction. With a shield it's mostly a block, with a weapon mostly a parry, and with fists mostly a dodge (`HealthController`). The odds are cosmetic: the d20 already decided the miss.

**Warrior abilities** (`WarriorAbilities`, keys `2`–`5`): each rolls to hit like a swing, so it can miss, crit (doubling all its dice) or kill. All the numbers are on the component.

| Key | Ability | Effect | Cooldown |
|---|---|---|---|
| `2` | Heroic Strike | Your weapon attack, plus 1d8 damage | 6 s |
| `3` | Shield Bash | Needs a shield. 1d4 + STR damage, and a hit stuns the target for 2 s (`NpcCombat.Stun`: no moving, no swinging) | 12 s |
| `4` | Whirlwind | A weapon attack on every enemy in reach, all around you; no target needed | 10 s |
| `5` | Charge | Rush a target 8–25 units away at 3× run speed, strike on arrival and stun it 1 s. Not while swimming; gives up after 3 s | 15 s |

- **Action bar (`ActionBarHud`):** keys 1–6 at the bottom middle of the screen, 64 px up: Attack, the four abilities, then the potion on 6. Each slot shows its key, a short code and its name.
  - **Cooldowns:** a shade drains as the cooldown runs out, with the seconds left on the long ones.
  - **Red** means it can't be used right now: no shield for Bash, no enemy in reach, or the target outside Charge's range.
  - **Clicking** a slot is the same as pressing its key.
  - **Tooltips:** hover for the ability's range, cooldown, damage with your current weapon, what's stopping you, and the cooldown left, counting down live.
  - **At the helm** the bar hides, since the helm panel takes its place.
- **Errors** are WoW's: "Heroic Strike isn't ready yet.", "You are too far away!", "You need a shield equipped.", "Too close to charge."

**Gear on the player:** `EquipmentVisuals` dresses the player in whatever's equipped, from the looks *Harvest Gear* builds. Items without a look leave that part of the body bare. Chain mail, for one, shows a bare chest.
- **Washed up:** the player starts lying asleep on the sand by the wreck (WoW's `Sleep` clip), the storm that wrecked them still at its height, through the title screen and after it. The first key press wakes them: the storm starts clearing (over its Clearing length), they sit up (a slow blend into `SitGround`) and then stand (`SitGroundUp`); they can't move till they're on their feet. `ShipwreckWake`, added at Play (`ShipwreckWake.Lying` holds StormWeather at full and replaces Play's build-up); its `WashedUp` animator states come from **Update Warrior Animations**. With Storm on Play off there's no opening storm.
- **Starving:** he washes up Starving (`Hunger`): walking only, 2 less damage a hit, too weak to swim (his breath runs out at the surface, and he drowns), and he can't get more than 25 m from where he woke, staggering as he nears the edge. A cooked meal makes him **Weak** (still walking, still on the beach); then 20 s **sat by a lit campfire** (a **Resting** cast bar) gets his strength back for good (story flags `survival.fed`, `survival.strong`).
  - **After that:** a hunger bar under the player frame empties over 48 game hours. Below a quarter he's Hungry (a little slower and weaker), empty he's Starving again (no wall, no drowning). Cooked food fills it and leaves him **Well Fed** (+1 damage, 2 game hours); raw food fills it a little and makes him **Sick** (1 game hour). Right-click food in the bags to eat it.
- **Campfire (`Campfire`):** right-click **Flint & Steel** with 3 **Driftwood** to kneel and build one (a 3 s cast) in front of him — not in the sea, on a ship, indoors (anything overhead) or on steep ground. It burns 4 game hours, dimming over the last half hour, then leaves cold coals until the next one. One at a time. It lights the night, the spot he built it from becomes his respawn point, and it's saved.
- **Resting (`RestPanel`):** sitting down (`X`) by a lit campfire opens a rest window, with the cooking window beside it. The rest window has the time of day on a big copy of the minimap's sun-and-moon dial, a choice of 1, 2, 5, 8, 10 or 12 hours, and when he'll wake. **Rest** dims the screen while the big dial spins round to the waking hour, a few seconds for the lot. The hours really pass (`DayNightCycle.Rush`): the fire burns down and goes out if he sleeps past it, hunger grows, weather changes and taken things come back. A blow, death or `Esc` wakes him early. He wakes at full health (unless a blow woke him), and an hour's rest, once he's eaten, also gets his strength back. Standing up closes the window.
- **Cooking (`CookingPanel`):** click a lit campfire (it sits him down beside it, opening the cooking and rest windows) for a window of recipes (Raw Fish → Grilled Fish, Raw Chicken → Roast Chicken) with Cook and Cook All: 2.5 s each on the cast bar, the skillet over the fire, and him cooking where he sits (or knelt beside it, if he's on his feet). Walking off, the fire going out or a blow stops it.
- **Sheathing (`WeaponSheath`):** **Z** puts the weapon and shield away and draws them again; they stay equipped. `EquipmentVisuals.SetSheathed` moves their meshes onto WoW's sheath attachment points (26 back, 28 shield, 32 left hip), which **Harvest Gear** stores in the `GearLibrary` (re-run it once). A torch or lantern stays in hand. **Update Warrior Animations** adds an upper-body `Sheath` layer (the `Sheath` and `HipSheath` clips, masked from `bone_EXP_C1_Spine2` up) so he can draw on the run; the meshes swap halfway through the clip. Saved.
- **Sitting (`PlayerSit`):** **X** sits him on the ground (in the water X still dives). Moving, jumping, X or a blow stands him up. Needs **Update Warrior Animations** for the `SitGroundDown`/`SitGround`/`SitGroundUp` states.
- **Starting outfit:** none. The player starts with empty hands, empty bags and no coin; gear is found, looted or bought. The action bar stays hidden (and abilities 2–5 refuse) until a weapon is in the main hand. The vendor also sells two-handers like the Maul (2d6, 10 gp) and Claymore (2d6, 50 gp), shoulders: Leather Spaulders (5 sp, cosmetic) and Iron Pauldrons (2 gp, +1 AC), and legs: Leather Leggings (5 sp, cosmetic) and Iron Legguards (2 gp, +1 AC). The two iron pieces are the only non-magic AC outside chest armour and shields. The body armours are chest-only exports, so they don't cover the legs; the shorts or leggings show below them. The Pirate Hat is loot only; the Developer panel has **Pirate Hat** and **Linen Outfit** buttons.
- **Slots:** on the character sheet (C), laid out like WoW's: Head, Neck, Shoulders, Back and Chest down the left of his model, Hands, Legs, Feet and Ring down the right, and Main and Off under it. The model is a live copy of him wearing what he wears (weapon drawn or put away); drag on it to turn him round.
- **Three kinds of gear,** and one item can use several:
  - **Painted on:** cloth shirts, trousers and sandals are drawn into the body texture. Each item's pixels are pasted onto a copy of the bare body.
  - **Body pieces:** WoW swaps parts of the body mesh to match gear. The shirt adds sleeve cuffs (`Wrists2`) and the hat hides the hair (`Hair2`).
  - **Separate meshes:** rigid ones (helms, shoulders, weapons, shields, fishing poles) go on WoW's own attachment point for their slot: right hand 1, shield 0, shoulders 5 (right) and 6 (left). A character export names each shoulder's mesh `Shoulder (L)_Item<id>_0` / `Shoulder (R)_Item<id>_0`, and each goes on its own point. A helm's is `bone_168` (attachment 11), a child of `bone_Head`. Skinned armour is bound to the player's own bones by name.
- **Attachment points come from a raw export.** wow.export's glTF leaves them out, so *Harvest Gear* reads them from `thinhuman/raw/thinhumanmale.m2` (`M2Attachments`). To make one: Models tab → `character/thinhuman/male/thinhumanmale.m2` → **Export M2 / WMO (Raw)**, tick **M2 .skel Files** in case a model keeps them there, then rename the folder to `raw`. Without it, helms fall back to `HelmFitter`'s guess, which puts a hat's brim at the chin.
- **Adding a piece:** there are two ways, depending on whether the item is held or worn.
  - **Held (weapons, shields, fishing poles):** export just the item from the Models tab, as glTF. It lands under `WowExports/item/`, in the same item space as a character export's copy, and goes on the attachment point for its slot. A standalone export uses the model's default colour, though.
  - **Worn (shirts, armour, boots, gloves, helms):** in the Characters tab, export the player's model wearing **only** that item: same customisation as `base`, glTF with animations. Rename the new `thinhuman/male` folder at once, since wow.export always writes this model there and the next export overwrites it. Only a character export shows the painted-on texture and the body pieces an item adds or hides.
  - **Then re-run *Harvest Gear*.** It finds new exports by itself and adds a row for each to `WowExports/GearSources.asset`, guessing the item from the name (`knife` → Dagger, `sword_1h_long` → Longsword, `fishingpole` → Fishing Pole). The Console lists every guess. Fix a wrong one, fill in one it couldn't make (such as a multi-item export), or clear the item to leave an export out, all in the Inspector. Then run it again. Each item gets one look, and the first row wins.
  - **How it guesses**, in order: the `Picks` table for exports whose names mislead (`hammer_2h_maul_b_03white` → Warhammer), an exact catalog name, a catalog name inside the export name (longest wins), then a word table, which only accepts a guess when the export's weapon family agrees (a `2h` export won't become a one-hander).
  - **Legendaries and magic copies:** an item with a look under its own name uses it; otherwise it uses its base item's look ("+1 Longsword" → Longsword).
  - **Fit:** some items need resizing or moving on this character. `GearHarvester.Fits` holds those in code, so they survive a fresh clone: the Iron Pauldrons at 0.65× (WoW's plate shoulders are huge on a human) and the Iron Helm lowered 0.03.
  - **Tuning a fit:** a GearSources row's **Scale** (anything but 1) and **Offset** (anything but zero, in the character's axes: y up, and he's about 1.9 tall) override the built-in fit. Scale is also copied to the look's *Attached Scale*, which you can change during Play and re-equip to try values; the next harvest overwrites it. Offset is baked into the look at harvest, so it needs a re-harvest. Put a value you settle on into `Fits`: the GearSources asset is gitignored and per machine.
- **Painting order:** painted-on layers go on in WoW's order, legs, feet, chest, then hands, so gloves cover a shirt's sleeves.

**Title screen, settings and developer options:** none of these need setup. Each is created when you press Play.
- **Title screen (`TitleScreen`):**
  - **The shot:** Play opens on the warrior where the game begins, with the camera slowly circling them, **Tidegrave Isle** in gold, a pulsing **Click to Start**, and Settings and Quit buttons.
  - **While it's up:** the world is calm and safe. The storm cycle waits (`StormWeather.BeginPlay`), nothing aggros, and the HUD and controls are held back.
  - **Starting:** clicking, Enter or Space swoops the camera in behind the warrior.
  - **With a saved game:** **Continue** and **New Game** buttons, with a line saying the level, the time on the isle and when it was saved. Continue (or Enter/Space) fades to black and back in where the Captain left off, with no shipwreck opening. New Game asks first, then wipes the save.
- **Saving (`SaveGame`):**
  - **One save:** `save.json` in `Application.persistentDataPath` (in the editor, `%USERPROFILE%\AppData\LocalLow\DefaultCompany\wow-sandbox`).
  - **What's in it:** where the Captain stands (a spot on a ship's deck if he's aboard), level, experience, health, coin, bags and gear; journal pages, story flags and the map; the time and the sky; things picked up, each chest (opened or not, and what's left), dead enemies, and where the ships are and whether they're anchored.
  - **When:** a couple of seconds after anything happens in the story, every 5 minutes, and on quitting (Play stopping included), with "Game saved" fading in the corner. Esc → **Save Game** saves now. Not on the title screen, while he's lying in the sand, dead or talking; the timed saves wait out a fight.
  - **Where he comes back:** saved swimming, climbing or mid-jump, he's put back on the last ground or deck he stood on. A storm that was on comes back as an overcast sky.
- **Settings (`SettingsPanel`, Esc):**
  - **Opening:** Esc closes any open window first, then opens the menu.
  - **Settings:** view distance (the sky's spheres size themselves to it), fog density, master/music/sound-effects volume, mouse sensitivity, and the camera's max zoom-out (2–20), zoom speed (per scroll notch) and field of view. The zoom settings apply to following the player; the helm keeps its own ship-sized range.
  - **Applying:** changes take effect as you drag and are remembered between sessions (`GameSettings`, PlayerPrefs). Anything never changed follows the scene's own values, and **Defaults** returns to them.
  - **Buttons:** Defaults, Save Game, Quit Game, Developer and Close.
- **Developer (`DevPanel`, from Settings):**
  - **Weather:** storm Start/Roll In/Clear Up/Stop/Lightning, a live intensity readout, and the build-up, full-storm and clearing lengths.
  - **Toggles:** Storm on Play, Skip Title (which goes straight into the saved game if there is one; Delete Save to start fresh), Clouds (hides the clouds) and, under Time of day, Clock.
  - **Time of day:** a time slider, time speed (0 pauses the clock, up to 30×), and Dawn / Noon / Dusk / Midnight.
  - **Sea:** summon sharks or the Leviathan.
  - **Spawn:** an enemy or a chicken in front of you, copied from ones in the scene at Play start.
  - **Chests:** one button per exported chest model, dropping it 3 units in front of you with random test loot: a treasure roll, a 1 in 4 chance of a second, a potion 30% of the time, and 2–12 gold. They're prefabs the editor builds on Play into `_Generated/Resources/DevChests/` (`DevChestBuilder`), since models can't be loaded by path at Play.
  - **Player:** God Mode, Heal, +10 Gold, Rare, Very Rare and Legendary items, the Pirate Hat and linen outfit, a Light Kit (3 torches, both lanterns, 2 flasks of oil), max zoom-out over the full 0–100, and game speed. Save Now, Load Save and Delete Save.
  - **What's remembered:** the storm settings, Skip Title, Clouds and Clock (`DevOptions`; Clouds hides the clouds, Clock the time-of-day dial); the rest lasts until Play ends.
  - **Layout:** two columns (weather, time and sea on the left; spawning and the player on the right), sized to the taller one.
  - **Hiding it:** set `DevOptions.Enabled` to `false` to hide all of it for players.

**Controls** (WoW-style, character-relative — never camera-relative):

| Input | Action |
|---|---|
| `W` / `S` | Forward / backpedal along the character's own facing |
| `A` / `D` | Turn in place — becomes strafe while right-mouse is held |
| `Q` / `E` | Strafe left / right |
| `Shift` | Walk (the character runs by default, as in WoW) |
| Right-drag | Steer the character; camera follows behind |
| Left-drag | Orbit the camera only; character keeps its facing. Let go and it swings back behind him (press right mid-drag to turn him to face that way instead) |
| Scroll | Zoom |
| `Space` | Jump (physics only — see below) — swims **up** while in water |
| `X` | Swim **down** (in water only) |
| Middle mouse / `Num Lock` | Auto-run (`W` or `S` cancels) |
| Left-click | Target; on a corpse in reach, loot it; on a chest, open it; on an item on the ground, pick it up; on a vendor, open the shop; on the ship's wheel, take the helm; on a fishing spot, cast |
| `1` | Attack your target (one swing a second) |
| `2`–`5` | Heroic Strike, Shield Bash, Whirlwind, Charge (or click them on the action bar) |
| `6` | Drink a healing potion |
| `Z` | Put your weapon and shield away (two-handers on the back, one-handers on the hip, shields on the back); `Z` again draws them. The action bar hides while they're away; a swing, an ability or a blow draws them |
| `Tab` | Target the nearest enemy on screen within 40 units; press again for the next one out (`TargetingController`; neutral creatures only with *Tab Includes Neutral*) |
| `B` / `I` | Bags (drag an item out of them onto the world to drop it on the ground, after a Drop / Cancel question) |
| `C` | Character sheet, with his model in the middle (drag it to turn him) |
| `M` | World map (scroll over it to zoom) |
| `Esc` | Close the top window; with none open, Settings |

**Beach and vegetation:**
- **Beach sand:** *Paint Beach Sand* adds a sand terrain layer (`Assets/Terrain/BeachSandLayer.terrainlayer`) and repaints by height, keeping every other layer's proportions in what's left.
- **Vegetation (*Scatter Vegetation*):**
  - **What it finds:** every glTF under `WowExports` whose file name says what it is: `palm`, `bush`/`fern`/`plant`/`shrub`/`jungle`/`frond`, `flower`, `boulder`/`rock`, `fallen`/`log`/`driftwood`. The window lists them with tick boxes.
  - **Rules per kind** (`VegetationScatter.Rules`): density per 1,000 square units at *Lushness* 1, clustering, size range, steepest slope, and how far onto the beach sand. Undergrowth grows in thickets with clearings between, on grass only; palms also on the sand; boulders and driftwood here and there. Nothing under water, on steep slopes, inside anything with a collider, or within *Clear around start* (8) of the player and the vendor.
  - **How it's drawn:** palms are full prefab copies with `TreeSway`, at the size of palms already placed by hand. Everything else is static meshes in its rest pose, since a thousand skinned bushes would be slow. Leafy textures with see-through edges get a cut-out, two-sided material.
  - **Colliders:** trunks and boulders are solid (`MeshColliderTool`); bushes, ferns and flowers you walk through.
  - **Re-running** replaces everything under the `Vegetation` object; **Clear Scattered** removes it.
- **Grass carpet (*Paint Grass*):** terrain detail grass on the green ground only (not sand, water or steep slopes), waving in the wind. Generated tufts, or an exported WoW grass texture. *Density* (0–10, 3 by default) is tufts per patch, counted individually (instance-count scatter mode); *Patchiness* (0.25) leaves bare patches between clumps; *Draw distance* (120) is the costliest setting. Unity's Quality setting *Terrain Detail Density Scale* multiplies it all, so keep that at 1.

**Waves and storms:** the `WaterWaves` component on `WaterSurface` owns the waves. Drag **Storm intensity** from 0 (calm) to 1 (storm); each end is a sea state you can tune: height, choppiness (rolling swell → sharp peaks), chaos (waves with the wind → from every direction), speed, whitecaps and ripples. Everything intensity blends changes smoothly, so it's safe to animate, and in Play mode the component's ⋮ menu has **Roll In Storm** and **Calm Down** (20-second transitions; `TransitionTo(intensity, seconds)` from code). Wind direction, wavelength range, wave count and seed rebuild the wave set, so those jump rather than blend. Swimming, breath and the underwater view all follow the moving surface, and once you reach the surface you ride the swell until `X` takes you under.

**Storm weather:** with a `StormWeather` in the scene, its **Intensity** drives the waves too (it takes over `WaterWaves`' build-up) and Play runs one storm cycle: it builds from light to full over a minute, holds at full strength for two, then clears back to fine weather over one (`buildUpSeconds` / `fullStormSeconds` / `clearingSeconds`). Each effect has its own onset window on that dial: cloud and light go first, rain from 0.25, fog closes from 0.1 down to `stormFogEnd` (90 units), lightning above 0.6 — up to three return-stroke flashes, a jagged bolt, and a light pulse on the world. A fog-coloured band around the horizon hides the strip of clear sky below the clouds as the fog closes in. Its ⋮ menu has **Roll In Storm**, **Clear Up** and **Strike Lightning Now** in Play mode; the first two stop the automatic cycle. It's Play-mode only and puts the scene's own fog, sky, sun and ambient back on exit. Rain collides with the world and dies on roofs, decks and terrain (anything with colliders — run the collider tools on new buildings), and over water it's timed to vanish at the surface; drop to *Rain Collision Quality* Medium if High costs too much. **Storm Audio** (on the same object) plays the recorded `rainlightloop`/`rainheavyloop` from `Assets/WowExports/sound/ambience/weather/` (picked up automatically in the editor; gitignored), crossfading light to heavy as the rain builds, with the `fx_weather_80_lightningstorm_loop` coming in near the peak. Without those files it falls back to rain synthesised in code, levelled by RMS and soft-limited (an early version used sine-tone drops that pinged and stacked into loud bursts); *Rain Brightness* trades patter for hiss. Rain muffles under a roof or below deck. Thunder arrives after each strike at *Sound Speed*, with a crack for close strikes and duller, quieter rumble for far ones.

**Day and night:** `DayNightCycle` (added by `StormWeather` at Play, so its defaults are the code's) runs a 24-minute day: 1 real minute is 1 game hour, sunrise 6:00, sunset 18:00. The scene's own sun is noon: the light rises in the east, passes through that position and sets in the west, and at night the same light turns round to be a dim blue moon opposite it (they swap at the horizon, where both are at zero). It doesn't write fog, sky or ambient itself — `StormWeather` already owns those, so the cycle hands it this hour's clear-weather sky (`Evaluate`) and the storm blends from that; a storm at night is near black, and lightning hits hard. Nights are properly dark (*Night Tint*, *Moonlight*, *Night Sky Exposure*); once the sun is down the procedural sky is also recoloured (*Night Sky Tint*, thin *Night Atmosphere*), or its daytime horizon shows as an orange glow. Stars and the moon are `NightSky.shader` on a camera-pinned sphere: procedural stars that wheel with the hour and twinkle, and a moon disc with darker seas exactly where the moonlight comes from, under the clouds and hidden by storm overcast. The wreck is at 4:00 AM — the clock waits while you lie there, then runs on at the normal pace. The world map's snapshot is taken in plain noon light (`StormWeather.InDaylight`) whatever the hour. The clock sits top-right in the minimap's header beside the zone name: a half-dome dial with the sun and moon turning across it, and the time; it shows before you find the map.

**Lanterns and torches:** at Play, `NightLight` lights every doodad named lantern, torch, brazier or campfire (the pirate ship's 17 lanterns among them — nothing lying on the ground to pick up): a warm point light that fades in at dusk, partly on in a heavy storm, with a flicker, and for open fires a flame built in code (WoW's flames are particle emitters the exports drop). A lantern's light sits a third of the way up (*LanternLightHeight*), below its hook; its glass (`mat_hanginglantern01` / `mat_elwynnlantern01`, unlit and opaque in WoW) glows warm with the flame instead of reading as a solid pane. Particular models can be brighter (`ModelLights`: the ship's `new_small_lantern02`). **WoW Sandbox → Place Light Source** places any exported light-source model with a NightLight. **WoW Sandbox → Preview Night Lights** shows those lights in Edit mode with the sun dimmed, as never-saved objects under *Night Light Preview (not saved)*; drag one, or change its Intensity, Range or Color, and its lantern keeps it (a NightLight with Light Offset, Intensity, Range and Color — undoable, zero to go back to automatic).

**Held lights:** a **Torch** (1 cp, stacks of 10), **Hooded Lantern** (5 gp) or **Bullseye Lantern** (10 gp, brighter, further) goes in the off hand instead of a shield (`HeldLight`). It lights itself in the dark and goes out by day, in the water and when you die, and only burns while it's equipped and lit, in game time: a torch lasts 4 hours and the next in the stack lights; a lantern burns 8 hours per **Flask of Oil** (1 sp), filled from your bags. A lit torch sends chickens running (`WanderingNpc.FearsFire`) and **Torch Swing** (key 7, 6 s) hits for 1d4 + STR, fire while lit, with no weapon needed — the action bar shows for a torch too. Briggs sells all four; humanoids drop torches and oil, and chests often hold some. The models are item exports from wow.export's Models tab (`club_1h_torch_a_01`, `misc_1h_lantern_a/b_01silver`) harvested as **Off-hand (held)** — WoW's left-hand point, where plain Off-hand is the shield's forearm point; without them a stick and a shrunken ship lantern stand in. Place Ground Item always lists the lights.

**Night life:** (`NightLife`) from about 19:30 each NPC goes to bed and gets up around 5:30–6:30, each at its own moment. People walk home and lie down (`Sleep`, or `SitGround` for a model without it); chickens huddle at the middle of their flock's homes. A sleeper wakes if hit or if you come within 3 u (6 u carrying a light), and a Hostile one wakes up fighting; asleep, nothing spots you. The humanoid nearest each lantern on the island (within 20 u of its home) keeps watch instead, patrolling 5 u around it, and in the dark a watcher only sees you at half its aggro range unless you carry a lit torch or lantern. Briggs sleeps at his stall and the shop is closed until dawn. After dark, half the Uncommon and Rare fish you land are night-only: **Luminous Squid** (25 sp) and **Moonlit Ray** (8 gp). **WoW Sandbox → Add Night Animations** adds the sleep states (an `Asleep` bool) to the animators of the NPCs, chickens and vendor already in the scene; newly spawned ones get them anyway.

Wade in past roughly waist height and the character switches to swimming, playing the `Swim`/`SwimIdle` clips (WoW animation IDs 42 and 41). Gravity is off below the surface: `Space` is the only thing that raises you, `X` sinks you, and with neither held you hold the depth you stopped at rather than drifting back up. The exception is being above the waterline — after jumping in from a ledge — where you settle down onto the surface instead of hanging in the air. Duck the camera under for the underwater view.

Scripts live in `Assets/Scripts/` (runtime) and `Assets/Editor/` (tooling).

The camera stores its yaw as an **offset from the character's facing** rather than as a world angle. That single choice is what makes all three camera behaviours fall out for free: turning with `A`/`D` or right-drag carries the camera along automatically, while left-drag changes only the offset.

### Gotchas worth knowing

- **Terrain gloss comes from the albedo's alpha channel.** With no mask map, URP's terrain shader reads smoothness from diffuse alpha and **ignores the TerrainLayer's own Smoothness value** (`m_SmoothnessSource: 1`). Alpha 1 means glass. Zero the alpha, not the slider.
- **Editor scripts that touch `RenderSettings` or a camera must mark the scene dirty**, or the change is silently lost on reload. Anything applied during Play mode is discarded outright.
- **Editing an editor script only affects newly spawned objects** — objects already in the scene keep whatever they were built with.
- **Water needs `Cull Off`, not back-face culling.** You swim *under* the surface, and a one-sided plane vanishes the moment the camera dips below it. The fragment shader flips the normal on back faces (`SV_IsFrontFace`) so fresnel and specular stay correct from underneath, and tints the underside separately — there's no sky down there to reflect.
- **Water refraction needs the URP asset's Opaque Texture.** `PC_RPAsset` has both Opaque and Depth Texture on; `Mobile_RPAsset` has neither. Without Depth Texture the depth colour ramp and shoreline foam collapse to a flat sheet, so `Setup Water` checks the active asset and warns by name rather than shipping a silently broken material.
- **The NavMeshSurface bakes render meshes across the whole scene**, so the water plane itself would bake as a walkable floor and NPCs would stroll across the lake. The surface carries a `NavMeshModifier` with `ignoreFromBuild` for that, which is a *separate* fix from the `NavMeshModifierVolume` that marks the submerged terrain unwalkable. Both are needed.
- **A black sky is usually the camera, not the sky material.** `RenderSettings.skybox` and the camera's Clear Flags are set in two different places, and a camera clearing to a solid colour never draws the skybox at all — so a perfectly good procedural sky still reads as flat black behind the clouds. `Setup Sky and Sun` now sets both.
- **Move the water by moving the `WaterSurface` object.** `WaterVolume.SurfaceY` is derived from the transform, not stored, so the visible mesh and the swim check can't drift apart. It was a serialized field at first, and dragging the water up left gameplay testing the old height — you waded well past your head before swimming engaged, and no threshold tuning could fix it because the threshold wasn't what was wrong.
- **The waves exist twice, and both copies must agree.** `WaterWaves.HeightAt` (C#) mirrors `WaveHeight` in `Water.shader` line for line, so the swim check tests the same surface that's drawn. Change one and you must change the other, or you'll swim in air and walk under crests. That's also why displacement is vertical only: a Gerstner surface can't be queried at a point without solving for it. Waves shorter than about 2.5 grid quads are left out of both copies' displacement (they'd alias) and only light the surface.
- **WoW palms carry no wind sway.** WoW sways doodads in its shader, so the export has one bone and an empty Stand clip. That bone sits up at the crown, not the base, so `TreeSway` rocks it about the tree's foot: a gentle sway when calm, leaning downwind with gusts as `StormWeather` builds. The tree leans as one piece rather than bending, and the colliders stay put.
- **The clouds and the procedural skybox ignore fog**, so storm fog alone would swallow the hills while bright clouds sailed on overhead. `StormWeather` sets global `_WeatherOvercast*` / `_WeatherFlash` values that `Clouds.shader` and `Water.shader` read, greying the clouds and the water's sky reflection along with the fog. They're all zero, meaning no change, without a weather object. Likewise `_DayNightTint` (the hour's light, which also darkens the water's own colour and foam): its alpha is 0 without a cycle, which the shaders read as white.
- **Skybox/Procedural can't go grey.** It's a scattering model: tinting, thinning or dimming it only gives a darker blue, which sat behind grey storm clouds. `Clouds.shader` greys the sky behind its clouds by the same `_WeatherOvercast` global (this used to be a separate `OvercastSky.shader` sphere) — so sky and clouds grey over and clear together.
- **The clouds are raymarched, not the WoW dome.** The exported dome's fixed cloud texture couldn't go from a clear sky to overcast, so it's gone (with `SkyDomeFollow` and Setup Sky Dome), replaced by `CloudLayer` and `Clouds.shader`: 24 steps through a curved slab of sky, shaped by a tileable coverage map and 3D erosion noise built in C# at Play (no texture assets), lit by the scene's own sun/moon and ambient probe — so the time of day, storms, night brightness and lightning all reach it. Fine weather is `CloudSettings.Current` (Developer → Cloud Settings: presets and sliders, saved); a storm blends toward `CloudSettings.Storm` by `StormWeather.Overcast`. Light march is 3 samples toward the sun per dense step.
- **`UnderwaterEffect` doesn't own the above-water fog when there's weather.** It used to snapshot the fog once and restore it on surfacing, which would snap a storm back to a clear day. It now asks `StormWeather.Active` for the current above-water fog.
- **Thickening the procedural sky's atmosphere makes a sunset.** The storm first raised `_AtmosphereThickness` to darken things, which put an orange band all round the horizon. It now thins it (`stormAtmosphere`) and greys the sky's ground colour. Even then, the strip of sky below the clouds stayed brighter than the fogged hills, because neither sky layer takes fog. `HorizonFog.shader` paints that strip in `unity_FogColor` on a camera-pinned sphere just inside the far clip, where the terrain is already fully fogged, so the two meet without a seam.
- **WMO OBJ exports come in untextured.** A WMO's `.mtl` points into the shared texture library with Windows separators (`map_Kd ..\..\..\..\dungeons\textures\...`), which Unity's OBJ importer doesn't resolve — so wow.unity extracts every material with no texture, while doodads (whose `.mtl` names a sibling file) are fine. wow.unity also never overwrites a material it already extracted, so reimporting can't fix it. `Fix OBJ Textures in Selection` resolves the paths and assigns the textures.
- **The water is one flat sheet across the map, so it shows inside every hull.** `WaterDryZone` cuts it out, and `WaterVolume` honours the same shape so the hold isn't swimmable. An ellipse sized from the model's bounds was useless — the bounds are mostly rigging, so it cut open water out beside the hull. The `Hull` shape is sliced from the hull mesh at 7 heights through the wave band, as convex outlines sampled in 48 directions; the shader (`InDryHull`) and C# (`HullContains`) interpolate the same table. The slicer ignores doodads, which would bulge the outline into open water.
- **wow.unity marks every prefab it builds Static, so a floating ship's hull gets static-batched in place.** At Play, Unity merges static meshes into a fixed batch: the hull you see stays put while its colliders (and you, riding them) follow `ShipBuoyancy` — you sink into or float above a deck that looks still while the waves roll past it. It cost several rounds of "the carry code is broken" before logging the renderer next to its collider showed the split. `ShipBuoyancy` clears the Static flag on everything under it in the editor (batching happens before any script runs) and logs an error at Play if a renderer is still batched.
- **The NavMesh does not stop the player.** It only constrains `NavMeshAgent` NPCs. The player is a `CharacterController`, which is stopped by physics colliders and nothing else — so baking a NavMesh around new trees and rocks makes the chickens route around them while you keep walking straight through. Colliders stop you, the NavMesh stops them, and both are wanted.
- **Every M2 is skinned, even a boulder.** M2 geosets carry `JOINTS_0`/`WEIGHTS_0` and import as `SkinnedMeshRenderer` with **no MeshFilter**, so a MeshFilter-only collider sweep silently misses every tree and rock while the WMO hut — which has no skeleton — works fine. That exact split is the tell. Their single animation has zero channels, so the mesh never deforms and `BakeMesh` on the rest pose gives an exact collider. Don't filter scenery by "has an Animator" either: glTFast puts one on every M2, doodads included.
- **Collide tree trunks, not tree leaves — but only on M2s.** M2 doodads split into one geoset per material, so a palm arrives as a `_wood_` mesh and a `_fronds_` mesh; a MeshCollider on the fronds puts invisible walls out in the air wherever the leaf cards hang. The collider tool matches material names against a foliage word list and skips those geosets. **WMOs are exempt**, because there the same words mean architecture: `12tr_amani_hut01`'s roof is built from `mat_12tr_amani_leafy_roof_01` and `mat_12tr_amani_leafs_01`, and skipping those drops you through the hut's roof.
- **Code-generated normal maps must be unpacked as plain RGB.** A `Texture2D` created in code never passes through a `TextureImporter`, so it can't be tagged as a normal map — `UnpackNormal` would decode it as DXT5nm on desktop (x from alpha, y from green). The water shader calls `UnpackNormalRGB` explicitly.
- **`WaterVolume.Contains` is false inside a hull's dry zone, on purpose.** So "is this point still sea?" can't use it: the ship's own bow probe sat inside its dry zone and the ship refused to move ("can't sail past the edge of the sea"). Use `WaterVolume.AnyCovers` for area-only checks. The same hole shows up in a top-down map snapshot as a ship-shaped patch of seabed unless the dry zones are suspended for the capture (`WaterDryZone.Suspend`).
- **wow.export writes every export of the same model to the same folder** (`character/thinhuman/male`), named after the game's model file, so a second export silently replaces the first. Rename the folder after each export, at the same depth: a shared item texture like the hat's is linked by a relative path (`../../../item/...`).
- **A helm's position can't be measured from the head.** Lining the top of the helm up with the top of the head suits a skull-hugging helmet, but it pulls a hat down over the face. WoW places it by the model's attachment point, which only the raw `.m2` has. In the rest pose, the item is just its mesh moved by that point's position (wow.export's `getAttachmentTransform`). The attachments sit at `0xF0` in the MD20 header, 40 bytes each, behind an ID lookup, and their bone indices follow the glTF skin's joint order.
- **Every body mesh in a character export carries the model's whole vertex buffer,** every hairstyle and beard included, whether shown or not. Only the triangles differ. Measuring "the top of the head" from all the head-weighted vertices finds the tallest hairstyle in the model, so `HelmFitter.TryFitPose` can measure from the vertices the triangles actually use. That's *Harvest Gear*'s fallback when there's no raw export.
- **Painted-on gear diffs exactly.** Comparing a piece export's body texture with the bare one, every changed pixel is that item's: the shirt, shorts and sandals never touch the same pixel, and layering all three onto the bare body reproduces an export wearing all three, pixel for pixel. Character materials are opaque, so the layer's alpha is free to carry the mask.
- **HD character exports carry an `EyeGlow` geoset textured with the eye-reflection map.** In WoW it's blended faintly over the eyes; imported as an ordinary opaque mesh it's a solid white sheet over them. `Vendor.HideEyeGlow` turns it off on the vendor and, at Play, on every wandering NPC (the Kul Tiran thieves have it too). The real eyes are a separate geoset underneath.
- **The pirate ship's wheel is baked into the hull mesh** (`exterior1`/`exterior2`), not a doodad, so there's no object to click. `ShipHelm` is an invisible trigger box placed over it.
- **The Ancient Leviathan's natural pose is root rotation (0, 0, 0)** — kraken-like, upright with tentacles down. Pointing its root forward at the sky (as the swim-too-far grab still does) makes its roars and casts look aimed at the sky.
- **Build Warrior Animator (and Spawn Warrior Player) delete and recreate `Warrior.controller`**, which gives it a new GUID. A player already in the scene is left pointing at nothing. To change the existing controller, edit it in place, as *Update Warrior Animations* does.
- **wow.export's glTF doesn't mark see-through textures.** No material in any export here has an `alphaMode`, so a texture whose gaps are alpha, like the rope ladder's, imports opaque and the gaps show solid. Give it a cut-out material yourself (URP Lit: Alpha Clipping on, Cull Off for two-sided), as `ShipLadderSetup.RopeLadderMaterial` does.
- **wow.export's sound exports are `.ogg`**, and the rain loops live under `sound/ambience/weather/`.

**Known gaps:**
- **Falling without jumping has no animation.** Jumps play JumpStart → Jump → JumpEnd, but walking off a ledge keeps the run clip. The export has a `Fall` clip (ID 40) for when it's wanted.
- **Turning in place plays idle**, so the character pivots with their feet planted — there are no turn clips in the export.
- **Ladders can clip protruding woodwork.** The drape follows the hull along one line down the ladder's middle, so a rib or moulding under one of its ropes can poke through; the scene's ladders were nudged out by hand where that happened.
- **Cloaks haven't been tried.** *Harvest Gear* puts them on attachment point 12, but nothing has been exported to test it. Weapons and shields work: their points sit at the model's origin in the rest pose (hand `bone_177` under `bone_WeaponR`, shield `bone_174`) and the animations move them into the hands.
- **The combat animations and abilities are new and lightly tested:** the two-handed grip, Charge's run and arrival, and how much Whirlwind turns are the ones to watch. A stunned thief just stops; there's no dazed animation.
- **Not every item has a look yet:** nothing goes in the Back slot (a cloak needs a character model that can wear one), and each new item needs its export (the exports themselves are gitignored, so every clone needs its own). Rings and amulets never show, as in WoW.

## Export format: glTF for M2, OBJ for WMO

**Use OBJ for WMOs and glTF for M2 creatures.** This isn't a preference — WMO doodad placement only exists in the OBJ path, on both sides of the toolchain.

- **M2 (creatures) → glTF.** Skeleton and animation clips come through, and textures are bundled locally. OBJ would lose the rig entirely.
- **WMO (buildings) → OBJ.** WMOs are static geometry, so OBJ costs nothing meaningful, and it's the only format that yields doodads (furniture/props). glTF gives you a bare shell forever.

### Why — resolved, was previously an open issue

An earlier `gnomehut` glTF export produced only shell nodes (`gnomehut_Ext0-5`, `gnomehut_Int0-10`), no doodads, and no placement metadata anywhere in the output tree. Reading both codebases showed the glTF path simply has no doodad support:

- `wow.export` (`src/app.js`, readable — the portable build ships unminified source):
  - `WMOExporter.exportAsGLTF` (~78260–78345) contains **zero** doodad references.
  - `WMOExporter.exportAsOBJ` (~78346+) is the only path that reads `this.doodadSetMask`, writes `<name>_ModelPlacementInformation.csv` (~78453), and exports each referenced doodad M2 (~78500).
  - The export handler passes the doodad set mask to the exporter for *every* format (~85616) — **glTF accepts it and ignores it.** This is why hunting the UI for a doodad toggle was futile: the WMO "Sets" panel exists and genuinely does nothing for glTF.
- `wow.unity` (`Editor/`) expects that same OBJ-flavored CSV:
  - `WoWExportUnityPostProcessor.cs:124` triggers on files containing `_ModelPlacementInformation.csv`.
  - `ItemCollectionUtility.cs:141` resolves each doodad's model by replacing `_ModelPlacementInformation.csv` with **`.obj`** in the path.

So the "metadata provided from wow.export" in wow.unity's README *is* that CSV, and both halves of the pipeline only speak OBJ for doodads.

**Traps once you are on OBJ:**
- **Tick a doodad set in the WMO's Sets panel.** With none ticked the CSV has no rows, and wow.export's `CSVWriter.write` skips writing an empty file — so there's no CSV, no doodad OBJs, and nothing in the log to say why.
- **wow.unity can build the prefab before the doodads exist.** It processes the CSV on import, and wow.export writes the doodad OBJs after the WMO. If the prefab comes out bare, right-click the CSV → **Reimport** to run placement again.
- wow.unity writes the materials for OBJ imports to `Assets/Materials/`, which is gitignored alongside `Assets/WowExports/`.

## Gotchas learned along the way

- **Unity Hub project creation can double-nest the folder.** If "Location" in the New Project dialog already ends in `wow-sandbox`, Hub appends the project name again, producing `wow-sandbox/wow-sandbox/`. Check for this right after creating the project — flatten it before doing any real work if it happened.
- **M2 models face -X, not Unity's +Z.** A wow.export glTF character comes in rotated 90° from the direction a Unity `CharacterController` drives it, so pressing forward makes the model appear to run sideways. Fix: set the **model child's** local Y rotation to **+90**, leaving the logic root at 0 — that keeps `transform.forward` honest so the movement code needs no compensating fudge. Handled automatically by `WoW Sandbox → Spawn Warrior Player` (`ModelYawOffset` in `Assets/Editor/WarriorSetup.cs`). Note the *axis* is provable from mesh bounds (models are symmetric about Z, so Z is left/right), but the *sign* is not — centroid heuristics on feet and head both point the wrong way, because the calf bulges rearward and hair/helm mass sits behind the skull. Confirm the sign visually in the editor.
- **M2 exports without a selected animation land on an arbitrary bind/rest pose.** A model can look "broken" (e.g. a chicken with its eye apparently missing) when it's actually just posed mid-animation (e.g. a sleep frame with the eye closed). Play an actual animation clip before concluding a texture/material is wrong.
- **WMO exports don't bundle their own textures** (observed on the glTF path; re-check whether the OBJ/MTL path behaves the same). WMO tilesets share textures across many buildings, so `wow.export` writes WMO `.gltf` files with image `uri`s pointing several directories up into a shared library (e.g. `../../../../../dungeons/textures/walls/...`) instead of copying files locally. For a self-contained Unity import: copy the specific referenced textures into a local folder next to the `.gltf` (e.g. `textures/`) and rewrite the `images[].uri` entries to the local relative path. Moving just the model's own export folder without doing this silently breaks all its textures.
- `Assets/WowExports/` holds raw wow.export output (glTF + PNG textures) and is **gitignored** — never commit converted WoW assets to this public repo. See the root `.gitignore` and `docs/wow-model-research.md` §5 for the reasoning (personal/non-commercial use only, per Blizzard's EULA).
- If/when this outgrows glTF fidelity (WMO portal culling, live M2 particle effects, full ADT terrain streaming), the fallback plan is a custom runtime M2/WMO parser — see "Path B" and the Rust/Bevy runner-up option in the research doc.
