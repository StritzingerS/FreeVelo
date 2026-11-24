"""Fits the rigged Tripo rider onto the game's bike with the game's joint targets (AvatarModel: hips, shoulders,
hands on the hoods, ankles from the crank angle) by IK, and renders it. Test only.
blender -b --factory-startup --python rider_on_bike.py -- <rigged.glb> <bike.obj> <out stem> <crank deg> <scale>

Pose approved by the owner on 2026-11-24 (docs/rider_grip_approved.png), bike from
`FreeVelo.ModelPreview --bikeobj bike90.obj 90`, rider tools/blender/sources/rider_tripo_jersey.glb:
  90 1.9 axis=X kx=0.250 kz=0.888 ky=0.610 roll=1.0 rollup=-0.3 wr=0.228,0.500,0.925 wrap=2 nowalk=1 grasp=1
  gsoft=1.0 zmin=0.74 lean=0.11 ttip=0.180,0.612,0.895 lttip=0.180,0.600,0.900 skinz=0.70 fr=0.016
Hands: back of the hand outwards, knuckle row upright beside the hood, horn between thumb and index finger,
fingers closed onto hood/lever/bar until contact (grasp), thumb along the inside of the hood. Back: pelvis tips
forward with the spine (no hollow back). Prints SKIN (deepest hand vertex inside the bike) for checking."""
import math
import sys

import bpy
from mathutils import Quaternion, Vector

args = sys.argv[sys.argv.index("--") + 1:]
rig_path, bike_path, out, crank, scale = args[0], args[1], args[2], float(args[3]), float(args[4])
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

bpy.ops.import_scene.gltf(filepath=rig_path)
arm = next(o for o in scene.objects if o.type == "ARMATURE")
for o in list(scene.objects):
    if o.type == "MESH" and o.name.startswith("Icosphere"):
        bpy.data.objects.remove(o)
arm.rotation_mode = "XYZ"
arm.rotation_euler = (0, 0, math.radians(90))   # the rider faces +X in the file; the bike runs along +Y
arm.scale = (scale, scale, scale)
bpy.context.view_layer.update()

