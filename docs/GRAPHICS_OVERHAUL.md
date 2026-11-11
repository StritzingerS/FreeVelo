# Graphics overhaul: research, decisions and plan

Status: **planned, not started** (November 2026). This document keeps everything researched and decided about the
new approach to 3D models, their integration into the map, animations, phone performance and the link to the game
logic, so the work can be picked up later.

Research was done by five agents. Their web research was partly rate-limited: numbers marked *(estimate)* are
judgement, not measurements. Verify prices and licence terms again before spending money.

## 1. Why

- Every 3D model (houses, trees, cars, fences, rider) is built in C# from boxes, tubes and spheres with flat colours
  and no textures (`src/FreeVelo.Core/World/Gen/SceneryModels*.cs`). This cannot get past "programmer art".
- Models are placed first and repaired afterwards. Symptoms reported and patched one by one: floating tables and
  benches, forecourt slabs hanging over slopes, houses half sunk, beer-garden floors disappearing, fences and a
  church wall in the road, trees on the road, pits next to roads.
- Requirements from the owner: good-looking models; nicely integrated into the map (nothing floats or looks out of
  place); natural animations; performant on a phone; the game logic (climbs, junctions, routes, group rides) keeps
  working. Photoreal is preferred, but only if everything looks good and fits together; otherwise stylised.

## 2. Decisions

| Topic | Decision |
|---|---|
| Art direction | **Stylised realism** (recommended, awaiting the owner's final yes): realistic proportions and real PBR textures (wood, plaster, shingles, asphalt, grass), bevelled geometry, detail simplified in a controlled way. Not photoreal, not flat-shaded low-poly. |
| Terrain and roads | Stay procedural, generated from the road data. They get matching textures, shading and edge details. |
| Road data | Frozen "world contract": never changed by graphics work. |
| Experience-based plans | Not related to graphics; decided separately to leave the training plans as they are. |
| Checking the look | Renders come from the real Unity on the owner's PC (render-shot tooling, section 9), not from guesses. |
| Budget | About $20–60 in total *(estimate)*, for one or two months of an AI model generator. Everything else is free. |

### Why not photoreal

- **Buildings:** no free or cheap photoreal Tyrolean houses, inns or onion-dome churches were found. A scanned
  landscape with visibly weaker houses is exactly the incoherence the owner rules out.
- **Phones:** realistic forests need layers of alpha-tested leaf cards, the most expensive thing for a tile-based
  phone GPU; photoscanned sets want 2–4k albedo/normal/mask maps per asset; a one-hour ride must not overheat and
  throttle. Verdict: not achievable well on mid-range Android. Zwift on phones is stylised too; Rouvy uses video.
- **Licences:** Megascans/Fab and Unity Asset Store assets may not be committed to a public repository (based on
  forum consensus and the store terms, not on reading the full EULAs).
- **Tuning by eye:** photoreal needs lighting and vegetation tuned by eye in Unity.

## 3. Models and where they come from

The repository is public and licensed PolyForm Noncommercial. Only CC0 assets, self-made assets and AI output from
a paid plan (check the terms) can be committed.

| Category | Source | Cost | Effort |
|---|---|---|---|
| Ground, road, rock, plaster, wood, shingle textures | CC0 PBR sets (Poly Haven, ambientCG), 1–2k | $0 | small |
| Terrain shading | Slope/height blend, detail normals, fog | $0 | medium |
| Trees (larch, fir, stone pine, broadleaf) | Blender-scripted trees with CC0 bark/needle atlases, LODs, impostors | $0 | large |
| Rocks | CC0 scans decimated, or procedural with a rock texture | $0 | small |
| Houses, inns, farms | Textured, bevelled, with real window/door/balcony/shutter parts. Open question: upgrade the C# generator (`SceneryModelsAlpine.cs`) with UVs and a Blender-built parts kit, or build whole houses by Blender script. Decide with one test house. | $0 | large |
| Church, chapel, maypole | Blender script, or AI image-to-3D plus clean-up | $0–5 | medium |
| Props (benches, signs, fences, log piles) | AI image-to-3D (Meshy or Tripo) or CC0 | $10–20 | small–medium |
| Cars | CC0 or AI-generated generic cars, no real brands | $0–10 | small |
| Rider and bike | Rigged CC0 or AI character; scripted bike | $0–20 | medium–large |

Notes:
- **AI generators:** good for props and single hero objects; weak for precise architecture (wobbly edges, melted
  windows, baked lighting, no modularity) and poor for vegetation. Image-to-3D beats text-to-3D for style control.
  Every model needs decimation, LODs and scale fixes (scriptable in headless Blender).
  - Meshy: API with credits; image-to-3D about 20 credits mesh-only, about 30 textured; Pro reported at about
    $20/month for 1,000 credits, roughly $0.60 per textured model *(third-party figures)*. Paid plans include
    commercial rights; free-plan output is CC BY 4.0.
  - Tripo: plans from $19.90/month; reviewers praise cleaner topology.
  - Rodin (Hyper3D): cleanest meshes, about $30/month.
  - Hunyuan3D 2.1: its licence excludes the EU, UK and South Korea. Do not use.
  - Open models need a GPU; the cloud session has none.
- **Paid packs:** Synty POLYGON Alpine Mountain covers nature but no village, is flat-shaded low-poly and cannot be
  committed. No Synty pack for Alpine villages was found.
- **CC0 packs** (Quaternius, Kenney): low-poly; useful for small props only in this style.
- **BlenderMCP** connects Blender to Claude on the owner's machine (can also pull Poly Haven assets and Rodin
  models). Optional.
