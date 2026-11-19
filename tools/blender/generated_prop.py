"""
A small prop generated with Tripo without texture (tools/blender/sources/*.glb), made fit for the game with the
allowed clean-up only (docs/GRAPHICS_OVERHAUL.md 3b): standing on its floor, its longest piece (a table top) turned
along the model's X axis, scaled to a real length, one FreeVelo material.

Run headless:
  blender -b --factory-startup --python tools/blender/generated_prop.py -- <in.glb> <length m> <material> <out stem>
      [whole]   (measure the whole model, not its longest piece)
      [uv=<metres per repeat>]   (own texture coordinates for the material's texture, e.g. uv=2.4 for Rock)
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fvm  # noqa: E402
import inspect_candidate  # noqa: E402


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    src, length, material, stem = args[0], float(args[1]), args[2], args[3]
    o = inspect_candidate.load(src)
    me = o.data

    # The longest piece (by its own extent) gives the long axis: turn it onto X. With "whole" (rocks: one body that
    # Tripo's P series delivers in several pieces) the whole model's extent counts.
    bm = bmesh.new()
    bm.from_mesh(me)
    pieces = [list(bm.faces)] if len(args) > 4 and args[4] == "whole" else inspect_candidate.islands(bm)
    biggest = max(pieces, key=lambda p: max(
        max(v.co.x for f in p for v in f.verts) - min(v.co.x for f in p for v in f.verts),
        max(v.co.y for f in p for v in f.verts) - min(v.co.y for f in p for v in f.verts)))
    dx = max(v.co.x for f in biggest for v in f.verts) - min(v.co.x for f in biggest for v in f.verts)
    dy = max(v.co.y for f in biggest for v in f.verts) - min(v.co.y for f in biggest for v in f.verts)
    bm.free()
    if dy > dx:
        me.transform(Matrix.Rotation(math.radians(90), 4, "Z"))
        dx = dy

    xs = [v.co.x for v in me.vertices]
    ys = [v.co.y for v in me.vertices]
    zs = [v.co.z for v in me.vertices]
    s = length / dx
    centre = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs)))
    me.transform(Matrix.Scale(s, 4) @ Matrix.Translation(-centre))
    print(f"size {(max(xs) - min(xs)) * s:.2f} x {(max(ys) - min(ys)) * s:.2f} x {(max(zs) - min(zs)) * s:.2f} m")

    uv_option = next((a for a in args[4:] if a.startswith("uv=")), None)
    if uv_option is None:
        me.materials.clear()
        me.materials.append(fvm.material(material))
        for p in me.polygons:
            p.use_smooth = False
        fvm.export([o], f"{stem}.bytes")
        return

    # uv=<metres per repeat> (rocks): the game's own projection works per face and turns Tripo's many small facets
    # into a patchwork; real texture coordinates in large islands keep the material's scan continuous over the rock
    # (the game still tints it with the material's colour). Smooth shading with sharp edges above 45 degrees.
    tile = uv_option[3:]
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(70), island_margin=0.02, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    # Smart projection packs into 0..1: scale back to metres / tile, so the scan has its real size on every rock.
    uv = me.uv_layers.active.data
    area3d = sum(p.area for p in me.polygons)
    area_uv = 0.0
    for p in me.polygons:
        pts = [uv[i].uv for i in p.loop_indices]
        area_uv += abs(sum(pts[k].x * pts[(k + 1) % len(pts)].y - pts[(k + 1) % len(pts)].x * pts[k].y for k in range(len(pts)))) / 2
    f = math.sqrt(area3d / area_uv) / float(tile)
    for d in uv:
        d.uv = d.uv * f
    me.materials.clear()
    me.materials.append(fvm.material(material))
    for p in me.polygons:
        p.use_smooth = True
    try:
        me.set_sharp_from_angle(angle=math.radians(45))
    except AttributeError:
        pass
    print(f"own texture coordinates, one repeat {tile} m (uv scale {f:.2f})")
    fvm.export([o], f"{stem}.bytes", uvs=True)


if __name__ == "__main__":
    main()
