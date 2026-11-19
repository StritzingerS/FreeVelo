"""
Tyrolean house (SceneryKind.AlpineHouse) generated with Tripo (tools/blender/sources/*.glb, paid API output owned by
the project, see docs/THIRD_PARTY_ASSETS.md), made fit for the game - with the allowed clean-up only
(docs/GRAPHICS_OVERHAUL.md 3b: no geometry surgery; a model that needs it is regenerated):

- the front (Tripo exports it towards +X) turned to +Y here, +Z in Unity;
- scaled to fit inside the coded model's footprint (SceneryModelsAlpine.AlpineHouse, variant 0: 10.5 x 12 m), so
  the placement does not change; standing on its floor, the plinth slightly sunk;
- Tripo's own texture (saved at 1024 px as Resources/Surfaces/<stem>_diff / _nor), format FVM2 with texture
  coordinates;
- a reduced far version for the middle distance (joined along the texture seams first, so it does not tear).

Run headless:
  blender -b --factory-startup --python tools/blender/generated_house.py -- <in.glb> <out stem> <texture stem>
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fvm  # noqa: E402

WIDTH, DEPTH = 10.5, 12.0     # walls of the coded AlpineHouse variant 0 (x, and z = front in Unity)
SINK = 0.25
FAR_TRIANGLES = 900


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.parent = None
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return o


def wall_extent(me, z0, z1):
    """Extent of the walls (the well-populated outer layer, not the overhangs) between heights z0 and z1."""
    pts = [v.co for v in me.vertices if z0 < v.co.z < z1]
    xs = sorted(p.x for p in pts)
    ys = sorted(p.y for p in pts)
    k = max(1, len(pts) // 50)
    return xs[k], xs[-k - 1], ys[k], ys[-k - 1]


def normalise(o):
    me = o.data
    me.transform(Matrix.Rotation(math.radians(90), 4, "Z"))   # Tripo front +X -> +Y
    zs = [v.co.z for v in me.vertices]
    lo, hi = min(zs), max(zs)
    h = hi - lo
    # The ground floor (between 10 % and 35 % of the height) gives the walls without roof overhang or balcony.
    x0, x1, y0, y1 = wall_extent(me, lo + h * 0.1, lo + h * 0.35)
    # Fit inside the coded footprint (never larger: neighbours, fences and lanes are placed around it), proportions kept.
    s = min(WIDTH / (x1 - x0), DEPTH / (y1 - y0))
    centre = Vector(((x0 + x1) / 2, (y0 + y1) / 2, lo))
    me.transform(Matrix.Translation(Vector((0, 0, -SINK))) @ Matrix.Scale(s, 4) @ Matrix.Translation(-centre))
    print(f"walls {(x1 - x0) * s:.1f} x {(y1 - y0) * s:.1f} m, height {h * s:.1f} m (scale {s:.2f})")
    return h * s


def textured(o, stem):
    """One material that tells the game to use the generated texture (saved as <stem>_diff / _nor at 1024 px)."""
    me = o.data
    me.materials.clear()
    me.materials.append(bpy.data.materials.new("Textured:" + stem))


def save_textures(out_dir, stem):
    for prefix, suffix in (("Color", "_diff"), ("NormalGL", "_nor")):
        img = next(i for i in bpy.data.images if i.name.startswith(prefix) and i.size[0])
        copy = img.copy()
        copy.scale(1024, 1024)
        copy.filepath_raw = os.path.join(out_dir, stem + suffix + ".jpg")
        copy.file_format = "JPEG"
        copy.save()
        print(f"saved {copy.filepath_raw}")


def shade(o):
    for p in o.data.polygons:
        p.use_smooth = True
    try:
        o.data.set_sharp_from_angle(angle=math.radians(35))
    except AttributeError:
        pass


def reduce(o, target):
    # Tripo's mesh is split into separate pieces along its texture seams; reduced as they are, their borders move
    # apart and the far version tears open. Joined first (texture coordinates stay per corner), it stays closed.
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.002)
    bm.to_mesh(o.data)
    bm.free()
    tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
    if tris <= target:
        return
    m = o.modifiers.new("Decimate", "DECIMATE")
    m.ratio = target / tris
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:]
    src, stem, tex_stem = args[0], args[1], args[2]
    for detail in (0, 1):
        house = load(src)
        normalise(house)
        if detail == 0:
            save_textures(os.path.join(os.path.dirname(os.path.abspath(stem)), "..", "Surfaces"), tex_stem)
        textured(house, tex_stem)
        if detail == 1:
            reduce(house, FAR_TRIANGLES)
        shade(house)
        fvm.export([house], f"{stem}_lod1.bytes" if detail else f"{stem}.bytes", uvs=True)
