"""
ORBITAL MARSHAL v3 - from an AI-generated sculpt to a game-ready animated character.

    blender -b --factory-startup -P Tools/Blender/zcom_marine_gen.py -- <models_dir> <previews_dir> <sculpt.glb> <turnaround.png>

Pipeline
  1. Sculpt: Hunyuan3D-2 mesh generated from the Codex turnaround (Art/Reference/Codex/marshal_turnaround.png, front view).
  2. Normalise (1.95 m incl. antenna, feet on the ground, facing -Y) and decimate to a game mesh (~45k tris).
  3. Albedo: the three orthographic turnaround views (front / left side / back) are projected onto the mesh and blended
     by surface normal, then baked into a 2048 UV atlas. Normals: the 500k-triangle sculpt is baked onto the game mesh.
     Emission (cyan visor / lights) and metallic-smoothness are derived from the albedo.
  4. Extraction backpack (not visible in the front view) is modelled procedurally and attached to the chest.
  5. Rig fitted to the measured anatomy, rule-based skin weights (arms never grab the torso, cape never follows the legs),
     IK-driven animations from zcom_marine.py (Idle, Run, RunBack, StrafeL/R, Death), FBX export.
"""
import bpy, bmesh, math, os, sys
import numpy as np
from mathutils import Vector, Matrix, Quaternion

ARGS = sys.argv[sys.argv.index("--") + 1:]
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import zcom_marine as zm   # reads ARGS[0], ARGS[1] (models, previews)

MODELS, PREVIEWS = zm.MODELS, zm.PREVIEWS
GLB, TURN = os.path.abspath(ARGS[2]), os.path.abspath(ARGS[3])
TEX_DIR = zm.TEX_DIR
V = Vector
HEIGHT = 1.95
TEX = 2048


def co_array(o):
    n = len(o.data.vertices)
    a = np.empty(n * 3)
    o.data.vertices.foreach_get("co", a)
    return a.reshape(-1, 3)


def set_co(o, a):
    o.data.vertices.foreach_set("co", a.ravel())
    o.data.update()


# ============================================================================ 1-2. import, normalise, decimate

def load_sculpt():
    bpy.ops.import_scene.gltf(filepath=GLB)
    hi = [o for o in bpy.context.scene.objects if o.type == "MESH"][0]
    for o in list(bpy.context.scene.objects):
        if o != hi and o.type != "MESH":
            bpy.data.objects.remove(o)
    hi.data.transform(hi.matrix_world)
    hi.parent = None
    hi.matrix_world = Matrix.Identity(4)
    co = co_array(hi)
    mn, mx = co.min(0), co.max(0)
    s = HEIGHT / (mx[2] - mn[2])
    raw = dict(xmin=(mn[0] - (mn[0] + mx[0]) / 2) * s, xmax=(mx[0] - (mn[0] + mx[0]) / 2) * s)
    co[:, 0] -= (mn[0] + mx[0]) / 2
    co[:, 1] -= (mn[1] + mx[1]) / 2
    co[:, 2] -= mn[2]
    co *= s
    # centre the legs on x = 0 (the big left pauldron / antenna skew the bounding box)
    low = co[co[:, 2] < 0.45]
    shift = (np.percentile(low[:, 0], 2) + np.percentile(low[:, 0], 98)) / 2
    co[:, 0] -= shift
    set_co(hi, co)
    hi.name = hi.data.name = "Sculpt"
    raw["shift"] = shift
    raw["ymin"], raw["ymax"] = co[:, 1].min(), co[:, 1].max()
    print(f"[zcom] sculpt {len(co)} verts, scale {s:.3f}, leg shift {shift:.3f}")
    return hi, raw


def decimate(hi, target_tris=45000):
    lo = hi.copy()
    lo.data = hi.data.copy()
    lo.name = lo.data.name = "M_Marshal"
    bpy.context.scene.collection.objects.link(lo)
    tris = sum(len(p.vertices) - 2 for p in hi.data.polygons)
    d = lo.modifiers.new("Dec", "DECIMATE")
    d.ratio = min(1.0, target_tris / tris)
    zm.apply_mods(lo)
    for p in lo.data.polygons:
        p.use_smooth = True
    print(f"[zcom] game mesh {sum(len(p.vertices) - 2 for p in lo.data.polygons)} tris")
    return lo


