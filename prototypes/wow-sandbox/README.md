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

🎮 **Playable world:** a warrior you control on procedurally generated terrain, under a real WoW sky dome with fog; a gnome hut with collision you can walk inside; lakes you can swim in, with an underwater view; and wandering chickens, warriors and thieves routing around it all on a baked NavMesh. Every part is rebuildable from the `WoW Sandbox` menu, so the scene itself is disposable.

✅ **Doodad chain verified end-to-end** on `md_pirateship` exported as OBJ: wow.export writes `pirateship_ModelPlacementInformation.csv` plus each doodad's OBJ, and wow.unity rebuilds `pirateship.prefab` with all 36 doodads (cannons, lanterns, hammocks, anchor) in place. Drag the **`.prefab`**, not the `.obj`. The traps on the way are listed under "Export format" below.

## Playable sandbox

Two editor menu items rebuild the whole playable scene from scratch, so nothing needs hand-wiring in the Inspector:

| Menu item | What it does |
|---|---|
| **WoW Sandbox → Spawn Warrior Player** | Builds an AnimatorController from the glTF's clips, then assembles the rig: capsule sized from the model's real bounds, Animator + Avatar wired, camera hooked up, movement speeds scaled to the model's height, and the model child rotated +90° (see the facing gotcha below). |
| **WoW Sandbox → Generate Terrain** | Procedural Unity Terrain with layered Perlin noise. Flattens a pad at the origin and drops the player onto it. Self-contained — the ground texture is generated in code, so there are no external asset dependencies. |
| **WoW Sandbox → Fix Terrain Gloss** | Zeroes the albedo alpha on terrain layers in place. Fixes glass-looking ground without regenerating (which would orphan the baked NavMesh). |
| **WoW Sandbox → Add Mesh Colliders to Selection** | Adds MeshColliders across a whole hierarchy — a WMO is many group meshes, not one. Non-convex, so you can walk *into* buildings. Skips skinned meshes, and skips foliage geosets so trees collide on the trunk rather than on their leaf cards. |
| **WoW Sandbox → Add Colliders to All Scenery** | Same, swept across the whole scene instead of the selection — the step that actually gets missed after placing a batch of doodads. Idempotent. Skips the water surface, the sky dome and anything belonging to a character. |
| **WoW Sandbox → Bake NavMesh** | Creates/updates a NavMeshSurface and bakes. Re-run after moving buildings or regenerating terrain. **This does not stop the player** — see below. |
| **WoW Sandbox → Populate Chickens** | Scatters wandering chickens around the selection, snapped to the NavMesh. |
| **WoW Sandbox → Spawn Wandering NPCs** | Same, for any M2 glTF you drag in — sized from the model's own bounds. |
| **WoW Sandbox → Setup Sky and Sun** | Procedural sky plus a matching directional light; ambient comes from the sky. Also forces the camera's Clear Flags to Skybox, without which the sky never draws at all. |
| **WoW Sandbox → Setup Sky Dome** | Rebuilds an exported WoW sky model's cloud layers as transparent, depth-less materials pinned to the camera. |
| **WoW Sandbox → Set View Distance** | Sets far clip, fog range, and sky dome scale together — they're coupled and can't be set independently. |
| **WoW Sandbox → Setup Water** | Floods the terrain to a sea level given as a fraction of its height. Builds the wave mesh, generates the ripple normal maps in code, adds the `WaterVolume` the swim code reads, puts `UnderwaterEffect` on the camera, adds `WaterWaves`, and marks the submerged terrain unwalkable. Re-bake the NavMesh afterwards. |
| **WoW Sandbox → Add Storm Weather** | Adds a `StormWeather` that drives the waves, darkening cloud, failing light, closing fog, rain, wind and lightning from one intensity. Finds the sun, sky, water and camera itself at Play time, so there's nothing to wire. |
| **WoW Sandbox → Fix OBJ Textures in Selection** | Assigns the textures Unity's OBJ importer missed on WMO exports, by reading the `.mtl` itself. Run it once on any building or ship exported as OBJ that comes in untextured. **Not yet confirmed working** — it never ran when first tried, and the pirate ship was fixed by writing the texture references into its `.mat` files directly. |
| **WoW Sandbox → Add Water Dry Zone to Selection** | Slices the selected ship's hull at the waterline and cuts that shape out of the water, so it doesn't show inside the hull and you don't swim in the hold. Re-run it (on the ship) after moving the ship up or down. |

