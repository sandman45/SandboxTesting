# WoW 3D Assets → Custom Sandbox Engine: Research Report

*Scope: personal, non-commercial, non-redistributed experimentation with legally-owned WoW client data.*

*Researched: 2026-08-06*

---

## 1. WoW File Formats

| Format | Contains | Parse complexity |
|---|---|---|
| **M2** | Per-model geometry, bones/skeleton, animation sequences, attachment points, particle/ribbon emitters, material refs. Not chunked pre-Legion (fixed-offset header of count/offset pairs); Legion+ wraps it in an `MD21` chunk. Since ~Legion, skeleton/animation/bone data was split out into companion files (`.skel`, `.bone`, `.anim`, `.phys`), so a full parser needs to resolve several sidecar files, not just the `.m2`. | **Moderate-high.** Compact but version-fragile; expansion-to-expansion field/versioning quirks are the main pain, and animation data spread across multiple files adds indirection. |
| **WMO** | Static "world model objects" — buildings, dungeons, ships, large doodad-holding structures. Split into a root file (materials, doodad sets, group list, portals, lighting) + N group files (`_000.wmo`, `_001.wmo`…) each with polygon soup in an `MOGP` chunk plus sub-chunks (vertices, normals, UVs, batches, collision). | **Moderate-high.** Fully chunked (easier to skip unknown chunks than M2), but portal/visibility/lighting systems add real complexity if you want more than "just render the geometry." |
| **BLP** | Blizzard's proprietary texture format (BLP0/1/2), supports DXT1/3/5 compression, mipmaps, palettized (indexed) color for older versions. | **Low-moderate.** Well documented, straightforward to decode to PNG/DDS; the main gotcha is the old palettized variant used by classic-era textures. |
| **MPQ** (legacy archive) | Container used through Warlords of Draenor client data; blocks + hash table, encryption, and (multiple) compression codecs per file. | **Moderate.** Format is old and thoroughly reverse-engineered; StormLib is the mature reference implementation, so hand-rolling a reader is rarely necessary. |
| **CASC** (modern archive) | Content-addressable storage introduced in WoD (2014) and used by all current retail data — encoding/root/install manifests map content keys to on-disk archive blobs, plus BLTE chunk-level compression/encryption. | **High.** Much more involved than MPQ (manifest chain, TACT-style key management, BLTE decoding); virtually everyone uses CascLib or a wrapper rather than reimplementing it. |