# ============================================================================ 3. projected albedo + baked normals

def view_crops():
    """Find the three character views by their content columns (white gaps between them) and crop each tightly."""
    img = bpy.data.images.load(TURN)
    W, H = img.size
    px = np.array(img.pixels[:]).reshape(H, W, 4)
    ink = px[:, :, :3].mean(2) < 0.9
    cols = ink.sum(0) > 2
    runs, start = [], None
    for x in range(W):
        if cols[x] and start is None:
            start = x
        if (not cols[x] or x == W - 1) and start is not None:
            if x - start > 40:
                runs.append((start, x))
            start = None
    runs = sorted(runs, key=lambda r: r[1] - r[0], reverse=True)[:3]
    runs.sort()
    views = {}
    for name, (x0, x1) in zip(("front", "left", "back"), runs):
        rows = np.where(ink[:, x0:x1].sum(1) > 0)[0]
        y0, y1 = rows.min(), rows.max()
        pad = 6
        cx0, cx1 = max(0, x0 - pad), min(W, x1 + pad)
        crop = px[:, cx0:cx1, :].copy()
        ci = bpy.data.images.new(f"view_{name}", cx1 - cx0, H, alpha=True)
        ci.pixels = crop.ravel().tolist()
        ci.pack()
        bb = (x0 - cx0, x1 - cx0, y0, y1)
        views[name] = (ci, bb, cx1 - cx0, H)
        print(f"[zcom] view {name} cols {x0}-{x1} rows {y0}-{y1}")
    return views