- "Stylised but not simplistic" references: Lonely Mountains Downhill, Art of Rally, Zwift. Their richness comes
  from dense vegetation, fog and aerial perspective, good lighting and a tight palette.

## 4. Integration into the map (so nothing floats)

### What is wrong today (verified in the code)

- `Landscape.Generate` places first (`Place*`), then repairs (`LevelBuildingPlots`, `SeatFlatPlates`).
- Two ground truths: items are placed on `Terrain.Height`, but Unity draws `DrawnHeight = min(Height, RoadCap)`.
- Footprints live in four unsynchronised tables: `Size`, `Outline`, `PadSize`, `SceneryModels.Radius`.
- Pads are rectangles on a 4 m grid, flat to 1.5 m beyond the footprint, blended over 8 m; cells on road beds are
  skipped, so a pad next to a road is partly overridden.
- Yards and paths are separate box models (`SceneryKind.Yard`, `PathStone`); `SeatFlatPlates` deletes those with
  more than 0.45 m relief, which is why floors vanish.
- Slope is barely constrained (`Free` allows 0.4, sampled at the centre only).
- `GroundKind` has no gravel, paving or yard kinds.
- There is one test per past symptom in `LandscapeTests`, not one rule for everything.

### Target design

- **`AssetDef`** (pure C#, one per kind and variant) replaces the four tables: footprint polygon, floor height,
  plinth depth, maximum relief across the footprint, doors, surface zones (polygon + ground kind), ground anchors,
  snap points, clearance polygon, fence flag.
- **`PlotPlanner`**: for each candidate build the whole plot (building, yard, garden, driveway to the road edge),
  check water, road and lane clearance, overlap and relief; commit only if everything fits, otherwise try the next
  candidate. Replaces `FootprintClear`, `Free`, `FenceClear` and the deletion in `SeatFlatPlates`.
- **`GroundStamp`** replaces `Pad`: polygon, target height, falloff, priority; roads always win. Steep plots get
  two terraces and a retaining wall.
- **Order:** stamps are final first, then every item's height is read once from `DrawnHeight`.
- **Surfaces become ground:** new ground kinds (gravel, paving, lane, garden) and `Landscape.SurfaceAt(x, z)`.
  Because the 4 m ground mesh is too coarse, each plot gets a draped mesh at about 1 m (as `FieldPatch` already
  does for fields). `Yard` and `PathStone` are deleted.
- **Plinths:** every building has a base generated to follow the terrain under its outline.
- **Fences and walls** become polylines: posts on the drawn ground, rails sheared between them.
- **Road edge:** the driveway zone ends at the asphalt edge or the `JunctionCorners` arc, optional kerb.
- **Vegetation:** blocked cells are rasterised from plot polygons plus a margin.
- **Polish later:** terrain-blend shader at the wall base, contact shadows / baked AO, vegetation at the base.

### Guarantee: one invariant suite over every placed item

- every ground anchor within 5 cm of `DrawnHeight`; buildings within their plinth depth;
- no clearance polygon closer than its margin to a road, lane, junction corner or water;
- no overlapping plots; no nature item inside a plot;
- every door reaches a road across surface zones within a slope limit;
- no surface zone with more relief than its limit.

Plus an `--audit` mode in `tools/FreeVelo.ModelPreview` that renders the worst offenders, and golden images per
village.

### Imported models

- Metadata lives in the model: Blender empties and custom properties (`fv_footprint`, `fv_door`, `fv_anchor`,
  `fv_zone`), exported as glTF extras (exact key mapping not yet verified).
- A bake step writes one sidecar JSON per model; the core reads only that. A test fails if the mesh bounds exceed
  the footprint.
- Model rules: origin at floor level, skirt down to plinth depth, LODs as named nodes (`_LOD0..2`).

## 5. Animations

### What exists (verified)

- The rider is rigid segments, one `MeshRenderer` per node per material (`RiderAvatar.cs`): limbs cannot bend at
  the joints.
- `AvatarModel.Pose()` already does two-bone IK for legs and arms, ankling and a foot-down blend.
- Missing: torso and hip sway, head motion, standing and sprint poses, hand positions, wind, camera effects.

### Recommendation, by impact per effort

1. **Procedural body motion:** pelvis rock and torso sway scaled by power, head look into bends, corner lean,
   coasting pose. Small, $0, works on the current rig.
2. **Standing and sprint states** with bike rock, plus hoods/drops. Zwift stands on gradients over about 3 % at
   roughly 30–73 rpm and sprints out of the saddle above 2× FTP. Small to medium, $0.
3. **Skinned rider mesh** (CC0 or AI-generated, rigged in headless Blender, glTF/FBX). Keep the existing IK maths
   driving feet and hands so cadence stays exact; baked clips only for one-offs (drinking, looking back). Biggest
   change to the rider's look and the riskiest. Medium to large, $0–20.
4. **Living world:** vegetation wind, grass sway, flags and ribbons, scrolling-normal water, cloud shadows (cheap
   shader effects); chimney smoke and a few birds (cheap particles/scripts). Skip traffic and pedestrians.
5. **Camera feel:** speed-based field of view (+6–10° at most, smoothed), roll at 20–30 % of the bike's lean,
   spring-damped follow, no head bob, a "reduced motion" toggle.

- No road-cycling mocap pack with seated, standing and sprint clips was found.
- Licences: Mixamo files and Asset Store assets may not be committed to a public repository.
  **Check:** `.gitignore` does not exclude `Resources/FreeVeloBicycle` (the local Asset Store bike).
- 20 riders in a group do not need baked vertex animation: one skinned renderer per rider, about 25–35 bones at
  LOD0, about 15 at LOD1, the C# pose solver without an Animator, distant riders at half rate *(estimate)*.
- Verification without watching: unit tests on `Pose()` (foot-to-pedal distance, knee angle range), turntable
  renders at 60/90/110 rpm seated and standing, short recordings.

## 6. Phone performance

### What exists (verified)

- URP asset `Assets/Settings/Mobile_RPAsset.asset`: Forward, MSAA 4×, HDR, render scale 1.0, shadows 2048 px with
  2 cascades to 110 m, soft shadows, SRP Batcher on, GPU Resident Drawer off, no SSAO. Vulkan then GLES3, 60 fps
  target, linear fog 250–9000 m, texture streaming off.
- `WorldBuilder.LateUpdate`: every 5 m of camera travel it loops over all scenery chunks of the whole world and
  rebuilds every instance matrix (about 4 times a second at 80 km/h), then calls `Graphics.DrawMeshInstanced` per
  kind × variant × LOD × submesh every frame. No frustum or occlusion culling: everything within 380 m is
  submitted, including what is behind the rider. LOD0 instances cast shadows to 300 m although the shadow distance
  is 110 m. Materials are keyed by colour, which prevents atlasing.

**This rendering path must be reworked before textured models go in.**

### Budget for 60 fps on a mid-range phone *(estimate)*

| Item | Budget |
|---|---|
| Triangles per frame | 400k total: terrain 100k, trees 150k, buildings 60k, rider/road/props 50k, shadows 40k |
| Draw calls | 200 or fewer (SetPass under 60) |
| Trees 0–80 m | up to 150 at 1.5–3k triangles |
| Trees 80–400 m | up to 2,000 impostors at 8–24 triangles |
| Trees beyond 400 m | merged billboards, about 2 triangles each |
| Houses | LOD0 3–5k, LOD1 800, LOD2 100–150 triangles; village HLOD beyond 300 m |
| Textures | tree and house atlases 2k, terrain layers 1k × 4, props 1k; 250 MB or less in total (ASTC 6×6) |
| Shadow distance | 70 m |

Thermals are the real limit: use about 60 % of the frame time so the phone holds it for an hour.

### Techniques

- Cheap and effective: tonemapping with a colour LUT, height and distance fog tinted towards the sun, gradient
  sky, baked or vertex AO instead of SSAO, one atlas material per asset family, 2× MSAA instead of 4×.
- Shadows: one directional light, 2 cascades, 2048 px (1024 on low), 60–80 m.
- Trees: opaque stylised canopies instead of leaf cards, impostors at distance, dithered LOD cross-fade.
- Terrain: 4-layer splat, triplanar only on cliffs. Grass: instanced, within 40–60 m, no shadows.
- Avoid: SSAO, depth of field, motion blur, the opaque texture, extra cameras.
- Architecture: precompute matrices per chunk once and frustum-cull chunks; then either BatchRendererGroup (works
  on GLES budget devices) or pooled GameObjects with LODGroups under GPU Resident Drawer (needs Forward+ and
  Vulkan, so keep a fallback). Bake far chunks and village HLODs offline; load models per region; mipmap streaming.
- Quality tiers: low (30 fps, render scale 0.7, no or one shadow cascade, tree mesh radius 50 m), medium (the
  table), high (render scale 1.0, shadows to 110 m, bloom, grass).
- Measuring on the device: extend the in-app log (shareable via "Share log") with average and 95th/99th-percentile
  frame time per minute, CPU/GPU split (`FrameTimingManager`), thermal status and battery drop over a 60-minute
  ride. Unity Profiler and Frame Debugger over adb for details.

## 7. Game logic: the world contract

### Source of truth (verified)

- `WorldMap.Generate(seed)` is the whole gameplay world: pure data, never reads `Terrain`. Graph and centre lines
  (`RoadNode`, `RoadEdge` with `X/Z/E/D`), elevations (`SetElevations`), climbs as exact GPX copies, bridges
  (`LiftBridges` raises `RoadEdge.E`), routes (`BuildRoute`, `ClimbLoop`, `NetworkPath`), `Cornering`,
  `KeepTogetherGroup`.
- The ride uses only `Route.Sample().GradePercent`, `Cornering.StepLimit`, `Path.Update`, `UpdateClimb`.
- Terrain is derived from the roads, not the reverse.
- The server builds the routes itself (`LobbySession`); there is no world or protocol version check.

### Couplings to respect

- Rider and camera come from `Route.Sample`; the camera is clamped by `Terrain.Height`.
- `RoadEdge.Width` (6.5 / 4.0 m) is used by road meshes, bridges, summit signs, terrain carving and `RoadCap`;
  group-ride lanes (`GroupAvatars`) assume it.
- `JunctionCorners.Build` feeds both the terrain and the paving.
- `RoadCap` and `DrawnHeight` encode the ground mesh cell sizes (4/8/16 m).
- Building outlines drive the ground pads and the blocked cells.
- Signs and HUD share node and climb names.
- The terrain cache: `Terrain.FormatVersion` and `Fingerprint`. Pads and scenery are regenerated at each start;
  the maypole depends on the date.

### Safe and unsafe

- **Breaks the logic:** road tools or Unity splines as road authority, hand-sculpted terrain, real-world elevation
  data, resampling the climbs.
- **Risky:** a Unity Terrain object (cannot hold hairpin cuts); hand-placed prefabs in the editor (bypass the
  placement checks and tests).
- **Safe:** new materials and shaders, a prefab per scenery kind/variant, instancing and LODs, a rigged rider,
  decorative meshes generated along roads.

### Architecture

- Core contract (frozen): `WorldMap`, `Route`, `NetworkPath`, `Cornering`, `ClimbData`, `Bridge`,
  `JunctionCorners`, `Terrain.Height`, `SceneryItem` (kind, variant, position, rotation).
- Add `WorldMap.ContractVersion` with a hash of all road data, sent in the lobby handshake.
- Unity: a `SceneryCatalog` mapping (kind, variant) to prefab, LODs and materials; the coded models stay as a
  fallback until every kind is covered.
- Footprints and anchors flow back into the core as checked-in data with their own `CatalogVersion`.
- The road mesh stays procedural (better profile, texture, markings). Guard rails, kerbs, retaining walls and
  bridge parapets are modular pieces instanced along edges from a core `RoadFurniture` list. Tunnels: later.

### Tests to add before changing anything

1. Golden hash of all edges' `X/Z/E`, node names and lengths.
2. Golden length, ascent and climbs for every built-in route.
3. Golden `Terrain.Fingerprint` tied to `FormatVersion`.
4. Golden `NetworkPath.Options` per junction (turn order and names).
5. Footprint test: every catalogue entry fits its core outline; no road furniture within lane reach of a centre
   line.
6. Scenery layout hash for a fixed date.
7. A play-mode check: a ray cast down at route samples hits the road; the camera stays above ground.
8. Server/client contract-version mismatch test.

## 8. Order of work

1. **Safety net:** the golden tests (section 7) and the "nothing floats" invariant suite with audit renders
   (section 4).
2. **Vertical slice of Lärchwald:** one textured house, one tree type, ground and road materials, lighting, the
   new plot placement, the rider's body motion. The owner judges this before anything is scaled up.
3. **Rendering path:** the rework for phones (section 6), measured on the owner's device.
4. **Roll-out:** nature, then buildings, then road furniture, then the skinned rider.

Each step leaves the road data untouched, so the app stays shippable throughout.

## 9. Render shots from the real Unity (built, untested)

So that the look can be checked without screenshots going back and forth:

- `unity/FreeVelo/Assets/FreeVelo/Editor/FreeVeloRenderShots.cs`: when `RenderShots/request.json` appears next to
  the `Assets` folder, the editor enters play mode and leaves it when `RenderShots/done.json` is written. Menu:
  FreeVelo → Render shots now. Command line with the editor closed:
  `Unity -batchmode -projectPath unity/FreeVelo -executeMethod FreeVelo.EditorTools.FreeVeloRenderShots.RenderBatch`
  (no `-quit`, no `-nographics`).
- `Scripts/App/ShotRunner.cs` (editor only): waits for the world, puts the game camera at each view
  (`FreeVeloApp.CameraOverride`), waits until the streaming settles (`WorldBuilder.IsStreaming`), writes
  `RenderShots/out/<name>.png` without the HUD.
- `tools/FreeVelo.ModelPreview -- --shots <file>` writes 13 standard views (start from four sides and above,
  Lärchwald street, forest road, Kirchbach junction, climb start, house, inn, church, farm).
- `RenderShots/` is git-ignored.

**Status:** the runtime part compiles against Unity reference assemblies; the editor script has never been
compiled or run in Unity.

**To set up:** on the PC with Unity, add the repository folder in the Claude desktop app (Settings → Claude Code →
Remote Control), open the project so it compiles the scripts, keep both running. The feature is not available to
everyone yet. Fallbacks: trigger the renders with the menu item and send the pictures; or GitHub Actions with
GameCI (slower, software rendering, and licence activation for Unity Personal is uncertain).

Still only measurable on the phone: frame rate, heat and battery.

## 10. Open questions

- Final yes to stylised realism.
- Which AI generator (Meshy or Tripo), and an API key through the secure credential form.
- Houses: upgraded C# generator or Blender-scripted models (decide with the test house).
- Whether the Remote Control folder sharing works on the owner's PC.
- The Asset Store bike in `Resources/FreeVeloBicycle` and the public repository.

## 11. Sources

Art direction and assets
- https://forums.unrealengine.com/t/megascans-license/2081542
- https://unity.com/legal/as-terms
- https://forum.unity.com/threads/what-kind-of-license-can-be-used-to-protect-assets-purchased-from-the-asset-store-in-a-public-repo.1189066/
- https://assetstore.unity.com/packages/3d/environments/landscapes/polygon-alpine-mountain-nature-biomes-3d-environment-art-by-synt-271620
- https://docs.meshy.ai/api/pricing
- https://costbench.com/software/ai-3d-generation/tripo-ai/
- https://huggingface.co/tencent/Hunyuan3D-2/blob/main/LICENSE
- https://www.strayspark.studio/blog/generative-3d-tools-comparison-meshy-rodin-tripo-csm-2026
- https://www.3daistudio.com/3d-generator-ai-comparison-alternatives-guide/best-text-to-3d-generators-2026
- https://wavect.io/blog/ai-3d-model-generators-game-development-2026.md
- https://mcpservers.org/fr/servers/ahujasid/blender-mcp

Integration
- https://www.guerrilla-games.com/media/News/Files/GDC2017_VanMuijden_GPUBasedProceduralPlacementInHorizonZeroDawn.pdf
- https://www.guerrilla-games.com/read/gpu-based-procedural-placement-in-horizon-zero-dawn
- https://gdcvault.com/play/1024029/
- https://80.lv/articles/procedural-technology-in-ghost-recon-wildlands/
- https://forums.unrealengine.com/t/terrain-mesh-normal-blending-with-virtual-textures-guide/135471
- https://github.com/unitycoder/TerrainHeightBlend-Shader
- https://docs.blender.org/manual/en/dev/addons/scene_gltf2.html
- https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@6.8/manual/features.html

Animation
- https://zwiftinsider.com/stand-up
- https://zwiftinsider.com/sprinting-update/
- https://zwiftinsider.com/rider-hand-position
- https://community.adobe.com/questions-696/mixamo-faq-licensing-royalties-ownership-eula-and-tos-589400
- https://blog.unity.com/engine-platform/animation-instancing-for-skinnedmeshrenderer
- https://unity.com/blog/rendering-at-scale-efficient-strategies-for-massive-object-counts

Performance
- https://docs.unity3d.com/6000.3/Documentation/Manual/urp/gpu-resident-drawer.html
- https://docs.unity3d.com/Manual/urp/gpu-resident-drawer-performance.html
- https://docs.unity3d.com/Manual/urp/configure-for-better-performance.html
- https://unity.com/resources/mobile-xr-web-game-performance-optimization-unity-6
- https://docs.unity3d.com/ScriptReference/Graphics.DrawMeshInstanced.html
- https://unity.com/blog/engine-platform/batchrenderergroup-sample-high-frame-rate-on-budget-devices
- https://documentation-service.arm.com/static/67a62b17091bfc3e0a947695
- https://forums.zwift.com/t/android-graphics-are-meh/644116

Unity in CI (fallback for render shots)
- https://discussions.unity.com/t/unity-no-longer-supports-manual-activation-of-personal-licenses/926760
- https://dev.to/ankursheel/workaround-for-unity-personal-license-manual-activation-not-supported-49j2
- https://www.github.com/game-ci/unity-license-activate