**Floating ships:** add **Ship Buoyancy** to a ship (after its dry zone). In Play it rises, falls, pitches and rolls with `WaterWaves`, eased by *Response Time*, and the dry zone travels with it. The waterline is wherever you placed it in the editor. Standing on the deck carries you with it — `WowCharacterController` rides anything with a kinematic Rigidbody under its feet, which Ship Buoyancy adds.

**Controls** (WoW-style, character-relative — never camera-relative):

| Input | Action |
|---|---|
| `W` / `S` | Forward / backpedal along the character's own facing |
| `A` / `D` | Turn in place — becomes strafe while right-mouse is held |
| `Q` / `E` | Strafe left / right |
| `Shift` | Walk (the character runs by default, as in WoW) |
| Right-drag | Steer the character; camera follows behind |
| Left-drag | Orbit the camera only; character keeps its facing |
| Scroll | Zoom |
| `Space` | Jump (physics only — see below) — swims **up** while in water |
| `X` | Swim **down** (in water only) |

**Waves and storms:** the `WaterWaves` component on `WaterSurface` owns the waves. Drag **Storm intensity** from 0 (calm) to 1 (storm); each end is a sea state you can tune: height, choppiness (rolling swell → sharp peaks), chaos (waves with the wind → from every direction), speed, whitecaps and ripples. Everything intensity blends changes smoothly, so it's safe to animate, and in Play mode the component's ⋮ menu has **Roll In Storm** and **Calm Down** (20-second transitions; `TransitionTo(intensity, seconds)` from code). Wind direction, wavelength range, wave count and seed rebuild the wave set, so those jump rather than blend. Swimming, breath and the underwater view all follow the moving surface, and once you reach the surface you ride the swell until `X` takes you under.