bpy.ops.wm.obj_import(filepath=bike_path, forward_axis="Y", up_axis="Z")
bike_objs = [o for o in scene.objects if o.type == "MESH" and o.parent is None and o.name not in ("",) and "tripo" not in o.name]
bike_mat = bpy.data.materials.new("bike")
bike_mat.use_nodes = True
nt = bike_mat.node_tree
attr = nt.nodes.new("ShaderNodeVertexColor")
nt.links.new(attr.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
for o in bike_objs:
    if o.data.color_attributes:
        attr.layer_name = o.data.color_attributes[0].name
    o.data.materials.clear()
    o.data.materials.append(bike_mat)


def U(x, y, z):
    """Unity avatar coordinates (x right, y up, z forward) -> Blender (x right, y forward, z up)."""
    return Vector((x, z, y))


def empty(name, p):
    e = bpy.data.objects.new(name, None)
    e.location = p
    scene.collection.objects.link(e)
    return e


# Targets from AvatarModel
hip_c = U(0, 1.06, -0.185)
shoulder_c = U(0, 1.37, 0.262)
bb = (0.0, 0.27, 0.0)
targets = {}
for side, sign, name in ((0, 1, "Right"), (1, -1, "Left")):
    a = math.radians(crank + (0 if side == 0 else 180))
    pedal = (sign * 0.135, bb[1] + 0.17 * math.cos(a), bb[2] + 0.17 * math.sin(a))
    cleat = (pedal[0] + sign * 0.003, pedal[1] + 0.012, pedal[2])
    tilt = math.radians(10 - 8 * math.sin(a))
    ay, az = 0.08 * math.cos(tilt) + 0.1 * math.sin(tilt), 0.08 * math.sin(tilt) - 0.1 * math.cos(tilt)
    ankle = U(cleat[0], cleat[1] + ay, cleat[2] + az)
    toe = U(cleat[0], cleat[1] - 0.01, cleat[2] + 0.1)
    targets[name] = dict(ankle=ankle, toe=toe, hand=U(sign * 0.205, 0.91, 0.555), knee_pole=U(sign * 0.06, 0.6, 1.2),
                         elbow_pole=U(sign * 0.5, 0.6, 0.35))

pb = arm.pose.bones
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")

# Pelvis onto the saddle (hip joints ~4 cm under the Hips bone's head in this rig)
hips_t = empty("hips_t", hip_c + Vector((0, 0, 0.04)))
c = pb["Hips"].constraints.new("COPY_LOCATION")
c.target = hips_t

# Upper body bent forward, without a hollow back: the pelvis tips forward and every spine joint bends a little
# further forward, so the back is a gentle convex curve (the IK chain had bent the lower back backwards).
# All rotations are about the rider's side axis (the armature's Y in the file).
LEAN = float(next((a[5:] for a in args[5:] if a.startswith("lean=")), "0"))
neck_t = shoulder_c + Vector((0, 0.12 + LEAN, -LEAN * 0.5))
inv = arm.matrix_world.inverted()
hips_a = inv @ (hip_c + Vector((0, 0, 0.04)))
neck_a = inv @ neck_t
rest_up = arm.data.bones["Neck"].head_local - arm.data.bones["Hips"].head_local
want = neck_a - hips_a
total = math.atan2(want.x, want.z) - math.atan2(rest_up.x, rest_up.z)   # forward tilt needed, radians
PELVIS = float(next((a[7:] for a in args[5:] if a.startswith("pelvis=")), "0.55"))
share = {"Hips": PELVIS, "Spine": (1 - PELVIS) * 0.25, "Chest": (1 - PELVIS) * 0.35, "UpperChest": (1 - PELVIS) * 0.40}
side_axis = Vector((0, 1, 0))
for bone, part in share.items():
    pbone = pb[bone]
    pbone.rotation_mode = "QUATERNION"
    local_axis = (arm.data.bones[bone].matrix_local.to_3x3().inverted() @ side_axis).normalized()
    pbone.rotation_quaternion = Quaternion(local_axis, total * part)
print(f"BACK total forward tilt {math.degrees(total):.0f} deg")

# Legs, solved here with the rig's own bone lengths (Blender's IK pole flipped one knee backwards): the ball of the
# foot sits on the pedal, the foot pitched by the game's ankling, the knee bends forward in the plane of hip, ankle
# and a pole in front; each bone then points at the next joint (damped track: the same on both sides).
bpy.context.view_layer.update()
mw = arm.matrix_world


def rest(bone):
    return mw @ arm.data.bones[bone].head_local


for side, sign, name in ((0, 1, "Right"), (1, -1, "Left")):
    thigh = (rest(f"{name}_LowerLeg") - rest(f"{name}_UpperLeg")).length
    shin = (rest(f"{name}_Foot") - rest(f"{name}_LowerLeg")).length
    foot_v = rest(f"{name}_Toes") - rest(f"{name}_Foot")
    foot_len = foot_v.length
    foot_drop = math.atan2(-foot_v.z, math.hypot(foot_v.x, foot_v.y))   # ankle -> ball slopes down at rest
    ball_height = rest(f"{name}_Toes").z                                   # above the sole (the mesh stands on z = 0)
    a = math.radians(crank + (0 if side == 0 else 180))
    pedal = U(sign * 0.135, bb[1] + 0.17 * math.cos(a), bb[2] + 0.17 * math.sin(a))
    ball = pedal + Vector((sign * 0.003, 0, 0.012 + ball_height))
    pitch = math.radians(10 - 8 * math.sin(a))                             # toes down, as AvatarModel ankles
    theta = foot_drop + pitch
    ankle = ball - foot_len * Vector((0, math.cos(theta), -math.sin(theta)))
    hip = mw @ pb[f"{name}_UpperLeg"].head
    d_vec = ankle - hip
    d = min(d_vec.length, thigh + shin - 1e-4)
    if d_vec.length > thigh + shin:
        print(f"REACH {name}: short by {(d_vec.length - thigh - shin) * 100:.1f} cm")
    axis = d_vec.normalized()
    along = (thigh * thigh - shin * shin + d * d) / (2 * d)
    h = math.sqrt(max(thigh * thigh - along * along, 0))
    pole = Vector((sign * 0.08, 1.0, 0.25)).normalized()
    perp = (pole - axis * pole.dot(axis)).normalized()
    knee = hip + axis * along + perp * h
    ankle = hip + axis * d   # where the straightened leg ends if it is short
    for bone, target in ((f"{name}_UpperLeg", knee), (f"{name}_LowerLeg", ankle), (f"{name}_Foot", ball)):
        c = pb[bone].constraints.new("DAMPED_TRACK")
        c.target = empty(f"{bone}_t", target)
    targets[name]["ankle"] = ankle
    targets[name]["ball"] = ball

# Arms, solved like the legs: the knuckles over the top of the hood, the hand pointing forward and down along it
# (slightly inward), the back of the hand up and out; elbows bent out and down.
bpy.context.view_layer.update()
PALM = next((a[5:] for a in args[5:] if a.startswith("palm=")), "TRACK_NEGATIVE_Z")
for side, sign, name in ((0, 1, "Right"), (1, -1, "Left")):
    upper = (rest(f"{name}_LowerArm") - rest(f"{name}_UpperArm")).length
    fore = (rest(f"{name}_Hand") - rest(f"{name}_LowerArm")).length
    hand_len = (rest(f"{name}_MiddleProximal") - rest(f"{name}_Hand")).length
    # From the game's own hand on the hood (AvatarRider.BuildHand): knuckles over the front of the hood,
    # the wrist behind it, the hand along the hood, a little outboard and down.
    # Measured on the bike: the hood body runs y 0.54..0.62 at x ~0.208, its top ~0.91 rising to the horn 0.94
    # at y 0.60; the knuckle joints sit ~1.5 cm above the hood top, just behind the horn, so the fingers wrap over it.
    KX = float(next((a[3:] for a in args[5:] if a.startswith("kx=")), "0.214"))
    KY = float(next((a[3:] for a in args[5:] if a.startswith("ky=")), "0.585"))
    KZ = float(next((a[3:] for a in args[5:] if a.startswith("kz=")), "0.945"))
    ROLL = float(next((a[5:] for a in args[5:] if a.startswith("roll=")), "0.05"))
    ROLLUP = float(next((a[7:] for a in args[5:] if a.startswith("rollup=")), "0.15"))
    knuckles = U(sign * KX, KZ, KY)
    direction = U(sign * 0.008, -0.012, 0.07).normalized()
    WR = next((a[3:] for a in args[5:] if a.startswith("wr=")), None)   # wrist hint "x,y,z" (Blender, right side)
    if WR:
        wx, wy, wz = (float(v) for v in WR.split(","))
        direction = (knuckles - Vector((sign * wx, wy, wz))).normalized()
    wrist = knuckles - direction * hand_len
    shoulder = mw @ pb[f"{name}_UpperArm"].head
    d_vec = wrist - shoulder
    d = min(d_vec.length, upper + fore - 1e-4)
    if d_vec.length > upper + fore:
        print(f"REACH {name} arm: short by {(d_vec.length - upper - fore) * 100:.1f} cm")
    axis = d_vec.normalized()
    along = (upper * upper - fore * fore + d * d) / (2 * d)
    hgt = math.sqrt(max(upper * upper - along * along, 0))
    pole = U(sign * 0.7, -0.7, -0.1).normalized()
    perp = (pole - axis * pole.dot(axis)).normalized()
    elbow = shoulder + axis * along + perp * hgt
    for bone, target in ((f"{name}_UpperArm", elbow), (f"{name}_LowerArm", wrist), (f"{name}_Hand", knuckles)):
        c = pb[bone].constraints.new("DAMPED_TRACK")
        c.target = empty(f"{bone}_t", target)
    # Roll of the hand: its back faces up and a little out
    c = pb[f"{name}_Hand"].constraints.new("LOCKED_TRACK")
    c.target = empty(f"{name}_palm_t", wrist + U(sign * ROLL, ROLLUP, 0))
    c.lock_axis = "LOCK_Y"
    c.track_axis = PALM if sign > 0 else (PALM.replace("TRACK_NEGATIVE_", "TRACK_") if "NEGATIVE" in PALM else PALM.replace("TRACK_", "TRACK_NEGATIVE_")) if PALM.endswith("X") else PALM
    targets[name]["hand"] = wrist

# Fingers curled round the hoods
CURL = float(next((a[5:] for a in args[5:] if a.startswith("curl=")), "55"))
AXIS = next((a[5:] for a in args[5:] if a.startswith("axis=")), "X")
JOINT_CURL = {"Proximal": float(next((a[5:] for a in args[5:] if a.startswith("prox=")), "50")),
              "Intermediate": float(next((a[4:] for a in args[5:] if a.startswith("mid=")), "80")),
              "Distal": float(next((a[5:] for a in args[5:] if a.startswith("dist=")), "50"))}
for b in pb:
    if any(f in b.name for f in ("Index", "Middle", "Ring", "Pinky")) and not b.name.endswith("End"):
        joint = next(j for j in JOINT_CURL if b.name.endswith(j))
        b.rotation_mode = "XYZ"
        r = [0.0, 0.0, 0.0]
        r["XYZ".index(AXIS[-1])] = math.radians(JOINT_CURL[joint]) * (-1 if AXIS.startswith("-") else 1)
        b.rotation_euler = r

# Thumb down the inside of the hood
THUMB = float(next((a[6:] for a in args[5:] if a.startswith("thumb=")), "0"))
TAXIS = next((a[6:] for a in args[5:] if a.startswith("taxis=")), "Z")
for b in pb:
    if "Thumb" in b.name and not b.name.endswith("End"):
        sgn = 1 if b.name.startswith("Right") else -1
        if TAXIS.endswith("X"):
            sgn = 1
        b.rotation_mode = "XYZ"
        r = [0.0, 0.0, 0.0]
        r["XYZ".index(TAXIS[-1])] = math.radians(THUMB) * sgn * (-1 if TAXIS.startswith("-") else 1)
        b.rotation_euler = r

# Wrap: the hood as a round bar along the bike (centre x 0.209, z 0.90 in Blender); the four fingers run from their
# knuckles over the top and down the OUTSIDE of it, the thumb down the INSIDE. Each finger bone points at the next
# joint placed on that circle (chord = bone length), so the hood sits between thumb and fingers.
FR = float(next((a[3:] for a in args[5:] if a.startswith("fr=")), "0.010"))   # finger half thickness
ZMIN = float(next((a[5:] for a in args[5:] if a.startswith("zmin=")), "0.80"))
hood_pts = []
if any(a == "wrap=2" for a in args[5:]):
    for line in open(bike_path):
        if line.startswith("v "):
            x, y, z = (float(v) for v in line.split()[1:4])
            if 0.17 < abs(x) < 0.27 and 0.50 < y < 0.68 and z > ZMIN and x > 0:
                hood_pts.append((x, y, z))


def hull2d(pts):
    pts = sorted(set(pts))
    if len(pts) < 3:
        return pts
    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, hi = [], []
    for p in pts:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], p) <= 0:
            lo.pop()
        lo.append(p)
    for p in reversed(pts):
        while len(hi) >= 2 and cross(hi[-2], hi[-1], p) <= 0:
            hi.pop()
        hi.append(p)
    return lo[:-1] + hi[:-1]


