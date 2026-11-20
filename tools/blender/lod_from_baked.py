"""
Makes the middle-distance version (<stem>_lod1.bytes) of a baked textured model from its close-up file, with one
collapse pass to <target> triangles. For models whose generated_house.py far version fell apart (the Paarhof
dwelling: the repeated collapse passes squeezed it into a lens shape); the close-up file is read back as the game
loads it, so the bake arguments need not be repeated.

  blender -b --factory-startup --python tools/blender/lod_from_baked.py -- <model.bytes> <out_lod1.bytes> <target>
"""

import math
import os
import struct
import sys

import bmesh
import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fvm  # noqa: E402


def read(path):
    data = open(path, "rb").read()
    uvs = data[:4] == b"FVM2"
    off = 4
    (count,), off = struct.unpack_from("<i", data, off), off + 4
    parts = []
    for _ in range(count):
        (n,), off = struct.unpack_from("<i", data, off), off + 4
        name, off = data[off:off + n].decode(), off + n
        (nv,), off = struct.unpack_from("<i", data, off), off + 4
        pos = [struct.unpack_from("<3f", data, off + 12 * i) for i in range(nv)]
        off += 24 * nv                                   # positions, normals
        tex = [struct.unpack_from("<2f", data, off + 8 * i) for i in range(nv)] if uvs else None
        off += 8 * nv if uvs else 0
        (ni,), off = struct.unpack_from("<i", data, off), off + 4
        idx = struct.unpack_from("<%di" % ni, data, off)
        off += 4 * ni
        parts.append((name, pos, tex, idx))
    return parts, uvs


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    src, dst, target = args[0], args[1], int(args[2])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts, uvs = read(src)
    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap") if uvs else None
    names = []
    for mi, (name, pos, tex, idx) in enumerate(parts):
        names.append(name)
        verts = [bm.verts.new((p[0], p[2], p[1])) for p in pos]       # Unity (x, y up, z) -> Blender
        for t in range(0, len(idx), 3):
            a, b, c = idx[t], idx[t + 2], idx[t + 1]                    # reverse the winding again
            try:
                f = bm.faces.new((verts[a], verts[b], verts[c]))
            except ValueError:
                continue
            f.material_index = mi
            if uv_layer is not None:
                for loop, k in zip(f.loops, (a, b, c)):
                    loop[uv_layer].uv = tex[k]
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.002)       # one closed body, as in generated_house.reduce
    me = bpy.data.meshes.new("lod")
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new("lod", me)
    bpy.context.scene.collection.objects.link(o)
    for name in names:
        me.materials.append(bpy.data.materials.new(name))
    bpy.context.view_layer.objects.active = o
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    m = o.modifiers.new("Decimate", "DECIMATE")
    m.ratio = min(1.0, target / tris)
    bpy.ops.object.modifier_apply(modifier=m.name)
    for p in me.polygons:
        p.use_smooth = True
    try:
        me.set_sharp_from_angle(angle=math.radians(35))
    except AttributeError:
        pass
    print(f"far version: {tris} -> {sum(len(p.vertices) - 2 for p in me.polygons)} triangles")
    fvm.export([o], dst, uvs=uvs)


if __name__ == "__main__":
    main()
