"""
Tyrolean house (SceneryKind.AlpineHouse) from a model generated with Tripo (tools/blender/sources/*.glb, paid API
output owned by the project, see docs/THIRD_PARTY_ASSETS.md), made fit for the game:

- the gable with the door turned to the front (Tripo exports with the front towards +X; +Y here, +Z in Unity);
- the walls scaled to the coded model's footprint (SceneryModelsAlpine.AlpineHouse, variant 0: 10.5 x 12 m), so
  the placement does not change; standing on its floor, the plinth slightly sunk;
- small detached pieces sticking out of the walls (odd funnel shapes in the approved candidate) removed;
- surfaces either as FreeVelo materials, told apart by the generated texture's colour under each face (plaster, dark
  and light wood, shingles, stone, shutters, geraniums, windows), or with Tripo's own texture ("textured", needs the
  texture coordinates of format FVM2);
- a reduced far version for the middle distance.

Run headless:
  blender -b --factory-startup --python tools/blender/generated_house.py -- <in.glb> <materials|textured> <out stem>
      [texture stem] [preview.png]
"""

import colorsys
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fvm  # noqa: E402

WIDTH, DEPTH = 10.5, 12.0     # walls of the coded AlpineHouse variant 0 (x, and z = front in Unity)
SINK = 0.25
FAR_TRIANGLES = 900


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


def wall_extent(me, z0, z1):
    """Extent of the walls (the well-populated outer layer, not the overhangs) between heights z0 and z1."""
    pts = [v.co for v in me.vertices if z0 < v.co.z < z1]
    xs = sorted(p.x for p in pts)
    ys = sorted(p.y for p in pts)
    k = max(1, len(pts) // 50)
    return xs[k], xs[-k - 1], ys[k], ys[-k - 1]


def normalise(o):
    me = o.data
    me.transform(Matrix.Rotation(math.radians(90), 4, "Z"))   # Tripo front +X -> +Y
    zs = [v.co.z for v in me.vertices]
    lo, hi = min(zs), max(zs)
    h = hi - lo
    # The ground floor (between 10 % and 35 % of the height) gives the walls without roof overhang or balcony.
    x0, x1, y0, y1 = wall_extent(me, lo + h * 0.1, lo + h * 0.35)
    # Fit inside the coded footprint (never larger: neighbours, fences and lanes are placed around it), proportions kept.
    s = min(WIDTH / (x1 - x0), DEPTH / (y1 - y0))
    centre = Vector(((x0 + x1) / 2, (y0 + y1) / 2, lo))
    me.transform(Matrix.Translation(Vector((0, 0, -SINK))) @ Matrix.Scale(s, 4) @ Matrix.Translation(-centre))
    print(f"walls {(x1 - x0) * s:.1f} x {(y1 - y0) * s:.1f} m, height {h * s:.1f} m (scale {s:.2f})")
    return h * s


def remove_side_pieces(o, height):
    """Removes pieces attached to the outside of the side and back walls on the ground floor (the funnel shapes of
    the approved candidate). Seeds are faces more than 10 cm outside a wall plane; the selection grows across
    neighbouring faces that still belong to the piece (outside the wall plane, or close to it but not lying flat on
    the wall), so the whole piece goes, down to where it meets the wall, while flat wall faces, windows and shutters
    stay. close_holes then closes the opening flat. The front (balcony) and everything above the ground floor stay."""
    me = o.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.002)
    # The wall planes, measured on the plastered ground floor (below the balcony): the position (in 5 cm steps) that
    # holds the most AREA of faces looking that way - the wall itself, not the recessed window panes nor the funnels.
    # (Counting points let the dense funnel pull the wall outwards; an area median was pulled in by the windows.)
    def plane(axis, sign):
        bins = {}
        for f in bm.faces:
            c = f.calc_center_median()
            if f.normal[axis] * sign > 0.9 and 0.3 < c.z < 2.5:
                b = round(c[axis] / 0.05)
                bins[b] = bins.get(b, 0.0) + f.calc_area()
        if not bins:
            return 0.0
        best = max(bins, key=bins.get)
        # The neighbouring bins of the wall too (it is not perfectly flat): area-weighted mean over the three.
        near = [(b * 0.05, bins.get(b, 0.0)) for b in (best - 1, best, best + 1)]
        return sum(p * a for p, a in near) / sum(a for _, a in near)

    x_lo, x_hi, back, front = plane(0, -1), plane(0, 1), plane(1, -1), plane(1, 1)
    print(f"walls at x {x_lo:.2f} .. {x_hi:.2f}, back {back:.2f}, front {front:.2f}")

    # The approved candidate has a funnel-like piece at each back corner. Measured (outside the wall planes): on both
    # side walls from the back corner to 1.15 m forwards (y -5.0 .. -3.25 here), 0.65-1.95 m high, 0.5-0.8 m deep,
    # wrapping round onto the back wall. Further forward on the side walls sit the windows with shutters and sills
    # (up to 0.2 m out) and a balcony bracket - those stay. So: everything in these two corner regions goes, the
    # funnel together with the wall behind it; close_holes then closes the opening flat in the wall plane.
    reach_y = back + 1.15
    z0, z1 = 0.6, 2.05

    def in_region(c, slack=0.0):
        if not z0 - slack < c.z < z1 + slack:
            return False
        at_side = (c.x < x_lo + 0.05 + slack or c.x > x_hi - 0.05 - slack) and c.y < reach_y + slack
        at_back = c.y < back + 0.05 + slack and (c.x < x_lo + 1.0 + slack or c.x > x_hi - 1.0 - slack)
        return at_side or at_back

    # Cut along the region's edges first, so the opening has clean, straight borders in the wall planes.
    for co, no in (((0, 0, z0), (0, 0, 1)), ((0, 0, z1), (0, 0, 1)), ((0, reach_y, 0), (0, 1, 0)),
                   ((x_lo + 1.0, 0, 0), (1, 0, 0)), ((x_hi - 1.0, 0, 0), (1, 0, 0))):
        near = [f for f in bm.faces if in_region(f.calc_center_median(), slack=0.4)]
        geom = list({e for f in near for e in f.edges} | {v for f in near for v in f.verts}) + near
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)
    piece = [f for f in bm.faces if in_region(f.calc_center_median())]
    if piece:
        bmesh.ops.delete(bm, geom=piece, context="FACES")
    # Each opening wraps round a corner (side wall and back wall): an edge up the house corner splits it into two
    # flat openings, one in each wall plane, which close_holes can close flat.
    boundary = [v for v in bm.verts if any(e.is_boundary for e in v.link_edges)]
    for xc in (x_lo, x_hi):
        if not boundary:
            break
        bottom = min(boundary, key=lambda v: (v.co - Vector((xc, back, z0))).length)
        top = min(boundary, key=lambda v: (v.co - Vector((xc, back, z1))).length)
        if bottom is not top and bm.edges.get((bottom, top)) is None:
            bm.edges.new((bottom, top))
    bm.to_mesh(me)
    bm.free()
    print(f"removed {len(piece)} faces in the two back-corner regions")


