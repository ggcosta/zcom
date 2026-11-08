"""
Environment props from AI sculpts -> game-ready FBX + PBR textures.

    blender -b --factory-startup -P Tools/Blender/zcom_props.py -- <models_dir> <previews_dir> <spec.json>

spec.json: [{"name": "Rock_Boulder", "glb": "...", "size": 6.0, "axis": "height|length", "tris": 6000,
             "mode": "tile|concept", "tile": "rock|metal|hive|regolith", "concept": "crop.png" (optional),
             "dust": 0.6, "glow": 0.0, "tint": [r,g,b]}]

Per prop: normalise (base on z=0, centred, facing -Y), decimate, UV, bake sculpt normals + AO, bake albedo
(box-projected tileable texture and/or the concept image projected from the front, multiplied by AO, dust on
upward faces, grime near the ground), optional emissive glow spots (hive), metallic/smoothness, export FBX.
"""
import bpy, bmesh, math, os, sys, json
import numpy as np
from mathutils import Vector, Matrix

ARGS = sys.argv[sys.argv.index("--") + 1:]
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import zcom_marine as zm

MODELS, PREVIEWS = zm.MODELS, zm.PREVIEWS
SPEC = json.load(open(ARGS[2]))
TEX_DIR = os.path.abspath(os.path.join(MODELS, "..", "Textures", "Props"))
TILES = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(ARGS[2])), "tiles"))
os.makedirs(TEX_DIR, exist_ok=True)
V = Vector


def co_array(o):
    a = np.empty(len(o.data.vertices) * 3)
    o.data.vertices.foreach_get("co", a)
    return a.reshape(-1, 3)


def load(spec):
    zm.reset()
    bpy.ops.import_scene.gltf(filepath=spec["glb"])
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    hi = meshes[0]
    for o in list(bpy.context.scene.objects):
        if o != hi:
            bpy.data.objects.remove(o)
    hi.data.transform(hi.matrix_world)
    hi.parent = None
    hi.matrix_world = Matrix.Identity(4)
    co = co_array(hi)
    mn, mx = co.min(0), co.max(0)
    ext = mx - mn
    ref = ext[2] if spec.get("axis", "height") == "height" else max(ext[0], ext[1])
    s = spec["size"] / ref
    co[:, 0] -= (mn[0] + mx[0]) / 2
    co[:, 1] -= (mn[1] + mx[1]) / 2
    co[:, 2] -= mn[2] + spec.get("sink", 0.02) * ext[2]
    co *= s
    hi.data.vertices.foreach_set("co", co.ravel())
    hi.data.update()
    hi.name = hi.data.name = "Sculpt"
    return hi


def decimate(hi, tris):
    lo = hi.copy(); lo.data = hi.data.copy()
    lo.name = lo.data.name = "Prop"
    bpy.context.scene.collection.objects.link(lo)
    n = sum(len(p.vertices) - 2 for p in hi.data.polygons)
    d = lo.modifiers.new("Dec", "DECIMATE"); d.ratio = min(1.0, tris / n)
    zm.apply_mods(lo)
    for p in lo.data.polygons:
        p.use_smooth = True
    return lo


def tile_image(kind):
    path = os.path.join(TILES, f"{kind}.png")
    return bpy.data.images.load(path, check_existing=True)


