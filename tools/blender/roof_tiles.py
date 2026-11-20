"""Paints rows of clay roof tiles into the base colour texture of a Tripo house whose texture task left the roof a
smooth colour (texture only, geometry untouched). Every roof texel gets its 3D point; rows run level (along the ridge,
Tripo's X axis), each row darker towards its lower edge with a shadow line, tiles in half-offset joints, each tile a
little lighter or darker. The roof's own colour is kept.

  blender -b --factory-startup --python tools/blender/roof_tiles.py -- <in.glb> <out.glb> <model height in m>
"""
import math
import sys

import bpy
import numpy as np
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
src, dst, height_m = args[0], args[1], float(args[2])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
o = [m for m in bpy.context.scene.objects if m.type == "MESH"][0]
mw = o.matrix_world
me = o.data
ws = [mw @ v.co for v in me.vertices]
zlo, zhi = min(p.z for p in ws), max(p.z for p in ws)
unit = (zhi - zlo) / height_m          # model units per metre

ROW = 0.34 * unit                       # tile row along the slope
TILE = 0.24 * unit                      # tile width along the ridge

mat = me.materials[0]
bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
img = bsdf.inputs["Base Color"].links[0].from_node.image
W, H = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
uvl = me.uv_layers.active.data
me.calc_loop_triangles()
rng = np.random.default_rng(7)
shade = rng.uniform(0.9, 1.08, size=(4096, 4096)).astype(np.float32)   # per tile brightness by (row, column) index

done = np.zeros((H, W), dtype=bool)
count = 0
for t in me.loop_triangles:
    n = (mw.to_3x3() @ t.normal).normalized()
    pts = [ws[i] for i in t.vertices]
    if n.z < 0.35 or n.z > 0.97 or sum(p.z for p in pts) / 3 < zlo + 0.5 * (zhi - zlo):
        continue
    # slope coordinate: distance along the roof plane, measured down from the top (level rows)
    slope_len = 1 / max(math.sqrt(max(1 - n.z * n.z, 1e-6)), 1e-3)   # metres along the slope per metre of height
    uv = np.array([[uvl[l].uv.x * W - 0.5, uvl[l].uv.y * H - 0.5] for l in t.loops])
    P = np.array([[p.x, p.y, p.z] for p in pts])
    x0, x1 = int(np.floor(uv[:, 0].min())), int(np.ceil(uv[:, 0].max()))
    y0, y1 = int(np.floor(uv[:, 1].min())), int(np.ceil(uv[:, 1].max()))
    xs, ys = np.meshgrid(np.arange(max(x0, 0), min(x1, W - 1) + 1), np.arange(max(y0, 0), min(y1, H - 1) + 1))
    (ax, ay), (bx, by), (cx, cy) = uv
    den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
    if abs(den) < 1e-9 or xs.size == 0:
        continue
    l1 = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / den
    l2 = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / den
    l3 = 1 - l1 - l2
    inside = (l1 > -0.08) & (l2 > -0.08) & (l3 > -0.08)
    if not inside.any():
        continue
    xs, ys, l1, l2, l3 = xs[inside], ys[inside], l1[inside], l2[inside], l3[inside]
    pos = l1[:, None] * P[0] + l2[:, None] * P[1] + l3[:, None] * P[2]
    s = (zhi - pos[:, 2]) * slope_len / ROW            # rows counted down from the ridge
    row = np.floor(s).astype(int)
    f = s - row                                         # 0 at the top of a row, 1 at its lower edge
    along = pos[:, 0] / TILE + 0.5 * (row % 2)
    col = np.floor(along).astype(int)
    g = along - col
    k = 1.0 - 0.22 * f ** 2                             # each tile darker towards its lower edge
    k = np.where(f > 0.9, 0.62, k)                      # shadow under the next row
    k = np.where((g < 0.06) | (g > 0.94), k * 0.8, k)   # joints between the tiles
    k *= shade[row % 4096, col % 4096]
    sel = ~done[ys, xs]
    ys, xs, k = ys[sel], xs[sel], k[sel]
    done[ys, xs] = True
    px[ys, xs, :3] = np.clip(px[ys, xs, :3] * k[:, None], 0, 1)
    count += len(xs)

print(f"tiles painted on {count} texels (row {ROW / unit:.2f} m, tile {TILE / unit:.2f} m)")
img.pixels[:] = px.ravel()
img.pack()
bpy.ops.export_scene.gltf(filepath=dst)
print("DONE")
