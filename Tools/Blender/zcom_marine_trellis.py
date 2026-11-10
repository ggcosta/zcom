"""
ORBITAL MARSHAL v4 - the v3 pipeline (zcom_marine_gen.py) fed with a TRELLIS.2 sculpt.

    blender -b --factory-startup -P Tools/Blender/zcom_marine_trellis.py -- <models_dir> <previews_dir> <trellis.glb> <turnaround.png> [diag]

TRELLIS.2 (microsoft/TRELLIS.2, MIT weights; Hugging Face Space microsoft/TRELLIS.2) turns the front view of the
turnaround into a textured mesh that is much crisper than the Hunyuan3D-2 sculpt. What changes against v3:
  1. The GLB faces +X and is split along its UV seams: it is turned to face -Y and welded before anything else.
  2. Albedo comes from TRELLIS's own texture (selected-to-active bake onto the game mesh) instead of projecting the
     three turnaround views; emission / metallic-smoothness are still derived from it by zcom_marine_gen's rules.
  3. Skinning: the TRELLIS cape is a separate sheet that wraps around the hips and reaches the shoulders, so it is
     found by geometry (the surface behind the torso / outside the limbs) and never weighted to the arms.
Everything else (decimation, fitted rig, backpack, animations, FBX export) is zcom_marine_gen / zcom_marine unchanged.
With "diag" it only renders the rest pose coloured by the dominant bone region (no export).
"""
import bpy, bmesh, math, os, sys
import numpy as np
from mathutils import Matrix, Vector

ARGS = sys.argv[sys.argv.index("--") + 1:]
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import zcom_marine_gen as gen   # noqa: E402  (imports zcom_marine, which reads ARGS[0..1])
zm = gen.zm
DIAG = len(ARGS) > 4 and ARGS[4] == "diag"
ALBEDO_LUM = 0.22      # mean albedo luminance (sRGB-encoded values; TRELLIS delivers ~0.09, the v3 atlas ~0.3)
V = Vector


def setup_gpu(samples):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        for kind in ("OPTIX", "CUDA", "HIP", "ONEAPI"):
            try:
                prefs.compute_device_type = kind
                prefs.get_devices()
                devs = [d for d in prefs.devices if d.type == kind]
                if devs:
                    for d in prefs.devices:
                        d.use = d.type == kind
                    sc.cycles.device = "GPU"
                    print(f"[trellis] cycles on {kind}: {', '.join(d.name for d in devs)}")
                    break
            except TypeError:
                continue
    except Exception as ex:
        print("[trellis] GPU setup failed:", ex)
    sc.cycles.samples = samples


zm.setup_gpu = setup_gpu


# ============================================================================ 0. weapon hold
# v3 held the rifle at belly height with the stock buried in the stomach and the support hand barely ahead of the
# trigger hand. The hold is tuned here against the real Weapon_*.fbx (zcom_weapons.py: grip at the origin, stock
# butt ~0.33 m behind it, handguard 0.14-0.36 m ahead).
GRIP = V((-0.12, -0.36, 1.31))      # right-hand grip in armature space (neutral stance): chest height, out in front
STANCE_YAW = -26.0                  # chest yaw of the rifle stance (left shoulder forward)
SUPPORT = V((0.0, -0.20, 0.0))      # left palm on the handguard, weapon space (forward is -Y)
PITCH_BIAS = 5.0                    # raises the muzzle of every pose (v3 idled 9 deg muzzle-down)
POLE_R = V((-0.3, 0.2, -0.35))      # elbow poles relative to each shoulder
POLE_L = V((0.35, 0.05, -0.35))


