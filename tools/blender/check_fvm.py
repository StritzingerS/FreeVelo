"""
Checks exported FreeVelo models (FVM1/FVM2) as the game draws them: loads the .bytes files back (Unity space ->
Blender, winding reversed again), renders them from four sides with back-face culling, as Unity does, so missing or
inside-out triangles show up as holes; and counts open edges (borders of holes).

  blender -b --factory-startup --python tools/blender/check_fvm.py -- <out.png> <model.bytes> [<model.bytes> ...]
"""

import math
import os
import struct
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fvm  # noqa: E402


def read(path):
    data = open(path, "rb").read()
    magic, off = data[:4], 4
    uvs = magic == b"FVM2"
    (count,), off = struct.unpack_from("<i", data, off), off + 4
    parts = []
    for _ in range(count):
        (n,), off = struct.unpack_from("<i", data, off), off + 4
        name, off = data[off:off + n].decode(), off + n
        (nv,), off = struct.unpack_from("<i", data, off), off + 4
        pos = [struct.unpack_from("<3f", data, off + 12 * i) for i in range(nv)]
        off += 12 * nv
        off += 12 * nv              # normals
        if uvs:
            off += 8 * nv
        (ni,), off = struct.unpack_from("<i", data, off), off + 4
        idx = struct.unpack_from("<%di" % ni, data, off)
        off += 4 * ni
        parts.append((name, pos, idx))
    return parts


def build(path, x_offset):
    bm = bmesh.new()
    total = 0
    for name, pos, idx in read(path):
        verts = [bm.verts.new((p[0] + x_offset, p[2], p[1])) for p in pos]   # Unity (x, y up, z) -> Blender
        for t in range(0, len(idx), 3):
            a, b, c = verts[idx[t]], verts[idx[t + 1]], verts[idx[t + 2]]
            try:
                bm.faces.new((a, c, b))    # reverse the winding again
            except ValueError:
                pass                       # duplicate face
            total += 1
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    open_edges = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    me = bpy.data.meshes.new(os.path.basename(path))
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(me.name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(fvm.material("WallWhite"))
    print(f"CHECK {os.path.basename(path)}: {total} triangles, {open_edges} open edges")
    return o


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:]
    out, files = args[0], args[1:]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    objs = [build(f, i * 30.0) for i, f in enumerate(files)]
    shading = bpy.context.scene.display.shading
    shading.show_backface_culling = True
    centre = Vector(((len(files) - 1) * 15.0, 0, 6))
    for a in (30, 210):
        fvm.preview(out.replace(".png", f"_{a}.png"), centre, 22 + 18 * len(files), 4, angle_deg=a, size=(1100, 520))
        for ob in list(bpy.data.objects):
            if ob.name.startswith(("cam", "ground")):
                bpy.data.objects.remove(ob, do_unlink=True)
        bpy.context.scene.display.shading.show_backface_culling = True