def section(y):
    """Convex outline (x, z) of the right hood at y, as a polar function around its centre, grown by FR."""
    s = [(p[0], p[2]) for p in hood_pts if abs(p[1] - y) < 0.006]
    if len(s) < 3:
        return None
    h = hull2d(s)
    cx = sum(p[0] for p in h) / len(h)
    cz = sum(p[1] for p in h) / len(h)
    def r_at(th):
        dx, dz = math.cos(th), math.sin(th)
        best = 0.0
        for i in range(len(h)):
            ax, az = h[i][0] - cx, h[i][1] - cz
            bx, bz = h[(i + 1) % len(h)][0] - cx, h[(i + 1) % len(h)][1] - cz
            den = dx * (bz - az) - dz * (bx - ax)
            if abs(den) < 1e-12:
                continue
            t = (ax * (bz - az) - az * (bx - ax)) / den
            u = (ax * dz - az * dx) / den
            if t > 0 and -1e-9 <= u <= 1 + 1e-9:
                best = max(best, t)
        return best + FR
    return cx, cz, r_at


def hsection(z):
    """Convex outline (x, y) of hood, lever and bar at height z (right side), polar around its centre, grown by FR."""
    s = [(p[0], p[1]) for p in hood_pts if abs(p[2] - z) < 0.006]
    if len(s) < 3:
        return None
    h = hull2d(s)
    cx = sum(p[0] for p in h) / len(h)
    cy = sum(p[1] for p in h) / len(h)
    def r_at(th):
        dx, dy = math.cos(th), math.sin(th)
        best = 0.0
        for i in range(len(h)):
            ax, ay = h[i][0] - cx, h[i][1] - cy
            bx, by = h[(i + 1) % len(h)][0] - cx, h[(i + 1) % len(h)][1] - cy
            den = dx * (by - ay) - dy * (bx - ax)
            if abs(den) < 1e-12:
                continue
            t = (ax * (by - ay) - ay * (bx - ax)) / den
            u = (ax * dy - ay * dx) / den
            if t > 0 and -1e-9 <= u <= 1 + 1e-9:
                best = max(best, t)
        return best + FR
    return cx, cy, r_at


