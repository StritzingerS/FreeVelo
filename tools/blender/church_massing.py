"""Massing model of the village church with an exact plan (rectangular nave, 3/8 polygonal choir end the full width,
square tower centred in front of the gable, entrance at the tower base, side door), rendered as four orthographic
views, a top view and a 3/4 view. blender -b --factory-startup --python church_block.py -- <out dir>"""
import sys, math, os
import bpy

out = sys.argv[sys.argv.index("--") + 1:][0]
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def mat(name, rgb, rough=0.85):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    return m


WALL = mat("wall", (0.80, 0.78, 0.72))
TRIM = mat("trim", (0.62, 0.48, 0.28))
PLINTH = mat("plinth", (0.30, 0.29, 0.27))
ROOF = mat("roof", (0.16, 0.14, 0.13))
DOME = mat("dome", (0.10, 0.22, 0.17), 0.5)
WIN = mat("window", (0.04, 0.05, 0.06), 0.3)
DOOR = mat("door", (0.22, 0.12, 0.05))
STONE = mat("stone", (0.50, 0.49, 0.46))
DIAL = mat("dial", (0.85, 0.83, 0.75))
GOLD = mat("gold", (0.60, 0.45, 0.12), 0.3)


def link(o, m):
    o.data.materials.append(m)
    return o


def mesh(name, verts, faces, m):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    o = bpy.data.objects.new(name, me)
    scene.collection.objects.link(o)
    return link(o, m)


def cube(c, s, m, rz=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=c, rotation=(0, 0, rz))
    o = bpy.context.object
    o.scale = s
    return link(o, m)


def cyl(c, r, depth, m, rot=(0, 0, 0), n=24):
    bpy.ops.mesh.primitive_cylinder_add(vertices=n, radius=r, depth=depth, location=c, rotation=rot)
    return link(bpy.context.object, m)


def prism(name, outline, z0, z1, m):
    k = len(outline)
    vs = [(x, y, z0) for x, y in outline] + [(x, y, z1) for x, y in outline]
    fs = [list(range(k))[::-1], list(range(k, 2 * k))]
    fs += [[i, (i + 1) % k, k + (i + 1) % k, k + i] for i in range(k)]
    return mesh(name, vs, fs, m)


def offset(outline, d):
    # outward offset of a convex counter-clockwise polygon
    k = len(outline)
    res = []
    for i in range(k):
        p0, p1, p2 = outline[i - 1], outline[i], outline[(i + 1) % k]
        n = []
        for a, b in ((p0, p1), (p1, p2)):
            ex, ey = b[0] - a[0], b[1] - a[1]
            l = math.hypot(ex, ey)
            n.append((ey / l, -ex / l))
        bx, by = n[0][0] + n[1][0], n[0][1] + n[1][1]
        dot = bx * n[0][0] + by * n[0][1]
        res.append((p1[0] + bx * d / dot, p1[1] + by * d / dot))
    return res


def arched(x, y, nx, ny, w, h, z, m, depth=0.14, frame=None):
    """An opening with a round top on a wall: centre (x, y) on the wall surface, outward normal (nx, ny),
    width w, straight height h from the sill z."""
    rz = math.atan2(-nx, ny)
    if frame:
        arched(x + nx * 0.01, y + ny * 0.01, nx, ny, w + 0.3, h, z - 0.15, frame, depth)
        x, y = x + nx * 0.03, y + ny * 0.03
    cube((x, y, z + h / 2), (w, depth, h), m, rz)
    cyl((x, y, z + h), w / 2, depth, m, (math.pi / 2, 0, rz))


W, L, H, PITCH, OVER = 12.0, 18.5, 9.0, math.radians(50), 0.6
HS = W / 2 * math.tan(math.radians(22.5))            # half side of the octagon
CY = L - HS                                          # centre of the choir octagon
BACK = CY + W / 2
TAN = math.tan(PITCH)
RIDGE = H + W / 2 * TAN

