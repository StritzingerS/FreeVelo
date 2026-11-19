"""
A small prop generated with Tripo without texture (tools/blender/sources/*.glb), made fit for the game with the
allowed clean-up only (docs/GRAPHICS_OVERHAUL.md 3b): standing on its floor, its longest piece (a table top) turned
along the model's X axis, scaled to a real length, one FreeVelo material.

Run headless:
  blender -b --factory-startup --python tools/blender/generated_prop.py -- <in.glb> <length m> <material> <out stem>
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

    # The longest piece (by its own extent) gives the long axis: turn it onto X.
    bm = bmesh.new()
    bm.from_mesh(me)
    biggest = max(inspect_candidate.islands(bm), key=lambda p: max(
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

    me.materials.clear()
    me.materials.append(fvm.material(material))
    for p in me.polygons:
        p.use_smooth = False
    fvm.export([o], f"{stem}.bytes")


if __name__ == "__main__":
    main()
