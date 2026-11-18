"""
Tyrolean house for FreeVelo, built by script in Blender (graphics overhaul, vertical slice).

Same footprint and storeys as the coded model SceneryKind.AlpineHouse, variant 0 (SceneryModelsAlpine.cs: 10.5 x 12 m,
one plastered floor, wooden upper floor, 23 degree roof, gable to the front, side balcony), so the placement in the
core does not change. What changes is the geometry: recessed windows with frames, sills and louvred shutters, bevelled
edges, a thick roof with rafter ends and barge boards, sawn balcony boards, brackets and geraniums.

Run headless:
  blender -b --factory-startup --python tools/blender/alpine_house.py -- <out.bytes> [preview.png]

Output: the FreeVelo baked model format read by unity/.../World/BakedModels.cs, already in Unity's coordinates
(x right, y up, z = front; left-handed, clockwise front faces). Material names are WorldMaterial names; texture
coordinates are made in Unity from position and normal (SurfaceTextures.UV), so none are stored here.

Blender coordinates while modelling: X = Unity x, Y = Unity z (front of the house at +Y), Z = Unity y (up).
"""

import math
import struct
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

# ---------------------------------------------------------------- dimensions (as SceneryModelsAlpine.AlpineBody)

W, D = 10.5, 12.0          # width (x) and depth (y, gable to the front)
FLOOR_H = 2.9
PLINTH_TOP = 0.5
PLASTER_TOP = PLINTH_TOP + FLOOR_H          # 3.4: wooden upper floor from here
WALL_TOP = PLINTH_TOP + 2 * FLOOR_H         # 6.3
PITCH = math.radians(23)
RIDGE = WALL_TOP + (W / 2) * math.tan(PITCH)
OVER_EAVE, OVER_GABLE, ROOF_T = 1.3, 1.6, 0.3

COLORS = {  # preview colours only (Unity uses WorldMaterials + textures)
    "Plinth": (0.45, 0.44, 0.42), "WallWhite": (0.9, 0.89, 0.85), "WoodDark": (0.32, 0.21, 0.13),
    "WoodLight": (0.6, 0.43, 0.27), "RoofBrown": (0.38, 0.22, 0.16), "Window": (0.13, 0.17, 0.21),
    "Frame": (0.95, 0.95, 0.93), "Shutter": (0.22, 0.38, 0.24), "Door": (0.36, 0.22, 0.13),
    "LeavesMid": (0.27, 0.44, 0.17), "Geranium": (0.82, 0.12, 0.14), "Stone": (0.58, 0.56, 0.52),
    "RoofGrey": (0.27, 0.28, 0.3), "Iron": (0.16, 0.16, 0.17),
}

# ---------------------------------------------------------------- scene helpers

def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for c in list(bpy.data.collections):
        bpy.data.collections.remove(c)


MATS = {}


def mat(name):
    if name not in MATS:
        m = bpy.data.materials.new(name)
        r, g, b = COLORS.get(name, (0.8, 0.8, 0.8))
        m.diffuse_color = (r, g, b, 1)
        MATS[name] = m
    return MATS[name]


PARTS = []          # objects that end up in the model
CUTTERS = None      # collection of window/door holes (boolean difference on the walls)