def projection_material(views, raw):
    m = bpy.data.materials.new("MarshalArmor")
    m.use_nodes = True
    nt = m.node_tree
    N, L = nt.nodes, nt.links
    for n in list(N):
        N.remove(n)
    out = N.new("ShaderNodeOutputMaterial")
    geo = N.new("ShaderNodeNewGeometry")
    sep = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Position"], sep.inputs[0])
    nsep = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Normal"], nsep.inputs[0])

    def affine(sock, a, b):
        m1 = N.new("ShaderNodeMath"); m1.operation = "MULTIPLY_ADD"
        L.new(sock, m1.inputs[0]); m1.inputs[1].default_value = a; m1.inputs[2].default_value = b
        return m1.outputs[0]

    def sample(name, u_sock, v_sock):
        img, bb, w, h = views[name]
        cx = N.new("ShaderNodeCombineXYZ")
        L.new(u_sock, cx.inputs[0]); L.new(v_sock, cx.inputs[1])
        t = N.new("ShaderNodeTexImage"); t.image = img; t.extension = "EXTEND"; t.interpolation = "Cubic"
        L.new(cx.outputs[0], t.inputs["Vector"])
        return t.outputs["Color"]

    shift = raw["shift"]
    # front: +X (character's left) is image-right; the bbox spans the raw (pre-shift) x range
    img, bb, w, h = views["front"]
    ppm_x = (bb[1] - bb[0]) / (raw["xmax"] - raw["xmin"])
    ppm = (bb[3] - bb[2]) / HEIGHT
    u_front = affine(sep.outputs[0], ppm_x / w, (bb[0] + (shift - raw["xmin"]) * ppm_x) / w)
    v_front = affine(sep.outputs[2], ppm / h, bb[2] / h)
    c_front = sample("front", u_front, v_front)
    # back: mirrored, centred on the character
    img, bb, w, h = views["back"]
    ppm_b = (bb[3] - bb[2]) / HEIGHT
    cxb = (bb[0] + bb[1]) / 2
    u_back = affine(sep.outputs[0], -ppm_b / w, (cxb - (shift + (raw["xmin"] + raw["xmax"]) / 2) * -ppm_b) / w)
    v_back = affine(sep.outputs[2], ppm_b / h, bb[2] / h)
    c_back = sample("back", u_back, v_back)
    # left side: the character faces image-left, frontmost point at the bbox left edge
    img, bb, w, h = views["left"]
    ppm_s = (bb[3] - bb[2]) / HEIGHT
    u_side = affine(sep.outputs[1], ppm_s / w, (bb[0] - raw["ymin"] * ppm_s) / w)
    v_side = affine(sep.outputs[2], ppm_s / h, bb[2] / h)
    c_side = sample("left", u_side, v_side)

    def weight(sock, sign, absval=False):
        a = N.new("ShaderNodeMath")
        if absval:
            a.operation = "ABSOLUTE"; L.new(sock, a.inputs[0])
        else:
            a.operation = "MULTIPLY"; L.new(sock, a.inputs[0]); a.inputs[1].default_value = sign
        c = N.new("ShaderNodeMath"); c.operation = "MAXIMUM"; L.new(a.outputs[0], c.inputs[0]); c.inputs[1].default_value = 0.0
        p = N.new("ShaderNodeMath"); p.operation = "POWER"; L.new(c.outputs[0], p.inputs[0]); p.inputs[1].default_value = 3.0
        return p.outputs[0]
    wf, wb, ws = weight(nsep.outputs[1], -1), weight(nsep.outputs[1], 1), weight(nsep.outputs[0], 1, True)

    def scale(col, wsock):
        s = N.new("ShaderNodeVectorMath"); s.operation = "SCALE"
        L.new(col, s.inputs[0]); L.new(wsock, s.inputs["Scale"])
        return s.outputs[0]

    def add(a, b):
        s = N.new("ShaderNodeVectorMath"); s.operation = "ADD"
        L.new(a, s.inputs[0]); L.new(b, s.inputs[1])
        return s.outputs[0]

    tot = N.new("ShaderNodeMath"); tot.operation = "ADD"; L.new(wf, tot.inputs[0]); L.new(wb, tot.inputs[1])
    tot2 = N.new("ShaderNodeMath"); tot2.operation = "ADD"; L.new(tot.outputs[0], tot2.inputs[0]); L.new(ws, tot2.inputs[1])
    tot3 = N.new("ShaderNodeMath"); tot3.operation = "MAXIMUM"; L.new(tot2.outputs[0], tot3.inputs[0]); tot3.inputs[1].default_value = 1e-4
    inv = N.new("ShaderNodeMath"); inv.operation = "DIVIDE"; inv.inputs[0].default_value = 1.0; L.new(tot3.outputs[0], inv.inputs[1])
    col = scale(add(add(scale(c_front, wf), scale(c_back, wb)), scale(c_side, ws)), inv.outputs[0])
    em = N.new("ShaderNodeEmission")
    L.new(col, em.inputs["Color"])
    L.new(em.outputs[0], out.inputs["Surface"])
    m["zcom_kind"] = "projection"
    return m


