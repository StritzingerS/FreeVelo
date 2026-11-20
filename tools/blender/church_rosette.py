"""Paints a plain rose window over the round relief on the tower front (+X) of the church candidate, in the base
colour texture only (geometry untouched); the normal map is flattened there. The disc is given in pixels of the
800 px straight-on +X view made by ortho_four.py.
blender -b --factory-startup --python rosette.py -- <in.glb> <out.glb> <cx px> <cy px> <r px>"""
import sys, math
import bpy
import numpy as np
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
src, dst = args[0], args[1]
cxp, cyp, rp = float(args[2]), float(args[3]), float(args[4])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
o = [m for m in bpy.context.scene.objects if m.type == "MESH"][0]
mw = o.matrix_world
me = o.data
ws = [mw @ v.co for v in me.vertices]
lo = Vector((min(p.x for p in ws), min(p.y for p in ws), min(p.z for p in ws)))
hi = Vector((max(p.x for p in ws), max(p.y for p in ws), max(p.z for p in ws)))
c = (lo + hi) / 2
s = max(hi - lo) * 1.08 / 800
cy, cz, R = c.y + (cxp - 400) * s, c.z - (cyp - 400) * s, rp * s
print(f"disc centre y {cy:.3f} z {cz:.3f} radius {R:.3f}")

mat = me.materials[0]
nodes = mat.node_tree.nodes
bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
base_img = bsdf.inputs["Base Color"].links[0].from_node.image


def img_of(socket):
    if not socket.links:
        return None
    n = socket.links[0].from_node
    while n.type != "TEX_IMAGE":
        ins = [i for i in n.inputs if i.links]
        if not ins:
            return None
        n = ins[0].links[0].from_node
    return n.image


nor_img = img_of(bsdf.inputs["Normal"])
W, H = base_img.size
base = np.array(base_img.pixels[:], dtype=np.float32).reshape(H, W, 4)
nor = np.array(nor_img.pixels[:], dtype=np.float32).reshape(nor_img.size[1], nor_img.size[0], 4) if nor_img else None
uvl = me.uv_layers.active.data
me.calc_loop_triangles()

# Front faces of the tower near the disc
tris = []
xf = max(p.x for p in ws if math.hypot(p.y - cy, p.z - cz) < R * 1.6)
for t in me.loop_triangles:
    pts = [ws[i] for i in t.vertices]
    if min(p.x for p in pts) < xf - R * 0.5:
        continue
    if min(math.hypot(p.y - cy, p.z - cz) for p in pts) > R * 1.6 and \
       math.hypot(sum(p.y for p in pts) / 3 - cy, sum(p.z for p in pts) / 3 - cz) > R * 1.6:
        continue
    tris.append((pts, [uvl[l].uv.copy() for l in t.loops]))
print("triangles", len(tris))


def texels(pts, uvs):
    """Yields (row, col, world point) for every texel centre inside the triangle in UV space."""
    P = [(uv.x * W - 0.5, (uv.y) * H - 0.5) for uv in uvs]
    x0, x1 = int(math.floor(min(p[0] for p in P))), int(math.ceil(max(p[0] for p in P)))
    y0, y1 = int(math.floor(min(p[1] for p in P))), int(math.ceil(max(p[1] for p in P)))
    (ax, ay), (bx, by), (qx, qy) = P
    den = (by - qy) * (ax - qx) + (qx - bx) * (ay - qy)
    if abs(den) < 1e-9:
        return
    for yy in range(max(y0, 0), min(y1, H - 1) + 1):
        for xx in range(max(x0, 0), min(x1, W - 1) + 1):
            l1 = ((by - qy) * (xx - qx) + (qx - bx) * (yy - qy)) / den
            l2 = ((qy - ay) * (xx - qx) + (ax - qx) * (yy - qy)) / den
            l3 = 1 - l1 - l2
            if min(l1, l2, l3) < -0.12:
                continue
            yield yy, xx, pts[0] * l1 + pts[1] * l2 + pts[2] * l3


# Plaster colour: median of the ring just outside the disc
ring = []
for pts, uvs in tris:
    for yy, xx, p in texels(pts, uvs):
        r = math.hypot(p.y - cy, p.z - cz) / R
        if 1.5 < r < 1.6:
            ring.append(base[yy, xx, :3])
plaster = np.median(np.array(ring), axis=0)
print("plaster", plaster)

STONE = np.array([0.80, 0.68, 0.52])   # as stored in the texture (sRGB), like the window frames
GLASS = np.array([0.15, 0.16, 0.18])


def rosette(dy, dz):
    r = math.hypot(dy, dz)
    a = math.atan2(dz, dy)
    if r > 1.0:
        return None
    seg = 2 * math.pi / 8
    if r > 0.88:
        return STONE                                  # frame
    if r < 0.07:
        return STONE                                  # boss in the middle
    if 0.33 < r < 0.37:
        return STONE                                  # inner ring
    if r > 0.37:                                      # eight thin spokes between the rings
        k = (a / seg) % 1.0
        width = 0.028 / r / seg
        if k < width or k > 1 - width:
            return STONE
    # foils: a small circle in every outer segment, and eight petals round the centre
    for i in range(8):
        am = (i + 0.5) * seg
        if abs(math.hypot(dy - 0.63 * math.cos(am), dz - 0.63 * math.sin(am)) - 0.17) < 0.022:
            return STONE
        ap = i * seg
        if abs(math.hypot(dy - 0.2 * math.cos(ap), dz - 0.2 * math.sin(ap)) - 0.1) < 0.018 and r < 0.33:
            return STONE
    return GLASS


done = set()
for pts, uvs in tris:
    for yy, xx, p in texels(pts, uvs):
        if (yy, xx) in done:
            continue
        dy, dz = (p.y - cy) / R, (p.z - cz) / R
        r = math.hypot(dy, dz)
        if r > 1.45:
            continue
        done.add((yy, xx))
        col = rosette(dy, dz)
        if col is None:
            o3 = base[yy, xx, :3]
            if o3[0] - o3[2] > 0.12:                   # keep the ochre cornice band
                continue
            t = min(max((r - 1.1) / 0.35, 0), 1)      # plaster over the old clock rim, fading into the original
            col = plaster * (1 - t) + base[yy, xx, :3] * t
        base[yy, xx, :3] = col
        if nor is not None and r < 1.15:
            ny, nx = int(yy * nor.shape[0] / H), int(xx * nor.shape[1] / W)
            nor[ny, nx, :3] = (0.5, 0.5, 1.0)
print("texels", len(done))
base_img.pixels[:] = base.ravel()
base_img.pack()
if nor is not None:
    nor_img.pixels[:] = nor.ravel()
    nor_img.pack()
print("images", base_img.name, nor_img.name if nor_img else None)
base_img.name = "Color_rosette"           # generated_house.py finds Tripo's textures by these name prefixes
if nor_img is not None:
    nor_img.name = "NormalGL_rosette"
bpy.ops.export_scene.gltf(filepath=dst, export_image_format="AUTO")
print("DONE")