def obj_from_bmesh(name, bm, material, collection=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    (collection or bpy.context.scene.collection).objects.link(o)
    if material:
        o.data.materials.append(mat(material))
    for p in o.data.polygons:
        p.use_smooth = False
    if collection is None:
        PARTS.append(o)
    return o


def box(name, center, size, material, rot=None, bevel=0.0, collection=None):
    """Axis-aligned box (full size), optionally rotated about its centre (Matrix) and bevelled."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if rot is not None:
        bmesh.ops.transform(bm, matrix=rot, verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    o = obj_from_bmesh(name, bm, material, collection)
    if bevel > 0:
        add_bevel(o, bevel)
    return o


def add_bevel(o, width, segments=1):
    m = o.modifiers.new("Bevel", "BEVEL")
    m.width = width
    m.segments = segments
    m.limit_method = "ANGLE"
    m.angle_limit = math.radians(40)
    m.harden_normals = False


def prism(name, pts_front, depth_vec, material, bevel=0.0):
    """Extruded polygon: pts_front (counter-clockwise seen from the extrusion side's opposite) moved by depth_vec."""
    bm = bmesh.new()
    a = [bm.verts.new(Vector(p)) for p in pts_front]
    b = [bm.verts.new(Vector(p) + Vector(depth_vec)) for p in pts_front]
    n = len(a)
    bm.faces.new(a)
    bm.faces.new(list(reversed(b)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new([a[i], b[i], b[j], a[j]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = obj_from_bmesh(name, bm, material)
    if bevel > 0:
        add_bevel(o, bevel)
    return o


def blob(name, center, radius, material, subdiv=1, squash=1.0):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    bmesh.ops.scale(bm, vec=Vector((1, 1, squash)), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    return obj_from_bmesh(name, bm, material)


def cutter(name, center, size):
    return box(name, center, size, None, collection=CUTTERS)


def boolean_walls(o):
    m = o.modifiers.new("Holes", "BOOLEAN")
    m.operation = "DIFFERENCE"
    m.operand_type = "COLLECTION"
    m.collection = CUTTERS
    m.solver = "EXACT"
    # The bevel must come after the holes, so the reveals get soft edges too.
    o.modifiers.move(len(o.modifiers) - 1, 0)

# ---------------------------------------------------------------- parts

def rot_axis(axis, angle):
    return Matrix.Rotation(angle, 4, axis)


def facing(side):
    """Unit outward vector and the along-wall vector for a wall: side in 'front','back','left','right'."""
    return {
        "front": (Vector((0, 1, 0)), Vector((1, 0, 0))),
        "back": (Vector((0, -1, 0)), Vector((-1, 0, 0))),
        "right": (Vector((1, 0, 0)), Vector((0, -1, 0))),
        "left": (Vector((-1, 0, 0)), Vector((0, 1, 0))),
    }[side]


def sized(out, along, w_along, h, d_out):
    """Box size for a part on a wall: w_along along the wall, h high, d_out out of the wall."""
    v = Vector((0, 0, h))
    v += Vector([abs(c) for c in along]) * w_along
    v += Vector([abs(c) for c in out]) * d_out
    return v


def window(side, wall_point, z, wood_wall):
    """A recessed window: hole in the wall, glass at the back, white frame with a cross, sill, open louvred shutters."""
    out, along = facing(side)
    ww, wh, recess = 0.95, 1.15, 0.16
    c = Vector(wall_point) + Vector((0, 0, z))
    cutter("hole", c - out * 0.1, sized(out, along, ww, wh, 0.6))
    box("glass", c - out * recess, sized(out, along, ww, wh, 0.03), "Window")
    t = 0.08  # frame
    f = c - out * (recess - 0.04)
    for dz in (-1, 1):
        box("frame", f + Vector((0, 0, dz * (wh - t) / 2)), sized(out, along, ww, t, 0.08), "Frame")
    for da in (-1, 1):
        box("frame", f + along * (da * (ww - t) / 2), sized(out, along, t, wh, 0.08), "Frame")
    box("frame", f, sized(out, along, 0.05, wh, 0.06), "Frame")
    box("frame", f + Vector((0, 0, wh * 0.12)), sized(out, along, ww, 0.05, 0.06), "Frame")
    # Reveal lining on wooden walls (the boards would show in the hole otherwise)
    if wood_wall:
        for da in (-1, 1):
            box("reveal", c - out * (recess / 2) + along * (da * (ww / 2 + 0.01)), sized(out, along, 0.03, wh, recess), "WoodLight")
    # Sill
    box("sill", c + out * 0.05 + Vector((0, 0, -wh / 2 - 0.04)), sized(out, along, ww + 0.2, 0.07, 0.24), "Stone", bevel=0.015)
    # Shutters, open against the wall, with louvres
    for da in (-1, 1):
        sc = c + along * (da * (ww / 2 + 0.3)) + out * 0.04
        box("shutter", sc, sized(out, along, 0.5, wh + 0.1, 0.04), "Shutter")
        for k in range(5):
            zz = -wh / 2 + 0.16 + k * (wh - 0.2) / 4.5
            box("louvre", sc + out * 0.025 + Vector((0, 0, zz)), sized(out, along, 0.4, 0.05, 0.03), "Shutter")


def door(z0):
    out, along = facing("front")
    dw, dh = 1.15, 2.2
    c = Vector((0, D / 2, z0 + dh / 2))
    cutter("doorhole", c, sized(out, along, dw, dh, 0.8))
    box("door", c - out * 0.22, sized(out, along, dw, dh, 0.06), "Door")
    for k in range(-2, 3):  # vertical boards with gaps (the texture runs across)
        box("doorboard", c - out * 0.18 + along * (k * 0.23), sized(out, along, 0.2, dh - 0.1, 0.03), "Door")
    box("doorhandle", c - out * 0.14 + along * 0.4 + Vector((0, 0, -0.1)), sized(out, along, 0.04, 0.2, 0.05), "Iron")
    for da in (-1, 1):
        box("doorframe", c - out * 0.02 + along * (da * (dw / 2 + 0.06)), sized(out, along, 0.14, dh + 0.1, 0.12), "WoodLight", bevel=0.015)
    box("doorframe", c - out * 0.02 + Vector((0, 0, dh / 2 + 0.06)), sized(out, along, dw + 0.4, 0.14, 0.12), "WoodLight", bevel=0.015)
    # (The balcony above shelters the door; no extra canopy.) Two steps
    box("step", Vector((0, D / 2 + 0.5, z0 - 0.12)), (2.0, 1.0, 0.24), "Stone", bevel=0.02)
    box("step", Vector((0, D / 2 + 0.3, z0 - 0.36)), (2.4, 1.4, 0.24), "Stone", bevel=0.02)


def balcony(side, z, length, rng):
    """Tyrolean balcony: floor on brackets, flat sawn boards (Balkonbretter), handrail, flower box with geraniums."""
    out, along = facing(side)
    half = {"front": D / 2, "back": D / 2, "left": W / 2, "right": W / 2}[side] + 0.06
    base = out * half + Vector((0, 0, z))
    depth = 1.3
    box("balfloor", base + out * (depth / 2), sized(out, along, length, 0.14, depth), "WoodLight", bevel=0.01)
    n_br = max(3, int(length / 1.6))
    for i in range(n_br + 1):
        a = -length / 2 + 0.2 + i * (length - 0.4) / n_br
        box("bracket", base + along * a + out * (depth * 0.45) + Vector((0, 0, -0.32)),
            sized(out, along, 0.14, 0.5, depth * 0.9), "WoodDark", bevel=0.02)
    front = base + out * (depth - 0.05)
    box("rail", front + Vector((0, 0, 1.0)), sized(out, along, length, 0.1, 0.16), "WoodLight", bevel=0.015)
    box("rail", front + Vector((0, 0, 0.14)), sized(out, along, length, 0.08, 0.1), "WoodLight")
    for s in (-1, 1):  # side rails back to the wall
        sc = base + along * (s * (length / 2 - 0.05)) + out * (depth / 2)
        box("siderail", sc + Vector((0, 0, 1.0)), sized(out, along, 0.1, 0.1, depth), "WoodLight", bevel=0.015)
    boards = int(length / 0.19)
    for i in range(boards):
        a = -length / 2 + 0.1 + i * (length - 0.2) / max(1, boards - 1)
        bc = front + along * a + Vector((0, 0, 0.57))
        box("board", bc, sized(out, along, 0.13, 0.82, 0.025), "WoodLight")
    # Flower box on the rail, geraniums hanging over
    fb = front + out * 0.12 + Vector((0, 0, 1.18))
    box("flowerbox", fb, sized(out, along, length * 0.96, 0.24, 0.3), "WoodDark", bevel=0.01)
    clumps = int(length / 0.45)
    for i in range(clumps):
        a = -length * 0.46 + i * (length * 0.92) / max(1, clumps - 1)
        c = fb + along * a + Vector((0, 0, 0.2))
        blob("leaves", c + out * rng(-0.05, 0.08), rng(0.2, 0.26), "LeavesMid", squash=0.7)
        for k in range(2):
            off = along * rng(-0.15, 0.15) + out * rng(0.0, 0.18) + Vector((0, 0, rng(0.1, 0.2)))
            blob("geranium", c + off, rng(0.1, 0.13), "Geranium", subdiv=1)
        if i % 2 == 0:
            blob("hanging", c + out * 0.2 + Vector((0, 0, -0.18)), 0.12, "LeavesMid", subdiv=1, squash=1.4)


def roof():
    tan = math.tan(PITCH)
    run = W / 2 + OVER_EAVE
    slope = run / math.cos(PITCH)
    eave_z = WALL_TOP - OVER_EAVE * tan
    length = D + 2 * OVER_GABLE
    for s in (-1, 1):
        r = rot_axis("Y", s * PITCH)
        centre = Vector((s * run / 2, 0, (eave_z + RIDGE) / 2)) + r @ Vector((0, 0, ROOF_T / 2))
        box("roof", centre, (slope, length, ROOF_T), "RoofBrown", rot=r, bevel=0.03)
        # Rafter ends under the eaves (every 0.9 m), from the wall out to the roof edge, along the slope
        k = -length / 2 + 0.45
        while k < length / 2:
            inner = Vector((s * (W / 2), k, WALL_TOP - 0.05))
            outer = Vector((s * (run - 0.05), k, eave_z - 0.05))
            box("rafter", (inner + outer) / 2, ((outer - inner).length, 0.12, 0.16), "WoodLight", rot=r, bevel=0.01)
            k += 0.9
        # Fascia along the eave
        box("fascia", Vector((s * (run - 0.03), 0, eave_z + 0.05)) , (0.06, length, 0.26), "WoodLight")
    # Barge boards along both gable edges, front and back
    for y in (-1, 1):
        for s in (-1, 1):
            r = rot_axis("Y", s * PITCH)
            centre = Vector((s * run / 2, y * (length / 2 - 0.04), (eave_z + RIDGE) / 2 - 0.1)) + r @ Vector((0, 0, 0.0))
            box("barge", centre, (slope, 0.08, 0.34), "WoodLight", rot=r, bevel=0.01)
    # Purlins: beams along the house under the roof, their ends carry the gable overhang
    for x in (0.0, -W * 0.25, W * 0.25, -W * 0.5, W * 0.5):
        z = RIDGE - abs(x) * tan - 0.13
        box("purlin", Vector((x, 0, z)), (0.2, length - 0.12, 0.24), "WoodDark", bevel=0.015)
    # Ridge cap
    box("ridge",Vector((0, 0, RIDGE + ROOF_T + 0.02)), (0.32, length, 0.12), "RoofGrey", bevel=0.02)


def gables():
    for y in (-1, 1):
        yy = y * (D / 2 + 0.06)
        pts = [(-W / 2 - 0.06, yy, WALL_TOP), (W / 2 + 0.06, yy, WALL_TOP), (0, yy, RIDGE)]
        boolean_walls(prism("gable", pts, (0, -y * 0.3, 0), "WoodDark"))  # the gable window needs its hole


def chimney():
    x, y = W * 0.22, -D * 0.15
    box("chimney", Vector((x, y, RIDGE - 0.1)), (0.7, 0.7, 2.2), "WallWhite", bevel=0.03)
    box("chimneycap", Vector((x, y, RIDGE + 1.05)), (1.0, 1.0, 0.1), "RoofGrey", bevel=0.01)
    for dx in (-1, 1):
        box("chimneypost", Vector((x + dx * 0.4, y, RIDGE + 0.95)), (0.08, 0.08, 0.2), "Iron")


# ---------------------------------------------------------------- the house

def build():
    global CUTTERS
    clear()
    CUTTERS = bpy.data.collections.new("Cutters")
    bpy.context.scene.collection.children.link(CUTTERS)

    seed = [12345]

    def rng(lo, hi):
        seed[0] = (seed[0] * 1103515245 + 12345) & 0x7FFFFFFF
        return lo + (hi - lo) * (seed[0] / 0x7FFFFFFF)

    # Plinth (reaches 2.8 m into the ground, as in the core, so slopes never show a gap)
    box("plinth", Vector((0, 0, (PLINTH_TOP - 2.3) / 2)), (W + 0.3, D + 0.3, PLINTH_TOP + 2.3), "Plinth", bevel=0.04)
    walls = box("plaster", Vector((0, 0, (PLINTH_TOP + PLASTER_TOP) / 2)), (W, D, PLASTER_TOP - PLINTH_TOP), "WallWhite", bevel=0.05)
    upper = box("upper", Vector((0, 0, (PLASTER_TOP + WALL_TOP) / 2)), (W + 0.12, D + 0.12, WALL_TOP - PLASTER_TOP), "WoodDark", bevel=0.02)
    # Beam between the floors
    box("beam", Vector((0, 0, PLASTER_TOP + 0.04)), (W + 0.2, D + 0.2, 0.16), "WoodLight", bevel=0.02)

    # Windows: front and back on both floors, sides too (as the coded model)
    for floor in range(2):
        z = PLINTH_TOP + floor * FLOOR_H + FLOOR_H * 0.55
        wood = floor == 1
        wall_out = 0.06 if wood else 0.0
        per_row = max(2, int(W / 3.2))
        for i in range(per_row):
            x = -W / 2 + W * (i + 0.5) / per_row
            if not (floor == 0 and abs(x) < 1.4):
                window("front", (x, D / 2 + wall_out, 0), z, wood)
            window("back", (x, -D / 2 - wall_out, 0), z, wood)
        per_side = max(2, int(D / 3.6))
        for i in range(per_side):
            y = -D / 2 + D * (i + 0.5) / per_side
            window("right", (W / 2 + wall_out, y, 0), z, wood)
            window("left", (-W / 2 - wall_out, y, 0), z, wood)
    window("front", (0, D / 2 + 0.06, 0), WALL_TOP + 1.0, True)   # gable window

    door(PLINTH_TOP)
    boolean_walls(walls)
    boolean_walls(upper)

    balcony("front", PLASTER_TOP, W + 0.4, rng)
    balcony("right", PLASTER_TOP, D * 0.7, rng)       # variant 0 has the side balcony
    gables()
    roof()
    chimney()

    # The gable window sits in the gable prism, not in a wall with holes: keep its glass in front of the boards.
    CUTTERS.hide_render = True
    for o in CUTTERS.objects:
        o.hide_render = True
        o.hide_viewport = True


# ---------------------------------------------------------------- export

def export(path):
    deps = bpy.context.evaluated_depsgraph_get()
    groups = {}  # material -> (positions, normals, indices, index of (pos, normal))
    for o in PARTS:
        eo = o.evaluated_get(deps)
        me = eo.to_mesh()
        me.calc_loop_triangles()
        mw = o.matrix_world
        nm = mw.to_3x3().inverted().transposed()
        names = [s.material.name if s.material else "Plinth" for s in o.material_slots]
        for tri in me.loop_triangles:
            name = names[tri.material_index] if names else "Plinth"
            pos, nor, idx, lookup = groups.setdefault(name, ([], [], [], {}))
            corner = []
            for k in range(3):
                p = mw @ me.vertices[tri.vertices[k]].co
                try:
                    local_n = Vector(tri.split_normals[k])
                except AttributeError:  # newer API: per-corner normals on the mesh
                    local_n = Vector(me.corner_normals[tri.loops[k]].vector)
                n = (nm @ local_n).normalized()
                # Blender (x, y, z-up) -> Unity (x, y-up, z): swap y and z
                up = (round(p.x, 4), round(p.z, 4), round(p.y, 4))
                un = (round(n.x, 3), round(n.z, 3), round(n.y, 3))
                key = (up, un)
                i = lookup.get(key)
                if i is None:
                    i = len(pos)
                    lookup[key] = i
                    pos.append(up)
                    nor.append(un)
                corner.append(i)
            # Swapping two axes mirrors the mesh: reverse the winding so Unity sees the front faces.
            idx.extend((corner[0], corner[2], corner[1]))
        eo.to_mesh_clear()

    tris = 0
    with open(path, "wb") as f:
        f.write(b"FVM1")
        f.write(struct.pack("<i", len(groups)))
        for name in sorted(groups):
            pos, nor, idx, _ = groups[name]
            data = name.encode("utf-8")
            f.write(struct.pack("<i", len(data)))
            f.write(data)
            f.write(struct.pack("<i", len(pos)))
            for p in pos:
                f.write(struct.pack("<3f", *p))
            for n in nor:
                f.write(struct.pack("<3f", *n))
            f.write(struct.pack("<i", len(idx)))
            f.write(struct.pack("<%di" % len(idx), *idx))
            tris += len(idx) // 3
            print(f"  {name:10s} {len(pos):6d} vertices {len(idx) // 3:6d} triangles")
    print(f"wrote {path}: {tris} triangles in {len(groups)} materials")


def preview(path):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "MATERIAL"
    shading.show_shadows = True
    shading.show_cavity = True
    scene.render.resolution_x, scene.render.resolution_y = 1280, 860
    scene.world = scene.world or bpy.data.worlds.new("World")
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = 35
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = Vector((13.5, 20.0, 6.5))
    direction = Vector((0, 0, 4.0)) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    ground = bmesh.new()
    bmesh.ops.create_grid(ground, x_segments=1, y_segments=1, size=30)
    obj_from_bmesh("ground", ground, "LeavesMid", collection=scene.collection)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = argv[0] if argv else "AlpineHouse_0.bytes"
    build()
    export(out)
    if len(argv) > 1:
        preview(argv[1])
