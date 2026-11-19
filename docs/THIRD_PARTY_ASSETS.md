# Third-party assets

Only assets that may be published in this public repository are committed: CC0 / public domain, or our own work.
Asset Store, Megascans/Fab and Mixamo content stays out (see `docs/GRAPHICS_OVERHAUL.md`, section 3).

## Generated models (`tools/blender/sources/`)

Generated with the [Tripo](https://www.tripo3d.ai) API on a paid account; under Tripo's Terms of Service (section
5.2.2) paid users hold royalty-free, perpetual, worldwide rights to their outputs. Only the geometry is used: each
model is cleaned up by a script in `tools/blender/` and gets FreeVelo's own materials and textures.

| File | Model | Prompt (short) | Cleaned up by | In the game |
|---|---|---|---|---|
| `alpine_house_tripo.glb` | multiview-to-model v3.1-20260211, face limit 5,000, 19 November 2026, from four views of a front render of the earlier candidate B, funnels, gable lantern and corner flower boxes removed with edit-multiview (owner approved the model) | Tyrolean house: white plastered ground floor, log upper floor, balcony with geraniums, green shutters | `generated_house.py` (keeps Tripo's texture, saved as `Resources/Surfaces/tripo_house_*.jpg` at 1024 px) | `Resources/Models/AlpineHouse_0.bytes` + `_lod1` |
| `apple_tripo.glb` | text-to-model v3.1-20260211, face limit 3,000, 19 November 2026 (candidate B of two, approved by the owner; no fruit) | orchard apple tree, short trunk, broad dense rounded crown of soft leaf clumps | `generated_tree.py ... uv` (Bark, AppleLeaves) | `Resources/Models/Apple_{0,1,2}` (4.1, 4.6, 5.1 m) + `_lod1` |
| `beertable_tripo.glb` | text-to-model P1-20260311, no texture, face limit 600, 19 November 2026 (candidate B of two, approved by the owner; table and bench tops lie about 1.5 cm above their legs) | beer garden table set: one long table, two benches | `generated_prop.py` (one material, WoodLight; 1.9 m long, 0.79 m high) | `Resources/Models/BeerTable_{0,1,2}` |
| `bush_tripo.glb` | text-to-model v3.1-20260211, face limit 1,500, 19 November 2026 (candidate D of four, approved by the owner) | low wide alpine shrub, mounded shape of dense soft green leaf clumps | `generated_tree.py ... uv` (Bush; LeavesLight for variant 2; own texture coordinates close up) | `Resources/Models/Bush_{0,1,2}` (1.2, 1.45, 1.0 m) + `_lod1` |
| `broadleaf_tripo.glb` | text-to-model v3.1-20260211, face limit 8,000, 19 November 2026 (candidate C of three, approved by the owner) | beech: straight trunk, broad round crown of dense soft leaf clumps | `generated_tree.py ... uv` | `Resources/Models/{Beech,Maple,Linden,Alder}_{0,1,2}` (heights of the coded models) + `_lod1` |
| `stonepine_tripo.glb` | text-to-model v3.1-20260211, face limit 4,000, 19 November 2026 (candidate B of four, approved by the owner; narrower than a real Swiss stone pine) | Swiss stone pine, dense rounded dark green needle clumps, columnar shape | `generated_tree.py ... uv` (PineBark, StonePineNeedles) | `Resources/Models/StonePine_{0,1,2}` (12.9, 14.7, 16.4 m) + `_lod1` |
| `spruce_tripo.glb` | text-to-model v3.1-20260211, face limit 4,000, 19 November 2026 (candidate D of four, approved by the owner) | silver fir / spruce in the larch's style: dense horizontal layers of dark green needle clumps | `generated_tree.py ... uv` | `Resources/Models/Spruce_{0,1,2}` (14, 17, 20 m) and `Fir_{0,1,2}` (19, 22, 25 m) + `_lod1` |
| `inn_tripo.glb` | multiview-to-model v3.1-20260211, geometry quality detailed, face limit 10,000, 19 November 2026; from four views of a Tripo text-to-image reference (seedream_v4), downpipes, gutters and wall lamps removed with edit-multiview; approved by the owner | Tyrolean village inn: white ground floor, dark wooden upper floors, two balconies with geraniums, hanging inn sign | `generated_house.py` (14 x 17 m footprint, front-aligned; Tripo texture as `Resources/Surfaces/tripo_inn_*.jpg`) | `Resources/Models/Inn_{0,1,2}` + `_lod1` (5,300 triangles: the joined mesh does not reduce further, unjoined it tears) |
| `larch_tripo.glb` | text-to-model v3.1-20260211, face limit 4,000, 18 November 2026 (candidate B of two, approved by the owner) | tall slim alpine larch, layered drooping clumps of light-green needles | `generated_tree.py ... uv` | `Resources/Models/Larch_{0,1,2}.bytes` + `_lod1` (17, 19.5, 22 m) |
| `mountainpine_tripo.glb` | text-to-model v3.1-20260211, face limit 1,500, 19 November 2026 (candidate D of four, approved by the owner) | alpine dwarf pine, low dome of dense soft dark green needle clumps | `generated_tree.py ... uv` (StonePineNeedles throughout: the few stems lie under the dome, and one leaf face was read as bark) | `Resources/Models/MountainPine_{0,1,2}` (2.8, 2.6, 2.7 m) + `_lod1` |
| `rock_a_tripo.glb` | text-to-model P1-20260311, no texture, face limit 500, 19 November 2026 (approved by the owner; the H series came out crumpled at this size) | weathered alpine granite boulder, rounded irregular shape | `generated_prop.py ... whole uv=2.4` (Rock with its own texture coordinates, 2.2 m and 3.8 m long) | `Resources/Models/Rock_{0,2}` (478 triangles) |
| `rock_b_tripo.glb` | text-to-model P1-20260311, no texture, face limit 500, 19 November 2026 (approved by the owner) | flat angular limestone rock slab, low and wide | `generated_prop.py ... whole uv=2.4` (Rock, 3 m long, 0.5 m high) | `Resources/Models/Rock_1` (460 triangles) |
| `shrine_tripo.glb` | text-to-model v3.1-20260211, face limit 3,000, 18 November 2026 | Tyrolean wayside shrine (Marterl), whitewashed pillar, empty arched niche, shingle roof, no crucifix | `generated_shrine.py` | `Resources/Models/Shrine_0.bytes` |
| `willow_tripo.glb` | text-to-model v3.1-20260211, face limit 4,000, 19 November 2026 (weeping willow, approved by the owner instead of a white willow: Tripo's white willow came out as shards) | weeping willow, short trunk, long hanging curtains of soft green leaf clumps | `generated_tree.py ... uv pale` (Bark, WillowLeaves; only brownish faces are bark) | `Resources/Models/Willow_{0,1,2}` (8.6, 8.5, 9.1 m) + `_lod1` |

## Surface textures (`unity/FreeVelo/Assets/FreeVelo/Resources/Surfaces/`)

From [Poly Haven](https://polyhaven.com), licence [CC0 1.0](https://polyhaven.com/license) (public domain, no
attribution required, credited here anyway). Downloaded 15 November 2026 at 1k resolution: the diffuse map
(`*_diff.jpg`) and the OpenGL normal map (`*_nor.jpg`). Used as tiling detail; each material's colour is applied as
a tint (`SurfaceTextures.cs`).

| File stem | Used for | Real size of one repeat | Source |
|---|---|---|---|
| `plastered_wall_04` | plastered walls | 3.2 m | https://polyhaven.com/a/plastered_wall_04 |
| `wood_plank_wall` | wooden upper floors, gables, doors, shutters, boards | 1.0 m | https://polyhaven.com/a/wood_plank_wall |
| `clay_roof_tiles` | tiled roofs | 4.0 m | https://polyhaven.com/a/clay_roof_tiles |
| `roof_slates_02` | shingle roofs | 3.0 m | https://polyhaven.com/a/roof_slates_02 |
| `old_stone_wall` | plinths, stone | 2.0 m | https://polyhaven.com/a/old_stone_wall |
| `pine_bark` | tree bark | 2.0 m (used at 1 m) | https://polyhaven.com/a/pine_bark |
| `rock_face` | rocks | 2.4 m | https://polyhaven.com/a/rock_face |
| `gravel_floor` | gravel yards, lanes | 2.25 m | https://polyhaven.com/a/gravel_floor |
