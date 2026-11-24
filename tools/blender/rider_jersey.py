"""Smooths the jersey of the Tripo rider and makes it any colour: finds the blue kit texels (jersey, sleeves, the
blue leg bands) in the base colour texture, removes their large dark/light blotches (luminance high-pass: the
fabric's fine detail stays), and paints them in the target colour; writes a GLB with the recoloured texture and a
mask PNG (white = jersey) for the game. Texture only, the mesh and its skeleton are untouched.
blender -b --factory-startup --python jersey.py -- <rigged.glb> <out.glb> <mask.png> <r> <g> <b>"""
import sys

import bpy
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
src, dst, mask_out = args[0], args[1], args[2]
target = np.array([float(a) for a in args[3:6]], dtype=np.float32)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
mesh = next(o for o in bpy.context.scene.objects if o.type == "MESH" and not o.name.startswith("Icosphere"))
bsdf = next(n for n in mesh.data.materials[0].node_tree.nodes if n.type == "BSDF_PRINCIPLED")
img = bsdf.inputs["Base Color"].links[0].from_node.image
W, H = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
rgb = px[:, :, :3]
mx, mn = rgb.max(axis=2), rgb.min(axis=2)
d = np.maximum(mx - mn, 1e-6)
r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
hue = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
sat = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
jersey = (hue > 195) & (hue < 255) & (sat > 0.35) & (mx > 0.08)


def box(a, rad, axis):
    pad = np.concatenate([a.take(range(-rad, 0), axis=axis), a, a.take(range(0, rad + 1), axis=axis)], axis=axis)
    c = np.cumsum(pad, axis=axis)
    n = a.shape[axis]
    return (c.take(range(2 * rad + 1, 2 * rad + 1 + n), axis=axis) - c.take(range(0, n), axis=axis)) / (2 * rad + 1)


# Luminance of the jersey only (others filled with the jersey mean, so the blur does not drag in the shorts)
lum = rgb.mean(axis=2)
mean = float(lum[jersey].mean())
field = np.where(jersey, lum, mean)
blur = field
rad = max(4, W // 64)
for _ in range(3):
    blur = box(box(blur, rad, 0), rad, 1)
detail = np.clip((field - blur) / max(mean, 1e-3), -0.35, 0.35)   # fine fabric detail around 0
shade = 1.0 + detail * 0.8
out = np.clip(target[None, None, :] * shade[:, :, None], 0, 1)
rgb[jersey] = out[jersey]
px[:, :, :3] = rgb
img.pixels[:] = px.ravel()
img.pack()
img.name = "Color_jersey"

m = bpy.data.images.new("mask", W, H, alpha=False)
mpx = np.zeros((H, W, 4), dtype=np.float32)
mpx[jersey, :3] = 1
mpx[:, :, 3] = 1
m.pixels[:] = mpx.ravel()
m.filepath_raw = mask_out
m.file_format = "PNG"
m.save()
bpy.ops.export_scene.gltf(filepath=dst)
print(f"jersey texels {int(jersey.sum())} of {W * H} (mean luminance {mean:.2f})")