def bake_maps(hi, lo, views, raw):
    zm.setup_gpu(8)
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

    mat = projection_material(views, raw)
    lo.data.materials.clear()
    lo.data.materials.append(mat)
    hi.data.materials.clear()
    hi.data.materials.append(mat)
    base = bpy.data.images.new("Marshal_BaseColor", TEX, TEX, alpha=False)
    nrm = bpy.data.images.new("Marshal_Normal", TEX, TEX, alpha=False)
    nrm.colorspace_settings.name = "Non-Color"
    tnode = mat.node_tree.nodes.new("ShaderNodeTexImage")
    mat.node_tree.nodes.active = tnode

    tnode.image = base
    sc.render.bake.margin = 16
    bpy.ops.object.bake(type="EMIT", margin=16, use_clear=True)
    print("[zcom] baked projected albedo")

    # sculpt detail -> normal map on the game mesh
    hi.hide_render = False
    hi.select_set(True)
    lo.select_set(True)
    bpy.context.view_layer.objects.active = lo
    tnode.image = nrm
    sc.render.bake.normal_space = "TANGENT"
    bpy.ops.object.bake(type="NORMAL", use_selected_to_active=True, cage_extrusion=0.02, max_ray_distance=0.05, margin=16, use_clear=True)
    print("[zcom] baked sculpt normals")
    hi.select_set(False)

    # derived maps
    b = np.array(base.pixels[:]).reshape(-1, 4)
    r, g, bl = b[:, 0], b[:, 1], b[:, 2]
    lum = 0.2126 * r + 0.7152 * g + 0.0722 * bl
    mx, mn = b[:, :3].max(1), b[:, :3].min(1)
    sat = (mx - mn) / np.maximum(mx, 1e-4)
    cyan = (bl > 0.45) & (g > 0.4) & (r < 0.6 * g) & (sat > 0.35)
    # background leaking through silhouette mismatches (white) -> dark cloth
    bgm = (lum > 0.78) & (sat < 0.12)
    b[bgm, 0], b[bgm, 1], b[bgm, 2] = 0.045, 0.047, 0.052
    base.pixels = b.ravel().tolist()
    r, g, bl = b[:, 0], b[:, 1], b[:, 2]
    lum = 0.2126 * r + 0.7152 * g + 0.0722 * bl
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
    def save(name, arr, alpha=False, noncolor=True):
        im = bpy.data.images.new(name, TEX, TEX, alpha=alpha)
        if noncolor:
            im.colorspace_settings.name = "Non-Color"
        im.pixels = arr.ravel().tolist()
        im.filepath_raw = os.path.join(TEX_DIR, name + ".png")
        im.file_format = "PNG"
        im.save()
        return im
    for im, name in ((base, "Marshal_BaseColor"), (nrm, "Marshal_Normal")):
        im.filepath_raw = os.path.join(TEX_DIR, name + ".png")
        im.file_format = "PNG"
        im.save()
    emi = save("Marshal_Emission", em, noncolor=False)
    msi = save("Marshal_MetallicSmoothness", ms, alpha=True)

    # final textured material (preview + FBX name "MarshalArmor")
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    p = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(p.outputs[0], out.inputs[0])
    def tex(im):
        t = nt.nodes.new("ShaderNodeTexImage"); t.image = im
        return t
    nt.links.new(tex(base).outputs["Color"], p.inputs["Base Color"])
    tm = tex(msi)
    nt.links.new(tm.outputs["Color"], p.inputs["Metallic"])
    inv = nt.nodes.new("ShaderNodeMath"); inv.operation = "SUBTRACT"; inv.inputs[0].default_value = 1.0
    nt.links.new(tm.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs[0], p.inputs["Roughness"])
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tex(nrm).outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], p.inputs["Normal"])
    nt.links.new(tex(emi).outputs["Color"], p.inputs["Emission Color"])
    p.inputs["Emission Strength"].default_value = 4.0
    mat["zcom_kind"] = "baked"
    return mat


# ============================================================================ 4. backpack

def build_backpack(raw_back_y):
    zm.PARTS.clear()
    M = zm.make_mats()
    zm.build_backpack(M)
    parts = []
    for o, bone in zm.PARTS:
        if o.name.startswith("Strap"):
            bpy.data.objects.remove(o)
            continue
        zm.apply_mods(o)
        parts.append(o)
    dy = raw_back_y + 0.005 - 0.17          # the pack's front face sits at y = 0.17 in zcom_marine
    for o in parts:
        o.location.y += dy
        o.location.z += 0.02
    for o in parts:
        o.data.transform(o.matrix_world)
        o.matrix_world = Matrix.Identity(4)
    with bpy.context.temp_override(active_object=parts[0], selected_editable_objects=parts, selected_objects=parts, object=parts[0]):
        bpy.ops.object.join()
    pack = parts[0]
    pack.name = pack.data.name = "M_Backpack"
    pack.vertex_groups.new(name="Chest").add(list(range(len(pack.data.vertices))), 1.0, "REPLACE")
    return pack


# ============================================================================ 5. rig fitted to the sculpt

