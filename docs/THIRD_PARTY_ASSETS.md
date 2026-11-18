# Third-party assets

Only assets that may be published in this public repository are committed: CC0 / public domain, or our own work.
Asset Store, Megascans/Fab and Mixamo content stays out (see `docs/GRAPHICS_OVERHAUL.md`, section 3).

## Generated models (`tools/blender/sources/`)

Generated with the [Tripo](https://www.tripo3d.ai) API on a paid account; under Tripo's Terms of Service (section
5.2.2) paid users hold royalty-free, perpetual, worldwide rights to their outputs. Only the geometry is used: each
model is cleaned up by a script in `tools/blender/` and gets FreeVelo's own materials and textures.

| File | Model | Prompt (short) | Cleaned up by | In the game |
|---|---|---|---|---|
| `shrine_tripo.glb` | text-to-model v3.1-20260211, face limit 3,000, 18 November 2026 | Tyrolean wayside shrine (Marterl), whitewashed pillar, empty arched niche, shingle roof, no crucifix | `generated_shrine.py` | `Resources/Models/Shrine_0.bytes` |

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