def pose_weapon_arms(arm, sway_yaw=0.0, sway_pitch=0.0, drop=0.0):
    """zcom_marine.pose_weapon_arms with the support hand / elbow poles as parameters."""
    zm.upd()
    Mc = arm.pose.bones["Chest"].matrix.copy()
    g = Mc @ (zm.NEUTRAL_CHEST["inv"] @ zm.GRIP) + V((0, 0, -drop))
    rw = (zm.Rq((0, 0, 1), sway_yaw) @ zm.Rq((1, 0, 0), sway_pitch + PITCH_BIAS)).to_matrix()
    zm.set_world(arm, "Weapon", rw @ zm.rest3(arm, "Weapon"), g)
    yr = rw @ V((0.0, -0.6, -0.8)); zr = rw @ V((1, 0, 0))
    gl = g + rw @ SUPPORT
    yl = rw @ V((-0.62, -0.62, 0.15)).normalized(); zl = rw @ V((0, 0, 1))
    for s, palm, yh, zh, pole_off in (("R", g, yr, zr, POLE_R), ("L", gl, yl, zl, POLE_L)):
        fy = zm.frame_from(yh, zh)
        yh, zh = fy.col[1].copy(), fy.col[2].copy()
        wrist = palm - yh * 0.05 - zh * 0.028
        sh = arm.pose.bones[f"UpperArm_{s}"].head.copy()
        w = zm.ik(arm, f"UpperArm_{s}", f"ForeArm_{s}", wrist, sh + pole_off, -1.0)
        zm.set_world(arm, f"Hand_{s}", fy, w)


zm.pose_weapon_arms = pose_weapon_arms


def use_hold():
    """Applies this file's hold to the shared rig toolkit (call before build_rig / init_neutral)."""
    zm.GRIP = GRIP.copy()
    zm.STANCE_YAW = STANCE_YAW


# ============================================================================ 1. import, orient, weld, normalise

def load_trellis(path):
    bpy.ops.import_scene.gltf(filepath=path)
    tex = [o for o in bpy.context.scene.objects if o.type == "MESH"][0]
    for o in list(bpy.context.scene.objects):
        if o != tex:
            bpy.data.objects.remove(o)
    tex.data.transform(tex.matrix_world)
    tex.parent = None
    tex.matrix_world = Matrix.Identity(4)
    tex.data.transform(Matrix.Rotation(math.radians(-90), 4, "Z"))   # TRELLIS faces +X, the rig expects -Y
    co = gen.co_array(tex)
    mn, mx = co.min(0), co.max(0)
    s = gen.HEIGHT / (mx[2] - mn[2])
    raw = dict(xmin=(mn[0] - (mn[0] + mx[0]) / 2) * s, xmax=(mx[0] - (mn[0] + mx[0]) / 2) * s)
    co[:, 0] -= (mn[0] + mx[0]) / 2
    co[:, 1] -= (mn[1] + mx[1]) / 2
    co[:, 2] -= mn[2]
    co *= s
    low = co[co[:, 2] < 0.45]
    shift = (np.percentile(low[:, 0], 2) + np.percentile(low[:, 0], 98)) / 2
    co[:, 0] -= shift
    gen.set_co(tex, co)
    tex.name = tex.data.name = "TrellisTextured"
    raw["shift"] = shift
    raw["ymin"], raw["ymax"] = co[:, 1].min(), co[:, 1].max()
    # welded, untextured copy = the "sculpt" the v3 pipeline measures, decimates and bakes normals from
    hi = tex.copy()
    hi.data = tex.data.copy()
    bpy.context.scene.collection.objects.link(hi)
    hi.name = hi.data.name = "Sculpt"
    bm = bmesh.new()
    bm.from_mesh(hi.data)
    nv = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=2e-4)
    bm.to_mesh(hi.data)
    bm.free()
    while hi.data.uv_layers:
        hi.data.uv_layers.remove(hi.data.uv_layers[0])
    hi.data.materials.clear()
    tex.hide_render = True
    print(f"[trellis] sculpt {nv} -> {len(hi.data.vertices)} verts after welding, scale {s:.3f}, leg shift {shift:.3f}")
    return hi, tex, raw


# ============================================================================ 2. maps

