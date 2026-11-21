"""Gives a Tripo house another plaster colour: in its base colour texture every ochre plaster texel (hue 18-52
degrees, saturated, not dark) is replaced by the new colour at the same relative brightness, so the shading and dirt
stay; roof, windows, door and plinth are left alone. Texture only, geometry untouched.

  blender -b --factory-startup --python tools/blender/recolour_plaster.py -- <in.glb> <out.glb> <r> <g> <b>   (sRGB 0..1)
"""
import sys

import bpy
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
src, dst = args[0], args[1]
target = np.array([float(a) for a in args[2:5]], dtype=np.float32)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
o = [m for m in bpy.context.scene.objects if m.type == "MESH"][0]
bsdf = next(n for n in o.data.materials[0].node_tree.nodes if n.type == "BSDF_PRINCIPLED")
img = bsdf.inputs["Base Color"].links[0].from_node.image
W, H = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
rgb = px[:, :, :3]
mx, mn = rgb.max(axis=2), rgb.min(axis=2)
sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
d = np.maximum(mx - mn, 1e-6)
hue = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
plaster = (hue > 18) & (hue < 52) & (sat > 0.28) & (mx > 0.35)
ref = np.median(mx[plaster])
k = (mx[plaster] / ref)[:, None]
rgb[plaster] = np.clip(target[None, :] * k, 0, 1)
px[:, :, :3] = rgb
img.pixels[:] = px.ravel()
img.pack()
img.name = "Color_recoloured"
nor = bsdf.inputs["Normal"].links[0].from_node if bsdf.inputs["Normal"].links else None
while nor is not None and nor.type != "TEX_IMAGE":
    ins = [i for i in nor.inputs if i.links]
    nor = ins[0].links[0].from_node if ins else None
if nor is not None:
    nor.image.name = "NormalGL_recoloured"
bpy.ops.export_scene.gltf(filepath=dst)
print(f"recoloured {int(plaster.sum())} texels of {W * H} (median plaster value {ref:.2f})")