def build_bake_material(spec, lo):
    """Emission graph = final albedo (tile/concept * AO * dust * grime)."""
    m = bpy.data.materials.new("Prop_" + spec["name"])
    m.use_nodes = True
    nt = m.node_tree; N, L = nt.nodes, nt.links
    for n in list(N):
        N.remove(n)
    out = N.new("ShaderNodeOutputMaterial")
    tc = N.new("ShaderNodeTexCoord")
    geo = N.new("ShaderNodeNewGeometry")
    mp = N.new("ShaderNodeMapping")
    sc = spec.get("tile_scale", 0.33)
    mp.inputs["Scale"].default_value = (sc, sc, sc)
    L.new(tc.outputs["Object"], mp.inputs["Vector"])
    tile = N.new("ShaderNodeTexImage"); tile.image = tile_image(spec.get("tile", "rock"))
    tile.projection = "BOX"; tile.projection_blend = 0.35
    L.new(mp.outputs["Vector"], tile.inputs["Vector"])
    tint = N.new("ShaderNodeMix"); tint.data_type = "RGBA"; tint.blend_type = "MULTIPLY"
    tint.inputs["Factor"].default_value = 1.0
    L.new(tile.outputs["Color"], tint.inputs["A"])
    tint.inputs["B"].default_value = (*spec.get("tint", [1, 1, 1]), 1)
    col = tint.outputs["Result"]

    if spec.get("mode") == "concept" and spec.get("concept"):
        img = bpy.data.images.load(spec["concept"])
        w, h = img.size
        px = np.array(img.pixels[:]).reshape(h, w, 4)
        ys, xs = np.where(px[:, :, :3].mean(2) < 0.92)
        bb = (xs.min(), xs.max(), ys.min(), ys.max())
        co = co_array(lo)
        mn, mx = co.min(0), co.max(0)
        sep = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Position"], sep.inputs[0])
        def aff(sock, a, b):
            n_ = N.new("ShaderNodeMath"); n_.operation = "MULTIPLY_ADD"
            L.new(sock, n_.inputs[0]); n_.inputs[1].default_value = a; n_.inputs[2].default_value = b
            return n_.outputs[0]
        # the image is a high 3/4 view: map x -> u, and a blend of z (height) and -y (depth) -> v
        su = (bb[1] - bb[0]) / (mx[0] - mn[0])
        u = aff(sep.outputs[0], su / w, (bb[0] - mn[0] * su) / w)
        vz = N.new("ShaderNodeMath"); vz.operation = "MULTIPLY_ADD"
        L.new(sep.outputs[1], vz.inputs[0]); vz.inputs[1].default_value = 0.55; L.new(sep.outputs[2], vz.inputs[2])
        lo_v = mn[2] + 0.55 * mn[1]; hi_v = mx[2] + 0.55 * mx[1]
        sv = (bb[3] - bb[2]) / max(hi_v - lo_v, 1e-3)
        v = aff(vz.outputs[0], sv / h, (bb[2] - lo_v * sv) / h)
        cxyz = N.new("ShaderNodeCombineXYZ"); L.new(u, cxyz.inputs[0]); L.new(v, cxyz.inputs[1])
        ct = N.new("ShaderNodeTexImage"); ct.image = img; ct.extension = "EXTEND"
        L.new(cxyz.outputs[0], ct.inputs["Vector"])
        nsep = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Normal"], nsep.inputs[0])
        wf = N.new("ShaderNodeMath"); wf.operation = "MULTIPLY_ADD"
        L.new(nsep.outputs[1], wf.inputs[0]); wf.inputs[1].default_value = -0.9
        L.new(nsep.outputs[2], wf.inputs[2])
        wcl = N.new("ShaderNodeMath"); wcl.operation = "MULTIPLY"; wcl.use_clamp = True
        L.new(wf.outputs[0], wcl.inputs[0]); wcl.inputs[1].default_value = 1.6
        # never paint the concept's white background: fall back to the tiling texture there
        sepc = N.new("ShaderNodeSeparateColor"); L.new(ct.outputs["Color"], sepc.inputs[0])
        mn1 = N.new("ShaderNodeMath"); mn1.operation = "MINIMUM"; L.new(sepc.outputs[0], mn1.inputs[0]); L.new(sepc.outputs[1], mn1.inputs[1])
        mn2 = N.new("ShaderNodeMath"); mn2.operation = "MINIMUM"; L.new(mn1.outputs[0], mn2.inputs[0]); L.new(sepc.outputs[2], mn2.inputs[1])
        wm = N.new("ShaderNodeMapRange"); wm.inputs["From Min"].default_value = 0.72; wm.inputs["From Max"].default_value = 0.86
        wm.inputs["To Min"].default_value = 1.0; wm.inputs["To Max"].default_value = 0.0
        L.new(mn2.outputs[0], wm.inputs["Value"])
        wfin = N.new("ShaderNodeMath"); wfin.operation = "MULTIPLY"; L.new(wcl.outputs[0], wfin.inputs[0]); L.new(wm.outputs["Result"], wfin.inputs[1])
        mixc = N.new("ShaderNodeMix"); mixc.data_type = "RGBA"
        L.new(wfin.outputs[0], mixc.inputs["Factor"]); L.new(col, mixc.inputs["A"]); L.new(ct.outputs["Color"], mixc.inputs["B"])
        col = mixc.outputs["Result"]

    # AO grime
    ao = N.new("ShaderNodeAmbientOcclusion"); ao.inputs["Distance"].default_value = spec.get("ao_dist", 0.6); ao.samples = 16
    aor = N.new("ShaderNodeMapRange"); aor.inputs["To Min"].default_value = 0.35
    L.new(ao.outputs["AO"], aor.inputs["Value"])
    mul = N.new("ShaderNodeMix"); mul.data_type = "RGBA"; mul.blend_type = "MULTIPLY"; mul.inputs["Factor"].default_value = 1.0
    L.new(col, mul.inputs["A"]); L.new(aor.outputs["Result"], mul.inputs["B"])
    col = mul.outputs["Result"]
    # dust settles on upward faces
    nsep2 = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Normal"], nsep2.inputs[0])
    noise = N.new("ShaderNodeTexNoise"); noise.inputs["Scale"].default_value = 3.0; noise.inputs["Detail"].default_value = 6
    L.new(tc.outputs["Object"], noise.inputs["Vector"])
    up = N.new("ShaderNodeMapRange"); up.inputs["From Min"].default_value = 0.55; up.inputs["From Max"].default_value = 0.95
    L.new(nsep2.outputs[2], up.inputs["Value"])
    dn = N.new("ShaderNodeMath"); dn.operation = "MULTIPLY"; L.new(up.outputs["Result"], dn.inputs[0]); L.new(noise.outputs["Fac"], dn.inputs[1])
    dk = N.new("ShaderNodeMath"); dk.operation = "MULTIPLY"; dk.use_clamp = True; L.new(dn.outputs[0], dk.inputs[0]); dk.inputs[1].default_value = spec.get("dust", 0.6) * 1.6
    dust = N.new("ShaderNodeMix"); dust.data_type = "RGBA"
    L.new(dk.outputs[0], dust.inputs["Factor"]); L.new(col, dust.inputs["A"]); dust.inputs["B"].default_value = (0.24, 0.215, 0.26, 1)
    col = dust.outputs["Result"]
    # grime near the ground
    sep3 = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Position"], sep3.inputs[0])
    gr = N.new("ShaderNodeMapRange"); gr.inputs["From Min"].default_value = 0.0; gr.inputs["From Max"].default_value = 0.6
    gr.inputs["To Min"].default_value = 0.55; gr.inputs["To Max"].default_value = 1.0
    L.new(sep3.outputs[2], gr.inputs["Value"])
    gm = N.new("ShaderNodeMix"); gm.data_type = "RGBA"; gm.blend_type = "MULTIPLY"; gm.inputs["Factor"].default_value = 1.0
    L.new(col, gm.inputs["A"]); L.new(gr.outputs["Result"], gm.inputs["B"])
    col = gm.outputs["Result"]
    em = N.new("ShaderNodeEmission"); L.new(col, em.inputs["Color"]); L.new(em.outputs[0], out.inputs["Surface"])
    return m