def bake_maps(hi, tex, lo, views, raw):
    """zcom_marine_gen.bake_maps with the albedo taken from TRELLIS's texture instead of the turnaround projection.
    TRELLIS paints the visor and chest lights as dark glass, so the glowing parts still come from projecting the
    turnaround (zcom_marine_gen.projection_material): where it is cyan, albedo and emission take its colour."""
    zm.setup_gpu(4)
    sc = bpy.context.scene
    for o in sc.objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = lo
    lo.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.002, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.002)
    bpy.ops.object.mode_set(mode="OBJECT")

    mat = bpy.data.materials.new("MarshalArmor")
    mat.use_nodes = True
    lo.data.materials.clear()
    lo.data.materials.append(mat)
    TEX = gen.TEX
    base = bpy.data.images.new("Marshal_BaseColor", TEX, TEX, alpha=False)
    nrm = bpy.data.images.new("Marshal_Normal", TEX, TEX, alpha=False)
    nrm.colorspace_settings.name = "Non-Color"
    tnode = mat.node_tree.nodes.new("ShaderNodeTexImage")
    mat.node_tree.nodes.active = tnode
    sc.render.bake.margin = 16

    # TRELLIS albedo -> game mesh (colour only: no lighting baked in)
    tex.hide_render = False
    tex.select_set(True)
    lo.select_set(True)
    bpy.context.view_layer.objects.active = lo
    tnode.image = base
    sc.render.bake.use_pass_direct = False
    sc.render.bake.use_pass_indirect = False
    sc.render.bake.use_pass_color = True
    bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_selected_to_active=True,
                        cage_extrusion=0.02, max_ray_distance=0.05, margin=16, use_clear=True)
    tex.select_set(False)
    tex.hide_render = True
    print("[trellis] baked TRELLIS albedo")

    proj = bpy.data.images.new("Marshal_Projection", TEX, TEX, alpha=False)
    pm = gen.projection_material(views, raw)
    lo.data.materials[0] = pm
    pn = pm.node_tree.nodes.new("ShaderNodeTexImage")
    pn.image = proj
    pm.node_tree.nodes.active = pn
    bpy.ops.object.bake(type="EMIT", margin=16, use_clear=True)
    lo.data.materials[0] = mat
    print("[trellis] baked turnaround projection (lights only)")

    hi.hide_render = False
    hi.select_set(True)
    lo.select_set(True)
    bpy.context.view_layer.objects.active = lo
    tnode.image = nrm
    sc.render.bake.normal_space = "TANGENT"
    bpy.ops.object.bake(type="NORMAL", use_selected_to_active=True, cage_extrusion=0.02, max_ray_distance=0.05, margin=16, use_clear=True)
    print("[trellis] baked sculpt normals")
    hi.select_set(False)

    # derived maps: same rules as zcom_marine_gen.bake_maps (TRELLIS's albedo has no background leaks to repair)
    b = np.array(base.pixels[:]).reshape(-1, 4)
    r, g, bl = b[:, 0], b[:, 1], b[:, 2]
    lum = 0.2126 * r + 0.7152 * g + 0.0722 * bl
    mx, mn = b[:, :3].max(1), b[:, :3].min(1)
    sat = (mx - mn) / np.maximum(mx, 1e-4)
    pr = np.array(proj.pixels[:]).reshape(-1, 4)
    pmx, pmn = pr[:, :3].max(1), pr[:, :3].min(1)
    psat = (pmx - pmn) / np.maximum(pmx, 1e-4)
    cyan = (pr[:, 2] > 0.45) & (pr[:, 1] > 0.4) & (pr[:, 0] < 0.6 * pr[:, 1]) & (psat > 0.35)
    # TRELLIS paints the whole suit darker than the concept: lift it with a gamma curve (keeps the contrast between
    # steel plates and graphite cloth) to ALBEDO_LUM, and take out half of its slight olive cast.
    plum = 0.2126 * pr[:, 0] + 0.7152 * pr[:, 1] + 0.0722 * pr[:, 2]
    blum = 0.2126 * b[:, 0] + 0.7152 * b[:, 1] + 0.0722 * b[:, 2]
    valid = blum > 1e-4
    # a mild gamma (keeps the dark cloth from going muddy) then a straight gain up to ALBEDO_LUM; a pure gamma
    # lift flattened the plates into a washed-out grey
    lifted = np.where(valid, np.maximum(blum, 1e-4) ** 0.85, 0.0)
    k = ALBEDO_LUM / lifted[valid].mean()
    nl = np.clip(lifted * k, 0.0, 0.92)
    b[:, :3] *= (nl / np.maximum(blum, 1e-4))[:, None]
    b[:, :3] = nl[:, None] + 0.5 * (b[:, :3] - nl[:, None])
    np.clip(b, 0.0, 1.0, out=b)
    print(f"[trellis] albedo luminance {blum[valid].mean():.3f} -> {nl[valid].mean():.3f} (gain {k:.2f}; turnaround {plum.mean():.3f})")
    # glowing parts and the amber rank chevrons come from the turnaround itself
    amber_p = (pr[:, 0] > pr[:, 1]) & (pr[:, 1] > pr[:, 2]) & (psat > 0.45) & (plum > 0.12)
    b[cyan, :3] = pr[cyan, :3]
    b[amber_p, :3] = pr[amber_p, :3]
    base.pixels = b.ravel().tolist()
    r, g, bl = b[:, 0], b[:, 1], b[:, 2]
    lum = 0.2126 * r + 0.7152 * g + 0.0722 * bl
    mx, mn = b[:, :3].max(1), b[:, :3].min(1)
    sat = (mx - mn) / np.maximum(mx, 1e-4)
    em = np.zeros_like(b)
    em[cyan, 0], em[cyan, 1], em[cyan, 2] = r[cyan], g[cyan], bl[cyan]
    em[:, 3] = 1.0
    ms = np.zeros_like(b)
    steel = (sat < 0.25) & (lum > 0.07)
    amber = (r > g) & (g > bl) & (sat > 0.4)
    metal = np.where(steel, np.clip((lum - 0.05) * 2.0, 0.15, 0.45), np.where(amber, 0.1, 0.02))
    smooth = np.where(steel, 0.36, np.where(cyan, 0.8, 0.16))
    ms[:, 0] = ms[:, 1] = ms[:, 2] = metal
    ms[:, 3] = smooth
    print(f"[trellis] albedo mean lum {lum.mean():.3f}, cyan px {int(cyan.sum())}, steel px {int(steel.sum())}")

    def save(name, arr, alpha=False, noncolor=True):
        im = bpy.data.images.new(name, TEX, TEX, alpha=alpha)
        if noncolor:
            im.colorspace_settings.name = "Non-Color"
        im.pixels = arr.ravel().tolist()
        im.filepath_raw = os.path.join(gen.TEX_DIR, name + ".png")
        im.file_format = "PNG"
        im.save()
        return im
    for im, name in ((base, "Marshal_BaseColor"), (nrm, "Marshal_Normal")):
        im.filepath_raw = os.path.join(gen.TEX_DIR, name + ".png")
        im.file_format = "PNG"
        im.save()
    emi = save("Marshal_Emission", em, noncolor=False)
    msi = save("Marshal_MetallicSmoothness", ms, alpha=True)

    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    p = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(p.outputs[0], out.inputs[0])
    def tnode_(im):
        t = nt.nodes.new("ShaderNodeTexImage"); t.image = im
        return t
    nt.links.new(tnode_(base).outputs["Color"], p.inputs["Base Color"])
    tm = tnode_(msi)
    nt.links.new(tm.outputs["Color"], p.inputs["Metallic"])
    inv = nt.nodes.new("ShaderNodeMath"); inv.operation = "SUBTRACT"; inv.inputs[0].default_value = 1.0
    nt.links.new(tm.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs[0], p.inputs["Roughness"])
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tnode_(nrm).outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], p.inputs["Normal"])
    nt.links.new(tnode_(emi).outputs["Color"], p.inputs["Emission Color"])
    p.inputs["Emission Strength"].default_value = 4.0
    mat["zcom_kind"] = "baked"
    return mat