def fit_joints(hi):
    co = co_array(hi)
    def slice_(z, dz=0.012):
        return co[np.abs(co[:, 2] - z) < dz]
    def center_y(z, xr=0.12):
        s = slice_(z)
        s = s[np.abs(s[:, 0]) < xr]
        return (s[:, 1].min() + s[:, 1].max()) / 2 if len(s) else 0.0
    J = {}
    spine_y = center_y(1.05) * 0.5 + center_y(1.3) * 0.5 - 0.02
    J["spine_y"] = spine_y
    for s, x in (("L", 1), ("R", -1)):
        def arm_cluster(z):
            sl = slice_(z, 0.015)
            sl = sl[sl[:, 0] * x > 0.15]
            if len(sl) < 5:
                return None
            xs = np.sort(sl[:, 0] * x)
            gaps = np.diff(xs)
            k = np.where(gaps > 0.018)[0]
            if len(k) == 0:
                return None
            edge = (xs[k[-1]] + xs[k[-1] + 1]) / 2
            c = sl[sl[:, 0] * x > edge]
            return c.mean(0), edge
        pts = {z: arm_cluster(z) for z in np.arange(0.76, 1.2, 0.02)}
        good = {z: v for z, v in pts.items() if v is not None}
        zs = sorted(good)
        hand_z = zs[0]
        tip = V(good[hand_z][0])
        wz = min(zs, key=lambda z: abs(z - (hand_z + 0.16)))
        wri = V(good[wz][0])
        sho = V((0.235 * x, spine_y + 0.02, 1.445))
        ez = min(zs, key=lambda z: abs(z - 1.18)) if zs[-1] > 1.1 else None
        elb = V(good[ez][0]) + V((0, 0.03, 0)) if ez else (sho + wri) / 2 + V((0.01 * x, 0.04, 0.0))
        J["sho" + s], J["elb" + s], J["wri" + s] = sho, elb, wri
        J["hand" + s] = tip + V((0, 0, 0.02))
        J["armedge" + s] = {round(z, 2): v[1] for z, v in good.items()}
        leg = co[(co[:, 0] * x > 0.02) & (np.abs(co[:, 2] - 0.3) < 0.02)]
        leg = leg[np.abs(leg[:, 0] - np.median(leg[:, 0])) < 0.15]
        lx = float(np.median(leg[:, 0]))
        foot = co[(co[:, 0] * x > 0.02) & (co[:, 2] < 0.06)]
        toe_y, heel_y = foot[:, 1].min(), foot[:, 1].max()
        ank = V((lx * 1.05, heel_y - 0.1, 0.115))
        J["hip" + s] = V((0.105 * x, spine_y, 0.94))
        J["knee" + s] = V((lx * 0.95, spine_y - 0.035, 0.52))
        J["ank" + s] = ank
        J["ball" + s] = V((ank.x, toe_y + 0.09, 0.035))
        J["toe" + s] = V((ank.x, toe_y + 0.01, 0.035))
    # the left arm merges with the holster near the hand: mirror the cleanly measured right arm when it looks short
    if J["handL"].z > J["handR"].z + 0.05:
        dx = (J["wriL"].x + J["wriR"].x)              # how much wider/narrower the left arm hangs
        for k in ("elb", "wri", "hand"):
            r = J[k + "R"]
            J[k + "L"] = V((-r.x + dx * 0.5, r.y, r.z))
        J["armedgeL"] = {z: e for z, e in J["armedgeR"].items()}
    for k, v in J.items():
        if isinstance(v, dict):
            continue
        print(f"[zcom] joint {k} {tuple(round(c, 3) for c in v) if hasattr(v, '__len__') else round(v, 3)}")
    return J


def cape_line(hi):
    co = co_array(hi)
    pts = []
    for z in zm.CAPE_Z:
        s = co[(np.abs(co[:, 2] - z) < 0.02) & (np.abs(co[:, 0]) < 0.16)]
        pts.append(float(s[:, 1].max()) - 0.02 if len(s) else 0.2)
    return pts


