"""Massing model of the plain plastered Austrian house (after Tripo picture B): gable to the road, light ochre plaster
all round (no wooden cladding), white windows without shutters, the entrance door in the middle of the ground floor
(no canopy, the owner's choice), a small balcony with a white metal railing in front of a door on the upper floor, an attic
window, red brown tile gable roof. Its four orthographic views go to Tripo's multiview-to-model (as shop_massing.py).
blender -b --factory-startup --python house_massing.py -- <out dir>"""
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


WALL = mat("wall", (0.86, 0.62, 0.30))
ROOF = mat("roof", (0.50, 0.14, 0.06))
PLINTH = mat("plinth", (0.55, 0.53, 0.50))
GLASS = mat("glass", (0.10, 0.12, 0.15), 0.2)
FRAME = mat("frame", (0.93, 0.93, 0.91))
DOOR = mat("door", (0.30, 0.17, 0.08))
RAIL = mat("rail", (0.95, 0.95, 0.95), 0.4)
STEP = mat("step", (0.36, 0.35, 0.33))


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


def cube(c, s, m, rz=0.0, rx=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=c, rotation=(rx, 0, rz))
    o = bpy.context.object
    o.scale = s
    return link(o, m)


W, D, H, PITCH, OVER = 9.5, 10.5, 6.2, math.radians(42), 0.6
RIDGE = H + W / 2 * math.tan(PITCH)
y0, y1 = -D / 2, D / 2

cube((0, 0, 0.0), (W + 0.1, D + 0.1, 0.6), PLINTH)
cube((0, 0, (0.3 + H) / 2), (W, D, H - 0.3), WALL)
for y, s in ((y0, -1), (y1, 1)):
    mesh("gable", [(-W / 2, y, H), (W / 2, y, H), (0, y, RIDGE)], [[0, 1, 2] if s > 0 else [2, 1, 0]], WALL)

tan = math.tan(PITCH)
he = H - OVER * tan
ry0, ry1 = y0 - OVER, y1 + OVER
for side in (-1, 1):
    v = [(side * (W / 2 + OVER), ry0, he), (side * (W / 2 + OVER), ry1, he), (0, ry1, RIDGE), (0, ry0, RIDGE)]
    o = mesh("roof", v, [[0, 1, 2, 3] if side > 0 else [3, 2, 1, 0]], ROOF)
    o.modifiers.new("s", "SOLIDIFY").thickness = 0.25


def window(x, y, z, nx, ny, w=1.0, h=1.3, door=False):
    """White-framed window (or a glazed balcony door) on a wall: centre bottom (x, y, z), outward normal (nx, ny)."""
    rz = math.atan2(-nx, ny)
    cube((x, y, z + h / 2), (w + 0.24, 0.1, h + 0.24), FRAME, rz)
    cube((x + nx * 0.03, y + ny * 0.03, z + h / 2), (w - 0.1, 0.1, h - 0.1), GLASS, rz)
    cube((x + nx * 0.05, y + ny * 0.05, z + h / 2), (0.07, 0.1, h - 0.1), FRAME, rz)
    if not door:
        cube((x, y, z - 0.08), (w + 0.34, 0.22, 0.08), FRAME, rz)


GF, UF = 1.0, 3.9   # sills of the ground and upper floor windows

# Front gable (-Y): door in the middle, two windows; upper floor two windows and a balcony door
cube((0, y0, 1.45), (1.35, 0.14, 2.5), FRAME)
cube((0, y0 - 0.04, 1.42), (1.1, 0.12, 2.3), DOOR)
# two stone steps from the ground (the bottom of the plinth, z -0.3) up to the door
cube((0, y0 - 0.5, -0.15), (2.1, 1.0, 0.3), STEP)
cube((0, y0 - 0.275, 0.135), (1.8, 0.55, 0.27), STEP)
for x in (-2.6, 2.6):
    window(x, y0, GF, 0, -1, w=1.3)
    window(x, y0, UF, 0, -1)
window(0, y0, 3.4, 0, -1, w=0.9, h=2.1, door=True)
cube((0, y0 - 0.4, 3.32), (2.3, 0.8, 0.12), FRAME)                   # balcony slab
cube((0, y0 - 0.78, 3.9), (2.3, 0.04, 0.05), RAIL)                   # top rail
for side in (-1, 1):
    cube((side * 1.13, y0 - 0.4, 3.9), (0.04, 0.8, 0.05), RAIL)
for k in range(13):
    x = -1.1 + k * (2.2 / 12)
    cube((x, y0 - 0.78, 3.62), (0.03, 0.03, 0.56), RAIL)
for side in (-1, 1):
    for k in range(4):
        cube((side * 1.13, y0 - 0.15 - k * 0.2, 3.62), (0.03, 0.03, 0.56), RAIL)
window(0, y0, H + 0.9, 0, -1, w=0.8, h=1.0)

# Sides: three windows on each floor
for side in (-1, 1):
    for y in (-3.0, 0.0, 3.0):
        window(side * W / 2, y, GF, side, 0)
        window(side * W / 2, y, UF, side, 0)

# Back gable (+Y): two windows on each floor, attic window
for x in (-2.2, 2.2):
    window(x, y1, GF, 0, 1)
    window(x, y1, UF, 0, 1)
window(0, y1, H + 0.9, 0, 1, w=0.8, h=1.0)

# Rendering (as shop_massing.py)
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
zc = (RIDGE - 0.3) / 2 + 0.2


def shot(name, loc, rot, scale=14.5):
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
cam.location = (16, -19, 9)
cam.rotation_euler = (Vector((0, 0, 4.5)) - cam.location).to_track_quat("-Z", "Y").to_euler()
scene.render.filepath = os.path.join(out, "persp.png")
bpy.ops.render.render(write_still=True)
print(f"DONE {W} x {D} m, eaves {H}, ridge {RIDGE:.1f}")