# ============================================================================ 3. skin weights

def skin_weights(lo, arm, cape_y, J):
    """zcom_marine_gen.skin_weights with TRELLIS-specific limits.
    The TRELLIS sculpt hangs its arms close to the torso and wraps the cape from the shoulders round to the hips, so:
      - an arm owns a vertex only outside the torso (beyond the measured torso/arm gap of each slice, J["armedge*"]);
      - nothing behind y = 0.10 (cape / back plate) belongs to an arm, at any height;
      - the cape is the surface behind the back from the shoulders down (y > 0.11 above the belt, as in v3 below);
      - arm-cape and arm-hips seams are cut so they can separate when the arms move."""
    segs = {b.name: (np.array(b.head_local), np.array(b.tail_local)) for b in arm.data.bones}
    co = gen.co_array(lo)
    names = [n for n in zm.DEFORM]
    cape = [n for n in segs if n.startswith("Cape_")]
    allb = names + cape
    D = np.zeros((len(co), len(allb)))
    for j, n in enumerate(allb):
        a, b = segs[n]
        ab = b - a
        t = np.clip(((co - a) @ ab) / (ab @ ab), 0, 1)
        D[:, j] = np.linalg.norm(a + t[:, None] * ab - co, axis=1)
    idx = {n: j for j, n in enumerate(allb)}
    INF = 1e3
    x, y, z = co[:, 0], co[:, 1], co[:, 2]
    arm_cols = [idx[n] for n in allb if n.split("_")[0] in ("UpperArm", "ForeArm", "Hand")]
    leg_cols = [idx[n] for n in allb if n.split("_")[0] in ("Thigh", "Shin", "Foot", "Toe")]
    cape_cols = [idx[n] for n in cape]
    arm_ok = np.zeros(len(co), bool)
    for side, sx in (("L", 1), ("R", -1)):
        # TRELLIS's armoured forearms and gauntlets are thicker than the v3 radii (0.085 / 0.08 / 0.13)
        near = (D[:, idx[f"ForeArm_{side}"]] < 0.12) | (D[:, idx[f"Hand_{side}"]] < 0.11) | \
               ((D[:, idx[f"UpperArm_{side}"]] < 0.14) & (z > 1.02))
        edges = J["armedge" + side]
        ez = np.array(sorted(edges))
        ex = np.array([edges[k] for k in ez])
        edge = np.interp(z, ez, ex)                       # torso/arm gap, clamped outside the measured band
        outside = np.where(z > ez.max() + 0.1, x * sx > 0.2, x * sx > edge - 0.012)
        # inside the band where the gap was measured, everything beyond it is arm, however far from the bones
        in_band = (z > ez.min() - 0.06) & (z < ez.max() + 0.1) & (y < 0.1)
        # right next to the bones it is limb whatever the gap says (fingers curl in towards the thigh, the front of
        # the upper arm overhangs the chest)
        core = (D[:, idx[f"ForeArm_{side}"]] < 0.065) | (D[:, idx[f"Hand_{side}"]] < 0.075) | \
               ((D[:, idx[f"UpperArm_{side}"]] < 0.085) & (z > 1.1))
        arm_ok |= (((near | in_band) & outside) | core) & (x * sx > 0)
    not_arm = ~arm_ok
    D[np.ix_(not_arm, arm_cols)] = INF
    for n in allb:
        if n.endswith("_L"):
            D[x < -0.04, idx[n]] = INF
        elif n.endswith("_R"):
            D[x > 0.04, idx[n]] = INF
    D[np.ix_(z > 1.02, leg_cols)] = INF
    leg_d = D[:, leg_cols].min(1)
    is_cape = (((y > 0.08) & (z < 1.25)) | ((y > 0.11) & (z < 1.47))) & (z > 0.35) & (leg_d > 0.11) & ~arm_ok
    D[np.ix_(is_cape, leg_cols)] = INF
    D[np.ix_(y > 0.10, arm_cols)] = INF
    D[np.ix_(~is_cape, cape_cols)] = INF
    D[np.ix_(is_cape, [idx["Hips"], idx["Spine"]])] *= 1.6
    # Arm vertices blend only along their own arm chain: a forearm or hand vertex that also took some thigh / hips /
    # clavicle weight stays behind when the hand comes up to the rifle and stretches into a blade.
    for side, sx in (("L", 1), ("R", -1)):
        chain = [idx[f"{k}_{side}"] for k in ("UpperArm", "ForeArm", "Hand")]
        mine = arm_ok & (x * sx > 0)
        lower = mine & (np.minimum(D[:, idx[f"ForeArm_{side}"]], D[:, idx[f"Hand_{side}"]]) < D[:, idx[f"UpperArm_{side}"]])
        keep_upper = set(chain) | {idx[f"Shoulder_{side}"], idx["Chest"]}
        D[np.ix_(mine, [j for j in range(len(allb)) if j not in keep_upper])] = INF
        D[np.ix_(lower, [j for j in range(len(allb)) if j not in chain])] = INF
    order = np.argsort(D, axis=1)[:, :3]
    d3 = np.take_along_axis(D, order, 1)
    w = 1.0 / (d3 + 0.01) ** 5
    w[d3 >= INF * 0.5] = 0.0
    w /= np.maximum(w.sum(1, keepdims=True), 1e-9)

    def region(n):
        k = n.split("_")[0]
        return "arm" if k in ("UpperArm", "ForeArm", "Hand") else "leg" if k in ("Thigh", "Shin", "Foot", "Toe") else \
               "cape" if k == "Cape" else "low" if k in ("Hips", "Spine") else "other"
    dom = [region(allb[order[i, 0]]) for i in range(len(co))]
    forbidden = ({"arm", "low"}, {"arm", "leg"}, {"arm", "cape"}, {"cape", "leg"})
    bm = bmesh.new()
    bm.from_mesh(lo.data)
    bm.faces.ensure_lookup_table()
    kill = [f for f in bm.faces if any(fb <= {dom[v.index] for v in f.verts} for fb in forbidden)]
    bmesh.ops.delete(bm, geom=kill, context="FACES_ONLY")
    bm.faces.ensure_lookup_table()
    seen, islands = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        comp, stack = [], [f]
        seen.add(f.index)
        while stack:
            g = stack.pop()
            comp.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        islands.append(comp)
    islands.sort(key=len, reverse=True)
    # the arms and the cape are now islands of their own: keep anything big, drop only crumbs
    small = [f for comp in islands[1:] if len(comp) < 60 for f in comp]
    bmesh.ops.delete(bm, geom=small, context="FACES_ONLY")
    print(f"[trellis] cut {len(kill)} seam faces, removed {len(small)} faces in "
          f"{sum(1 for c in islands[1:] if len(c) < 60)} crumbs, islands kept {sum(1 for c in islands if len(c) >= 60)}")
    bm.to_mesh(lo.data)
    bm.free()
    groups = {n: lo.vertex_groups.new(name=n) for n in allb}
    for vi in range(len(co)):
        for k in range(3):
            if w[vi, k] > 0.02:
                groups[allb[order[vi, k]]].add([vi], float(w[vi, k]), "REPLACE")
    print(f"[trellis] skinned {len(co)} verts ({int(is_cape.sum())} cape, {int(arm_ok.sum())} arm)")