# Fingers as on the photos: each comes down the outside of the hood from its knuckle, then curls round the lever
# (index, middle) or the bar's bend (ring, little finger) horizontally: outside -> front -> inside.
FZ = {"Index": float(next((a[3:] for a in args[5:] if a.startswith("zi=")), "0")),
      "Middle": float(next((a[3:] for a in args[5:] if a.startswith("zm=")), "0")),
      "Ring": float(next((a[3:] for a in args[5:] if a.startswith("zr=")), "0")),
      "Pinky": float(next((a[3:] for a in args[5:] if a.startswith("zp=")), "0"))}
if hood_pts and any(FZ.values()):
    bpy.context.view_layer.update()
    for side, sign, name in ((0, 1, "Right"), (1, -1, "Left")):
        for finger, zf in FZ.items():
            if not zf:
                continue
            joints = [f"{name}_{finger}{j}" for j in ("Proximal", "Intermediate", "Distal", "DistalEnd")]
            knuckle = mw @ pb[joints[0]].head
            sec = hsection(zf)
            if sec is None:
                print(f"HWRAP no outline at z {zf}")
                continue
            cx, cy, r_at = sec
            # proximal: down the outside, to the outline point facing the knuckle
            th = math.atan2(knuckle.y - cy, sign * knuckle.x - cx)
            rr = r_at(th)
            q = Vector((sign * (cx + rr * math.cos(th)), cy + rr * math.sin(th), zf))
            c = pb[joints[0]].constraints.new("DAMPED_TRACK")
            c.target = empty(f"{joints[0]}_h", q)
            bpy.context.view_layer.update()
            cur = mw @ pb[joints[1]].head
            for k in (1, 2):
                length = (rest(joints[k + 1]) - rest(joints[k])).length
                th = math.atan2(cur.y - cy, sign * cur.x - cx)
                nxt = None
                for i in range(1, 400):
                    t2 = th + i * 0.01          # outside -> front -> inside
                    r = r_at(t2)
                    cand = Vector((sign * (cx + r * math.cos(t2)), cy + r * math.sin(t2), cur.z + (zf - cur.z) * 0.5))
                    if (cand - cur).length >= length:
                        nxt = cand
                        break
                if nxt is None:
                    break
                c = pb[joints[k]].constraints.new("DAMPED_TRACK")
                c.target = empty(f"{joints[k]}_h", nxt)
                bpy.context.view_layer.update()
                cur = mw @ pb[joints[k + 1]].head
            if sign > 0:
                print(f"HWRAP {finger}: knuckle ({knuckle.x:.3f},{knuckle.y:.3f},{knuckle.z:.3f}) tip ({cur.x:.3f},{cur.y:.3f},{cur.z:.3f}) outline centre ({cx:.3f},{cy:.3f})")

