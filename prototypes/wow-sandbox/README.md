# wow-sandbox

Prototype: pulling World of Warcraft models (M2/WMO) into Unity via `wow.export` → glTF → `wow.unity`, for personal, non-commercial sandbox use. Background/rationale/engine comparison lives in [`docs/wow-model-research.md`](../../docs/wow-model-research.md).

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
   - Export selected model as **glTF**, with **textures** and **animation** included.
   - Export destination: `prototypes/wow-sandbox/Assets/WowExports/` (inside `Assets/` so Unity/`wow.unity` picks it up automatically; already gitignored — raw WoW assets never get committed).
   - Try a small WMO (e.g. a simple building interior) as a second test, to validate the WMO group/texture path separately from M2.
6. Back in Unity, bring the exported glTF + textures into the project (drag into `Assets/`, or use wow.unity's importer per its README). Confirm:
   - Mesh renders with correct materials/textures.
   - If the model had animation data, it plays correctly in a bare test scene.

## Status

✅ **Pipeline validated end-to-end**, both asset types:
- **M2 (creature)**: `chicken2` (white) — mesh, textures, skeleton, and animation all confirmed working in a bare scene.
- **WMO (static world object)**: `gnomehut` — mesh + multi-texture materials confirmed working.

## Open issue — WMO doodads not exported

`gnomehut`'s `.gltf` contains only the building shell nodes (`gnomehut_Ext0-5`, `gnomehut_Int0-10`) — no doodads/furniture, and no accompanying placement manifest file was generated anywhere in the `wow.export` output tree. `wow.unity`'s README says it can "populate WMOs and ADTs with doodads automatically" by parsing "metadata provided from wow.export" that "includes doodad placements" — but doesn't specify which export option produces that metadata, and the wow.export wiki only documents automatic doodad placement through its **Blender add-on** workflow, not the plain glTF export path we're using.

**Next step:** screenshot the WMO/World Models tab's options panel in `wow.export` (the area equivalent to the M2 tab's Geosets/Textures/Animations checkboxes) to check for a doodad-related toggle or a "Sets" panel we haven't spotted yet. If nothing turns up there, check wow.export's global Settings/Config for an "export doodad sets" or "export manifest" option. Worst case, doodads may need exporting as individual M2s (same as the chicken) and be placed manually/by hand-written placement data.

## Gotchas learned along the way

- **Unity Hub project creation can double-nest the folder.** If "Location" in the New Project dialog already ends in `wow-sandbox`, Hub appends the project name again, producing `wow-sandbox/wow-sandbox/`. Check for this right after creating the project — flatten it before doing any real work if it happened.
- **M2 exports without a selected animation land on an arbitrary bind/rest pose.** A model can look "broken" (e.g. a chicken with its eye apparently missing) when it's actually just posed mid-animation (e.g. a sleep frame with the eye closed). Play an actual animation clip before concluding a texture/material is wrong.
- **WMO exports don't bundle their own textures.** WMO tilesets share textures across many buildings, so `wow.export` writes WMO `.gltf` files with image `uri`s pointing several directories up into a shared library (e.g. `../../../../../dungeons/textures/walls/...`) instead of copying files locally. For a self-contained Unity import: copy the specific referenced textures into a local folder next to the `.gltf` (e.g. `textures/`) and rewrite the `images[].uri` entries to the local relative path. Moving just the model's own export folder without doing this silently breaks all its textures.
- `Assets/WowExports/` holds raw wow.export output (glTF + PNG textures) and is **gitignored** — never commit converted WoW assets to this public repo. See the root `.gitignore` and `docs/wow-model-research.md` §5 for the reasoning (personal/non-commercial use only, per Blizzard's EULA).
- If/when this outgrows glTF fidelity (WMO portal culling, live M2 particle effects, full ADT terrain streaming), the fallback plan is a custom runtime M2/WMO parser — see "Path B" and the Rust/Bevy runner-up option in the research doc.