# ============================================================================ 4. diagnostics

REGION_COL = {"hand": (1, 0.2, 0.2), "upper": (1, 0.6, 0.1), "leg": (0.2, 0.4, 1), "cape": (0.2, 0.9, 0.3),
              "low": (0.8, 0.8, 0.2), "chest": (0.75, 0.75, 0.75), "head": (0.9, 0.3, 0.9), "other": (0.4, 0.4, 0.4)}


def region_of(bone):
    k = bone.split("_")[0]
    return {"ForeArm": "hand", "Hand": "hand", "UpperArm": "upper", "Shoulder": "upper", "Thigh": "leg", "Shin": "leg",
            "Foot": "leg", "Toe": "leg", "Cape": "cape", "Hips": "low", "Spine": "low", "Chest": "chest",
            "Neck": "head", "Head": "head"}.get(k, "other")


def diag_render(lo, tag):
    """Rest pose, vertices coloured by the bone with the largest weight."""
    names = {g.index: g.name for g in lo.vertex_groups}
    col = lo.data.color_attributes.new("Region", "FLOAT_COLOR", "POINT")
    for v in lo.data.vertices:
        best = max(v.groups, key=lambda g: g.weight, default=None)
        c = REGION_COL[region_of(names[best.group])] if best else (0, 0, 0)
        col.data[v.index].color = (*c, 1)
    m = bpy.data.materials.new("Diag"); m.use_nodes = True
    nt = m.node_tree
    a = nt.nodes.new("ShaderNodeVertexColor"); a.layer_name = "Region"
    bsdf = nt.nodes["Principled BSDF"]
    nt.links.new(a.outputs["Color"], bsdf.inputs["Base Color"])
    lo.data.materials.clear(); lo.data.materials.append(m)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = 500, 640
    w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[1].default_value = 3.0
    cam = bpy.data.objects.new("DiagCam", bpy.data.cameras.new("DiagCam")); zm.link(cam); sc.camera = cam
    cam.data.type = "ORTHO"; cam.data.ortho_scale = 2.2
    for name, deg in (("front", 0), ("side", 90), ("back", 180), ("q", 35)):
        a_ = math.radians(deg)
        cam.location = (5 * math.sin(a_), -5 * math.cos(a_), 0.98)
        cam.rotation_euler = (math.radians(90), 0, a_)
        sc.render.filepath = os.path.join(zm.PREVIEWS, f"Diag_{tag}_{name}.png")
        bpy.ops.render.render(write_still=True)
    print("[trellis] diag renders written")