if hood_pts:
    bpy.context.view_layer.update()
    worst = 0.0
    for side, sign, name in ((0, 1, "Right"), (1, -1, "Left")):
        for finger in ("Thumb", "Index", "Middle", "Ring", "Pinky"):
            joints = [f"{name}_{finger}{j}" for j in ("Proximal", "Intermediate", "Distal", "DistalEnd")]
            if FZ.get(finger):
                continue
            cur = mw @ pb[joints[0]].head
            tip_arg = next((a[5:] for a in args[5:] if a.startswith(finger[0].lower() + "tip=")), None)
            if name == "Left":
                tip_arg = next((a[6:] for a in args[5:] if a.startswith("l" + finger[0].lower() + "tip=")), tip_arg)
            if tip_arg:
                # Finger laid on the brake lever: a circular arc from the knuckle to a tip on the lever's front,
                # bulging out and up, with the finger's own length; each bone points at the next arc point.
                tx, ty, tz = (float(v) for v in tip_arg.split(","))
                if sign > 0:
                    print(f"KNUCKLE {finger}: ({cur.x:.3f},{cur.y:.3f},{cur.z:.3f})")
                tip = Vector((sign * tx, ty, tz))
                lens = [(rest(joints[k + 1]) - rest(joints[k])).length for k in range(3)]
                total = sum(lens)
                chord_v = tip - cur
                c = chord_v.length
                if c >= total * 0.999:
                    rad_arc = 1e6
                else:
                    lo, hi = c / 2 + 1e-6, 10.0
                    for _ in range(80):
                        mid_r = (lo + hi) / 2
                        if 2 * mid_r * math.asin(min(1.0, c / (2 * mid_r))) > total:
                            lo = mid_r
                        else:
                            hi = mid_r
                    rad_arc = (lo + hi) / 2
                u = chord_v.normalized()
                bulge = Vector((sign * (1.0 if finger != "Thumb" else -1.0), 0.0, 0.7))
                n = (bulge - u * bulge.dot(u)).normalized()
                half = math.asin(min(1.0, c / (2 * rad_arc)))
                sag = rad_arc * math.cos(half)
                centre = cur + chord_v / 2 - n * sag
                a0 = cur - centre
                a1 = tip - centre
                ang_total = a0.angle(a1)
                acc = 0.0
                for k in range(3):
                    acc += lens[k]
                    frac = min(1.0, acc / total)
                    q = Quaternion(a0.cross(a1).normalized(), ang_total * frac)
                    p = centre + q @ a0 if rad_arc < 1e5 else cur + chord_v * frac
                    sec = section(p.y)
                    if sec:   # keep the joint (and the finger's thickness) outside the hood and its horn
                        cx, cz, r_at = sec
                        lx, lz = sign * p.x - cx, p.z - cz
                        th = math.atan2(lz, lx)
                        rr = r_at(th) + 0.004
                        if math.hypot(lx, lz) < rr:
                            p = Vector((sign * (cx + rr * math.cos(th)), p.y, cz + rr * math.sin(th)))
                    con = pb[joints[k]].constraints.new("DAMPED_TRACK")
                    con.target = empty(f"{joints[k]}_lever", p)
                    bpy.context.view_layer.update()
                end = mw @ pb[joints[3]].head
                print(f"LEVERTIP {name} {finger}: {(end - tip).length * 100:.1f} cm from the lever point, chord {c*100:.1f} of {total*100:.1f} cm")
                continue
            if finger != "Thumb" and any(a == "nowalk=1" for a in args[5:]):
                continue          # natural per-joint curl only
            y = cur.y
            step =-1 if finger != "Thumb" else 1   # fingers: over the outside; thumb: down the inside
            for k in range(3):
                length = (rest(joints[k + 1]) - rest(joints[k])).length
                y += 0.004 if finger != "Thumb" else 0.008
                sec = section(y)
                if sec is None:
                    break
                cx, cz, r_at = sec
                lx, lz = sign * cur.x - cx, cur.z - cz
                th = math.atan2(lz, lx)
                nxt = None
                for i in range(1, 400):
                    t2 = th + step * i * 0.01
                    r = r_at(t2)
                    cand = Vector((sign * (cx + r * math.cos(t2)), y, cz + r * math.sin(t2)))
                    if (cand - cur).length >= length:
                        nxt = cand
                        break
                if nxt is None:
                    break
                c = pb[joints[k]].constraints.new("DAMPED_TRACK")
                c.target = empty(f"{joints[k]}_w", nxt)
                bpy.context.view_layer.update()
                if sign > 0:
                    print(f"WALK {finger} {k}: from ({cur.x:.3f},{cur.y:.3f},{cur.z:.3f}) target ({nxt.x:.3f},{nxt.y:.3f},{nxt.z:.3f}) centre ({cx:.3f},{cz:.3f})")
                cur = mw @ pb[joints[k + 1]].head
            # how far the finger tip ended up inside the hood outline (clipping check)
            sec = section(cur.y)
            if sec:
                cx, cz, r_at = sec
                lx, lz = sign * cur.x - cx, cur.z - cz
                inside = r_at(math.atan2(lz, lx)) - FR - math.hypot(lx, lz)
                worst = max(worst, inside)
    print(f"CLIP deepest finger tip inside the hood: {worst * 100:.1f} cm")