**Storm weather:** with a `StormWeather` in the scene, its **Intensity** drives the waves too (it takes over `WaterWaves`' build-up) and Play runs one storm cycle: it builds from light to full over a minute, holds at full strength for two, then clears back to fine weather over one (`buildUpSeconds` / `fullStormSeconds` / `clearingSeconds`). Each effect has its own onset window on that dial: cloud and light go first, rain from 0.25, fog closes from 0.1 down to `stormFogEnd` (90 units), lightning above 0.6 — up to three return-stroke flashes, a jagged bolt, and a light pulse on the world. A fog-coloured band around the horizon hides the strip of clear sky below the cloud dome as the fog closes in. Its ⋮ menu has **Roll In Storm**, **Clear Up** and **Strike Lightning Now** in Play mode; the first two stop the automatic cycle. It's Play-mode only and puts the scene's own fog, sky, sun and ambient back on exit.

Wade in past roughly waist height and the character switches to swimming, playing the `Swim`/`SwimIdle` clips (WoW animation IDs 42 and 41). Gravity is off below the surface: `Space` is the only thing that raises you, `X` sinks you, and with neither held you hold the depth you stopped at rather than drifting back up. The exception is being above the waterline — after jumping in from a ledge — where you settle down onto the surface instead of hanging in the air. Duck the camera under for the underwater view.

Scripts live in `Assets/Scripts/` (runtime) and `Assets/Editor/` (tooling).

The camera stores its yaw as an **offset from the character's facing** rather than as a world angle. That single choice is what makes all three camera behaviours fall out for free: turning with `A`/`D` or right-drag carries the camera along automatically, while left-drag changes only the offset.

### Gotchas worth knowing

- **Sky domes are cloud layers, not a sky.** A WoW skybox is an M2 dome of ~60–75% transparent cloud sheets, meant to composite *over* a sky — not to be one. Rendering them in the `Background` queue leaves the gaps showing the bare camera clear colour. They belong in the `Transparent` queue, drawn after the skybox, with a procedural sky supplying the blue behind. They also carry no `COLOR_0`, so the per-vertex gradient WoW uses for time-of-day tinting isn't in the export.
- **Sky dome scale doesn't change how the sky looks.** The dome is pinned to the camera, so scaling preserves every angle. It only decides whether the dome gets clipped by the far plane and whether distant terrain correctly occludes it — so it must stay larger than the terrain but inside the far clip. Use *Cloud tiling* and *Height offset* to change the look.
- **Terrain gloss comes from the albedo's alpha channel.** With no mask map, URP's terrain shader reads smoothness from diffuse alpha and **ignores the TerrainLayer's own Smoothness value** (`m_SmoothnessSource: 1`). Alpha 1 means glass. Zero the alpha, not the slider.
- **Editor scripts that touch `RenderSettings` or a camera must mark the scene dirty**, or the change is silently lost on reload. Anything applied during Play mode is discarded outright.
- **Editing an editor script only affects newly spawned objects** — objects already in the scene keep whatever they were built with.
- **Water needs `Cull Off`, not back-face culling.** You swim *under* the surface, and a one-sided plane vanishes the moment the camera dips below it. The fragment shader flips the normal on back faces (`SV_IsFrontFace`) so fresnel and specular stay correct from underneath, and tints the underside separately — there's no sky down there to reflect.
- **Water refraction needs the URP asset's Opaque Texture.** `PC_RPAsset` has both Opaque and Depth Texture on; `Mobile_RPAsset` has neither. Without Depth Texture the depth colour ramp and shoreline foam collapse to a flat sheet, so `Setup Water` checks the active asset and warns by name rather than shipping a silently broken material.
- **The NavMeshSurface bakes render meshes across the whole scene**, so the water plane itself would bake as a walkable floor and NPCs would stroll across the lake. The surface carries a `NavMeshModifier` with `ignoreFromBuild` for that, which is a *separate* fix from the `NavMeshModifierVolume` that marks the submerged terrain unwalkable. Both are needed.
- **A black sky is usually the camera, not the sky material.** `RenderSettings.skybox` and the camera's Clear Flags are set in two different places, and a camera clearing to a solid colour never draws the skybox at all — so a perfectly good procedural sky still reads as flat black behind the cloud dome. `Setup Sky and Sun` now sets both.
- **Move the water by moving the `WaterSurface` object.** `WaterVolume.SurfaceY` is derived from the transform, not stored, so the visible mesh and the swim check can't drift apart. It was a serialized field at first, and dragging the water up left gameplay testing the old height — you waded well past your head before swimming engaged, and no threshold tuning could fix it because the threshold wasn't what was wrong.
- **The waves exist twice, and both copies must agree.** `WaterWaves.HeightAt` (C#) mirrors `WaveHeight` in `Water.shader` line for line, so the swim check tests the same surface that's drawn. Change one and you must change the other, or you'll swim in air and walk under crests. That's also why displacement is vertical only: a Gerstner surface can't be queried at a point without solving for it. Waves shorter than about 2.5 grid quads are left out of both copies' displacement (they'd alias) and only light the surface.
- **The sky dome and the procedural skybox ignore fog**, so storm fog alone would swallow the hills while bright clouds sailed on overhead. `StormWeather` sets global `_WeatherOvercast*` / `_WeatherFlash` values that `SkyDome.shader` and `Water.shader` read, greying the clouds and the water's sky reflection along with the fog. They're all zero, meaning no change, without a weather object.
- **`UnderwaterEffect` doesn't own the above-water fog when there's weather.** It used to snapshot the fog once and restore it on surfacing, which would snap a storm back to a clear day. It now asks `StormWeather.Active` for the current above-water fog.
- **Thickening the procedural sky's atmosphere makes a sunset.** The storm first raised `_AtmosphereThickness` to darken things, which put an orange band all round the horizon. It now thins it (`stormAtmosphere`) and greys the sky's ground colour. Even then, the strip of sky below the cloud dome stayed brighter than the fogged hills, because neither sky layer takes fog. `HorizonFog.shader` paints that strip in `unity_FogColor` on a camera-pinned sphere just inside the far clip, where the terrain is already fully fogged, so the two meet without a seam.
- **WMO OBJ exports come in untextured.** A WMO's `.mtl` points into the shared texture library with Windows separators (`map_Kd ..\..\..\..\dungeons\textures\...`), which Unity's OBJ importer doesn't resolve — so wow.unity extracts every material with no texture, while doodads (whose `.mtl` names a sibling file) are fine. wow.unity also never overwrites a material it already extracted, so reimporting can't fix it. `Fix OBJ Textures in Selection` resolves the paths and assigns the textures.
- **The water is one flat sheet across the map, so it shows inside every hull.** `WaterDryZone` cuts it out, and `WaterVolume` honours the same shape so the hold isn't swimmable. An ellipse sized from the model's bounds was useless — the bounds are mostly rigging, so it cut open water out beside the hull. The `Hull` shape is sliced from the hull mesh at 7 heights through the wave band, as convex outlines sampled in 48 directions; the shader (`InDryHull`) and C# (`HullContains`) interpolate the same table. The slicer ignores doodads, which would bulge the outline into open water.
- **wow.unity marks every prefab it builds Static, so a floating ship's hull gets static-batched in place.** At Play, Unity merges static meshes into a fixed batch: the hull you see stays put while its colliders (and you, riding them) follow `ShipBuoyancy` — you sink into or float above a deck that looks still while the waves roll past it. It cost several rounds of "the carry code is broken" before logging the renderer next to its collider showed the split. `ShipBuoyancy` clears the Static flag on everything under it in the editor (batching happens before any script runs) and logs an error at Play if a renderer is still batched.
- **The NavMesh does not stop the player.** It only constrains `NavMeshAgent` NPCs. The player is a `CharacterController`, which is stopped by physics colliders and nothing else — so baking a NavMesh around new trees and rocks makes the chickens route around them while you keep walking straight through. Colliders stop you, the NavMesh stops them, and both are wanted.
- **Every M2 is skinned, even a boulder.** M2 geosets carry `JOINTS_0`/`WEIGHTS_0` and import as `SkinnedMeshRenderer` with **no MeshFilter**, so a MeshFilter-only collider sweep silently misses every tree and rock while the WMO hut — which has no skeleton — works fine. That exact split is the tell. Their single animation has zero channels, so the mesh never deforms and `BakeMesh` on the rest pose gives an exact collider. Don't filter scenery by "has an Animator" either: glTFast puts one on every M2, doodads included.
- **Collide tree trunks, not tree leaves — but only on M2s.** M2 doodads split into one geoset per material, so a palm arrives as a `_wood_` mesh and a `_fronds_` mesh; a MeshCollider on the fronds puts invisible walls out in the air wherever the leaf cards hang. The collider tool matches material names against a foliage word list and skips those geosets. **WMOs are exempt**, because there the same words mean architecture: `12tr_amani_hut01`'s roof is built from `mat_12tr_amani_leafy_roof_01` and `mat_12tr_amani_leafs_01`, and skipping those drops you through the hut's roof.
- **Code-generated normal maps must be unpacked as plain RGB.** A `Texture2D` created in code never passes through a `TextureImporter`, so it can't be tagged as a normal map — `UnpackNormal` would decode it as DXT5nm on desktop (x from alpha, y from green). The water shader calls `UnpackNormalRGB` explicitly.

**Known gaps:**
- **Jump has no animation.** The export contains no jump clip (WoW's JumpStart/JumpEnd weren't included), so the character arcs through the air still playing idle. The `Jump` trigger and `Grounded` bool already exist in the controller, ready for a state once those clips are exported.
- **Turning in place plays idle**, so the character pivots with their feet planted — there are no turn clips in the export.
- **Left-click both attacks and orbits the camera.** Harmless in practice since orbiting needs a drag, but move attack to a number key if it grates.

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