# ============================================================================ main

if __name__ == "__main__":
    zm.reset()
    hi, tex, raw = load_trellis(os.path.abspath(ARGS[2]))
    lo = gen.decimate(hi)
    if not DIAG:
        bake_maps(hi, tex, lo, gen.view_crops(), raw)
    J = gen.fit_joints(hi)
    back_y = float(np.percentile(gen.co_array(hi)[(np.abs(gen.co_array(hi)[:, 2] - 1.3) < 0.02) & (np.abs(gen.co_array(hi)[:, 0]) < 0.12)][:, 1], 98))
    cape_y = gen.cape_line(hi)
    print(f"[trellis] back_y {back_y:.3f}, cape line {[round(c, 3) for c in cape_y]}")
    bpy.data.objects.remove(hi)
    bpy.data.objects.remove(tex)
    pack = gen.build_backpack(back_y)
    use_hold()
    arm = gen.build_rig(J, cape_y)
    skin_weights(lo, arm, cape_y, J)
    if DIAG:
        gen.attach(lo, arm)
        zm.init_neutral(arm)
        zm.anim_idle(arm)
        arm.animation_data.action = bpy.data.actions["Idle"]
        bpy.context.scene.frame_set(0)
        dg = bpy.context.evaluated_depsgraph_get()
        ev = lo.evaluated_get(dg).to_mesh()
        P = np.array([v.co[:] for v in ev.vertices]); R = gen.co_array(lo)
        E = np.array([e.vertices[:] for e in lo.data.edges])
        lr = np.linalg.norm(R[E[:, 0]] - R[E[:, 1]], axis=1); lp = np.linalg.norm(P[E[:, 0]] - P[E[:, 1]], axis=1)
        bad = E[lp > 3 * lr + 0.02]
        names = {g.index: g.name for g in lo.vertex_groups}
        print(f"[diag] {len(bad)} stretched edges; unweighted verts {sum(1 for v in lo.data.vertices if not v.groups)}")
        seen = set()
        for a_, b_ in bad[:400]:
            for vi in (a_, b_):
                if vi in seen:
                    continue
                seen.add(vi)
                gs = sorted(((names[g.group], round(g.weight, 2)) for g in lo.data.vertices[vi].groups), key=lambda t: -t[1])
                if len(seen) < 40:
                    print("[diag]", vi, tuple(round(c, 3) for c in R[vi]), gs)
        diag_render(lo, "v4pose")
        sys.exit(0)
    mods = [gen.attach(lo, arm), gen.attach(pack, arm)]
    for m in mods:
        m.show_viewport = False
    sc = bpy.context.scene
    sc.render.fps = zm.FPS
    zm.init_neutral(arm)
    zm.anim_idle(arm)
    zm.anim_gait(arm, "Run", 16, (0, -1), 5.6, 0.36, 0.2, 8, 0, 0, crouch=0.03, bounce=0.03)
    zm.anim_gait(arm, "RunBack", 16, (0, 1), 4.2, 0.4, 0.12, 3, 0, 0, back=True, crouch=0.03, bounce=0.02)
    zm.anim_gait(arm, "StrafeL", 16, (1, 0), 5.0, 0.38, 0.15, 4, 26, 30, crouch=0.03, bounce=0.025)
    zm.anim_gait(arm, "StrafeR", 16, (-1, 0), 5.0, 0.38, 0.15, 4, -28, -32, crouch=0.03, bounce=0.025)
    zm.anim_death(arm)
    arm.animation_data.action = None
    for act in list(bpy.data.actions):
        tr = arm.animation_data.nla_tracks.new()
        tr.name = act.name
        tr.strips.new(act.name, 0, act)
    sc.frame_start, sc.frame_end = 0, 90
    for m in mods:
        m.show_viewport = True
    gen.export(arm, [lo, pack])
    zm.previews(arm, ARGS[5] if len(ARGS) > 5 else "")
    print("[trellis] marine v4 done")