def bike_bvh():
    from mathutils.bvhtree import BVHTree
    import bmesh
    deps = bpy.context.evaluated_depsgraph_get()
    bm = bmesh.new()
    for o in bike_objs:
        tmp = o.evaluated_get(deps).to_mesh()
        tmp.transform(o.matrix_world)
        bm.from_mesh(tmp)
        o.evaluated_get(deps).to_mesh_clear()
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not (0.15 < v.co.x < 0.30 and 0.45 < v.co.y < 0.72 and v.co.z > 0.70)], context="VERTS")
    bm.normal_update()
    return BVHTree.FromBMesh(bm)


# Grasp: each finger curls (all three joints together, in natural proportions) until it touches the hood, lever
# or bar, the way a relaxed hand closes round a grip. Finger = capsules of radius GR along its bones.
if any(a == "grasp=1" for a in args[5:]):
    GR = float(next((a[3:] for a in args[5:] if a.startswith("gr=")), "0.009"))
    RATIO = (1.0, 1.25, 0.8)
    GSOFT = float(next((a[6:] for a in args[5:] if a.startswith("gsoft=")), "0.5"))   # soft finger pads
    gbvh = bike_bvh()
    bpy.context.view_layer.update()

    def hits(points, sign):
        for p in points:
            q = Vector((sign * p.x, p.y, p.z))      # left hand tested against the mirrored right hood
            hit = gbvh.find_nearest(q, 0.05)
            if hit[0] is None:
                continue
            loc, nrm, _, dist = hit
            if dist < GR * GSOFT or (q - loc).dot(nrm) < 0:
                return True
        return False

    for side, sign, name in ((0, 1, "Right"), (1, -1, "Left")):
        for finger in ("Index", "Middle", "Ring", "Pinky"):
            bones = [pb[f"{name}_{finger}{j}"] for j in ("Proximal", "Intermediate", "Distal")]
            for b in bones:
                b.rotation_mode = "XYZ"
                b.rotation_euler = (0, 0, 0)
            bpy.context.view_layer.update()
            heads = [mw @ b.head for b in bones] + [mw @ pb[f"{name}_{finger}DistalEnd"].head]
            axis = (mw.to_3x3() @ bones[0].matrix.to_3x3().col[0]).normalized()
            if AXIS.startswith("-"):
                axis = -axis
            segs = [heads[k + 1] - heads[k] for k in range(3)]
            # the rest pose already has bent fingers: start stretched out (negative curl), keep the last free pose
            # before the finger closes onto the grip
            best = None
            first_hit = None
            for step in range(-35, 90):
                c = step * 0.02
                pts, p, cum = [], heads[0].copy(), 0.0
                for k in range(3):
                    cum += c * RATIO[k]
                    d = Quaternion(axis, cum) @ segs[k]
                    for s in range(1, 6):
                        pts.append(p + d * (s / 5))
                    p = p + d
                if hits(pts[2:], sign):
                    if best is not None:
                        first_hit = c
                        break
                    continue
                best = c
            if best is None:
                best = 0.0
            for k, b in enumerate(bones):
                r = [0.0, 0.0, 0.0]
                r["XYZ".index(AXIS[-1])] = best * RATIO[k] * (-1 if AXIS.startswith("-") else 1)
                b.rotation_euler = r
            bpy.context.view_layer.update()
            if sign > 0:
                tip = mw @ pb[f"{name}_{finger}DistalEnd"].head
                print(f"GRASP {finger}: curl {math.degrees(best):.0f}/{math.degrees(best*1.25):.0f}/{math.degrees(best*0.8):.0f} deg, "
                      f"{'touches' if first_hit is not None else 'no contact'}, tip ({tip.x:.3f},{tip.y:.3f},{tip.z:.3f})")