def close_holes(o, max_sides=400):
    """Closes the holes that cutting off the side pieces leaves in the walls (they were joined to it). The mesh is
    joined along its texture seams first (otherwise every seam would count as a hole); each new face takes the
    texture coordinates its corners have in the neighbouring faces, so the patch shows the surrounding wall.
    Only holes above the ground are closed: the open underside below the plinth stays under the terrain."""
    me = o.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.002)
    uv = bm.loops.layers.uv.active
    before = set(bm.faces)
    edges = [e for e in bm.edges if e.is_boundary and min(v.co.z for v in e.verts) > 0.2]
    bmesh.ops.holes_fill(bm, edges=edges, sides=max_sides)
    # Small holes on the ground floor whose border is not one simple loop: triangulate each one on its own, facing
    # the same way as the wall faces around it.
    rest = {e for e in bm.edges if e.is_boundary and all(0.3 < v.co.z < 2.8 for v in e.verts)}
    while rest:
        group, stack = set(), [rest.pop()]
        while stack:
            e = stack.pop()
            group.add(e)
            for v in e.verts:
                for n in v.link_edges:
                    if n in rest:
                        rest.discard(n)
                        stack.append(n)
        if len(group) > 40:
            continue
        around = [f.normal for e in group for f in e.link_faces]
        filled = bmesh.ops.triangle_fill(bm, edges=list(group), use_beauty=True)["geom"]
        if around:
            avg = sum(around, Vector()) / len(around)
            for f in (g for g in filled if isinstance(g, bmesh.types.BMFace)):
                if f.normal.dot(avg) < 0:
                    f.normal_flip()
    new = [f for f in bm.faces if f not in before]
    # Every patch faces the outside, away from the middle of the house: the fillers do not guarantee the direction,
    # and a patch facing inwards is not drawn from outside (back-face culling) - you would see through the wall.
    flipped = 0
    for f in new:
        c = f.calc_center_median()
        if f.normal.x * c.x + f.normal.y * c.y < 0:
            f.normal_flip()
            flipped += 1
    print(f"{flipped} patches turned to face outwards")
    # Texture: a patch can be large (the cut-off corners), and coordinates taken corner by corner from the
    # neighbours come from all over the texture image (a jumble). Instead each patch shows one spot: the texture at
    # the centre of the largest neighbouring face that faces the same way - the plain wall next to it.
    # Of the neighbouring faces facing the same way, the plainest wall: the brightest, least coloured spot in the
    # texture (plaster), weighted by size - not a shutter or a frame next to the patch.
    image, w, h, px = texture_pixels()

    def plaster_score(u, v):
        x = min(w - 1, max(0, int((u % 1.0) * w)))
        y = min(h - 1, max(0, int((v % 1.0) * h)))
        k = (y * w + x) * 4
        return min(px[k], px[k + 1], px[k + 2])   # white plaster: high; wood, shutters, glass: low

    new_set = set(new)
    for f in new:
        best, best_score = None, -1.0
        for v in f.verts:
            for g in v.link_faces:
                if g in new_set or g.normal.dot(f.normal) < 0.8:
                    continue
                cu = sum(l[uv].uv.x for l in g.loops) / len(g.loops)
                cv = sum(l[uv].uv.y for l in g.loops) / len(g.loops)
                score = plaster_score(cu, cv) * (1 + min(g.calc_area(), 1.0))
                if score > best_score:
                    best, best_score = (cu, cv), score
        if best is None:
            continue
        for loop in f.loops:
            loop[uv].uv = best
    for f in []:
        for loop in f.loops:
            for other in loop.vert.link_loops:
                if other.face is not f and other.face in before:
                    loop[uv].uv = other[uv].uv
                    break
    # Tripo's walls have an inner surface facing into the house, normally hidden behind the outer one. Where taking
    # off a piece removed some outer wall, it is exposed and seen from behind (not drawn: you look through the
    # house). A face facing inwards with nothing of the house in front of it towards the outside is turned round.
    from mathutils.bvhtree import BVHTree
    tree = BVHTree.FromBMesh(bm)
    exposed = 0
    for f in bm.faces:
        c = f.calc_center_median()
        if not 0.3 < c.z < 4.5:
            continue
        out = Vector((c.x, c.y, 0))
        if out.length < 1.0 or f.normal.dot(out.normalized()) > -0.5:
            continue
        # Look out along the face's back side (where a viewer outside would see it from) and radially; exposed if
        # nothing of the house is in front of it either way.
        backside = -f.normal
        blocked = any(tree.ray_cast(c + d * 0.02, d, 3.0)[0] is not None
                      for d in (backside, out.normalized()) if d.length > 0.1)
        if not blocked:
            f.normal_flip()
            exposed += 1
    print(f"{exposed} exposed inner wall faces turned outwards")
    open_edges = sum(1 for e in bm.edges if e.is_boundary)
    bm.to_mesh(me)
    bm.free()
    print(f"closed {len(new)} holes, {open_edges} open edges left")