nave = [(-W / 2, 0), (W / 2, 0), (W / 2, L), (HS, BACK), (-HS, BACK), (-W / 2, L)]
prism("plinth", offset(nave, 0.15), -1.0, 0.8, PLINTH)
prism("nave", nave, -1.0, H, WALL)
prism("eave_band", offset(nave, 0.12), H - 0.45, H, TRIM)
mesh("gable", [(-W / 2, 0, H), (W / 2, 0, H), (0, 0, RIDGE), (-W / 2, 0.3, H), (W / 2, 0.3, H), (0, 0.3, RIDGE)],
     [[0, 1, 2], [5, 4, 3]], WALL)

# Roof: gable over the nave, hipped over the polygonal end; every plane rises at the same pitch from its eave
r = W / 2 + OVER
hs = r * math.tan(math.radians(22.5))
he = H - OVER * TAN
F = -0.6
v = [(-r, F, he), (r, F, he), (r, CY + hs, he), (hs, CY + r, he), (-hs, CY + r, he), (-r, CY + hs, he),
     (0, F, RIDGE), (0, CY, RIDGE)]
roof = mesh("roof", v, [[1, 2, 7, 6], [2, 3, 7], [3, 4, 7], [4, 5, 7], [5, 0, 6, 7]], ROOF)
sol = roof.modifiers.new("s", "SOLIDIFY")
sol.thickness = 0.3

# Windows along the nave and in the two diagonal choir walls, the side door on the right near the front
for side in (-1, 1):
    for y in (6.3, 10.0, 13.7):
        arched(side * W / 2, y, side, 0, 1.3, 3.4, 3.6, WIN, frame=TRIM)
    mx, my = side * (W / 2 + HS) / 2, (L + BACK) / 2
    arched(mx, my, side * 0.7071, 0.7071, 1.3, 3.4, 3.6, WIN, frame=TRIM)
arched(W / 2, 2.6, 1, 0, 1.3, 2.1, 0.0, DOOR, frame=STONE)
cube((W / 2 + 0.75, 2.6, 3.25), (1.6, 2.4, 0.15), ROOF)                     # small canopy over the side door
arched(-W / 2, 2.6, -1, 0, 1.3, 3.0, 4.2, WIN, frame=TRIM)                  # a window where the right has the door

# Tower: square, centred in front of the gable, door at its base towards the street (-Y)
S, TT = 5.6, 22.5
ty0 = -S + 0.4
tc = ty0 + S / 2
prism("tower_plinth", offset([(-S / 2, ty0), (S / 2, ty0), (S / 2, ty0 + S), (-S / 2, ty0 + S)], 0.15), -1.0, 0.8, PLINTH)
prism("tower", [(-S / 2, ty0), (S / 2, ty0), (S / 2, ty0 + S), (-S / 2, ty0 + S)], -1.0, TT, WALL)
for z in (H - 0.45, 17.0, TT - 0.5):
    prism("band", offset([(-S / 2, ty0), (S / 2, ty0), (S / 2, ty0 + S), (-S / 2, ty0 + S)], 0.12), z, z + 0.5, TRIM)
for sx in (-1, 1):
    for sy in (-1, 1):
        cube((sx * (S / 2 - 0.2), tc + sy * (S / 2 - 0.2), TT / 2), (0.55, 0.55, TT), TRIM)
