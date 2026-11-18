"""
Wayside shrine (SceneryKind.Shrine, a Marterl with an empty niche) from a model generated with Tripo
(tools/blender/sources/shrine_tripo.glb, paid API output owned by the project, see docs/THIRD_PARTY_ASSETS.md),
made fit for the game:

- scaled to 3.6 m, standing on its floor (slightly sunk), centred; the niche turned to the front (+Y here, +Z in Unity);
- reduced only if it has more than about 3,000 triangles (Tripo's face_limit already caps it);
- Tripo's own 2048 px textures are not used (memory on phones, and the palette of the village): faces get FreeVelo
  materials by where they are – roof (shingles on top, dark wood below), plinth slabs, the recessed niche, the
  whitewashed pillar.

Run headless:
  blender -b --factory-startup --python tools/blender/generated_shrine.py -- <in.glb> <out.bytes> [preview.png]
"""

import math
import os
import statistics
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fvm  # noqa: E402

HEIGHT = 3.6        # metres, as the coded shrine (cross at 4.6 m, body to 3.5 m)
SINK = 0.15         # the plinth slabs reach this far into the ground
TARGET_TRIS = 3200


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


def bounds(me):
    xs = [v.co.x for v in me.vertices]
    ys = [v.co.y for v in me.vertices]
    zs = [v.co.z for v in me.vertices]
    return Vector((min(xs), min(ys), min(zs))), Vector((max(xs), max(ys), max(zs)))


def normalise(o):
    me = o.data
    lo, hi = bounds(me)
    s = HEIGHT / (hi.z - lo.z)
    centre = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    me.transform(Matrix.Translation(Vector((0, 0, -SINK))) @ Matrix.Scale(s, 4) @ Matrix.Translation(-centre))


