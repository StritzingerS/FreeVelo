"""Massing model of the village fire station (style of Tripo picture B): gable to the road with two big dark red
garage doors, dark wooden boarding in the gable, white plaster, windows with brown shutters, grey shingle roof, a
square hose tower at a back corner with wooden louvres at the top and a pyramid roof. Its four orthographic views go
to Tripo's multiview-to-model, so the 3D model gets a clean, consistent shape (as church_massing.py, shop_massing.py).
blender -b --factory-startup --python firestation_massing.py -- <out dir>"""
import sys, math, os
import bpy
from mathutils import Vector

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


WALL = mat("wall", (0.85, 0.84, 0.80))
ROOF = mat("roof", (0.10, 0.10, 0.11))
PLINTH = mat("plinth", (0.45, 0.44, 0.42))
GLASS = mat("glass", (0.08, 0.10, 0.12), 0.2)
FRAME = mat("frame", (0.92, 0.92, 0.90))
SHUTTER = mat("shutter", (0.12, 0.05, 0.02))
DOOR = mat("door", (0.30, 0.02, 0.03))
WOOD = mat("wood", (0.14, 0.07, 0.03))
LOUVRE = mat("louvre", (0.22, 0.12, 0.06))


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


W, D, H, PITCH, OVER = 13.0, 12.0, 4.6, math.radians(25), 1.0
RIDGE = H + W / 2 * math.tan(PITCH)
y0, y1 = -D / 2, D / 2

cube((0, 0, 0.0), (W + 0.12, D + 0.12, 0.6), PLINTH)
cube((0, 0, (0.3 + H) / 2), (W, D, H - 0.3), WALL)
# gables: dark wooden boarding above the eaves line
for y, s in ((y0, -1), (y1, 1)):
    mesh("gable", [(-W / 2, y + s * 0.06, H), (W / 2, y + s * 0.06, H), (0, y + s * 0.06, RIDGE)], [[0, 1, 2] if s > 0 else [2, 1, 0]], WOOD)
    cube((0, y + s * 0.08, H - 0.05), (W + 0.1, 0.12, 0.18), WOOD)

tan = math.tan(PITCH)
he = H - OVER * tan
ry0, ry1 = y0 - OVER, y1 + OVER
for side in (-1, 1):
    v = [(side * (W / 2 + OVER), ry0, he), (side * (W / 2 + OVER), ry1, he), (0, ry1, RIDGE), (0, ry0, RIDGE)]
    o = mesh("roof", v, [[0, 1, 2, 3] if side > 0 else [3, 2, 1, 0]], ROOF)
    o.modifiers.new("s", "SOLIDIFY").thickness = 0.3


def window(x, y, z, nx, ny, w=0.9, h=1.2, shutters=True):
    rz = math.atan2(-nx, ny)
    ax, ay = math.cos(rz), math.sin(rz)
    cube((x, y, z + h / 2), (w + 0.16, 0.1, h + 0.16), FRAME, rz)
    cube((x + nx * 0.03, y + ny * 0.03, z + h / 2), (w - 0.1, 0.1, h - 0.1), GLASS, rz)
    cube((x + nx * 0.05, y + ny * 0.05, z + h / 2), (0.07, 0.1, h - 0.1), FRAME, rz)
    if shutters:
        for s in (-1, 1):
            off = s * (w / 2 + 0.08 + w / 4)
            cube((x + ax * off + nx * 0.04, y + ay * off + ny * 0.04, z + h / 2), (w / 2, 0.08, h + 0.05), SHUTTER, rz)


# Front gable (-Y): two garage doors with a row of small panes, a window at each side
for x in (-2.0, 2.0):
    cube((x, y0, 1.95), (3.3, 0.14, 3.7), FRAME)
    cube((x, y0 - 0.03, 1.9), (3.0, 0.14, 3.5), DOOR)
    for k in range(6):
        cube((x - 1.1 + k * 0.44, y0 - 0.06, 3.15), (0.26, 0.12, 0.2), GLASS)
    for k in range(1, 6):
        cube((x, y0 - 0.05, 0.3 + k * 0.55), (2.95, 0.1, 0.03), WOOD)
for x in (-5.2, 5.2):
    window(x, y0, 1.4, 0, -1)
window(0, y0 - 0.06, H + 1.0, 0, -1, w=0.8, h=0.9, shutters=False)   # small window in the boarded gable

# Sides: three windows each; back: two windows and a door
for side in (-1, 1):
    for y in ((-3.5, 0.5, 4.0) if side > 0 else (-3.5, 0.5)):   # the tower stands at the back left
        window(side * W / 2, y, 1.4, side, 0)
for x in (-1.5, 2.5):
    window(x, y1, 1.4, 0, 1)
cube((5.0, y1, 1.15), (1.1, 0.14, 2.2), WOOD)

# Hose tower at the back left corner, rising through the eaves
S, T = 3.4, 12.5
tx, ty = -W / 2 + S / 2 - 0.3, y1 - S / 2 + 0.3
cube((tx, ty, 0.0), (S + 0.12, S + 0.12, 0.6), PLINTH)
cube((tx, ty, (0.3 + T - 2.4) / 2), (S, S, T - 2.4 - 0.3), WALL)
cube((tx, ty, T - 1.2), (S, S, 2.4), WOOD)                  # louvred top
for f in range(4):
    a = f * math.pi / 2
    nx, ny = math.sin(a), -math.cos(a)
    for k in range(6):
        cube((tx + nx * (S / 2 + 0.02), ty + ny * (S / 2 + 0.02), T - 2.2 + k * 0.36), (S - 0.4, 0.06, 0.08) if abs(ny) > 0.5 else (0.06, S - 0.4, 0.08), LOUVRE)
r = S / 2 + 0.5
mesh("tower_roof", [(tx - r, ty - r, T), (tx + r, ty - r, T), (tx + r, ty + r, T), (tx - r, ty + r, T), (tx, ty, T + 2.2)],
     [[0, 1, 4], [1, 2, 4], [2, 3, 4], [3, 0, 4], [3, 2, 1, 0]], ROOF)
window(tx, ty - S / 2, 6.5, 0, -1, w=0.6, h=0.9, shutters=False)
window(tx - S / 2, ty, 6.5, -1, 0, w=0.6, h=0.9, shutters=False)

# Rendering (as church_massing.py)
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
zc = (T + 2.2 - 0.3) / 2


def shot(name, loc, rot, scale=17.5):
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = scale
    cam.location = loc
    cam.rotation_euler = [math.radians(a) for a in rot]
    scene.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)


shot("front", (0, -60, zc), (90, 0, 0))
shot("back", (0, 60, zc), (90, 0, 180))
shot("right", (60, 0, zc), (90, 0, 90))
shot("left", (-60, 0, zc), (90, 0, -90))
cam.data.type = "PERSP"
cam.data.lens = 35
cam.location = (19, -22, 11)
cam.rotation_euler = (Vector((0, 0, 4.5)) - cam.location).to_track_quat("-Z", "Y").to_euler()
scene.render.filepath = os.path.join(out, "persp.png")
bpy.ops.render.render(write_still=True)
print(f"DONE {W} x {D} m, eaves {H}, ridge {RIDGE:.1f}, tower {T + 2.2:.1f}")