def texture_pixels():
    image = next((i for i in bpy.data.images if i.name.startswith("Color") and i.size[0]), None)
    return image, image.size[0], image.size[1], image.pixels[:]


def classify(o, height):
    """FreeVelo materials from the generated texture's colour under each face (and where the face is)."""
    me = o.data
    image, w, h, px = texture_pixels()
    uv = me.uv_layers.active.data
    names = ["WallWhite", "WoodDark", "WoodLight", "Shingle", "Plinth", "Shutter", "Geranium", "Window"]
    me.materials.clear()
    for n in names:
        me.materials.append(fvm.material(n))
    index = {n: i for i, n in enumerate(names)}
    xs = [v.co.z for v in me.vertices]
    eave = height * 0.62 - SINK   # below this only walls, balconies and openings
    counts = dict.fromkeys(names, 0)

    def sample(u, v):
        x = min(w - 1, max(0, int((u % 1.0) * w)))
        y = min(h - 1, max(0, int((v % 1.0) * h)))
        k = (y * w + x) * 4
        return px[k], px[k + 1], px[k + 2]

    for p in me.polygons:
        # Average a few samples (the corners and the centre) against texture noise.
        us = [uv[i].uv for i in p.loop_indices]
        cu = sum(t.x for t in us) / len(us)
        cv = sum(t.y for t in us) / len(us)
        samples = [sample(cu, cv)] + [sample((t.x + cu) / 2, (t.y + cv) / 2) for t in us]
        r = sum(s[0] for s in samples) / len(samples)
        g = sum(s[1] for s in samples) / len(samples)
        b = sum(s[2] for s in samples) / len(samples)
        hue, sat, val = colorsys.rgb_to_hsv(r, g, b)
        c, n = p.center, p.normal
        if c.z > eave and n.z > 0.35:
            name = "Shingle"                                  # roof surface
        elif sat > 0.35 and (hue < 0.04 or hue > 0.93) and val > 0.25:
            name = "Geranium"
        elif sat > 0.25 and 0.17 < hue < 0.5:
            name = "Shutter"                                  # green shutters and the geraniums' leaves
        elif c.z < 1.1 and sat < 0.2 and val < 0.7:
            name = "Plinth"                                   # grey stone at the foot
        elif sat < 0.18 and val > 0.6:
            name = "WallWhite"
        elif val < 0.13:
            name = "Window"
        elif 0.02 < hue < 0.14 and sat > 0.2:
            name = "WoodLight" if val > 0.42 else "WoodDark"
        elif c.z > eave:
            name = "WoodDark" if n.z < -0.2 else "Shingle"
        else:
            name = "WoodDark" if val < 0.45 else "WallWhite"
        p.material_index = index[name]
        counts[name] += 1
    print("faces: " + ", ".join(f"{k} {v}" for k, v in counts.items() if v))


