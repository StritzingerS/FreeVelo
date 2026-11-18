"""
Shared by the FreeVelo Blender scripts: writes the baked model format "FVM1" read by
unity/FreeVelo/Assets/FreeVelo/Scripts/World/BakedModels.cs.

Per material (named after a WorldMaterial): positions and normals in Unity space (x right, y up, z = front; the
Blender model is built with its front at +Y and Z up) and triangles with Unity's clockwise front faces.
"""

import struct

from mathutils import Vector

# Preview colours (Workbench renders only; Unity uses WorldMaterials and the surface textures).
COLORS = {
    "Plinth": (0.45, 0.44, 0.42), "WallWhite": (0.9, 0.89, 0.85), "WallCream": (0.93, 0.86, 0.7),
    "WoodDark": (0.32, 0.21, 0.13), "WoodLight": (0.6, 0.43, 0.27), "RoofBrown": (0.38, 0.22, 0.16),
    "Window": (0.13, 0.17, 0.21), "Frame": (0.95, 0.95, 0.93), "Shutter": (0.22, 0.38, 0.24),
    "Door": (0.36, 0.22, 0.13), "LeavesMid": (0.27, 0.44, 0.17), "Geranium": (0.82, 0.12, 0.14),
    "Stone": (0.58, 0.56, 0.52), "RoofGrey": (0.27, 0.28, 0.3), "Iron": (0.16, 0.16, 0.17),
    "Shingle": (0.33, 0.29, 0.26), "Gold": (0.86, 0.68, 0.26), "SignBlue": (0.1, 0.25, 0.55),
    "Bark": (0.30, 0.23, 0.17), "LarchNeedles": (0.45, 0.58, 0.25),
}


def export(objects, path):
    """Writes the evaluated meshes of <objects> (modifiers applied, world space) to <path>; returns the triangle count."""
    import bpy

    deps = bpy.context.evaluated_depsgraph_get()
    groups = {}  # material -> (positions, normals, indices, lookup of (pos, normal) -> index)
    for o in objects:
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
                # Blender (x, y = front, z = up) -> Unity (x, y = up, z = front): swap y and z.
                up = (round(p.x, 4), round(p.z, 4), round(p.y, 4))
                un = (round(n.x, 3), round(n.z, 3), round(n.y, 3))
                i = lookup.get((up, un))
                if i is None:
                    i = len(pos)
                    lookup[(up, un)] = i
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
    return tris


def material(name):
    """A Blender material named after a WorldMaterial, with its preview colour."""
    import bpy

    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    r, g, b = COLORS.get(name, (0.8, 0.8, 0.8))
    m.diffuse_color = (r, g, b, 1)
    return m


def preview(path, target, distance, height, angle_deg=35, size=(1280, 860)):
    """Workbench render (material colours, shadows, cavity) from the front-right, looking at <target>."""
    import math

    import bmesh
    import bpy

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "MATERIAL"
    shading.show_shadows = True
    shading.show_cavity = True
    scene.render.resolution_x, scene.render.resolution_y = size
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.lens = 35
    scene.collection.objects.link(cam)
    a = math.radians(angle_deg)
    t = Vector(target)
    cam.location = t + Vector((math.sin(a) * distance, math.cos(a) * distance, height))
    cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=distance * 2)
    me = bpy.data.meshes.new("ground")
    bm.to_mesh(me)
    bm.free()
    ground = bpy.data.objects.new("ground", me)
    scene.collection.objects.link(ground)
    ground.data.materials.append(material("LeavesMid"))
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
