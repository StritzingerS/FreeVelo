"""Massing model of the village shop (after Tripo picture A): gable to the road with the shop front (two display
windows, glazed door, red awning, sign board), plastered walls, windows with green shutters at fixed places, tiled
gable roof. Its four orthographic views go to Tripo's multiview-to-model, so the 3D model gets a clean, consistent
shape (Tripo's own views put the side windows in different places, and the model got overlapping windows).
blender -b --factory-startup --python shop_massing.py -- <out dir>"""
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
ROOF = mat("roof", (0.55, 0.16, 0.07))
PLINTH = mat("plinth", (0.45, 0.44, 0.42))
GLASS = mat("glass", (0.08, 0.10, 0.12), 0.2)
FRAME = mat("frame", (0.92, 0.92, 0.90))
SHUTTER = mat("shutter", (0.03, 0.22, 0.08))
AWNING = mat("awning", (0.38, 0.03, 0.04))
SIGN = mat("sign", (0.55, 0.38, 0.20))
TRIM = mat("trim", (0.92, 0.92, 0.90))


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


W, D, H, PITCH, OVER = 9.5, 11.0, 6.4, math.radians(40), 0.8
RIDGE = H + W / 2 * math.tan(PITCH)
y0, y1 = -D / 2, D / 2

cube((0, 0, 0.0), (W + 0.12, D + 0.12, 0.5), PLINTH)
cube((0, 0, (0.2 + H) / 2), (W, D, H - 0.2), WALL)
for y, s in ((y0, -1), (y1, 1)):
    mesh("gable", [(-W / 2, y, H), (W / 2, y, H), (0, y, RIDGE)], [[0, 1, 2] if s > 0 else [2, 1, 0]], WALL)

# Roof: two planes with overhang at eaves and gables, white barge boards
tan = math.tan(PITCH)
he = H - OVER * tan
ry0, ry1 = y0 - OVER, y1 + OVER
for side in (-1, 1):
    v = [(side * (W / 2 + OVER), ry0, he), (side * (W / 2 + OVER), ry1, he), (0, ry1, RIDGE), (0, ry0, RIDGE)]
    o = mesh("roof", v, [[0, 1, 2, 3] if side > 0 else [3, 2, 1, 0]], ROOF)
    o.modifiers.new("s", "SOLIDIFY").thickness = 0.28
    for y in (ry0, ry1):
        L = (W / 2 + OVER) / math.cos(PITCH)
        cube((side * (W / 2 + OVER) / 2, y, (he + RIDGE) / 2 - 0.12), (L, 0.12, 0.3), TRIM, rx=0)
        bpy.context.object.rotation_euler = (0, side * PITCH, 0)


def window(x, y, z, nx, ny, w=0.95, h=1.25, shutters=True):
    """Window on a wall: centre bottom (x, y, z) on the surface, outward normal (nx, ny)."""
    rz = math.atan2(-nx, ny)
    ax, ay = math.cos(rz), math.sin(rz)  # along the wall
    cube((x, y, z + h / 2), (w + 0.16, 0.1, h + 0.16), FRAME, rz)
    cube((x + nx * 0.03, y + ny * 0.03, z + h / 2), (w - 0.1, 0.1, h - 0.1), GLASS, rz)
    cube((x + nx * 0.05, y + ny * 0.05, z + h / 2), (0.07, 0.1, h - 0.1), FRAME, rz)
    cube((x + nx * 0.05, y + ny * 0.05, z + h * 0.62), (w - 0.1, 0.1, 0.07), FRAME, rz)
    cube((x, y, z - 0.06), (w + 0.3, 0.2, 0.08), FRAME, rz)
    if shutters:
        for s in (-1, 1):
            off = s * (w / 2 + 0.08 + w / 4)
            cube((x + ax * off + nx * 0.04, y + ay * off + ny * 0.04, z + h / 2), (w / 2, 0.08, h + 0.05), SHUTTER, rz)


UP = 4.0       # sill of the upper floor windows
GROUND = 1.0   # sill of the ground floor windows on the sides and back

# Front gable (-Y): shop front
for x in (-2.55, 2.55):
    cube((x, y0, 1.5), (2.8, 0.16, 2.4), FRAME)
    cube((x, y0 - 0.04, 1.55), (2.55, 0.12, 2.15), GLASS)
cube((0, y0, 1.35), (1.35, 0.16, 2.7), FRAME)
cube((0, y0 - 0.04, 1.37), (1.1, 0.12, 2.55), GLASS)
cube((0, y0 - 0.06, 1.37), (0.06, 0.12, 2.55), FRAME)
# awning: sloping cloth with a short front valance
cube((0, y0 - 0.67, 3.05), (W - 0.6, 1.4, 0.1), AWNING, rx=math.radians(18))   # front edge lower
cube((0, y0 - 1.33, 2.68), (W - 0.6, 0.06, 0.32), AWNING)
cube((0, y0 - 0.06, 3.75), (3.4, 0.08, 0.75), SIGN)
for x in (-2.0, 2.0):
    window(x, y0, UP + 0.2, 0, -1)
window(0, y0, 7.5, 0, -1, w=0.7, h=0.9, shutters=False)

# Sides (both the same): two windows on each floor, one above the other
for side in (-1, 1):
    for y in (-2.2, 2.6):
        window(side * W / 2, y, UP + 0.2, side, 0)
        window(side * W / 2, y, GROUND, side, 0)

# Back gable (+Y): two windows on each floor, small attic window
for x in (-2.0, 2.0):
    window(x, y1, UP + 0.2, 0, 1)
    window(x, y1, GROUND, 0, 1)
window(0, y1, 7.5, 0, 1, w=0.7, h=0.9, shutters=False)

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
zc = (RIDGE - 1.0) / 2 + 0.3


def shot(name, loc, rot, ortho=True, scale=15.5):
    cam.data.type = "ORTHO" if ortho else "PERSP"
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
cam.rotation_euler = (Vector((0, 0, 4)) - cam.location).to_track_quat("-Z", "Y").to_euler()
scene.render.filepath = os.path.join(out, "persp.png")
bpy.ops.render.render(write_still=True)
print(f"DONE {W} x {D} m, eaves {H}, ridge {RIDGE:.1f}")
