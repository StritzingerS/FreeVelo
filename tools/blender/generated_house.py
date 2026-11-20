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
      [width depth]   (default: the AlpineHouse footprint) [front] [turn=<deg>]   (default 90: Tripo's front to the road)
      [square]   (turn the walls square to the axes first, for a model generated askew)
      [height=<m>]   (scale to this total height instead of the footprint: textured props)  [sink=<m>]
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
FRONT_ALIGN = False
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


TURN = 90   # Tripo front +X -> +Y; the farmhouse (long side at Tripo's front) turns 180: house end to the road


SQUARE = False   # turn the walls square to the axes first (the barn came 32 degrees askew inside its model)
HEIGHT = None    # scale to a total height (props) instead of fitting the footprint


def wall_turn(me):
    """Angle of the walls against the model's axes: area-weighted direction of the near-vertical faces, mod 90."""
    sx = sy = 0.0
    for p in me.polygons:
        if abs(p.normal.z) > 0.3:
            continue
        a = math.atan2(p.normal.y, p.normal.x)
        sx += p.area * math.cos(4 * a)
        sy += p.area * math.sin(4 * a)
    return math.atan2(sy, sx) / 4


def normalise(o):
    me = o.data
    if SQUARE:
        t = wall_turn(me)
        me.transform(Matrix.Rotation(-t, 4, "Z"))
        print(f"walls squared: turned {math.degrees(-t):.1f} degrees")
    me.transform(Matrix.Rotation(math.radians(TURN), 4, "Z"))
    zs = [v.co.z for v in me.vertices]
    lo, hi = min(zs), max(zs)
    h = hi - lo
    # The ground floor (between 10 % and 35 % of the height) gives the walls without roof overhang or balcony.
    x0, x1, y0, y1 = wall_extent(me, lo + h * 0.1, lo + h * 0.35)
    # Fit inside the coded footprint (never larger: neighbours, fences and lanes are placed around it), proportions kept.
    s = min(WIDTH / (x1 - x0), DEPTH / (y1 - y0))
    if HEIGHT:   # props (maypole, flower box): scaled to a real height instead of a footprint
        s = HEIGHT / h
    centre = Vector(((x0 + x1) / 2, (y0 + y1) / 2, lo))
    # With FRONT_ALIGN the front wall stands at the footprint's front edge (where the path or forecourt begins) instead
    # of the middle: a model shallower than its plot otherwise leaves a strip of meadow before the door.
    shift = ((DEPTH - ((y1 - y0) * s)) / 2) if FRONT_ALIGN else 0
    me.transform(Matrix.Translation(Vector((0, shift, -SINK))) @ Matrix.Scale(s, 4) @ Matrix.Translation(-centre))
    print(f"walls {(x1 - x0) * s:.1f} x {(y1 - y0) * s:.1f} m, height {h * s:.1f} m (scale {s:.2f}), moved {shift:.1f} m to the front")
    return h * s


def textured(o, stem):
    """One material that tells the game to use the generated texture (saved as <stem>_diff / _nor at 1024 px)."""
    me = o.data
    me.materials.clear()
    me.materials.append(bpy.data.materials.new("Textured:" + stem))


def save_textures(out_dir, stem):
    # Image names: "Color*" / "NormalGL*" from generation, "tripo_base_color*" / "tripo_normal*" from Tripo's texture task
    for prefixes, suffix in ((("Color", "tripo_base_color"), "_diff"), (("NormalGL", "tripo_normal"), "_nor")):
        img = next(i for i in bpy.data.images if i.name.startswith(prefixes) and i.size[0])
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
    # On some models the joined mesh stops above the target (the inn: 5,300 instead of 900). Reducing it unjoined
    # reaches 900 but tears it (1,642 open edges, checked), so the closed, larger far version is kept (10 inns only).
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.002)
    bm.to_mesh(o.data)
    bm.free()
    _collapse(o, target)
    print(f"far version: {sum(len(p.vertices) - 2 for p in o.data.polygons)} triangles")


def _collapse(o, target):
    # One collapse pass can stop far above the target on meshes with many separate pieces (the inn stayed at 5,344
    # instead of 900): repeat until it gets there or stops shrinking.
    bpy.context.view_layer.objects.active = o
    for _ in range(6):
        tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
        if tris <= target * 1.1:
            break
        m = o.modifiers.new("Decimate", "DECIMATE")
        m.ratio = target / tris
        bpy.ops.object.modifier_apply(modifier=m.name)
        if sum(len(p.vertices) - 2 for p in o.data.polygons) >= tris * 0.97:
            break


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:]
    src, stem, tex_stem = args[0], args[1], args[2]
    if len(args) > 4:   # another building's footprint (width along the road, depth towards it), e.g. the inn 14 x 17
        WIDTH, DEPTH = float(args[3]), float(args[4])
    FRONT_ALIGN = "front" in args[5:]
    TURN = next((int(a[5:]) for a in args[5:] if a.startswith("turn=")), TURN)
    SQUARE = "square" in args[5:]
    HEIGHT = next((float(a[7:]) for a in args[5:] if a.startswith("height=")), None)
    SINK = next((float(a[5:]) for a in args[5:] if a.startswith("sink=")), SINK)
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