def bake(spec, hi, lo, size):
    zm.setup_gpu(16)
    sc = bpy.context.scene
    for o in sc.objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = lo
    lo.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.003, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.003)
    bpy.ops.object.mode_set(mode="OBJECT")
    m = build_bake_material(spec, lo)
    lo.data.materials.clear(); lo.data.materials.append(m)
    hi.data.materials.clear(); hi.data.materials.append(m)
    base = bpy.data.images.new(spec["name"] + "_BaseColor", size, size, alpha=False)
    nrm = bpy.data.images.new(spec["name"] + "_Normal", size, size, alpha=False)
    nrm.colorspace_settings.name = "Non-Color"
    t = m.node_tree.nodes.new("ShaderNodeTexImage"); m.node_tree.nodes.active = t
    hi.hide_render = True
    t.image = base
    bpy.ops.object.bake(type="EMIT", margin=12, use_clear=True)
    hi.hide_render = False
    hi.select_set(True); lo.select_set(True); bpy.context.view_layer.objects.active = lo
    t.image = nrm
    sc.render.bake.normal_space = "TANGENT"
    bpy.ops.object.bake(type="NORMAL", use_selected_to_active=True, cage_extrusion=spec["size"] * 0.01,
                        max_ray_distance=spec["size"] * 0.03, margin=12, use_clear=True)
    hi.select_set(False)

    b = np.array(base.pixels[:]).reshape(-1, 4)
    lum = b[:, :3] @ np.array([0.2126, 0.7152, 0.0722])
    sat = (b[:, :3].max(1) - b[:, :3].min(1)) / np.maximum(b[:, :3].max(1), 1e-4)
    ms = np.zeros_like(b)
    metal = spec.get("metal", 0.0)
    ms[:, 0] = ms[:, 1] = ms[:, 2] = np.where(sat < 0.25, metal, metal * 0.3)
    ms[:, 3] = np.clip(spec.get("smooth", 0.25) + (lum - 0.15) * 0.3, 0.05, 0.7)
    em = np.zeros_like(b); em[:, 3] = 1
    if spec.get("glow", 0) > 0:
        gc = np.array(spec.get("glow_color", [0.3, 1.0, 0.25]))
        green = (b[:, 1] > b[:, 0] * 1.4) & (b[:, 1] > b[:, 2] * 1.2) & (b[:, 1] > 0.18)
        em[green, :3] = gc * np.clip((b[green, 1] - 0.15) * 3, 0, 1)[:, None]
    if spec.get("emit_lights"):        # cyan / amber lights painted on the concept
        cyan = (b[:, 2] > 0.45) & (b[:, 1] > 0.4) & (b[:, 0] < 0.55 * b[:, 1])
        amber = (b[:, 0] > 0.6) & (b[:, 1] > 0.35) & (b[:, 2] < 0.25) & (sat > 0.55)
        em[cyan, :3] = b[cyan, :3]; em[amber, :3] = b[amber, :3]
    def save(name, arr, noncolor):
        im = bpy.data.images.new(name, size, size, alpha=True)
        if noncolor:
            im.colorspace_settings.name = "Non-Color"
        im.pixels = arr.ravel().tolist()
        im.filepath_raw = os.path.join(TEX_DIR, name + ".png"); im.file_format = "PNG"; im.save()
        return im
    for im in (base, nrm):
        im.filepath_raw = os.path.join(TEX_DIR, im.name + ".png"); im.file_format = "PNG"; im.save()
    msi = save(spec["name"] + "_MetallicSmoothness", ms, True)
    emi = save(spec["name"] + "_Emission", em, False) if (spec.get("glow", 0) > 0 or spec.get("emit_lights")) else None

    # preview / export material
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial"); p = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(p.outputs[0], out.inputs[0])
    def tex(im):
        tt = nt.nodes.new("ShaderNodeTexImage"); tt.image = im; return tt
    nt.links.new(tex(base).outputs["Color"], p.inputs["Base Color"])
    nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(tex(nrm).outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs[0], p.inputs["Normal"])
    p.inputs["Roughness"].default_value = 0.75
    p.inputs["Metallic"].default_value = metal * 0.5
    if emi:
        nt.links.new(tex(emi).outputs["Color"], p.inputs["Emission Color"]); p.inputs["Emission Strength"].default_value = 3.0