if any(a == "wrap=1" for a in args[5:]):
    CX, CZ = 0.209, 0.90
    RAD = float(next((a[4:] for a in args[5:] if a.startswith("rad=")), "0.03"))
    bpy.context.view_layer.update()
    for side, sign, name in ((0, 1, "Right"), (1, -1, "Left")):
        for finger in ("Index", "Middle", "Ring", "Pinky", "Thumb"):
            joints = [f"{name}_{finger}{j}" for j in ("Proximal", "Intermediate", "Distal", "DistalEnd")]
            p = mw @ pb[joints[0]].head
            lx, lz = sign * p.x - CX, p.z - CZ          # mirrored so the outside is +x on both sides
            theta = math.atan2(lz, lx)
            step = -1 if finger != "Thumb" else 1        # fingers: top -> outside -> under; thumb: top -> inside
            r0 = math.hypot(lx, lz)
            y = p.y
            for k in range(3):
                length = (rest(joints[k + 1]) - rest(joints[k])).length
                r = RAD if finger != "Thumb" else RAD * 0.9
                rr = r0 + (r - r0) * min(1.0, (k + 1) / 1.5)
                theta += step * 2 * math.asin(min(0.99, length / (2 * rr)))
                y += 0.006 if finger != "Thumb" else 0.01
                t = Vector((sign * (CX + rr * math.cos(theta)), y, CZ + rr * math.sin(theta)))
                c = pb[joints[k]].constraints.new("DAMPED_TRACK")
                c.target = empty(f"{joints[k]}_w", t)
                bpy.context.view_layer.update()

# Head up, looking down the road
look = empty("look", U(0, 1.75, 3.0))
c = pb["Head"].constraints.new("DAMPED_TRACK")
c.target = look
c.track_axis = "TRACK_Y"
bpy.ops.object.mode_set(mode="OBJECT")
bpy.context.view_layer.update()

# Reports: how far the IK ends are from their targets
for name, t in targets.items():
    for bone, key in ((f"{name}_Foot", "ankle"), (f"{name}_Toes", "ball"), (f"{name}_Hand", "hand")):
        head = arm.matrix_world @ pb[bone].head
        print(f"MISS {bone}: {(head - t[key]).length * 100:.1f} cm")

