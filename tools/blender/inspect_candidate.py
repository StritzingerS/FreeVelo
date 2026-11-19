"""
Acceptance check for a generated model BEFORE the owner is asked to approve it (docs/GRAPHICS_OVERHAUL.md, 3b).
Finds what must not be cleaned up by "surgery" later, marks it in red on the approval picture and lists it:

- attachments: compact pieces sticking out of a building's walls below the eaves (lamps, gutters, funnels...);
  a balcony or bracket row along most of a wall does not count;
- loose parts: small pieces not connected to the main body (floating bits);
- holes: open edges above the ground (after joining the texture seams);
- inside-out faces: faces pointing into a closed body with nothing in front of them;
- the triangle count against the limit.

  blender -b --factory-startup --python tools/blender/inspect_candidate.py -- <in.glb> <building|tree|prop>
      <triangle limit> <out.png>

Prints "FINDING ..." lines and "VERDICT clean" or "VERDICT findings: n". The picture: the textured model from four
sides (top row) and grey with the findings in red (bottom row).
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.parent = None
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return o


def islands(bm):
    seen, result = set(), []
    for f in bm.faces:
        if f in seen:
            continue
        group, stack = [], [f]
        seen.add(f)
        while stack:
            g = stack.pop()
            group.append(g)
            for e in g.edges:
                for n in e.link_faces:
                    if n not in seen:
                        seen.add(n)
                        stack.append(n)
        result.append(group)
    return result


def wall_plane(bm, axis, sign, z0, z1):
    """Where the most area of faces looking that way lies (5 % steps of the size): the wall, not windows or bumps."""
    bins = {}
    for f in bm.faces:
        c = f.calc_center_median()
        if f.normal[axis] * sign > 0.9 and z0 < c.z < z1:
            b = round(c[axis] / STEP)
            bins[b] = bins.get(b, 0.0) + f.calc_area()
    if not bins:
        return None
    best = max(bins, key=bins.get)
    near = [(b * STEP, bins.get(b, 0.0)) for b in (best - 1, best, best + 1)]
    return sum(p * a for p, a in near) / sum(a for _, a in near)


def check(o, kind, limit):
    findings, red = [], set()
    me = o.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.faces.index_update()
    bm.faces.ensure_lookup_table()
    tris = len(bm.faces)
    zs = [v.co.z for v in bm.verts]
    lo, hi = min(zs), max(zs)
    size = hi - lo
    xs = [v.co.x for v in bm.verts]
    ys = [v.co.y for v in bm.verts]
    extent = max(max(xs) - min(xs), max(ys) - min(ys), size)
    global STEP
    STEP = extent * 0.005
    print(f"STATS triangles {tris} (limit {limit}); size {max(xs) - min(xs):.3f} x {max(ys) - min(ys):.3f} x {size:.3f}")
    if tris > limit:
        findings.append(f"triangles {tris} over the limit {limit}")

    # Loose parts: everything but the largest piece, if small (floating bits). Trees may consist of several pieces.
    parts = sorted(islands(bm), key=len, reverse=True)
    for p in parts[1:]:
        if len(p) < 0.05 * tris and kind != "tree":
            c = sum((f.calc_center_median() for f in p), Vector()) / len(p)
            findings.append(f"loose part: {len(p)} faces at ({c.x:.2f}, {c.y:.2f}, {c.z:.2f})")
            red.update(f.index for f in p)

    # Holes: open edges above the lowest 3 % (the underside may be open, it sits in the ground).
    open_edges = [e for e in bm.edges if e.is_boundary and min(v.co.z for v in e.verts) > lo + size * 0.03]
    if open_edges and kind == "tree":
        # Needle and leaf layers are thin open surfaces: open edges there are normal (inside-out faces still count).
        print(f"NOTE {len(open_edges)} open edges in the foliage (normal for trees)")
    elif open_edges:
        findings.append(f"holes: {len(open_edges)} open edges above the ground")
        for e in open_edges:
            red.update(f.index for f in e.link_faces)

    # Inside-out faces: facing into the body, with nothing of the model in front of their back side.
    tree = BVHTree.FromBMesh(bm)
    centre = Vector(((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, lo + size / 2))
    inside_out = []
    for f in bm.faces:
        c = f.calc_center_median()
        to_out = c - centre
        to_out.z *= 0.3
        if to_out.length < 1e-6 or f.normal.dot(to_out.normalized()) > -0.6 or f.calc_area() < (STEP * 2) ** 2:
            continue
        back = -f.normal
        if tree.ray_cast(c + back * STEP * 0.2, back, extent)[0] is None:
            inside_out.append(f)
    if inside_out:
        findings.append(f"inside-out faces: {len(inside_out)} (you would look through the model there)")
        red.update(f.index for f in inside_out)

    # Attachments on buildings: compact pieces sticking out of the walls between the plinth and the eaves.
    if kind == "building":
        z0, z1 = lo + size * 0.08, lo + size * 0.55
        band0, band1 = lo + size * 0.1, lo + size * 0.35
        planes = {(0, -1): wall_plane(bm, 0, -1, band0, band1), (0, 1): wall_plane(bm, 0, 1, band0, band1),
                  (1, -1): wall_plane(bm, 1, -1, band0, band1), (1, 1): wall_plane(bm, 1, 1, band0, band1)}
        out_by = extent * 0.02     # 2 % of the size: about 25 cm on a 12 m house
        outside = set()
        for f in bm.faces:
            c = f.calc_center_median()
            if not z0 < c.z < z1:
                continue
            for (axis, sign), p in planes.items():
                if p is not None and (c[axis] - p) * sign > out_by:
                    outside.add(f)
                    break
        # Group the outside faces into pieces; a piece along most of a wall (balcony, plinth) is part of the house.
        seen = set()
        for f in outside:
            if f in seen:
                continue
            group, stack = [], [f]
            seen.add(f)
            while stack:
                g = stack.pop()
                group.append(g)
                for e in g.edges:
                    for n in e.link_faces:
                        if n in outside and n not in seen:
                            seen.add(n)
                            stack.append(n)
            pts = [v.co for g in group for v in g.verts]
            dx = max(p.x for p in pts) - min(p.x for p in pts)
            dy = max(p.y for p in pts) - min(p.y for p in pts)
            along = max(dx, dy)
            wall_len = max(max(xs) - min(xs), max(ys) - min(ys))
            # Ledges belong to the house: window sills measured 0.17-0.22 m high (on a 12 m scale); real attachments
            # like the funnels of house B 1.05 m. Below 0.3 m it is a ledge.
            dz = max(p.z for p in pts) - min(p.z for p in pts)
            if dz < extent * 0.025:
                continue
            if along < 0.35 * wall_len and len(group) >= 3:
                c = sum((g.calc_center_median() for g in group), Vector()) / len(group)
                findings.append(f"attachment: {len(group)} faces, {along / extent * 12:.1f} m wide and "
                                f"{dz / extent * 12:.2f} m high on a 12 m scale, at ({c.x:.2f}, {c.y:.2f}, {c.z:.2f})")
                red.update(g.index for g in group)

    bm.free()
    return findings, red


def render(o, red, out):
    scene = bpy.context.scene
    zs = [(o.matrix_world @ v.co) for v in o.data.vertices]
    lo = Vector((min(p.x for p in zs), min(p.y for p in zs), min(p.z for p in zs)))
    hi = Vector((max(p.x for p in zs), max(p.y for p in zs), max(p.z for p in zs)))
    centre, size = (lo + hi) / 2, max(hi - lo)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.lens = 40
    scene.collection.objects.link(cam)
    scene.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3
    sun.rotation_euler = (math.radians(45), 0, math.radians(30))
    scene.collection.objects.link(sun)
    scene.render.resolution_x, scene.render.resolution_y = 480, 400
    files = []
    for row, mode in enumerate(("textured", "findings")):
        if mode == "textured":
            for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
                try:
                    scene.render.engine = engine
                    break
                except TypeError:
                    continue
            world = bpy.data.worlds.new("w")
            world.use_nodes = True
            world.node_tree.nodes["Background"].inputs[0].default_value = (0.7, 0.78, 0.9, 1)
            scene.world = world
        else:
            scene.render.engine = "BLENDER_WORKBENCH"
            sh = scene.display.shading
            sh.light, sh.color_type, sh.show_backface_culling = "STUDIO", "MATERIAL", True
            grey = bpy.data.materials.new("grey")
            grey.diffuse_color = (0.75, 0.75, 0.75, 1)
            redm = bpy.data.materials.new("red")
            redm.diffuse_color = (0.9, 0.05, 0.05, 1)
            o.data.materials.clear()
            o.data.materials.append(grey)
            o.data.materials.append(redm)
            for p in o.data.polygons:
                p.material_index = 1 if p.index in red else 0
        for i, ang in enumerate((30, 120, 210, 300)):
            a = math.radians(ang)
            cam.location = centre + Vector((math.sin(a), -math.cos(a), 0.45)) * size * 1.9
            cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = out.replace(".png", f"_{row}{i}.png")
            bpy.ops.render.render(write_still=True)
            files.append(scene.render.filepath)
    return files


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:]
    src, kind, limit, out = args[0], args[1], int(args[2]), args[3]
    model = load(src)
    findings, red_faces = check(model, kind, limit)
    for f in findings:
        print("FINDING " + f)
    print("VERDICT clean" if not findings else f"VERDICT findings: {len(findings)}")
    # The red marks refer to the triangulated, joined mesh: apply the same to the object before rendering.
    bm = bmesh.new()
    bm.from_mesh(model.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(model.data)
    bm.free()
    for path in render(model, red_faces, out):
        print("IMAGE " + path)