Reference documentation for all of the above lives on the community-maintained **[wowdev.wiki](https://wowdev.wiki/M2)** ([M2](https://wowdev.wiki/M2), [WMO](https://wowdev.wiki/WMO)), which is the de facto spec.

---

## 2. Existing Open-Source Tooling

| Tool | Language | Role | Maintenance (as observed 2026) |
|---|---|---|---|
| **[wow.export](https://github.com/Kruithne/wow.export)** (Kruithne) | Node.js/Electron | The flagship extraction/conversion GUI. Reads CASC (local install or Blizzard CDN, works with just a legally installed client) and MPQ, previews M2/WMO/ADT/maps/DB2s, exports models to **OBJ and glTF**, textures to **PNG**, plus audio/video/DBC export. glTF export now includes **full animation/skeleton data**, not just a static mesh. | **Actively maintained.** Latest release 0.2.19 (June 2026), roughly monthly release cadence, MIT licensed, thousands of commits. This is the tool the current hobbyist community centers on. |
| **[wow.export.unity](https://github.com/Selzier/wow.export.unity)** | Node.js/Electron | Fork tuned for Unity-friendly output. | Active fork, smaller audience. |
| **[wow.unity](https://github.com/briochie/wow.unity)** | C# (Unity) | Postprocessors/shaders that make wow.export's glTF/PNG output "just work" in Unity Built-in/URP (material setup, WMO doodad placement, collision). | Actively maintained companion project. |
| **libwarcraft** ([WowDevTools](https://github.com/WowDevTools/libwarcraft)) | C# | Managed library for BLP, MPQ, DBC, MDX, WDT/WDL, WMO (full read/write). Powers **Everlook**, a model viewer. | Historically strong but development has slowed; focused on formats through WotLK. |
| **Warcraft.NET** ([ModernWoWTools](https://github.com/ModernWoWTools/Warcraft.NET)) | C# | Successor-ish library reading/writing most modern WoW binary formats. | Moderate activity. |
| **warcraft-rs** ([wowemulation-dev](https://github.com/wowemulation-dev/warcraft-rs)) | Rust | Unified CLI + crate family for MPQ, ADT, WDT, WDL, M2, WMO, DBC across client versions 1.12.1–5.4.8 (Vanilla–MoP); StormLib-compatible MPQ handling with parallel processing. | Explicitly described by its author as **"a nights-and-weekends effort by one person"** — real but modest (~300 commits, 47 stars at time of research). No CASC support yet (so it tops out at MoP-era clients) and no built-in glTF export. Good building block, not a turnkey exporter. |
| **StormLib** / **CascLib** (Ladislav Zezula) | C/C++ | The canonical low-level MPQ and CASC readers nearly every other tool wraps (StormLibSharp for C#, various Python/Rust bindings). | Long-lived, stable, still the reference implementation for archive access. |
| **pywowlib** ([wowdev](https://github.com/wowdev/pywowlib)) | Python | Read/write library for M2, WMO, and related formats, targeting WoW 3.3.5a onward; primarily built to back the Blender addon below. | Maintained as a dependency of WoW Blender Studio rather than standalone. |
| **WoW Blender Studio** ([GitLab/skarnproject](https://gitlab.com/skarnproject/blender-wow-studio)) | Python (Blender addon) | Current-generation Blender import/edit pipeline for WMO/M2/ADT, successor to the older `Blender-WMO-import-export-scripts` (now deprecated, Blender 2.79-only). | Actively maintained, targets modern Blender. |
| **WoWbjectImporter** ([ThatAsherGuy](https://github.com/ThatAsherGuy/WoWbjectImporter)) | Python (Blender addon) | Lighter-weight M2 importer (NPCs, armor, weapons, skyboxes) with auto material/texture setup. | Smaller, less comprehensive than WoW Blender Studio but simple. |
| **WoW Model Viewer (WMV)** | C++ | The historically important legacy viewer/exporter; original codebase is largely unmaintained and notorious for being hard to build and having broken export functions. | **Effectively legacy/dead.** Superseded by forks. |
| **WMVx** ([Frostshake](https://github.com/Frostshake/WMVx)) | C++ | 2023+ rewrite/fork of WMV supporting both legacy and modern clients on a modern codebase. | Active successor to WMV. |
| **Everlook** ([WowDevTools](https://github.com/WowDevTools/Everlook)) | C# | Cross-platform viewer built to showcase libwarcraft, created partly out of frustration with WMV's build/export issues. | Modest activity. |
| **Noggit3 / Noggit Red** ([wowdev/noggit3](https://github.com/wowdev/noggit3)) | C++ | The reference ADT/WMO **map editor** (3.3.5a-focused); useful as a reference implementation for how WMO placement, ADT terrain, and doodad sets fit together, even if you don't use it directly for extraction. | Actively used in the private-server modding scene; forks (Noggit Red) add UI/asset-browser improvements. |
| **three-m2loader** ([Mugen87](https://github.com/Mugen87/three-m2loader)) | JS (three.js) | A three.js loader that reads M2 directly in-browser — interesting proof that a "live parser at runtime" is viable even in a web engine. | Smaller/experimental. |
| **jsWoWModelViewer** / **classic-wow-model-viewer** | JS | Older/alternate in-browser M2 viewers (Wowhead-data-driven in the latter case). | Niche, limited fidelity vs. wow.export. |

---

## 3. Conversion Path: Offline glTF Pipeline vs. Live In-Engine Parser

**Path A — Offline convert with wow.export, then import glTF (recommended starting point):**
- Pipeline: install WoW client (or point wow.export at Blizzard's public CDN with valid keys) → wow.export reads CASC → browse/preview M2/WMO → export to glTF (with animations) + PNG textures → drop the glTF into your engine's standard importer.
- **Pros:** By far the least effort — wow.export already solves CASC/BLTE decoding, M2 versioning across every expansion, WMO group/portal assembly, BLP decoding, and now bakes skeletal animation into glTF. Every mainstream engine has first-class glTF import. You get a working model in minutes, not weeks.
- **Cons:** You inherit wow.export's fidelity ceiling — e.g., some particle effects, ribbon emitters, and WMO-specific dynamic lighting/portal culling don't translate to glTF, since glTF has no native concept of them. Large-scale WMO/ADT world *streaming* (as opposed to single-object export) is more manual. Texture/material conventions (specular, environment cube maps) need re-authoring per target engine's shader model.

**Path B — Write a live M2/WMO runtime importer:**
- Pipeline: use warcraft-rs / libwarcraft / pywowlib (or your own parser against wowdev.wiki specs) plus StormLib/CascLib bindings, load M2/WMO directly at runtime, build your own skeleton/animation/portal system.
- **Pros:** Full fidelity — you can preserve WMO portals/lighting, M2 particle systems, live re-skinning/equipment swaps, and stream whole zones (ADT+WDT) the way the real client does. No lossy intermediate format.
- **Cons:** Substantially more work — this is essentially "build a chunk of a WoW client." Animation retargeting, bone hierarchies, and per-expansion format drift are the hard parts. Best treated as a *follow-up* project once the sandbox's gameplay loop is proven with Path A assets.

**Licensing note relevant to both paths:** wow.export's own exported assets are not licensed for redistribution — they're just game data in a different container. The MIT license on the *tool* has nothing to do with the *game data* it reads; that data remains Blizzard's copyrighted content regardless of format, so the local/personal/non-distributed constraint applies identically whether you convert offline or parse live.

**Recommended realistic pipeline for this project:** Start with Path A to get models moving in your sandbox fast; keep warcraft-rs (Rust) or pywowlib (Python) in your back pocket as the Path B on-ramp if/when you outgrow glTF fidelity (e.g., wanting live WMO portal culling or full ADT terrain streaming).

---

## 4. Engine/Language Comparison (for the sandbox engine itself)

| Engine / Language | glTF import fidelity | Effort to prototype fast | Effort for direct M2/WMO runtime parsing later | Notable prior art |
|---|---|---|---|---|
| **Unity (C#)** | Very good — mature glTF importers (e.g. UnityGLTF, glTFast), strong Mecanim/Animator retargeting. | High — huge asset store, fast iteration loop, C# is approachable. | Moderate — good P/Invoke story for StormLib/CascLib C libs; **wow.unity** already exists specifically to bridge wow.export output into Unity. | **wow.unity** (postprocessing toolkit for wow.export→Unity), **wow.export.unity** (export fork), **wowedit_unity** (WoW editor recreation in Unity), **Warcraft-Arena-Unity** (combat-system recreation). Deepest existing "WoW assets + engine" ecosystem of any option here. |
| **Godot (GDScript/C#)** | Good — native glTF2 import pipeline (skeleton, animation, materials) is solid and improving each 4.x release. | High — free/open, lightweight, quick scene iteration, GDScript optimized for prototyping; C# available if preferred. | Harder than Unity/Rust for native format parsing — no first-class C-library binding story as smooth as Unity's, though GDExtension (C++/Rust via gdext) is workable. | Sparse direct precedent — mostly generic "export WoW model → COLLADA/glTF → import into Godot" community guides rather than dedicated integration projects; less mature than the Unity or Rust ecosystems here. |
| **Unreal (C++/Blueprints)** | Very good, arguably best-in-class skeletal animation fidelity and glTF/Interchange pipeline, but heavier tooling overhead. | Lower for "quick prototyping many ideas" — larger engine, longer iteration/compile cycles, steeper learning curve. | Strong if you're already in C++ — natural to link StormLib/CascLib directly. | **WowUnreal** ([Clancey](https://github.com/Clancey/WowUnreal)) — an actively-worked full WotLK (3.3.5a) client recreation on UE5.7, the most ambitious "WoW in a modern commercial engine" project found. |
| **Web stack (three.js/Babylon.js, JS/TS)** | Good — glTF is basically three.js/Babylon's native format; skeletal animation supported well. | Very high for quick, shareable prototypes (no install, live-reload, trivial to iterate); weaker for anything CPU/GPU-heavy at scale. | Directly demonstrated viable — **three-m2loader** parses M2 straight in-browser without an offline conversion step at all. | **three-m2loader**, **jsWoWModelViewer**, **classic-wow-model-viewer** — an entire lineage of browser-native WoW model viewers going back over a decade. |
| **Rust (Bevy)** | Improving but least mature of the group — `bevy_gltf` covers meshes/skinning/animation but is younger and rougher around edges than Unity/Godot/Unreal importers. | Moderate — Bevy's ECS and hot-reload are prototyping-friendly, but the ecosystem (plugins, editor tooling) is thinner; more code required for things other engines give free. | **Best fit if you want to go straight to Path B.** Natural home for warcraft-rs, and there's a working precedent. | **benilla** ([samwhosung](https://github.com/samwhosung/benilla)) — a from-scratch WoW 1.12.1 client in Rust+Bevy with its own MPQ/BLP/DBC/ADT/WDT/WDL/M2/WMO readers wired directly into Bevy as an asset source (no third-party WoW crates, no bundled assets). Also **WoWee** ([Kelsidavis](https://github.com/Kelsidavis/WoWee)) — a from-scratch C++/Vulkan client (not Rust, but same "runtime parser" philosophy) with WMO/M2/terrain/liquids/lighting. |

---

## 5. Legal / ToS Note

Blizzard's End User License Agreement grants a personal, non-exclusive, non-transferable license for home/noncommercial/personal use of client content, and it does **not** authorize extracting and redistributing game assets — the client files, once decoded into OBJ/glTF/PNG/etc., are still Blizzard's copyrighted work regardless of container format. In practice, Blizzard has long tolerated the hobbyist tooling ecosystem (wow.export, Noggit, WMV/WMVx, Blender addons) precisely because that community keeps output local and personal and doesn't ship Blizzard's assets to others — the same posture this project should take: keep exported models/textures on your own machine, don't commit them to the public SandboxTesting repo or otherwise distribute them, and treat this purely as private prototyping against content you already own.

---

## Ranked Recommendation

**Top pick: Unity (C#) + wow.export → glTF, with wow.unity as the import bridge.**
Rationale: this is the shortest path from "I have a WoW model" to "it's moving around in my sandbox." wow.unity exists *specifically* to eliminate the material/shader/postprocessing friction that normally eats the first weekend of any glTF-into-engine pipeline. Unity's animation retargeting (Animator/Mecanim) is mature enough to handle M2 skeletal data exported via wow.export without much fuss, and C# gives you a smooth path to P/Invoke StormLib/CascLib directly later if you decide to build a custom runtime importer — so it doesn't close Path B off.

**Runner-up: Rust + Bevy, using warcraft-rs directly (skip glTF, go straight to Path B).**
Rationale: if "keep the door open to a custom M2/WMO runtime importer" is actually a near-term goal rather than a someday-maybe, Bevy is the only option here with existing prior art (benilla) proving the whole pipeline works end-to-end in that exact stack, and warcraft-rs gives you real parsing code to start from instead of writing a binary reader from wowdev.wiki specs cold. The tradeoff is a rougher glTF/animation import story if you *also* want to lean on wow.export output for quick wins, and more DIY engine-tooling overhead for "just try an idea fast."

**Honorable mention, if minimal friction to first pixel matters most:** three.js/Babylon.js — glTF just works, iteration is instant (no build step), and three-m2loader shows even a live in-browser M2 parser is realistic if you want to skip the offline-conversion step entirely. Weaker choice if the eventual goal is a persistent, stateful "game" rather than viewers/prototypes.

Godot and Unreal are both credible but sit behind the top two for this specific use case: Godot lacks the dedicated WoW-asset tooling Unity has (wow.unity has no Godot equivalent), and Unreal's iteration loop works against "rapidly test different game ideas" even though WowUnreal proves the fidelity ceiling is very high if you're willing to pay UE's overhead.

---

## First Steps Checklist

1. **Get a legal WoW install.** Retail or Classic client you own, fully patched (wow.export reads CASC from the local install directory, or can stream from Blizzard's CDN with your account).
2. **Install wow.export.** Clone/download from https://github.com/Kruithne/wow.export (Node.js/Electron; releases page has prebuilt binaries — no need to build from source).
3. **Point wow.export at your client install**, let it index the CASC storage.
4. **Pick one simple test asset first** — a small, single-piece creature or doodad M2 (something without complex gear layering) — and export it to **glTF with textures + animation**.
5. **Try a WMO next** (a small building/interior) to validate the group-file assembly and texture export path separately from M2.
6. **If using Unity:** clone https://github.com/briochie/wow.unity alongside your Unity project and follow its README for postprocessor setup before importing the glTF — this saves manually rebuilding materials/shaders.
   **If using Bevy:** clone https://github.com/wowemulation-dev/warcraft-rs and skim https://github.com/samwhosung/benilla's asset-loading code as a working reference for wiring M2/WMO readers into a Bevy asset source.
7. **Import the glTF into your engine**, confirm mesh + texture + (if applicable) animation all came through correctly, and get it rendering in a bare scene before building any gameplay around it.
8. **Bookmark https://wowdev.wiki/M2 and https://wowdev.wiki/WMO** now, even if you don't need them yet — they're the reference you'll return to the moment you outgrow glTF fidelity and start Path B.
9. **Keep all exported assets local** — do not commit converted models/textures into the public SandboxTesting GitHub repo; treat `.gitignore` for any `/export` or `/assets/wow` working directory as step zero once you start producing files.

---

**Sources:**
- [Kruithne/wow.export](https://github.com/Kruithne/wow.export) · [releases](https://github.com/Kruithne/wow.export/releases) · [CHANGELOG](https://github.com/Kruithne/wow.export/blob/main/CHANGELOG.md)
- [wowdev.wiki M2](https://wowdev.wiki/M2) · [wowdev.wiki WMO](https://wowdev.wiki/WMO)
- [WowDevTools/libwarcraft](https://github.com/WowDevTools/libwarcraft) · [Everlook](https://github.com/WowDevTools/Everlook)
- [ModernWoWTools/Warcraft.NET](https://github.com/ModernWoWTools/Warcraft.NET)
- [wowemulation-dev/warcraft-rs](https://github.com/wowemulation-dev/warcraft-rs)
- [ladislav-zezula/CascLib](https://github.com/ladislav-zezula/CascLib) · [StormLib](http://www.zezula.net/en/mpq/stormlib.html)
- [wowdev/pywowlib](https://github.com/wowdev/pywowlib)
- [wowdev/noggit3](https://github.com/wowdev/noggit3)
- [Frostshake/WMVx](https://github.com/Frostshake/WMVx) · [WoW Model Viewer wiki](https://warcraft.wiki.gg/wiki/WoW_Model_Viewer)
- [WoW Blender Studio (GitLab)](https://gitlab.com/skarnproject/blender-wow-studio) · [ThatAsherGuy/WoWbjectImporter](https://github.com/ThatAsherGuy/WoWbjectImporter)
- [samwhosung/benilla](https://github.com/samwhosung/benilla) · [Kelsidavis/WoWee](https://github.com/Kelsidavis/WoWee)
- [Clancey/WowUnreal](https://github.com/Clancey/WowUnreal)
- [briochie/wow.unity](https://github.com/briochie/wow.unity) · [Selzier/wow.export.unity](https://github.com/Selzier/wow.export.unity) · [CucFlavius/wowedit_unity](https://github.com/CucFlavius/wowedit_unity) · [Reinisch/Warcraft-Arena-Unity](https://github.com/Reinisch/Warcraft-Arena-Unity)
- [Mugen87/three-m2loader](https://github.com/Mugen87/three-m2loader) · [vjeux/jsWoWModelViewer](https://github.com/vjeux/jsWoWModelViewer)
- [Blizzard End User License Agreement](https://www.blizzard.com/en-us/legal/08b946df-660a-40e4-a072-1fbde65173b1/blizzard-end-user-license-agreement)