arched(0, ty0, 0, -1, 1.5, 2.3, 0.0, DOOR, frame=STONE)
for nx, ny, cx, cy in ((0, -1, 0, ty0), (1, 0, S / 2, tc), (-1, 0, -S / 2, tc), (0, 1, 0, ty0 + S)):
    rz = math.atan2(-nx, ny)
    if ny != 1:
        arched(cx, cy, nx, ny, 0.45, 1.0, 11.5, WIN)                         # slit window
    arched(cx, cy, nx, ny, 1.4, 2.0, 18.2, WIN, frame=TRIM)                  # belfry
    cyl((cx + nx * 0.03, cy + ny * 0.03, 15.2), 1.15, 0.12, PLINTH, (math.pi / 2, 0, rz), 32)  # clock rim
    cyl((cx + nx * 0.05, cy + ny * 0.05, 15.2), 1.0, 0.12, DIAL, (math.pi / 2, 0, rz), 32)   # clock
    cyl((cx + nx * 0.06, cy + ny * 0.06, 15.2), 0.12, 0.14, GOLD, (math.pi / 2, 0, rz), 12)

# Onion dome with a lantern, spire and cross
prof = [(3.05, 0.0), (3.05, 0.25), (2.7, 0.35), (3.0, 1.2), (2.85, 2.2), (2.1, 3.1), (1.0, 3.8), (0.55, 4.3),
        (0.55, 5.2), (0.95, 5.7), (0.85, 6.2), (0.35, 6.7), (0.08, 8.2)]
n = 32
vs, fs = [], []
for i, (rr, z) in enumerate(prof):
    for j in range(n):
        a = 2 * math.pi * j / n
        vs.append((rr * math.cos(a), tc + rr * math.sin(a), TT + z))
for i in range(len(prof) - 1):
    for j in range(n):
        a, b = i * n + j, i * n + (j + 1) % n
        fs.append([a, b, b + n, a + n])
top = len(vs)
vs.append((0, tc, TT + prof[-1][1]))
fs += [[(len(prof) - 1) * n + j, (len(prof) - 1) * n + (j + 1) % n, top] for j in range(n)]
dome = mesh("dome", vs, fs, DOME)
for p in dome.data.polygons:
    p.use_smooth = True
cube((0, tc, TT + 9.0), (0.1, 0.1, 1.8), GOLD)
cube((0, tc, TT + 9.3), (0.8, 0.1, 0.1), GOLD)
cube((0, tc, TT + 9.3), (0.1, 0.8, 0.1), GOLD)

# Ground plate so nothing reads as floating in the views
bpy.ops.object.select_all(action="DESELECT")

# Rendering
for e in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
    try:
        scene.render.engine = e
        break
    except TypeError:
        continue
scene.view_settings.view_transform = "Standard"
world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (1, 1, 1, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.55
scene.world = world
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sun.data.energy = 3.2
sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
scene.collection.objects.link(sun)
scene.render.film_transparent = True
scene.render.resolution_x = scene.render.resolution_y = 1024

cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
scene.camera = cam
midy = (ty0 + BACK) / 2
zc = (TT + 9.6 - 1.0) / 2


def shot(name, loc, rot, ortho=True, scale=36.0, lens=50):
    cam.data.type = "ORTHO" if ortho else "PERSP"
    cam.data.ortho_scale = scale
    cam.data.lens = lens
    cam.location = loc
    cam.rotation_euler = [math.radians(a) for a in rot]
    scene.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)


shot("front", (0, -80, zc), (90, 0, 0))
shot("back", (0, 80, zc), (90, 0, 180))
shot("right", (80, midy, zc), (90, 0, 90))
shot("left", (-80, midy, zc), (90, 0, -90))
sun.rotation_euler = (math.radians(35), 0, math.radians(-35))
shot("top", (0, midy, 80), (0, 0, 0), scale=34)
from mathutils import Vector
cam.data.type = "PERSP"
cam.data.lens = 35
cam.location = (40, -38, 20)
cam.rotation_euler = (Vector((0, midy, 11)) - cam.location).to_track_quat("-Z", "Y").to_euler()
scene.render.filepath = os.path.join(out, "persp.png")
bpy.ops.render.render(write_still=True)
bpy.ops.export_scene.gltf(filepath=os.path.join(out, "church_block.glb"))
print(f"DONE length {BACK - ty0:.1f} width {W} ridge {RIDGE:.1f} top {TT + 9.6:.1f}")