def build_rig(J, cape_y):
    ad = bpy.data.armatures.new("MarshalRig")
    arm = zm.link(bpy.data.objects.new("MarshalRig", ad))
    for o in bpy.context.scene.objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = ad.edit_bones
    sy = J["spine_y"]
    def B(name, h, t, parent=None, connect=False, roll=V((0, -1, 0))):
        b = eb.new(name)
        b.head, b.tail = V(h), V(t)
        if parent:
            b.parent = eb[parent]; b.use_connect = connect
        b.align_roll(roll)
    B("Hips", (0, sy, 0.97), (0, sy, 1.07))
    B("Spine", (0, sy, 1.07), (0, sy, 1.24), "Hips", True)
    B("Chest", (0, sy, 1.24), (0, sy, 1.46), "Spine", True)
    B("Neck", (0, sy - 0.01, 1.5), (0, sy - 0.03, 1.6), "Chest")
    B("Head", (0, sy - 0.03, 1.6), (0, sy - 0.03, 1.84), "Neck", True)
    for s, x in (("L", 1), ("R", -1)):
        B(f"Shoulder_{s}", (0.05 * x, sy, 1.43), J["sho" + s], "Chest")
        B(f"UpperArm_{s}", J["sho" + s], J["elb" + s], f"Shoulder_{s}", True)
        B(f"ForeArm_{s}", J["elb" + s], J["wri" + s], f"UpperArm_{s}", True)
        B(f"Hand_{s}", J["wri" + s], J["hand" + s], f"ForeArm_{s}", True)
        B(f"Thigh_{s}", J["hip" + s], J["knee" + s], "Hips")
        B(f"Shin_{s}", J["knee" + s], J["ank" + s], f"Thigh_{s}", True)
        B(f"Foot_{s}", J["ank" + s], J["ball" + s], f"Shin_{s}", True, roll=V((0, 0, 1)))
        B(f"Toe_{s}", J["ball" + s], J["toe" + s], f"Foot_{s}", True, roll=V((0, 0, 1)))
    B("Weapon", zm.GRIP, zm.GRIP + V((0, -0.2, 0)), "Chest", roll=V((0, 0, 1)))
    for c, x in enumerate(zm.CAPE_COLS):
        prev = "Chest"
        for r in range(3):
            z0, z1 = zm.CAPE_Z[r], zm.CAPE_Z[r + 1]
            B(f"Cape_{c}{r}", (x * (1 + 0.2 * r), cape_y[r], z0), (x * (1 + 0.2 * (r + 1)), cape_y[r + 1], z1), prev, r > 0, roll=V((0, 1, 0)))
            prev = f"Cape_{c}{r}"
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return arm