def textured(o, stem):
    """One material that tells the game to use the generated texture (saved as <stem>_diff / _nor at 1024 px)."""
    me = o.data
    me.materials.clear()
    me.materials.append(bpy.data.materials.new("Textured:" + stem))


def save_textures(out_dir, stem):
    for prefix, suffix in (("Color", "_diff"), ("NormalGL", "_nor")):
        img = next(i for i in bpy.data.images if i.name.startswith(prefix) and i.size[0])
        copy = img.copy()
        copy.scale(1024, 1024)
        copy.filepath_raw = os.path.join(out_dir, stem + suffix + ".jpg")
        copy.file_format = "JPEG"
        copy.save()
        print(f"saved {copy.filepath_raw}")


def shade(o):
    for p in o.data.polygons:
        p.use_smooth = True
    try:
        o.data.set_sharp_from_angle(angle=math.radians(35))
    except AttributeError:
        pass


def reduce(o, target):
    # Tripo's mesh is split into separate pieces along its texture seams; reduced as they are, their borders move
    # apart and the far version tears open (1,276 open edges measured). Joined first (texture coordinates stay per
    # corner), the reduction keeps it closed (53 open edges at 900 triangles, fewer than the close version).
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.002)
    bm.to_mesh(o.data)
    bm.free()
    tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
    if tris <= target:
        return
    m = o.modifiers.new("Decimate", "DECIMATE")
    m.ratio = target / tris
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:]
    src, mode, stem = args[0], args[1], args[2]
    tex_stem = args[3] if len(args) > 3 and args[3] != "-" else None
    preview = args[4] if len(args) > 4 else None
    for detail in (0, 1):
        house = load(src)
        height = normalise(house)
        remove_side_pieces(house, height)
        close_holes(house)
        if mode == "textured":
            if detail == 0 and tex_stem:
                save_textures(os.path.join(os.path.dirname(os.path.abspath(stem)), "..", "Surfaces"), tex_stem)
            textured(house, tex_stem)
        else:
            classify(house, height)
        if detail == 1:
            reduce(house, FAR_TRIANGLES)
        shade(house)
        fvm.export([house], f"{stem}_lod1.bytes" if detail else f"{stem}.bytes", uvs=(mode == "textured"))
        if preview and detail == 0:
            fvm.preview(preview, (0, 0, height * 0.4), 24, 6, angle_deg=35, size=(900, 650))
