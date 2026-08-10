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

🎮 **Playable:** the warrior walks around on generated terrain with a WoW-style controller and camera.

**Not yet done:** re-export `gnomehut` as **OBJ** to verify the doodad chain end-to-end (CSV → wow.unity auto-population → furniture in scene). See the format section above for why OBJ is required.

## Playable sandbox

Two editor menu items rebuild the whole playable scene from scratch, so nothing needs hand-wiring in the Inspector:

| Menu item | What it does |
|---|---|
| **WoW Sandbox → Spawn Warrior Player** | Builds an AnimatorController from the glTF's clips, then assembles the rig: capsule sized from the model's real bounds, Animator + Avatar wired, camera hooked up, movement speeds scaled to the model's height, and the model child rotated +90° (see the facing gotcha below). |
| **WoW Sandbox → Generate Terrain** | Procedural Unity Terrain with layered Perlin noise. Flattens a pad at the origin and drops the player onto it. Self-contained — the ground texture is generated in code, so there are no external asset dependencies. |

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
| `Space` | Jump (physics only — see below) |

Scripts live in `Assets/Scripts/` (runtime) and `Assets/Editor/` (tooling).

The camera stores its yaw as an **offset from the character's facing** rather than as a world angle. That single choice is what makes all three camera behaviours fall out for free: turning with `A`/`D` or right-drag carries the camera along automatically, while left-drag changes only the offset.

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

## Gotchas learned along the way

- **Unity Hub project creation can double-nest the folder.** If "Location" in the New Project dialog already ends in `wow-sandbox`, Hub appends the project name again, producing `wow-sandbox/wow-sandbox/`. Check for this right after creating the project — flatten it before doing any real work if it happened.
- **M2 models face -X, not Unity's +Z.** A wow.export glTF character comes in rotated 90° from the direction a Unity `CharacterController` drives it, so pressing forward makes the model appear to run sideways. Fix: set the **model child's** local Y rotation to **+90**, leaving the logic root at 0 — that keeps `transform.forward` honest so the movement code needs no compensating fudge. Handled automatically by `WoW Sandbox → Spawn Warrior Player` (`ModelYawOffset` in `Assets/Editor/WarriorSetup.cs`). Note the *axis* is provable from mesh bounds (models are symmetric about Z, so Z is left/right), but the *sign* is not — centroid heuristics on feet and head both point the wrong way, because the calf bulges rearward and hair/helm mass sits behind the skull. Confirm the sign visually in the editor.
- **M2 exports without a selected animation land on an arbitrary bind/rest pose.** A model can look "broken" (e.g. a chicken with its eye apparently missing) when it's actually just posed mid-animation (e.g. a sleep frame with the eye closed). Play an actual animation clip before concluding a texture/material is wrong.
- **WMO exports don't bundle their own textures** (observed on the glTF path; re-check whether the OBJ/MTL path behaves the same). WMO tilesets share textures across many buildings, so `wow.export` writes WMO `.gltf` files with image `uri`s pointing several directories up into a shared library (e.g. `../../../../../dungeons/textures/walls/...`) instead of copying files locally. For a self-contained Unity import: copy the specific referenced textures into a local folder next to the `.gltf` (e.g. `textures/`) and rewrite the `images[].uri` entries to the local relative path. Moving just the model's own export folder without doing this silently breaks all its textures.
- `Assets/WowExports/` holds raw wow.export output (glTF + PNG textures) and is **gitignored** — never commit converted WoW assets to this public repo. See the root `.gitignore` and `docs/wow-model-research.md` §5 for the reasoning (personal/non-commercial use only, per Blizzard's EULA).
- If/when this outgrows glTF fidelity (WMO portal culling, live M2 particle effects, full ADT terrain streaming), the fallback plan is a custom runtime M2/WMO parser — see "Path B" and the Rust/Bevy runner-up option in the research doc.