def skin_weights(lo, arm, cape_y, J=None):
    """Distance-to-bone weights with anatomical rules (arms only own the arms, legs never pull the cape)."""
    segs = {b.name: (np.array(b.head_local), np.array(b.tail_local)) for b in arm.data.bones}
    co = co_array(lo)
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
    # arms own only what lies within the limb's own radius around its bones (no edge cutting through the palm)
    arm_ok = np.zeros(len(co), bool)
    for side in ("L", "R"):
        arm_ok |= (D[:, idx[f"ForeArm_{side}"]] < 0.085) | (D[:, idx[f"Hand_{side}"]] < 0.08) | \
                  ((D[:, idx[f"UpperArm_{side}"]] < 0.13) & (z > 1.02))
    not_arm = ~arm_ok
    D[np.ix_(not_arm, arm_cols)] = INF
    # hands / forearms never reach the belt pouches and holster
    hand_cols = [idx[n] for n in allb if n.split("_")[0] in ("Hand", "ForeArm")]
    D[np.ix_(not_arm, hand_cols)] = INF
    # wrong side arm/leg
    for n in allb:
        if n.endswith("_L"):
            D[x < -0.04, idx[n]] = INF
        elif n.endswith("_R"):
            D[x > 0.04, idx[n]] = INF
    # legs below the belt only
    D[np.ix_(z > 1.02, leg_cols)] = INF
    # cape: behind the legs, away from the leg bones
    leg_d = D[:, leg_cols].min(1)
    is_cape = (y > 0.08) & (z < 1.25) & (z > 0.35) & (leg_d > 0.11)
    D[np.ix_(is_cape, leg_cols)] = INF
    # arms hang beside / in front of the body, never behind it (keeps the cape edge off the hands)
    D[np.ix_((y > 0.05) & (z < 1.2), arm_cols)] = INF
    D[np.ix_(~is_cape, cape_cols)] = INF
    D[np.ix_(is_cape, [idx["Hips"], idx["Spine"]])] *= 1.6
    order = np.argsort(D, axis=1)[:, :3]
    d3 = np.take_along_axis(D, order, 1)
    w = 1.0 / (d3 + 0.01) ** 5
    w[d3 >= INF * 0.5] = 0.0
    w /= np.maximum(w.sum(1, keepdims=True), 1e-9)
    # The sculpt is one fused surface: where a hand touches the hip scanner or the cape touches a leg, the skin is
    # shared. Cut those bridging faces so independent parts can separate when animated.
    def region(n):
        k = n.split("_")[0]
        return "hand" if k in ("ForeArm", "Hand") else "leg" if k in ("Thigh", "Shin", "Foot", "Toe") else \
               "cape" if k == "Cape" else "low" if k in ("Hips", "Spine") else "other"
    dom = [region(allb[order[i, 0]]) for i in range(len(co))]
    forbidden = ({"hand", "low"}, {"hand", "leg"}, {"cape", "leg"})
    bm = bmesh.new()
    bm.from_mesh(lo.data)
    bm.faces.ensure_lookup_table()
    kill = []
    for f in bm.faces:
        rs = {dom[v.index] for v in f.verts}
        if any(fb <= rs for fb in forbidden):
            kill.append(f)
    bmesh.ops.delete(bm, geom=kill, context="FACES_ONLY")
    # drop small fragments the cut left floating (faces only, vertex indices stay valid for the weights)
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
    small = [f for comp in islands[1:] if len(comp) < 1500 for f in comp]
    bmesh.ops.delete(bm, geom=small, context="FACES_ONLY")
    print(f"[zcom] removed {len(small)} faces in {sum(1 for c in islands[1:] if len(c) < 1500)} loose fragments")
    bm.to_mesh(lo.data)
    bm.free()
    print(f"[zcom] cut {len(kill)} bridging faces")
    groups = {n: lo.vertex_groups.new(name=n) for n in allb}
    for vi in range(len(co)):
        for k in range(3):
            if w[vi, k] > 0.02:
                groups[allb[order[vi, k]]].add([vi], float(w[vi, k]), "REPLACE")
    print(f"[zcom] skinned {len(co)} verts ({int(is_cape.sum())} cape)")


def attach(obj, arm):
    obj.parent = arm
    m = obj.modifiers.new("Armature", "ARMATURE")
    m.object = arm
    return m


# ============================================================================ main

def export(arm, meshes):
    e = bpy.data.objects.new("Front", None)
    e.location = (0, -1, 1)
    zm.link(e)
    for o in bpy.data.objects:
        o.select_set(o in [arm, e] + meshes)
    path = os.path.join(MODELS, "Marshal.fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH", "ARMATURE", "EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        mesh_smooth_type="FACE", use_mesh_modifiers=True, use_armature_deform_only=False, add_leaf_bones=False,
        primary_bone_axis="Y", secondary_bone_axis="X", path_mode="STRIP",
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
    print(f"[zcom] exported {path}")


if __name__ == "__main__":
    zm.reset()
    hi, raw = load_sculpt()
    lo = decimate(hi)
    views = view_crops()
    bake_maps(hi, lo, views, raw)
    J = fit_joints(hi)
    back_y = float(np.percentile(co_array(hi)[(np.abs(co_array(hi)[:, 2] - 1.3) < 0.02) & (np.abs(co_array(hi)[:, 0]) < 0.12)][:, 1], 98))
    cape_y = cape_line(hi)
    bpy.data.objects.remove(hi)
    pack = build_backpack(back_y)
    zm.GRIP = V((-0.065, -0.25, 1.17))
    zm.STANCE_YAW = -22.0
    arm = build_rig(J, cape_y)
    skin_weights(lo, arm, cape_y, J)
    mods = [attach(lo, arm), attach(pack, arm)]
    for m in mods:
        m.show_viewport = False
    # Gait tuned for a heavy marine: ~3.75 steps/s, ~1.45 m strides (no lunging overstride), calm strafes.
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
    export(arm, [lo, pack])
    zm.previews(arm, ARGS[4] if len(ARGS) > 4 else "")
    print("[zcom] marine v3 done")