def zones(o):
    """Heights where the plinth ends and the roof begins, and the pillar's half width (from the horizontal extent
    per height band: roof and plinth stick out further than the pillar)."""
    me = o.data
    bands = 36
    ext = [0.0] * bands
    for v in me.vertices:
        b = min(bands - 1, max(0, int((v.co.z + SINK) / HEIGHT * bands)))
        ext[b] = max(ext[b], abs(v.co.x), abs(v.co.y))
    pillar = statistics.median(ext[int(bands * 0.3):int(bands * 0.55)])
    roof_band = next((b for b in range(bands // 2, bands) if ext[b] > pillar * 1.15), int(bands * 0.8))
    plinth_band = max([b for b in range(int(bands * 0.25)) if ext[b] > pillar * 1.06] or [0]) + 1
    roof_z = roof_band * HEIGHT / bands - SINK
    plinth_z = plinth_band * HEIGHT / bands - SINK
    print(f"pillar half-width {pillar:.2f} m, plinth to {plinth_z:.2f} m, roof from {roof_z:.2f} m")
    return pillar, plinth_z, roof_z


def turn_to_front(o):
    """Tripo exports with the model's front towards +X (export_orientation "+x", its default; checked on the renders
    of this model: the niche faces +X). The game's front is +Y here (+Z in Unity): turn by 90 degrees about Z."""
    o.data.transform(Matrix.Rotation(math.radians(90), 4, "Z"))


def decimate(o):
    me = o.data
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    if tris <= TARGET_TRIS:
        return
    m = o.modifiers.new("Decimate", "DECIMATE")
    m.ratio = TARGET_TRIS / tris
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)


def classify(o, pillar, plinth_z, roof_z):
    me = o.data
    me.materials.clear()
    names = ["WallWhite", "Shingle", "WoodDark", "Plinth", "WallCream"]
    for n in names:
        me.materials.append(fvm.material(n))
    index = {n: i for i, n in enumerate(names)}

    # The pillar's front surface (where the niche is cut in)
    front = [v.co.y for v in me.vertices if plinth_z < v.co.z < roof_z]
    front_y = sorted(front)[int(len(front) * 0.97)]
    half_x = sorted(abs(v.co.x) for v in me.vertices if plinth_z < v.co.z < roof_z)[-1]

    niche = []
    for p in me.polygons:
        c, n = p.center, p.normal
        outside = max(abs(c.x), abs(c.y)) > pillar * 1.04   # beyond the pillar's walls
        wall_top = not outside and abs(n.z) < 0.3 and c.z < roof_z + 0.3   # the walls run on up to the eaves
        gable = abs(n.x) > 0.8 and abs(c.x) > pillar * 0.85   # in the plane of a side wall, not a step in the roof
        if c.z >= roof_z - 0.05 and (outside or not gable) and not wall_top:
            # Roof: shingles on top (including the steps between the rows), boards underneath. The gable walls face
            # sideways (±X after turning) and stay plaster where they lie inside the walls' line.
            p.material_index = index["WoodDark" if n.z < -0.2 else "Shingle"]
        elif c.z <= plinth_z and (outside or c.z < plinth_z - 0.08 or n.z > 0.3):
            p.material_index = index["Plinth"]
        elif c.y > 0 and c.y < front_y - 0.12 and abs(c.x) < half_x * 0.8 and plinth_z < c.z < roof_z:
            p.material_index = index["WallCream"]
            niche.append(p.index)
        else:
            p.material_index = index["WallWhite"]
    print(f"niche: {len(niche)} faces")
    return niche, index


def replace_crucifix(o, niche, index):
    """Removes the generated crucifix (the part standing forward of the niche's back wall) and adds a plain cross."""
    me = o.data
    if not niche:
        print("no niche found: nothing replaced")
        return
    ys = sorted(me.polygons[i].center.y for i in niche)
    back = ys[int(len(ys) * 0.15)]
    xs = [me.polygons[i].center.x for i in niche]
    zs = [me.polygons[i].center.z for i in niche]
    cx, x_half = statistics.median(xs), (max(xs) - min(xs)) / 2
    z_lo, z_hi = min(zs), max(zs)
    figure = [i for i in niche
              if me.polygons[i].center.y > back + 0.05 and abs(me.polygons[i].center.x - cx) < x_half * 0.7
              and me.polygons[i].normal.y > -0.2]
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in figure], context="FACES")
    bm.to_mesh(me)
    bm.free()
    print(f"niche {len(niche)} faces, back wall at y {back:.2f}; removed {len(figure)} crucifix faces")

    # Plain wooden cross on the back wall, centred in the niche.
    h = (z_hi - z_lo) * 0.7
    cz = (z_lo + z_hi) / 2
    y = back + 0.05
    bm = bmesh.new()
    for size, centre in (((0.07, 0.06, h), (cx, y, cz)), ((h * 0.55, 0.06, 0.07), (cx, y, cz + h * 0.22))):
        geom = bmesh.ops.create_cube(bm, size=1.0)["verts"]
        bmesh.ops.scale(bm, vec=Vector(size), verts=geom)
        bmesh.ops.translate(bm, vec=Vector(centre), verts=geom)
    cross = bpy.data.meshes.new("cross")
    bm.to_mesh(cross)
    bm.free()
    obj = bpy.data.objects.new("cross", cross)
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(fvm.material("WoodDark"))
    return obj


def shade(o):
    for p in o.data.polygons:
        p.use_smooth = True
    try:
        o.data.set_sharp_from_angle(angle=math.radians(40))
    except AttributeError:
        pass


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:]
    src, out = args[0], args[1]
    shrine = load(src)
    normalise(shrine)
    pillar, plinth_z, roof_z = zones(shrine)
    turn_to_front(shrine)
    decimate(shrine)
    classify(shrine, pillar, plinth_z, roof_z)
    shade(shrine)
    fvm.export([shrine], out)
    if len(args) > 2:
        fvm.preview(args[2], (0, 0, HEIGHT * 0.45), 6.5, 1.6, angle_deg=25, size=(900, 1000))