def export(spec, lo):
    for o in bpy.data.objects:
        o.select_set(o == lo)
    lo.name = lo.data.name = "Prop_" + spec["name"]
    path = os.path.join(MODELS, "Prop_" + spec["name"] + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             mesh_smooth_type="FACE", use_mesh_modifiers=True, bake_anim=False, path_mode="STRIP")
    print(f"[zcom] exported {path} ({sum(len(p.vertices) - 2 for p in lo.data.polygons)} tris)")


if __name__ == "__main__":
    for spec in SPEC:
        try:
            hi = load(spec)
            lo = decimate(hi, spec.get("tris", 6000))
            bake(spec, hi, lo, spec.get("tex", 1024))
            bpy.data.objects.remove(hi)
            export(spec, lo)
            zm.setup_render(16, (700, 520))
            bpy.context.scene.cycles.device = "CPU"
            sz = spec["size"]
            far = sz * (1.6 if spec.get("axis") == "length" else 3.2)
            for l in [o for o in bpy.data.objects if o.type == "LIGHT"]:
                l.data.energy *= max(1.0, (far / 8.0) ** 2)
            zm.render_views("Prop_" + spec["name"], {"q34": (32, -35)}, target=V((0, 0, sz * 0.25)), dist=far, lens=50)
        except Exception as e:
            import traceback; traceback.print_exc()
            print(f"[zcom] FAILED {spec['name']}: {e}")
    print("[zcom] props done")