# Skin check: how deep the deformed right hand mesh goes into the hood (convex outline per 4 mm slice)
if hood_pts:
    rider = next(o for o in scene.objects if o.type == "MESH" and o.find_armature() == arm)
    deps = bpy.context.evaluated_depsgraph_get()
    ev = rider.evaluated_get(deps)
    me = ev.to_mesh()
    gidx = {g.index for g in rider.vertex_groups if g.name.startswith(("Right_", "Left_")) and
            any(k in g.name for k in ("Hand", "Index", "Middle", "Ring", "Pinky", "Thumb"))}
    gname = {g.index: g.name for g in rider.vertex_groups}
    from mathutils.bvhtree import BVHTree
    import bmesh
    bm = bmesh.new()
    for o in bike_objs:
        tmp = o.evaluated_get(deps).to_mesh()
        tmp.transform(o.matrix_world)
        bm.from_mesh(tmp)
        o.evaluated_get(deps).to_mesh_clear()
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not (0.15 < v.co.x < 0.30 and 0.45 < v.co.y < 0.72 and v.co.z > 0.70)], context="VERTS")
    bm.normal_update()
    bvh = BVHTree.FromBMesh(bm)
    SKINZ = float(next((a[6:] for a in args[5:] if a.startswith("skinz=")), str(ZMIN)))
    deepest, where, count = 0.0, None, 0
    for v_src, v_ev in zip(rider.data.vertices, me.vertices):
        best = max(((g.weight, g.group) for g in v_src.groups if g.group in gidx), default=(0, -1))
        if best[0] < 0.3:
            continue
        p = rider.matrix_world @ v_ev.co
        if gname[best[1]].startswith("Left_"):
            p.x = -p.x          # the bike is symmetric: test the left hand against the right hood
        if not (0.50 < p.y < 0.68 and p.z > SKINZ and p.x > 0.15):
            continue
        hit = bvh.find_nearest(p, 0.04)
        if hit[0] is None:
            continue
        loc, nrm, _, dist = hit
        depth = dist if (p - loc).dot(nrm) < 0 else 0.0
        if depth > 0.002:
            count += 1
        if depth > deepest:
            deepest, where = depth, (p.copy(), gname[best[1]])
    ev.to_mesh_clear()
    if where:
        print(f"SKIN deepest {deepest * 100:.1f} cm at ({where[0].x:.3f},{where[0].y:.3f},{where[0].z:.3f}) {where[1]}, {count} verts > 2 mm")
    else:
        print("SKIN no hand vertex inside the hood")

for f in ("Thumb", "Index", "Middle", "Ring", "Pinky"):
    k = arm.matrix_world @ pb[f"Right_{f}Proximal"].head
    t = arm.matrix_world @ pb[f"Right_{f}DistalEnd"].head
    print(f"POS {f}: base ({k.x:.3f},{k.y:.3f},{k.z:.3f}) tip ({t.x:.3f},{t.y:.3f},{t.z:.3f})")
VIEWS = next((a[6:] for a in args[5:] if a.startswith("views=")), None)
# Render
for e in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
    try:
        scene.render.engine = e
        break
    except TypeError:
        continue
scene.view_settings.view_transform = "Standard"
world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.82, 0.92, 1)
scene.world = world
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sun.data.energy = 3.0
sun.rotation_euler = (math.radians(50), 0, math.radians(35))
scene.collection.objects.link(sun)
bpy.ops.mesh.primitive_plane_add(size=30, location=(0, 0, 0))
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
scene.camera = cam
scene.render.resolution_x, scene.render.resolution_y = 900, 900
CENTRE = (0, 0.05, 0.85)
HOOD = (0.228, 0.6, 0.9)
for nm, loc, lens, aim in (("side", (3.4, 0.1, 1.0), 40, CENTRE), ("leftside", (-3.4, 0.1, 1.0), 40, CENTRE),
                           ("front34", (2.2, 2.6, 1.4), 40, CENTRE), ("rear34", (-2.0, -2.5, 1.55), 40, CENTRE),
                           ("game", (0, -4.6, 1.9), 28, CENTRE), ("back", (2.6, -0.2, 1.35), 50, (0, 0.0, 1.15)),
                           ("handside", (0.75, 0.62, 0.95), 50, HOOD), ("handfront", (0.35, 1.05, 1.05), 50, HOOD),
                           ("handtop", (0.3, 0.45, 1.35), 50, HOOD),
                           ("handin", (-0.02, 0.95, 1.0), 50, HOOD), ("handahead", (0.24, 1.15, 0.93), 50, HOOD),
                           ("handabove", (0.24, 0.50, 1.30), 60, (0.22, 0.61, 0.90))):
    if VIEWS is not None and nm not in VIEWS.split(","):
        continue
    cam.location = loc
    cam.data.lens = lens
    cam.rotation_euler = (Vector(aim) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = f"{out}_{nm}.png"
    bpy.ops.render.render(write_still=True)
print("DONE")
