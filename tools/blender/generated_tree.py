"""
A tree generated with Tripo (tools/blender/sources/*.glb, paid API output owned by the project, see
docs/THIRD_PARTY_ASSETS.md), made fit for the game:

- stands on its trunk base (slightly sunk), scaled to the variant's height (the coded model's heights, so the
  placement does not change);
- bark and needles are told apart by the generated texture's colour under each face (green = foliage); they get
  FreeVelo's materials and shared textures instead of the three 2048 px maps (memory on phones);
- the foliage normals are bent away from the trunk, so the crown is lit as one soft volume;
- two levels of detail: the generated mesh close up, and a reduced copy (about 450 triangles) for 70 to 300 m.

Run headless:
  blender -b --factory-startup --python tools/blender/generated_tree.py -- <in.glb> <height m> <bark material>
      <foliage material> <out stem> [preview.png] [uv] [pale]
  (uv: own texture coordinates for the close version, so the leaf texture does not break up per facet)
e.g. ... -- tools/blender/sources/larch_tripo.glb 17 Bark LarchNeedles unity/.../Resources/Models/Larch_0
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fvm  # noqa: E402

SINK = 0.2
FAR_TRIANGLES = 450
TRUNK_SCALE = 0.7   # generated trunks come out thick (owner's review, 18 November 2026)


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


def normalise(o, height):
    """Scale to <height>, trunk base at the origin (the centre of the lowest vertices), slightly sunk."""
    me = o.data
    zs = [v.co.z for v in me.vertices]
    lo, hi = min(zs), max(zs)
    base = [v.co for v in me.vertices if v.co.z < lo + (hi - lo) * 0.03]
    cx = sum(p.x for p in base) / len(base)
    cy = sum(p.y for p in base) / len(base)
    s = height / (hi - lo)
    me.transform(Matrix.Translation(Vector((0, 0, -SINK))) @ Matrix.Scale(s, 4) @ Matrix.Translation(Vector((-cx, -cy, -lo))))


def slim_trunk(o, factor):
    """Generated trunks come out thick: pull the bark-only vertices towards the trunk axis by <factor>."""
    me = o.data
    foliage = set()
    for p in me.polygons:
        if p.material_index == 1:
            foliage.update(p.vertices)
    moved = 0
    for p in me.polygons:
        if p.material_index != 0:
            continue
        for i in p.vertices:
            if i in foliage:
                continue
            v = me.vertices[i]
            v.co.x *= factor
            v.co.y *= factor
            foliage.add(i)   # move each vertex once
            moved += 1
    print(f"trunk: {moved} vertices pulled in by {factor}")


def split_by_texture(o, bark, foliage):
    """Material per face from the generated colour texture under its UV centre: green foliage, the rest bark."""
    me = o.data
    image = next((i for i in bpy.data.images if i.name.startswith("Color") and i.size[0]), None)
    if image is None:
        raise RuntimeError("no colour texture in the model")
    w, h = image.size
    px = image.pixels[:]
    uv = me.uv_layers.active.data
    me.materials.clear()
    me.materials.append(fvm.material(bark))
    me.materials.append(fvm.material(foliage))
    counts = [0, 0]
    for p in me.polygons:
        u = sum(uv[i].uv.x for i in p.loop_indices) / p.loop_total
        v = sum(uv[i].uv.y for i in p.loop_indices) / p.loop_total
        x = min(w - 1, max(0, int((u % 1.0) * w)))
        y = min(h - 1, max(0, int((v % 1.0) * h)))
        k = (y * w + x) * 4
        r, g, b = px[k], px[k + 1], px[k + 2]
        # Pale foliage (the weeping willow's silvery leaves) is not clearly green: with PALE only brownish faces
        # (more red than green) count as bark.
        green = (g >= r) if PALE else (g > r * 1.08 and g > b)
        p.material_index = 1 if green else 0
        counts[p.material_index] += 1
    print(f"faces: {counts[0]} {bark}, {counts[1]} {foliage}")


def soft_crown(o, foliage_name, radial=0.5):
    """Smooth shading; foliage normals bent away from the trunk axis (the crown shades as one volume)."""
    me = o.data
    for p in me.polygons:
        p.use_smooth = True
    foliage = set()
    for p in me.polygons:
        slot = o.material_slots[p.material_index].material if o.material_slots else None
        if slot is not None and slot.name == foliage_name:
            foliage.update(p.vertices)
    normals = []
    for v in me.vertices:
        n = v.normal.copy()
        if v.index in foliage:
            out = Vector((v.co.x, v.co.y, 0.35))
            out.normalize()
            n = n.lerp(out, radial)
            n.normalize()
        normals.append(n)
    me.normals_split_custom_set_from_vertices(normals)


def apply(o, m):
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)


def far_version(o, height, bark, foliage, target):
    """Far level of detail: reducing the thin, layered foliage directly shatters it into shards, so the crown is
    first closed into a volume (voxel remesh, keeps the layered outline), then reduced; the trunk stays as it is."""
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.separate(type="MATERIAL")
    bpy.ops.object.mode_set(mode="OBJECT")
    parts = [p for p in bpy.context.selected_objects]
    # The crown is the largest part with the foliage material (with bark and foliage the same material, as on the
    # mountain pine, the small stem part would otherwise be taken and the real crown left unreduced as "trunk").
    crown = max((p for p in parts if any(s.material and s.material.name == foliage for s in p.material_slots)),
                key=lambda p: len(p.data.polygons))
    trunk = [p for p in parts if p is not crown]
    # Thin needle sheets would fall between the voxels and the crown would come out too narrow: thicken them first.
    s = crown.modifiers.new("Solidify", "SOLIDIFY")
    s.thickness = height * 0.025
    s.offset = 0
    apply(crown, s)
    m = crown.modifiers.new("Remesh", "REMESH")
    m.mode = "VOXEL"
    m.voxel_size = height * 0.02
    apply(crown, m)
    budget = max(150, target - sum(len(p.data.polygons) * 2 for p in trunk))
    # One collapse pass can stop well above the budget (the mountain pine's dome stayed at about 870 of 450): repeat
    # until it gets there or stops shrinking.
    for _ in range(4):
        tris = sum(len(p.vertices) - 2 for p in crown.data.polygons)
        if tris <= budget * 1.1:
            break
        d = crown.modifiers.new("Decimate", "DECIMATE")
        d.ratio = budget / tris
        apply(crown, d)
        if sum(len(p.vertices) - 2 for p in crown.data.polygons) >= tris * 0.97:
            break
    crown.data.materials.clear()
    crown.data.materials.append(fvm.material(foliage))
    for p in crown.data.polygons:
        p.material_index = 0
    return [crown] + trunk


PALE = False
TILE = {"Bark": 1.0}    # metres per repeat of the game's textures (SurfaceTextures: foliage 0.6 m, bark used at 1 m)
FOLIAGE_TILE = 0.6


def own_uvs(o, bark):
    """Texture coordinates in large islands (close version only): the game's own projection works per face and turns
    the many small leaf facets into a patchwork. Scaled to metres / the material's repeat, so the texture keeps its
    real size; the far version (remeshed) keeps the game's projection."""
    me = o.data
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(70), island_margin=0.02, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    uv = me.uv_layers.active.data
    area3d, area_uv = 0.0, 0.0
    for p in me.polygons:
        pts = [uv[i].uv for i in p.loop_indices]
        area3d += p.area
        area_uv += abs(sum(pts[k].x * pts[(k + 1) % len(pts)].y - pts[(k + 1) % len(pts)].x * pts[k].y
                           for k in range(len(pts)))) / 2
    metres = math.sqrt(area3d / area_uv)
    for p in me.polygons:
        name = o.material_slots[p.material_index].material.name
        f = metres / (TILE.get(bark, 1.0) if name == bark else FOLIAGE_TILE)
        for i in p.loop_indices:
            uv[i].uv = uv[i].uv * f
    print(f"own texture coordinates ({metres:.2f} m per uv unit)")


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:]
    src, height, bark, foliage, stem = args[0], float(args[1]), args[2], args[3], args[4]
    preview = next((a for a in args[5:] if a.endswith(".png")), None)
    PALE = "pale" in args[5:]   # pale foliage: only brownish faces are bark (the weeping willow)
    with_uvs = "uv" in args[5:]   # own texture coordinates for the close version (all trees and the bush use it)
    for detail in (0, 1):
        tree = load(src)
        normalise(tree, height)
        split_by_texture(tree, bark, foliage)
        slim_trunk(tree, TRUNK_SCALE)
        parts = far_version(tree, height, bark, foliage, FAR_TRIANGLES) if detail == 1 else [tree]
        if detail == 0 and with_uvs:
            own_uvs(tree, bark)
        for p in parts:
            soft_crown(p, foliage)
        fvm.export(parts, f"{stem}_lod1.bytes" if detail else f"{stem}.bytes", uvs=detail == 0 and with_uvs)
        if preview:
            fvm.preview(preview.replace(".png", f"_lod{detail}.png"), (0, 0, height * 0.5), height * 1.6, 0.0,
                        angle_deg=30, size=(600, 740))
