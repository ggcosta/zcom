"""
ORBITAL MARSHAL v2 - space-marine playable character (Art/Reference/Codex/orbital_marshal_model_sheet.png).

    blender -b --factory-startup -P Tools/Blender/zcom_marine.py -- <models_dir> <previews_dir> [stage]

    stage = "model"  : build + preview renders only (fast iteration)
            "full"   : build, rig, animate, bake textures, export FBX + textures, previews (default)

Construction
  * Undersuit: an anatomical body made with a Skin modifier over a joint graph, subdivided (smooth muscle forms).
  * Armour: faceted plates generated as convex hulls of points ray-cast onto the body surface and pushed out,
    so every plate hugs the anatomy; bevelled with hardened normals for crisp hard-surface highlights.
  * Helmet with T-visor, layered command pauldron with amber chevrons, high collar, extraction backpack,
    belt with pouches and hip scanner, gloves, chunky boots, long split cape with an inverted-V stripe.

Blender axes: Z up, the character faces -Y, +X is the character's LEFT.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Quaternion, Euler
from mathutils.bvhtree import BVHTree

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
MODELS = os.path.abspath(ARGS[0]) if len(ARGS) > 0 else os.path.abspath("Assets/ZCOM/Resources/Models")
PREVIEWS = os.path.abspath(ARGS[1]) if len(ARGS) > 1 else os.path.abspath("Tools/Blender/previews")
STAGE = ARGS[2] if len(ARGS) > 2 else "full"
TEX_DIR = os.path.abspath(os.path.join(MODELS, "..", "Textures", "Marshal"))
for d in (MODELS, PREVIEWS, TEX_DIR):
    os.makedirs(d, exist_ok=True)

V = Vector
rad = math.radians
PARTS = []          # (object, bone or None)  - None = smooth-skinned by distance (undersuit, cape)


# ============================================================================ scene / materials

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def _principled(m):
    nt = m.node_tree
    return nt.nodes.get("Principled BSDF"), nt


def material(name, color, metal, rough, kind="plain", emit=None, strength=0.0, wear=0.0, bump=0.0):
    """Procedural PBR material (used for preview renders and baked into the texture atlas).
    kind: plain | armor (edge wear + grime + scratches) | fabric (quilted weave) | emit"""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.diffuse_color = (*color, 1)
    b, nt = _principled(m)
    N, L = nt.nodes, nt.links
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    m["zcom_kind"] = kind
    if kind == "emit":
        b.inputs["Base Color"].default_value = (color[0] * 0.2, color[1] * 0.2, color[2] * 0.2, 1)
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = strength
        return m

    tc = N.new("ShaderNodeTexCoord")
    # grime: ambient occlusion darkens crevices
    ao = N.new("ShaderNodeAmbientOcclusion"); ao.inputs["Distance"].default_value = 0.06; ao.samples = 8
    noise = N.new("ShaderNodeTexNoise"); noise.inputs["Scale"].default_value = 18; noise.inputs["Detail"].default_value = 8
    L.new(tc.outputs["Object"], noise.inputs["Vector"])
    col = N.new("ShaderNodeMix"); col.data_type = "RGBA"; col.blend_type = "MULTIPLY"
    col.inputs["Factor"].default_value = 1.0
    col.inputs["A"].default_value = (*color, 1)
    # subtle albedo variation
    var = N.new("ShaderNodeMapRange"); var.inputs["To Min"].default_value = 0.82; var.inputs["To Max"].default_value = 1.12
    L.new(noise.outputs["Fac"], var.inputs["Value"])
    grime = N.new("ShaderNodeMapRange"); grime.inputs["To Min"].default_value = 0.45; grime.inputs["To Max"].default_value = 1.0
    L.new(ao.outputs["AO"], grime.inputs["Value"])
    mul = N.new("ShaderNodeMath"); mul.operation = "MULTIPLY"
    L.new(var.outputs["Result"], mul.inputs[0]); L.new(grime.outputs["Result"], mul.inputs[1])
    L.new(mul.outputs["Value"], col.inputs["B"])
    base = col.outputs["Result"]
    rough_out = None

    if kind == "armor" and wear > 0:
        # edge wear: compare bevelled normal with the true normal -> bright worn metal on edges
        bev = N.new("ShaderNodeBevel"); bev.inputs["Radius"].default_value = 0.012; bev.samples = 8
        geo = N.new("ShaderNodeNewGeometry")
        dot = N.new("ShaderNodeVectorMath"); dot.operation = "DOT_PRODUCT"
        L.new(bev.outputs["Normal"], dot.inputs[0]); L.new(geo.outputs["Normal"], dot.inputs[1])
        edge = N.new("ShaderNodeMapRange"); edge.inputs["From Min"].default_value = 0.985; edge.inputs["From Max"].default_value = 0.94
        L.new(dot.outputs["Value"], edge.inputs["Value"])
        n2 = N.new("ShaderNodeTexNoise"); n2.inputs["Scale"].default_value = 60; n2.inputs["Detail"].default_value = 10
        L.new(tc.outputs["Object"], n2.inputs["Vector"])
        chip = N.new("ShaderNodeMath"); chip.operation = "MULTIPLY"
        L.new(edge.outputs["Result"], chip.inputs[0])
        nm = N.new("ShaderNodeMapRange"); nm.inputs["From Min"].default_value = 0.35; nm.inputs["From Max"].default_value = 0.6
        nm.inputs["To Min"].default_value = 0.4; nm.inputs["To Max"].default_value = 1.6
        L.new(n2.outputs["Fac"], nm.inputs["Value"]); L.new(nm.outputs["Result"], chip.inputs[1])
        # scratches: stretched noise thresholded
        sc_map = N.new("ShaderNodeMapping"); sc_map.inputs["Scale"].default_value = (1.0, 1.0, 30.0)
        L.new(tc.outputs["Object"], sc_map.inputs["Vector"])
        sc = N.new("ShaderNodeTexNoise"); sc.inputs["Scale"].default_value = 30; sc.inputs["Detail"].default_value = 2
        L.new(sc_map.outputs["Vector"], sc.inputs["Vector"])
        scr = N.new("ShaderNodeMapRange"); scr.inputs["From Min"].default_value = 0.66; scr.inputs["From Max"].default_value = 0.72
        L.new(sc.outputs["Fac"], scr.inputs["Value"])
        scm = N.new("ShaderNodeMath"); scm.operation = "MULTIPLY"; scm.inputs[1].default_value = 0.45
        L.new(scr.outputs["Result"], scm.inputs[0])
        tot = N.new("ShaderNodeMath"); tot.operation = "ADD"; tot.use_clamp = True
        L.new(chip.outputs["Value"], tot.inputs[0]); L.new(scm.outputs["Value"], tot.inputs[1])
        wf = N.new("ShaderNodeMath"); wf.operation = "MULTIPLY"; wf.inputs[1].default_value = wear; wf.use_clamp = True
        L.new(tot.outputs["Value"], wf.inputs[0])
        worn = N.new("ShaderNodeMix"); worn.data_type = "RGBA"
        L.new(wf.outputs["Value"], worn.inputs["Factor"]); L.new(base, worn.inputs["A"])
        worn.inputs["B"].default_value = (0.55, 0.56, 0.58, 1)
        base = worn.outputs["Result"]
        mr = N.new("ShaderNodeMix"); mr.data_type = "FLOAT"
        L.new(wf.outputs["Value"], mr.inputs["Factor"]); mr.inputs["A"].default_value = rough; mr.inputs["B"].default_value = 0.28
        rough_out = mr.outputs["Result"]
        mm = N.new("ShaderNodeMix"); mm.data_type = "FLOAT"
        L.new(wf.outputs["Value"], mm.inputs["Factor"]); mm.inputs["A"].default_value = metal; mm.inputs["B"].default_value = 1.0
        L.new(mm.outputs["Result"], b.inputs["Metallic"])
        # micro dents
        bp = N.new("ShaderNodeBump"); bp.inputs["Strength"].default_value = 0.12; bp.inputs["Distance"].default_value = 0.002
        L.new(n2.outputs["Fac"], bp.inputs["Height"])
        L.new(bp.outputs["Normal"], b.inputs["Normal"])

    if kind == "fabric":
        # padded / quilted undersuit: wave bands + fine weave
        wv = N.new("ShaderNodeTexWave"); wv.inputs["Scale"].default_value = 26; wv.wave_profile = "SAW"
        wv.bands_direction = "Z"
        L.new(tc.outputs["Object"], wv.inputs["Vector"])
        weave = N.new("ShaderNodeTexNoise"); weave.inputs["Scale"].default_value = 400; weave.inputs["Detail"].default_value = 2
        L.new(tc.outputs["Object"], weave.inputs["Vector"])
        h = N.new("ShaderNodeMath"); h.operation = "ADD"
        hm = N.new("ShaderNodeMath"); hm.operation = "MULTIPLY"; hm.inputs[1].default_value = 0.25
        L.new(weave.outputs["Fac"], hm.inputs[0])
        L.new(wv.outputs["Fac"], h.inputs[0]); L.new(hm.outputs["Value"], h.inputs[1])
        bp = N.new("ShaderNodeBump"); bp.inputs["Strength"].default_value = bump or 0.35; bp.inputs["Distance"].default_value = 0.004
        L.new(h.outputs["Value"], bp.inputs["Height"])
        L.new(bp.outputs["Normal"], b.inputs["Normal"])

    L.new(base, b.inputs["Base Color"])
    if rough_out:
        L.new(rough_out, b.inputs["Roughness"])
    return m


def make_mats():
    return dict(
        steel=material("Steel", (0.17, 0.18, 0.195), 0.6, 0.45, "armor", wear=1.0),
        graphite=material("Graphite", (0.032, 0.034, 0.039), 0.35, 0.48, "armor", wear=0.55),
        fabric=material("Fabric", (0.022, 0.023, 0.026), 0.0, 0.8, "fabric", bump=0.4),
        pouch=material("Pouch", (0.055, 0.05, 0.04), 0.0, 0.78, "fabric", bump=0.25),
        rubber=material("Rubber", (0.016, 0.016, 0.018), 0.0, 0.7, "armor", wear=0.25),
        amber=material("Amber", (0.78, 0.36, 0.045), 0.15, 0.5, "armor", wear=0.6),
        cape=material("Cape", (0.03, 0.031, 0.034), 0.0, 0.9, "fabric", bump=0.15),
        stripe=material("Stripe", (0.32, 0.32, 0.31), 0.0, 0.85, "fabric", bump=0.15),
        glow=material("Glow", (0.25, 0.88, 0.94), 0.0, 0.2, "emit", emit=(0.25, 0.88, 0.94), strength=6.0),
        screen=material("Screen", (0.25, 0.88, 0.94), 0.0, 0.2, "emit", emit=(0.2, 0.75, 0.85), strength=2.5),
    )


# ============================================================================ mesh helpers

def link(obj):
    bpy.context.scene.collection.objects.link(obj)
    return obj


def mesh_from_bm(name, bm, mat=None, smooth=True):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = link(bpy.data.objects.new(name, me))
    if mat:
        me.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = smooth
    return o


def apply_mods(o):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    o.modifiers.clear()
    old = o.data
    o.data = me
    if old.users == 0:
        bpy.data.meshes.remove(old)
    return o


def harden(o, width=0.006, segs=2, angle=32, weld=False):
    """Hard-surface finish: angle-limited bevel with hardened normals + weighted normals."""
    if weld:
        w = o.modifiers.new("Weld", "WELD"); w.merge_threshold = 0.004
    b = o.modifiers.new("Bevel", "BEVEL")
    b.width = width
    b.segments = segs
    b.limit_method = "ANGLE"
    b.angle_limit = rad(angle)
    b.harden_normals = True
    b.miter_outer = "MITER_ARC"
    wn = o.modifiers.new("WN", "WEIGHTED_NORMAL"); wn.keep_sharp = True
    for p in o.data.polygons:
        p.use_smooth = True
    return o


def hull(name, pts, mat, bone, width=0.006, segs=2, angle=30, planar=4.0):
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in pts]
    res = bmesh.ops.convex_hull(bm, input=vs)
    kill = {g for g in res["geom_interior"] + res["geom_unused"] if isinstance(g, bmesh.types.BMVert)}
    if kill:
        bmesh.ops.delete(bm, geom=list(kill), context="VERTS")
    bmesh.ops.dissolve_limit(bm, angle_limit=rad(planar), verts=bm.verts[:], edges=bm.edges[:])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = mesh_from_bm(name, bm, mat)
    harden(o, width, segs, angle)
    PARTS.append((o, bone))
    return o


def box(name, loc, size, mat, bone, rot=(0, 0, 0), bevel=0.008, segs=2, taper=None):
    """Bevelled box. size = full extents. taper = (x_scale_top, y_scale_top) applied along local Z."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = V((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        if taper and v.co.z > 0:
            v.co.x *= taper[0]
            v.co.y *= taper[1]
    o = mesh_from_bm(name, bm, mat)
    o.rotation_euler = Euler([rad(a) for a in rot])
    o.location = loc
    harden(o, bevel, segs, 30)
    PARTS.append((o, bone))
    return o


def cyl(name, loc, r, depth, mat, bone, rot=(0, 0, 0), verts=16, bevel=0.004, r2=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=verts, radius1=r, radius2=r2 if r2 is not None else r, depth=depth)
    o = mesh_from_bm(name, bm, mat)
    o.rotation_euler = Euler([rad(a) for a in rot])
    o.location = loc
    harden(o, bevel, 2, 30)
    PARTS.append((o, bone))
    return o


def lerp(a, b, t):
    return a + (b - a) * t


def frame(axis, fwd=V((0, -1, 0))):
    ax = axis.normalized()
    f = fwd - ax * fwd.dot(ax)
    if f.length < 1e-4:
        f = V((0, 0, 1)) - ax * ax.z
    f.normalize()
    s = ax.cross(f).normalized()
    return ax, f, s


def around(ax, center_dir, deg):
    """Rotate center_dir (projected perpendicular to ax) about ax by deg."""
    c = (center_dir - ax * center_dir.dot(ax)).normalized()
    return (Quaternion(ax, rad(deg)) @ c).normalized()


# ============================================================================ joints / body

def joints():
    J = dict(
        pelvis=V((0, 0.01, 0.97)), waist=V((0, 0.015, 1.10)), chestlow=V((0, 0.005, 1.24)), chest=V((0, 0.0, 1.36)),
        uchest=V((0, 0.012, 1.45)), neck=V((0, 0.015, 1.54)), headlow=V((0, 0.0, 1.61)), head=V((0, 0.0, 1.685)),
    )
    for s, x in (("L", 1), ("R", -1)):
        J["clav" + s] = V((0.045 * x, 0.0, 1.47))
        J["sho" + s] = V((0.215 * x, 0.02, 1.465))
        J["bicep" + s] = V((0.295 * x, 0.028, 1.345))
        J["elb" + s] = V((0.365 * x, 0.035, 1.23))
        J["farm" + s] = V((0.428 * x, 0.015, 1.145))
        J["wri" + s] = V((0.5 * x, -0.01, 1.03))
        J["hand" + s] = V((0.54 * x, -0.025, 0.95))
        J["hip" + s] = V((0.1 * x, 0.0, 0.92))
        J["thigh" + s] = V((0.108 * x, -0.005, 0.74))
        J["knee" + s] = V((0.115 * x, -0.015, 0.53))
        J["calf" + s] = V((0.117 * x, 0.004, 0.37))
        J["ank" + s] = V((0.12 * x, 0.025, 0.115))
        J["ball" + s] = V((0.123 * x, -0.105, 0.03))
        J["toe" + s] = V((0.124 * x, -0.19, 0.03))
    return J


def build_body(J, M):
    """Undersuit body: Skin modifier over the joint graph -> Subdivision."""
    nodes = [
        ("pelvis", .17, .135), ("waist", .155, .125), ("chestlow", .185, .14), ("chest", .215, .155),
        ("uchest", .2, .14), ("neck", .07, .07), ("headlow", .08, .09), ("head", .095, .11),
    ]
    edges = [("pelvis", "waist"), ("waist", "chestlow"), ("chestlow", "chest"), ("chest", "uchest"),
             ("uchest", "neck"), ("neck", "headlow"), ("headlow", "head")]
    for s in ("L", "R"):
        nodes += [("sho" + s, .112, .112), ("bicep" + s, .094, .094), ("elb" + s, .072, .072), ("farm" + s, .076, .076),
                  ("wri" + s, .052, .052), ("hip" + s, .112, .112), ("thigh" + s, .118, .118), ("knee" + s, .09, .09),
                  ("calf" + s, .096, .096), ("ank" + s, .062, .062)]
        edges += [("uchest", "sho" + s), ("sho" + s, "bicep" + s), ("bicep" + s, "elb" + s), ("elb" + s, "farm" + s),
                  ("farm" + s, "wri" + s), ("pelvis", "hip" + s), ("hip" + s, "thigh" + s), ("thigh" + s, "knee" + s),
                  ("knee" + s, "calf" + s), ("calf" + s, "ank" + s)]
    idx = {n[0]: i for i, n in enumerate(nodes)}
    me = bpy.data.meshes.new("Body")
    me.from_pydata([J[n[0]] for n in nodes], [(idx[a], idx[b]) for a, b in edges], [])
    o = link(bpy.data.objects.new("Body", me))
    sk = o.modifiers.new("Skin", "SKIN")
    sk.use_smooth_shade = True
    sk.branch_smoothing = 0.6
    for i, n in enumerate(nodes):
        sv = me.skin_vertices[0].data[i]
        r = max(n[1], n[2]) if n[0][-1] in "LR" else n[1]
        sv.radius = (r, r) if n[0][-1] in "LR" else (n[1], n[1])
        sv.use_root = (i == 0)
    sub = o.modifiers.new("Sub", "SUBSURF")
    sub.levels = 2
    sub.render_levels = 2
    apply_mods(o)
    # torso / pelvis depth: skin radii are isotropic here, so shape the ellipse afterwards
    for v in o.data.vertices:
        p = v.co
        if p.z > 0.86 and p.z < 1.62 and abs(p.x) < 0.24:
            k = 0.78 if p.z > 1.5 else 0.8
            p.y = 0.012 + (p.y - 0.012) * k
            if p.z > 1.2 and p.z < 1.5 and p.y < 0:
                p.y -= 0.025 * math.sin(math.pi * (p.z - 1.2) / 0.3) * max(0.0, 1 - abs(p.x) / 0.2)   # pectorals
            if p.z < 1.02 and p.y > 0.03:
                p.y += 0.02 * max(0.0, 1 - abs(abs(p.x) - 0.08) / 0.08)                          # glutes
    sm = o.modifiers.new("Smooth", "CORRECTIVE_SMOOTH")
    sm.iterations = 6
    sm.smooth_type = "LENGTH_WEIGHTED"
    sm.use_only_smooth = True
    apply_mods(o)
    o.data.materials.append(M["fabric"])
    for p in o.data.polygons:
        p.use_smooth = True
    PARTS.append((o, None))
    return o


# ============================================================================ armour from the body surface

class Surface:
    def __init__(self, body):
        self.bvh = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())

    def radius(self, p, d, fallback=0.08):
        hit = self.bvh.ray_cast(p, d, 1.0)
        return (hit[3] if hit[0] is not None else fallback)

    def shell_points(self, a, b, center_dir, t_rows, half_deg, off, na=7, inner=0.35, fwd=None):
        """Points on the body surface between joints a->b. t_rows: list of (t, half_deg_scale, extra_off)."""
        ax, f, s = frame(b - a, fwd if fwd is not None else V((0, -1, 0)))
        pts = []
        for t, hs, eo in t_rows:
            p0 = lerp(a, b, t)
            h = half_deg * hs
            for k in range(na):
                ang = -h + 2 * h * k / (na - 1)
                d = around(ax, center_dir, ang)
                r = self.radius(p0, d)
                pts.append(p0 + d * (r + off + eo))
            dmid = around(ax, center_dir, 0)
            pts.append(p0 + dmid * self.radius(p0, dmid) * inner)
        return pts


def build_armor(J, M, S):
    st, gr, am, gl, rb, po = M["steel"], M["graphite"], M["amber"], M["glow"], M["rubber"], M["pouch"]
    up = V((0, 0, 1))
    F = V((0, -1, 0))

    # ---------------------------------------------------------------- torso
    spine_a, spine_b = V((0, 0.01, 0.9)), V((0, 0.01, 1.6))
    def tz(z):
        return (z - spine_a.z) / (spine_b.z - spine_a.z)

    # chest plate: broad faceted steel with a central ridge
    pts = S.shell_points(spine_a, spine_b, F, [(tz(1.24), 0.7, 0.0), (tz(1.3), 0.95, 0.01), (tz(1.38), 1.0, 0.016), (tz(1.45), 0.92, 0.01), (tz(1.5), 0.55, 0.0)], 80, 0.045, na=9)
    pts += [V((0, -0.232, 1.39)), V((0, -0.215, 1.29))]
    hull("ChestPlate", pts, st, "Chest", width=0.008)
    # chest lights
    for x in (-1, 1):
        box(f"ChestLight{x}", V((0.09 * x, -0.226, 1.425)), (0.06, 0.012, 0.014), gl, "Chest", rot=(-12, 0, -8 * x), bevel=0.003)
        box(f"ChestLightFrame{x}", V((0.09 * x, -0.219, 1.425)), (0.075, 0.012, 0.026), gr, "Chest", rot=(-12, 0, -8 * x), bevel=0.003)
    # sternum unit with vertical cyan bars
    box("Sternum", V((0, -0.222, 1.335)), (0.07, 0.03, 0.085), gr, "Chest", rot=(-6, 0, 0), bevel=0.006)
    for x in (-0.012, 0.012):
        box(f"SternumBar{x}", V((x, -0.239, 1.335)), (0.008, 0.008, 0.05), gl, "Chest", rot=(-6, 0, 0), bevel=0.002)
    # segmented abdominal plates (graphite)
    for i, (z0, z1) in enumerate(((1.035, 1.085), (1.095, 1.145), (1.155, 1.205))):
        pts = S.shell_points(spine_a, spine_b, F, [(tz(z0), 0.9, 0.0), (tz(z1), 1.0, 0.0)], 52, 0.028, na=5)
        hull(f"Ab{i}", pts, gr, "Spine", width=0.005)
    # side ribs (graphite) and back plate (steel) + lower back (graphite)
    for x in (-1, 1):
        pts = S.shell_points(spine_a, spine_b, V((x, 0.1, 0)), [(tz(1.12), 0.8, 0), (tz(1.3), 1.0, 0.004), (tz(1.4), 0.7, 0)], 26, 0.03, na=4)
        hull(f"Rib{x}", pts, gr, "Chest", width=0.005)
    pts = S.shell_points(spine_a, spine_b, V((0, 1, 0)), [(tz(1.2), 0.8, 0), (tz(1.32), 1.0, 0.01), (tz(1.47), 0.9, 0)], 70, 0.04, na=6)
    hull("BackPlate", pts, st, "Chest", width=0.008)
    pts = S.shell_points(spine_a, spine_b, V((0, 1, 0)), [(tz(1.03), 0.9, 0), (tz(1.17), 1.0, 0)], 60, 0.03, na=5)
    hull("LowerBack", pts, gr, "Spine", width=0.006)

    # high collar: ring hull, taller at the back
    ring = []
    for k in range(14):
        a = 2 * math.pi * k / 14
        d = V((math.sin(a), -math.cos(a), 0))
        back = max(0.0, -math.cos(a))           # 1 at the back
        front = max(0.0, math.cos(a))
        ring.append(V((0, 0.02, 1.47)) + V((d.x * 0.16, d.y * 0.145, 0)))
        ring.append(V((0, 0.02, 1.56 + 0.06 * back - 0.03 * front)) + V((d.x * 0.135, d.y * 0.125, 0)))
    hull("Collar", ring, gr, "Chest", width=0.007, planar=8)

    # ---------------------------------------------------------------- belt / hips
    ring = []
    for k in range(16):
        a = 2 * math.pi * k / 16
        d = V((math.sin(a), -math.cos(a), 0))
        for z in (0.955, 1.02):
            r = S.radius(V((0, 0.01, z)), d)
            ring.append(V((0, 0.01, z)) + d * (r + 0.022))
    hull("Belt", ring, gr, "Hips", width=0.004, planar=6)
    box("Buckle", V((0, -0.172, 0.99)), (0.075, 0.02, 0.05), st, "Hips", bevel=0.005)
    box("BuckleCore", V((0, -0.183, 0.99)), (0.04, 0.008, 0.026), gr, "Hips", bevel=0.003)
    # pouches (front-left pair, back pair, right-side)
    for i, x in enumerate((0.075, 0.135)):
        a = math.atan2(x, 0.17)
        box(f"Pouch{i}", V((x, -0.168 + i * 0.03, 0.965)), (0.055, 0.04, 0.08), po, "Hips", rot=(0, 0, math.degrees(a)), bevel=0.008)
        box(f"PouchFlap{i}", V((x, -0.19 + i * 0.03, 0.995)), (0.058, 0.012, 0.03), po, "Hips", rot=(0, 0, math.degrees(a)), bevel=0.004)
    for x in (-0.09, 0.09):
        box(f"BackPouch{x}", V((x, 0.16, 0.975)), (0.08, 0.045, 0.075), po, "Hips", bevel=0.008)
    # hip scanner (character's right)
    box("Scanner", V((-0.19, -0.075, 0.93)), (0.035, 0.1, 0.11), gr, "Hips", rot=(0, 0, 18), bevel=0.008)
    box("ScannerFrame", V((-0.205, -0.08, 0.93)), (0.012, 0.08, 0.085), st, "Hips", rot=(0, 0, 18), bevel=0.004)
    box("ScannerScreen", V((-0.212, -0.082, 0.932)), (0.006, 0.062, 0.066), M["screen"], "Hips", rot=(0, 0, 18), bevel=0.002)
    # codpiece + tassets
    pts = S.shell_points(V((0, 0.0, 1.0)), V((0, 0.0, 0.8)), F, [(0.15, 1.0, 0), (0.45, 0.75, 0.0), (0.62, 0.4, 0)], 40, 0.03, na=5)
    hull("Codpiece", pts, gr, "Hips", width=0.005)

    for s, x in (("L", 1), ("R", -1)):
        hip, knee, ank, ball = J["hip" + s], J["knee" + s], J["ank" + s], J["ball" + s]
        out = V((x, 0, 0))
        # tasset hanging from the belt (outer thigh)
        pts = S.shell_points(hip + V((0, 0, 0.08)), knee, around(V((0, 0, -1)), out, 0) * 0.8 + F * 0.2, [(0.05, 1.0, 0.035), (0.3, 1.0, 0.04), (0.42, 0.7, 0.04)], 40, 0.03, na=5)
        hull("Tasset" + s, pts, st, "Hips", width=0.006)
        # thigh front plate (steel, faceted) + outer plate (graphite)
        pts = S.shell_points(hip, knee, F, [(0.16, 0.7, 0), (0.28, 1.0, 0.01), (0.5, 0.95, 0.004)], 84, 0.038, na=7)
        hull("Thigh" + s, pts, st, "Thigh_" + s, width=0.008)
        pts = S.shell_points(hip, knee, F, [(0.53, 0.95, 0.0), (0.7, 1.0, 0.008), (0.86, 0.6, 0)], 80, 0.036, na=7)
        hull("ThighLow" + s, pts, st, "Thigh_" + s, width=0.008)
        pts = S.shell_points(hip, knee, out, [(0.3, 0.8, 0), (0.55, 1.0, 0), (0.8, 0.7, 0)], 40, 0.03, na=4)
        hull("ThighSide" + s, pts, gr, "Thigh_" + s, width=0.006)
        # knee: hexagonal pad with graphite rim
        kd = (knee - hip).normalized()
        kp = knee + V((0, -0.085, 0.01))
        cyl("KneeRim" + s, kp + V((0, 0.012, 0)), 0.068, 0.04, gr, "Shin_" + s, rot=(90, 0, 0), verts=6, bevel=0.006)
        cyl("Knee" + s, kp + V((0, -0.004, 0)), 0.056, 0.03, st, "Shin_" + s, rot=(90, 0, 0), verts=6, bevel=0.007, r2=0.044)
        # shin guard (steel) + calf plate (graphite)
        pts = S.shell_points(knee, ank, F, [(0.12, 0.7, 0), (0.3, 1.0, 0.01), (0.65, 1.0, 0.006), (0.84, 0.75, 0)], 95, 0.03, na=7)
        hull("Shin" + s, pts, st, "Shin_" + s, width=0.008)
        pts = S.shell_points(knee, ank, V((0, 1, 0)), [(0.15, 0.8, 0), (0.45, 1.0, 0), (0.75, 0.8, 0)], 60, 0.02, na=5)
        hull("Calf" + s, pts, gr, "Shin_" + s, width=0.006)
        # amber double stripe on the shin
        for i in range(2):
            p = lerp(knee, ank, 0.52 + i * 0.07)
            r = S.radius(p, F)
            box(f"ShinStripe{s}{i}", p + F * (r + 0.046) + V((0.012 * x, 0, 0)), (0.05, 0.006, 0.014), am, "Shin_" + s, rot=(-6, 0, 0), bevel=0.002)
        build_boot(s, x, ank, ball, J["toe" + s], M)

    # ---------------------------------------------------------------- arms
    for s, x in (("L", 1), ("R", -1)):
        sho, elb, wri = J["sho" + s], J["elb" + s], J["wri" + s]
        out = V((x, 0, 0))
        # upper arm outer plate
        pts = S.shell_points(sho, elb, out, [(0.3, 0.8, 0), (0.55, 1.0, 0), (0.85, 0.8, 0)], 85, 0.025, na=6)
        hull("UpperArm" + s, pts, st, "UpperArm_" + s, width=0.006)
        # elbow cop
        ed = (elb - sho).normalized()
        cyl("Elbow" + s, elb + V((0, 0.05, 0)) + out * 0.01, 0.042, 0.035, gr, "ForeArm_" + s, rot=(90, 0, 0), verts=6, bevel=0.005)
        # forearm bracer (steel, nearly closed) + amber stripe
        pts = S.shell_points(elb, wri, out, [(0.15, 0.85, 0), (0.42, 1.0, 0.006), (0.85, 1.0, 0)], 150, 0.028, na=9)
        hull("Bracer" + s, pts, st, "ForeArm_" + s, width=0.007)
        p = lerp(elb, wri, 0.55)
        d = (out * 0.6 + F * 0.8).normalized()
        r = S.radius(p, d)
        fa = (wri - elb).normalized()
        q = V((0, 0, 1)).rotation_difference(fa)
        stripe = box("BracerStripe" + s, p + d * (r + 0.036), (0.04, 0.006, 0.012), am, "ForeArm_" + s, bevel=0.002)
        stripe.rotation_euler = (q @ Quaternion(V((0, 0, 1)), math.atan2(d.x, -d.y))).to_euler()
        build_hand(s, x, wri, J["hand" + s], M)

    # right shoulder: compact plate
    build_pauldron("R", -1, J, M, big=False)
    # left shoulder: oversized command pauldron with amber chevrons
    build_pauldron("L", 1, J, M, big=True)


def build_boot(s, x, ank, ball, toe, M):
    st, gr, rb = M["steel"], M["graphite"], M["rubber"]
    bone = "Foot_" + s
    # boot shell: hull of an ankle ring and a foot block
    pts = []
    for k in range(10):
        a = 2 * math.pi * k / 10
        for z, r in ((0.2, 0.062), (0.12, 0.07)):
            pts.append(V((ank.x + math.sin(a) * r, ank.y + 0.005 - math.cos(a) * r * 1.1, z)))
    for dx in (-0.055, 0.055):
        pts += [V((ank.x + dx, toe.y + 0.01, 0.035)), V((ank.x + dx * 0.9, toe.y + 0.02, 0.085)), V((ank.x + dx, ank.y + 0.075, 0.035)), V((ank.x + dx, ank.y + 0.07, 0.11))]
    hull("Boot" + s, pts, gr, bone, width=0.008, planar=6)
    # steel toe cap and ankle guard
    pts = [V((ank.x + dx, y, z)) for dx in (-0.06, 0.06) for y, z in ((toe.y + 0.005, 0.04), (toe.y + 0.012, 0.095), (toe.y + 0.09, 0.11), (toe.y + 0.1, 0.045))]
    hull("ToeCap" + s, pts, st, bone, width=0.007)
    pts = []
    for k in range(7):
        a = math.radians(-70 + 140 * k / 6)
        for z in (0.13, 0.215):
            pts.append(V((ank.x + math.sin(a) * 0.078, ank.y - math.cos(a) * 0.082, z)))
    pts.append(V((ank.x, ank.y + 0.02, 0.17)))
    hull("AnkleGuard" + s, pts, st, bone, width=0.006)
    box("AnkleStrap" + s, V((ank.x, ank.y - 0.02, 0.105)), (0.135, 0.13, 0.025), rb, bone, bevel=0.006)
    # chunky ridged sole
    box("Sole" + s, V((ank.x, (toe.y + ank.y + 0.08) / 2, 0.02)), (0.125, ank.y + 0.08 - toe.y + 0.015, 0.04), rb, bone, bevel=0.008)
    for i in range(6):
        y = toe.y + 0.03 + i * 0.045
        box(f"Tread{s}{i}", V((ank.x, y, 0.003)), (0.13, 0.02, 0.012), rb, bone if y < ank.y - 0.06 else bone, bevel=0.003)


def build_hand(s, x, wri, hand, M):
    rb, gr = M["rubber"], M["graphite"]
    bone = "Hand_" + s
    d = (hand - wri).normalized()
    q = V((0, 0, -1)).rotation_difference(d)
    e = [math.degrees(a) for a in q.to_euler()]
    c = lerp(wri, hand, 0.42)
    box("Palm" + s, c, (0.07, 0.032, 0.075), rb, bone, rot=e, bevel=0.01)
    box("Knuckles" + s, c + V((0, -0.006, 0)) + d * 0.02, (0.072, 0.024, 0.026), gr, bone, rot=e, bevel=0.005)
    side = (q @ V((1, 0, 0))).normalized()
    for i in range(4):
        o = side * (-0.026 + i * 0.0175)
        base = hand + o + d * 0.005
        box(f"Finger{s}{i}", base + V((0, -0.018, -0.005)), (0.016, 0.022, 0.04), rb, bone, rot=(e[0] + 35, e[1], e[2]), bevel=0.006)
        box(f"FingerTip{s}{i}", base + V((0, -0.035, -0.03)), (0.015, 0.03, 0.02), rb, bone, rot=(e[0] + 80, e[1], e[2]), bevel=0.006)
    box("Thumb" + s, c + V((-x * 0.03, -0.03, -0.01)), (0.02, 0.048, 0.02), rb, bone, rot=(30, 0, -20 * x), bevel=0.007)
    box("Cuff" + s, wri + d * 0.005, (0.09, 0.08, 0.03), gr, bone, rot=e, bevel=0.008)


def build_pauldron(s, x, J, M, big):
    """Chunky faceted shoulder block (concept: big angular plate, chevrons on the outer face) + lame below."""
    st, gr, am = M["steel"], M["graphite"], M["amber"]
    bone = "Shoulder_" + s
    c = J["sho" + s] + V((0.0, 0.0, 0.0))
    k = 0.9 if big else 0.74
    def P(px, py, pz):
        return c + V((px * x * k, py * k, pz * k))
    pts = []
    for py, w in ((-0.17, 0.75), (-0.13, 1.0), (0.12, 1.0), (0.16, 0.75)):
        pts += [P(-0.07, py * w, 0.075), P(0.05, py, 0.125), P(0.15, py, 0.1), P(0.205, py * w, 0.02), P(0.215, py * w, -0.1), P(0.16, py * w, -0.13)]
    pts += [P(-0.08, 0, 0.04), P(0.02, 0, -0.06)]
    hull("Pauldron" + s, pts, st, bone, width=0.009, planar=3)
    lame = []
    for py in (-0.14, 0.13):
        lame += [P(0.1, py, -0.08), P(0.215, py, -0.1), P(0.22, py * 0.9, -0.18), P(0.16, py * 0.9, -0.2)]
    lame += [P(0.08, 0, -0.14)]
    hull("PauldronLame" + s, lame, gr, bone, width=0.006, planar=3)
    if big:
        for i in range(2):
            for side in (-1, 1):
                p = P(0.214, side * 0.042, 0.005 - i * 0.05)
                box(f"Chevron{i}{side}", p, (0.012, 0.085, 0.024), am, bone, rot=(-side * 40 * x, 0, 0), bevel=0.003)
        box("PauldronTrim" + s, P(0.08, -0.172, 0.06), (0.08, 0.012, 0.014), am, bone, rot=(0, -20 * x, 0), bevel=0.002)
        box("PauldronLight" + s, P(0.17, -0.168, -0.04), (0.012, 0.01, 0.045), M["glow"], bone, bevel=0.002)
    else:
        box("PauldronTrim" + s, P(0.15, -0.172, -0.02), (0.04, 0.012, 0.012), am, bone, bevel=0.002)


def build_helmet(J, M):
    n0 = len(PARTS)
    _build_helmet(J, M)
    c = V((0, -0.008, 1.69))
    for o, _ in PARTS[n0:]:          # helmet reads small next to the armour: scale it up as a unit
        o.location = c + (o.location - c) * 1.1
        o.scale = o.scale * 1.1


def _build_helmet(J, M):
    st, gr, gl = M["steel"], M["graphite"], M["glow"]
    c = V((0, -0.008, 1.69))
    pts = []
    for i in range(6):
        th = rad(i * 21)              # 0 = top .. 105 = below equator
        n = 1 if i == 0 else 10
        for k in range(n):
            ph = 2 * math.pi * (k + 0.5 * (i % 2)) / max(n, 1)
            d = V((math.sin(th) * math.sin(ph), -math.sin(th) * math.cos(ph), math.cos(th)))
            p = c + V((d.x * 0.132, d.y * 0.145, d.z * 0.14))
            front = max(0.0, -d.y)
            if th > rad(55):
                p.y -= 0.012 * front
                p.x *= 1.0 - 0.1 * front
            pts.append(p)
    pts += [c + V((0, -0.15, -0.125)), c + V((0.055, -0.13, -0.135)), c + V((-0.055, -0.13, -0.135)), c + V((0, 0.08, -0.12)),
            c + V((0.1, 0.03, -0.12)), c + V((-0.1, 0.03, -0.12))]
    hull("Helmet", pts, st, "Head", width=0.007, planar=2)
    # recessed dark visor band + T glow, sitting flush in the faceplate
    box("VisorFrame", c + V((0, -0.137, 0.018)), (0.2, 0.03, 0.048), gr, "Head", rot=(6, 0, 0), bevel=0.008)
    box("VisorBar", c + V((0, -0.151, 0.019)), (0.172, 0.006, 0.022), gl, "Head", rot=(6, 0, 0), bevel=0.003)
    for x in (-1, 1):
        box(f"VisorWing{x}", c + V((0.095 * x, -0.143, 0.008)), (0.032, 0.006, 0.02), gl, "Head", rot=(6, 0, 30 * x), bevel=0.003)
    box("VisorStemFrame", c + V((0, -0.152, -0.04)), (0.042, 0.022, 0.09), gr, "Head", rot=(-8, 0, 0), bevel=0.006)
    box("VisorStem", c + V((0, -0.163, -0.035)), (0.016, 0.006, 0.07), gl, "Head", rot=(-8, 0, 0), bevel=0.002, taper=(1.5, 1.0))
    # cheek plates either side of the stem
    for x in (-1, 1):
        box(f"Cheek{x}", c + V((0.06 * x, -0.135, -0.075)), (0.07, 0.03, 0.06), gr, "Head", rot=(-10, 0, -22 * x), bevel=0.008)
    # ear discs + antenna (left)
    for x in (-1, 1):
        cyl(f"Ear{x}", c + V((0.128 * x, 0.005, -0.01)), 0.048, 0.03, gr, "Head", rot=(0, 90, 0), verts=8, bevel=0.006)
        cyl(f"EarCore{x}", c + V((0.145 * x, 0.005, -0.01)), 0.024, 0.01, st, "Head", rot=(0, 90, 0), verts=8, bevel=0.003)
    cyl("AntennaBase", c + V((0.125, 0.05, 0.04)), 0.012, 0.04, gr, "Head", verts=8)
    cyl("Antenna", c + V((0.127, 0.055, 0.15)), 0.0035, 0.19, gr, "Head", verts=6, bevel=0.001)
    # amber stripe on the left of the dome
    st_pts = []
    for i in range(5):
        th = rad(12 + i * 12)
        for ph in (rad(56), rad(66)):
            d = V((math.sin(th) * math.sin(ph), -math.sin(th) * math.cos(ph), math.cos(th)))
            st_pts.append(c + V((d.x * 0.137, d.y * 0.15, d.z * 0.145)))
            st_pts.append(c + V((d.x * 0.127, d.y * 0.14, d.z * 0.135)))
    hull("HelmetStripe", st_pts, M["amber"], "Head", width=0.002)


def build_backpack(M):
    st, gr, am, gl = M["steel"], M["graphite"], M["amber"], M["glow"]
    b = "Chest"
    box("Pack", V((0, 0.235, 1.28)), (0.3, 0.13, 0.36), gr, b, bevel=0.018, taper=(0.92, 0.85))
    box("PackPanel", V((0, 0.305, 1.27)), (0.2, 0.03, 0.28), st, b, bevel=0.01)
    box("PackLightFrame", V((0, 0.322, 1.27)), (0.04, 0.012, 0.2), gr, b, bevel=0.004)
    box("PackLight", V((0, 0.328, 1.27)), (0.016, 0.008, 0.17), gl, b, bevel=0.003)
    for x in (-1, 1):
        cyl(f"Canister{x}", V((0.175 * x, 0.25, 1.25)), 0.045, 0.27, gr, b, verts=12, bevel=0.006)
        for z in (1.17, 1.33):
            cyl(f"CanBand{x}{z}", V((0.175 * x, 0.25, z)), 0.048, 0.02, am, b, verts=12, bevel=0.003)
        cyl(f"CanCap{x}", V((0.175 * x, 0.25, 1.395)), 0.03, 0.03, st, b, verts=12, bevel=0.004)
        box(f"PackSide{x}", V((0.12 * x, 0.3, 1.27)), (0.035, 0.03, 0.3), st, b, bevel=0.006)
        box(f"Strap{x}", V((0.1 * x, -0.01, 1.47)), (0.05, 0.36, 0.025), gr, b, rot=(8, 0, 0), bevel=0.006)
    box("HandleL", V((0.07, 0.25, 1.48)), (0.02, 0.03, 0.06), st, b, bevel=0.004)
    box("HandleR", V((-0.07, 0.25, 1.48)), (0.02, 0.03, 0.06), st, b, bevel=0.004)
    box("HandleTop", V((0, 0.25, 1.51)), (0.16, 0.03, 0.02), st, b, bevel=0.004)
    box("PackBottom", V((0, 0.22, 1.085)), (0.26, 0.11, 0.04), st, b, bevel=0.008)


def build_cape(M):
    """Long split cape hanging from the shoulder blades (behind the backpack), inverted-V stripe on the back."""
    cols, rows = 16, 26
    z0, z1 = 1.43, 0.42
    top_w, bot_w = 0.17, 0.27
    slit = 0.012
    bm = bmesh.new()
    grid = {}
    for side in (-1, 1):
        for j in range(rows + 1):
            v = j / rows
            z = lerp(z0, z1, v)
            half = lerp(top_w, bot_w, v ** 0.8)
            for i in range(cols // 2 + 1):
                u = i / (cols // 2)
                x = side * lerp(slit * min(1.0, v * 6), half, u)
                ang = x / 0.32
                y = 0.165 + 0.13 * (1 - math.cos(ang)) * -1 + 0.06 * v
                y += 0.0 if v > 0.1 else 0.0
                fold = 0.012 * math.sin(u * 9.0 + side) * (0.3 + v)
                y += fold
                if j == rows:
                    z += 0.02 * math.sin(u * 7 + side * 2)
                grid[(side, i, j)] = bm.verts.new(V((x, y + 0.1 * v * v, z)))
    for side in (-1, 1):
        for j in range(rows):
            for i in range(cols // 2):
                a, b, c, d = grid[(side, i, j)], grid[(side, i + 1, j)], grid[(side, i + 1, j + 1)], grid[(side, i, j + 1)]
                bm.faces.new((a, b, c, d) if side > 0 else (b, a, d, c))
    # join the two halves above the slit
    for j in range(4):
        a, b, c, d = grid[(-1, 0, j)], grid[(1, 0, j)], grid[(1, 0, j + 1)], grid[(-1, 0, j + 1)]
        bm.faces.new((b, a, d, c))
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0005)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = mesh_from_bm("Cape", bm, M["cape"])
    sol = o.modifiers.new("Sol", "SOLIDIFY"); sol.thickness = 0.008; sol.offset = 0
    apply_mods(o)
    PARTS.append((o, None))
    # stripe: inverted V across the lower back of the cape
    sb = bmesh.new()
    def cape_point(x, z):
        v = (z0 - z) / (z0 - z1)
        ang = x / 0.32
        return V((x, 0.165 - 0.13 * (1 - math.cos(ang)) + 0.06 * v + 0.1 * v * v + 0.012 * math.sin(abs(x) / max(lerp(top_w, bot_w, v ** 0.8), 1e-3) * 9.0 + (1 if x > 0 else -1)) * (0.3 + v) + 0.006, z))
    for k, (zc, w) in enumerate(((0.78, 0.07),)):
        prev = None
        for i in range(13):
            x = -0.3 + 0.6 * i / 12
            z = zc - abs(x) * 0.75
            p1, p2 = cape_point(x, z + w / 2), cape_point(x, z - w / 2)
            v1, v2 = sb.verts.new(p1), sb.verts.new(p2)
            if prev:
                sb.faces.new((prev[0], v1, v2, prev[1]))
            prev = (v1, v2)
    so = mesh_from_bm("CapeStripe", sb, M["stripe"])
    sol = so.modifiers.new("Sol", "SOLIDIFY"); sol.thickness = 0.004; sol.offset = 1
    apply_mods(so)
    PARTS.append((so, None))
    return o


# ============================================================================ preview

def setup_render(samples=48, res=(900, 1100)):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        prefs.compute_device_type = "METAL"
        prefs.get_devices()
        for d in prefs.devices:
            d.use = True
        sc.cycles.device = "GPU"
    except Exception as e:
        print("[zcom] GPU setup failed:", e)
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.film_transparent = False
    try:
        sc.view_settings.view_transform = "AgX"
        sc.view_settings.look = "AgX - Medium High Contrast"
    except Exception:
        pass
    w = bpy.data.worlds.new("World")
    sc.world = w
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.2, 0.21, 0.23, 1)
    bg.inputs["Strength"].default_value = 0.18
    def area(name, loc, energy, size, color=(1, 1, 1)):
        l = bpy.data.lights.new(name, "AREA"); l.energy = energy; l.size = size; l.color = color
        o = link(bpy.data.objects.new(name, l)); o.location = loc
        o.rotation_euler = (V((0, 0, 1.1)) - V(loc)).to_track_quat("-Z", "Y").to_euler()
    area("Key", (-2.2, -3.0, 3.2), 380, 2.5, (1.0, 0.97, 0.93))
    area("Fill", (3.0, -2.0, 1.6), 90, 3.0, (0.85, 0.9, 1.0))
    area("Rim", (1.0, 3.2, 2.8), 420, 2.0, (0.8, 0.92, 1.0))
    floor = bpy.data.meshes.new("Floor")
    bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=6); bm.to_mesh(floor); bm.free()
    fo = link(bpy.data.objects.new("Floor", floor))
    fm = bpy.data.materials.new("FloorMat"); fm.use_nodes = True
    fm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.06, 0.065, 0.075, 1)
    fm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.8
    floor.materials.append(fm)
    return fo


def render_views(prefix, views, target=V((0, 0, 0.95)), dist=4.2, lens=70, tag=""):
    sc = bpy.context.scene
    cd = bpy.data.cameras.new("Cam"); cd.lens = lens
    cam = link(bpy.data.objects.new("Cam", cd))
    sc.camera = cam
    out = []
    for name, (elev, azim) in views.items():
        d = V((math.sin(rad(azim)) * math.cos(rad(elev)), -math.cos(rad(azim)) * math.cos(rad(elev)), math.sin(rad(elev))))
        cam.location = target + d * dist
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        path = os.path.join(PREVIEWS, f"{prefix}_{name}{tag}.png")
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        out.append(path)
    bpy.data.objects.remove(cam)
    return out


# ============================================================================ main (model stage)

def build_all():
    reset()
    M = make_mats()
    J = joints()
    body = build_body(J, M)
    S = Surface(body)
    build_armor(J, M, S)
    build_helmet(J, M)
    build_backpack(M)
    build_cape(M)
    return M, J, body


# ============================================================================ rig

FPS = 30
DEFORM = ["Hips", "Spine", "Chest", "Neck", "Head"]
for _s in ("L", "R"):
    DEFORM += [f"Shoulder_{_s}", f"UpperArm_{_s}", f"ForeArm_{_s}", f"Hand_{_s}", f"Thigh_{_s}", f"Shin_{_s}", f"Foot_{_s}", f"Toe_{_s}"]
CAPE_COLS = (-0.11, 0.0, 0.11)
CAPE_Z = (1.43, 1.1, 0.77, 0.44)
GRIP = V((-0.07, -0.33, 1.2))     # right-hand grip in armature space (neutral stance)


def cape_y(x, z):
    v = (1.43 - z) / (1.43 - 0.42)
    return 0.165 - 0.13 * (1 - math.cos(x / 0.32)) + 0.06 * v + 0.1 * v * v


def build_armature(J):
    ad = bpy.data.armatures.new("MarshalRig")
    arm = link(bpy.data.objects.new("MarshalRig", ad))
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = ad.edit_bones
    def B(name, h, t, parent=None, connect=False, roll=V((0, -1, 0))):
        b = eb.new(name)
        b.head, b.tail = V(h), V(t)
        if parent:
            b.parent = eb[parent]
            b.use_connect = connect
        b.align_roll(roll)
        return b
    B("Hips", (0, 0.01, 0.97), (0, 0.01, 1.07))
    B("Spine", (0, 0.01, 1.07), (0, 0.01, 1.24), "Hips", True)
    B("Chest", (0, 0.01, 1.24), (0, 0.01, 1.46), "Spine", True)
    B("Neck", (0, 0.015, 1.5), (0, 0.0, 1.6), "Chest")
    B("Head", (0, 0.0, 1.6), (0, 0.0, 1.8), "Neck", True)
    for s, x in (("L", 1), ("R", -1)):
        B(f"Shoulder_{s}", (0.04 * x, 0.01, 1.45), J["sho" + s], "Chest")
        B(f"UpperArm_{s}", J["sho" + s], J["elb" + s], f"Shoulder_{s}", True)
        B(f"ForeArm_{s}", J["elb" + s], J["wri" + s], f"UpperArm_{s}", True)
        B(f"Hand_{s}", J["wri" + s], J["hand" + s], f"ForeArm_{s}", True)
        B(f"Thigh_{s}", J["hip" + s], J["knee" + s], "Hips")
        B(f"Shin_{s}", J["knee" + s], J["ank" + s], f"Thigh_{s}", True)
        B(f"Foot_{s}", J["ank" + s], J["ball" + s], f"Shin_{s}", True, roll=V((0, 0, 1)))
        B(f"Toe_{s}", J["ball" + s], J["toe" + s], f"Foot_{s}", True, roll=V((0, 0, 1)))
    B("Weapon", GRIP, GRIP + V((0, -0.2, 0)), "Chest", roll=V((0, 0, 1)))
    for c, x in enumerate(CAPE_COLS):
        prev = "Chest"
        for r in range(3):
            z0, z1 = CAPE_Z[r], CAPE_Z[r + 1]
            B(f"Cape_{c}{r}", (x * (1 + 0.25 * r), cape_y(x, z0), z0), (x * (1 + 0.25 * (r + 1)), cape_y(x, z1), z1), prev, r > 0, roll=V((0, 1, 0)))
            prev = f"Cape_{c}{r}"
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return arm


def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (a + ab * t - p).length


def skin(arm):
    segs = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in arm.data.bones}
    cape_bones = [n for n in segs if n.startswith("Cape_")]
    objs = []
    for o, bone in PARTS:
        if o.modifiers:
            apply_mods(o)
        vgs = {}
        def vg(n):
            if n not in vgs:
                vgs[n] = o.vertex_groups.new(name=n)
            return vgs[n]
        if bone:
            vg(bone).add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
        else:
            cape = o.name.startswith("Cape")
            names = cape_bones if cape else DEFORM
            power, k = (3, 3) if cape else (5, 3)
            for v in o.data.vertices:
                p = o.matrix_world @ v.co
                ds = sorted((seg_dist(p, *segs[n]), n) for n in names)[:k]
                ws = [1.0 / (d + 0.012) ** power for d, _ in ds]
                tot = sum(ws)
                for (d, n), w in zip(ds, ws):
                    if w / tot > 0.03:
                        vg(n).add([v.index], w / tot, "REPLACE")
        objs.append(o)
    with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs, selected_objects=objs, object=objs[0]):
        bpy.ops.object.join()
    body = objs[0]
    body.name = body.data.name = "M_Marshal"
    body.data.transform(body.matrix_world)
    body.matrix_world = Matrix.Identity(4)
    body.parent = arm
    mod = body.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    return body


# ============================================================================ animation

def upd():
    bpy.context.view_layer.update()


def Rq(axis, deg):
    return Quaternion(V(axis), rad(deg))


def frame_from(y, z):
    y = y.normalized()
    z = (z - y * z.dot(y)).normalized()
    x = y.cross(z)
    return Matrix((x, y, z)).transposed()


def rest3(arm, n):
    return arm.data.bones[n].matrix_local.to_3x3()


def set_fk(arm, n, q):
    pb = arm.pose.bones[n]
    rq = arm.data.bones[n].matrix_local.to_quaternion()
    pb.rotation_quaternion = rq.inverted() @ q @ rq


def set_world(arm, n, rot3, head=None):
    upd()
    pb = arm.pose.bones[n]
    h = pb.head.copy() if head is None else V(head)
    pb.matrix = Matrix.Translation(h) @ rot3.to_4x4()
    upd()


def ik(arm, up, lo, target, pole, zsign=1.0):
    upd()
    a = arm.pose.bones[up].head.copy()
    l1, l2 = arm.data.bones[up].length, arm.data.bones[lo].length
    d = target - a
    dist = min(max(d.length, 0.05), (l1 + l2) * 0.999)
    dn = d.normalized()
    ca = max(-1.0, min(1.0, (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist)))
    sa = math.sqrt(1 - ca * ca)
    pv = pole - a
    pv = (pv - dn * pv.dot(dn)).normalized()
    e = a + dn * (l1 * ca) + pv * (l1 * sa)
    w = a + dn * dist
    set_world(arm, up, frame_from(e - a, pv * zsign), a)
    set_world(arm, lo, frame_from(w - e, pv * zsign), e)
    return w


def reset_pose(arm):
    for pb in arm.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = V()


def key_all(arm, fr):
    for pb in arm.pose.bones:
        if pb.name.startswith("Cape_"):
            continue
        pb.keyframe_insert("rotation_quaternion", frame=fr)
        pb.keyframe_insert("location", frame=fr)


def new_action(arm, name):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    if not arm.animation_data:
        arm.animation_data_create()
    arm.animation_data.action = act
    return act


def place_foot(arm, s, ball, yaw, pitch, toe_bend=0.0):
    """Foot with its ball joint at `ball`, yawed and pitched (+pitch = heel up, pivots on the ball;
    -pitch = toe up, pivots on the heel)."""
    bb = arm.data.bones
    ank0, ball0 = bb[f"Foot_{s}"].head_local, bb[f"Foot_{s}"].tail_local
    heel0 = V((ank0.x, ank0.y + 0.07, ball0.z))
    qy = Rq((0, 0, 1), yaw)
    q = qy @ Rq((1, 0, 0), pitch)
    if pitch >= 0:
        ank = ball + q @ (ank0 - ball0)
    else:
        heel = ball + qy @ (heel0 - ball0)
        heel.z = ball.z
        ank = heel + q @ (ank0 - heel0)
    return ank, q


def pose_legs(arm, feet, knee_dir):
    for s, (ball, yaw, pitch, toe) in feet.items():
        ank, q = place_foot(arm, s, ball, yaw, pitch)
        hip = arm.pose.bones[f"Thigh_{s}"].head
        pole = hip + (Rq((0, 0, 1), yaw) @ knee_dir) * 0.6 + V((0, 0, -0.35))
        w = ik(arm, f"Thigh_{s}", f"Shin_{s}", ank, pole, 1.0)
        set_world(arm, f"Foot_{s}", q.to_matrix() @ rest3(arm, f"Foot_{s}"), w)
        tq = Rq((0, 0, 1), yaw) @ Rq((1, 0, 0), (pitch * 0.1 if pitch > 0 else pitch) - toe)
        set_world(arm, f"Toe_{s}", tq.to_matrix() @ rest3(arm, f"Toe_{s}"))


NEUTRAL_CHEST = {}


def pose_weapon_arms(arm, sway_yaw=0.0, sway_pitch=0.0, drop=0.0):
    upd()
    Mc = arm.pose.bones["Chest"].matrix.copy()
    local = NEUTRAL_CHEST["inv"] @ GRIP
    g = Mc @ local + V((0, 0, -drop))
    rw = (Rq((0, 0, 1), sway_yaw) @ Rq((1, 0, 0), sway_pitch)).to_matrix()
    set_world(arm, "Weapon", rw @ rest3(arm, "Weapon"), g)
    # right hand on the pistol grip, left hand cupping the handguard
    yr = rw @ V((0.0, -0.6, -0.8)); zr = rw @ V((1, 0, 0))
    gl = g + rw @ V((0.0, -0.26, 0.005))
    yl = rw @ V((-0.62, -0.62, 0.15)).normalized(); zl = rw @ V((0, 0, 1))
    for s, palm, yh, zh, pole_off in (("R", g, yr, zr, V((-0.3, 0.2, -0.35))), ("L", gl, yl, zl, V((0.35, 0.05, -0.35)))):
        fy = frame_from(yh, zh)
        yh, zh = fy.col[1].copy(), fy.col[2].copy()
        wrist = palm - yh * 0.05 - zh * 0.028
        sh = arm.pose.bones[f"UpperArm_{s}"].head.copy()
        w = ik(arm, f"UpperArm_{s}", f"ForeArm_{s}", wrist, sh + pole_off, -1.0)
        set_world(arm, f"Hand_{s}", fy, w)


def pose_torso(arm, hip_off, hip_yaw, lean_axis, lean, chest_yaw, chest_pitch=0.0, roll=0.0, head_yaw=None, head_pitch=0.0):
    bb = arm.data.bones
    hq = bb["Hips"].matrix_local.to_quaternion()
    arm.pose.bones["Hips"].location = hq.inverted() @ V(hip_off)
    set_fk(arm, "Hips", Rq((0, 0, 1), hip_yaw) @ Rq(lean_axis, lean) @ Rq((0, 1, 0), roll))
    twist = chest_yaw - hip_yaw
    set_fk(arm, "Spine", Rq((0, 0, 1), twist * 0.45) @ Rq((0, 1, 0), -roll * 0.6))
    set_fk(arm, "Chest", Rq((0, 0, 1), twist * 0.55) @ Rq((1, 0, 0), chest_pitch))
    hy = -chest_yaw if head_yaw is None else head_yaw
    set_fk(arm, "Neck", Rq((0, 0, 1), hy * 0.4) @ Rq((1, 0, 0), -lean * 0.3 + head_pitch * 0.5))
    set_fk(arm, "Head", Rq((0, 0, 1), hy * 0.6) @ Rq((1, 0, 0), -lean * 0.4 + head_pitch * 0.5))


STANCE_YAW = -16.0


def init_neutral(arm):
    reset_pose(arm)
    pose_torso(arm, (0, 0, 0), 0, (1, 0, 0), 0, STANCE_YAW)
    upd()
    NEUTRAL_CHEST["inv"] = arm.pose.bones["Chest"].matrix.inverted()
    reset_pose(arm)


def smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def anim_idle(arm, N=90):
    new_action(arm, "Idle")
    for fr in range(N + 1):
        p = fr / N
        reset_pose(arm)
        b = math.sin(2 * math.pi * p)
        b2 = math.sin(4 * math.pi * p + 0.6)
        pose_torso(arm, (0.012 * b, 0.0, -0.035 + 0.006 * b2), -10 + 1.5 * b, (1, 0, 0), 2.0, STANCE_YAW - 4,
                   chest_pitch=1.2 * b2, roll=1.2 * b, head_yaw=18 + 5 * math.sin(2 * math.pi * p + 1.2))
        pose_weapon_arms(arm, sway_yaw=1.2 * b, sway_pitch=-9 + 1.2 * b2, drop=0.03)
        pose_legs(arm, {"L": (V((0.15, -0.17, 0.03)), 12, 0, 0), "R": (V((-0.135, 0.03, 0.03)), -22, 0, 0)}, V((0, -1, 0)))
        key_all(arm, fr)


def anim_gait(arm, name, N, d2, speed, stance, lift, lean, hip_yaw, foot_yaw, back=False, crouch=0.07, bounce=0.035):
    new_action(arm, name)
    d = V((d2[0], d2[1], 0)).normalized()
    T = N / FPS
    L = speed * T * stance               # distance a planted foot travels
    front, behind = (0.42 * L, 0.58 * L) if not back else (0.45 * L, 0.55 * L)
    lean_axis = V((0, 0, 1)).cross(d)
    for fr in range(N + 1):
        p = fr / N
        reset_pose(arm)
        feet, comp, shift = {}, 0.0, V()
        for s, x, off in (("L", 1, 0.0), ("R", -1, 0.5)):
            u = (p + off) % 1.0
            lat = V((x * 0.1, 0, 0))
            lat -= d * lat.dot(d)
            if lat.length < 0.045:
                lat = (lat.normalized() if lat.length > 1e-4 else V((0, x * (1 if d.x > 0 else -1), 0))) * 0.045
            base = V((0, -0.08, 0.03)) + lat
            if u < stance:
                w = u / stance
                along = lerp(front, -behind, w)
                z = 0.0
                if back:
                    pitch = lerp(14, -2, smoothstep(w * 2)) if w < 0.5 else lerp(-2, 0, w)
                else:
                    pitch = lerp(-12, 0, smoothstep(w * 3.5)) if w < 0.3 else lerp(0, 38, smoothstep((w - 0.45) / 0.55))
                c = math.sin(math.pi * w)
                comp = max(comp, c)
                shift += lat.normalized() * 0.018 * c
                toe = 0.0 if back else max(0.0, pitch - 20) * 0.8
            else:
                w = (u - stance) / (1 - stance)
                e = smoothstep(w) ** (1.0 if back else 1.6)        # heel kicks up behind first, then drives forward
                along = lerp(-behind, front * 1.06, e)
                if w > 0.85:
                    along = lerp(front * 1.06, front, (w - 0.85) / 0.15)
                z = lift * math.sin(math.pi * min(1.0, w ** (1.0 if back else 0.7) * 1.05))
                if back:
                    pitch = lerp(-2, 14, smoothstep(w))
                else:
                    pitch = lerp(38, -14, smoothstep(w * 1.2))
                toe = 0.0
            feet[s] = (base + d * along + V((0, 0, z)), foot_yaw, pitch, toe)
        osc = math.sin(2 * math.pi * p)
        hip_z = -crouch - bounce * comp + 0.012 * (1 - comp)
        pose_torso(arm, (shift.x, shift.y, hip_z), hip_yaw + 7 * osc * (1 if not back else -0.6), lean_axis, lean,
                   STANCE_YAW, chest_pitch=1.5 * math.cos(4 * math.pi * p), roll=3 * osc)
        pose_weapon_arms(arm, sway_yaw=1.5 * osc, sway_pitch=-4 + 1.8 * math.cos(4 * math.pi * p), drop=0.0)
        knee = Rq((0, 0, 1), 0) @ V((0, -1, 0))
        pose_legs(arm, feet, knee)
        key_all(arm, fr)


def anim_death(arm, N=50):
    new_action(arm, "Death")
    for fr in range(N + 1):
        t = fr / N
        reset_pose(arm)
        a = smoothstep(t / 0.3)
        b = smoothstep((t - 0.25) / 0.45)
        hip = V((0.0, 0.12 * a + 0.5 * b, -0.04 - 0.22 * a - 0.5 * b))
        pose_torso(arm, hip, -10 + 20 * b, (1, 0, 0), -12 * a - 70 * b, STANCE_YAW, chest_pitch=-10 * a, roll=6 * b,
                   head_yaw=15, head_pitch=-20 * a + 10 * b)
        pose_weapon_arms(arm, sway_yaw=10 * b, sway_pitch=-15 * a - 30 * b, drop=0.05 * a)
        if t < 0.3:
            pose_legs(arm, {"L": (V((0.15, -0.17, 0.03)), 12, 0, 0), "R": (V((-0.135, 0.03, 0.03)), -22, 0, 0)}, V((0, -1, 0)))
        else:
            k = smoothstep((t - 0.3) / 0.4)
            for s, x in (("L", 1), ("R", -1)):
                set_fk(arm, f"Thigh_{s}", Rq((1, 0, 0), lerp(-35, 50, k) + (10 if s == "L" else -5)))
                set_fk(arm, f"Shin_{s}", Rq((1, 0, 0), lerp(70, 25, k) + (15 if s == "L" else 0)))
                set_fk(arm, f"Foot_{s}", Rq((1, 0, 0), -20 * k))
        key_all(arm, fr)


def bake_actions(arm):
    sc = bpy.context.scene
    sc.render.fps = FPS
    init_neutral(arm)
    anim_idle(arm)
    anim_gait(arm, "Run", 20, (0, -1), 5.2, 0.34, 0.24, 9, 0, 0, crouch=0.04)
    anim_gait(arm, "RunBack", 20, (0, 1), 4.2, 0.4, 0.13, 3, 0, 0, back=True, crouch=0.045, bounce=0.025)
    anim_gait(arm, "StrafeL", 20, (1, 0), 5.0, 0.36, 0.18, 7, 38, 42, crouch=0.05)
    anim_gait(arm, "StrafeR", 20, (-1, 0), 5.0, 0.36, 0.18, 7, -40, -44, crouch=0.05)
    anim_death(arm)
    arm.animation_data.action = None
    for act in [a for a in bpy.data.actions]:
        tr = arm.animation_data.nla_tracks.new()
        tr.name = act.name
        tr.strips.new(act.name, 0, act)
    sc.frame_start, sc.frame_end = 0, 90


# ============================================================================ texture bake

def bake_textures(obj, arm, size=2048, samples=16):
    import numpy as np
    sc = bpy.context.scene
    setup_gpu(samples)
    arm.data.pose_position = "REST"
    for o in bpy.context.scene.objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=rad(55), island_margin=0.002, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.002)
    bpy.ops.object.mode_set(mode="OBJECT")

    mats = [m for m in obj.data.materials if m.get("zcom_kind") != "emit"]
    imgs = {}
    for key, nc in (("BaseColor", False), ("Roughness", True), ("Normal", True), ("Metallic", True)):
        im = bpy.data.images.new(f"Marshal_{key}", size, size, alpha=False)
        if nc:
            im.colorspace_settings.name = "Non-Color"
        imgs[key] = im
    nodes = {}
    for m in mats:
        t = m.node_tree.nodes.new("ShaderNodeTexImage")
        m.node_tree.nodes.active = t
        nodes[m.name] = t
    def run(key, btype, **kw):
        for t in nodes.values():
            t.image = imgs[key]
        bpy.ops.object.bake(type=btype, margin=12, use_clear=True, **kw)
        print(f"[zcom] baked {key}")
    def emit_bake(key, socket_name):
        """Route a Principled input (linked or constant) into emission and bake it exactly
        (the DIFFUSE colour pass would darken metallic surfaces)."""
        saved = []
        for m in mats:
            nt = m.node_tree
            b = nt.nodes.get("Principled BSDF")
            out = nt.nodes.get("Material Output")
            em = nt.nodes.new("ShaderNodeEmission")
            inp = b.inputs[socket_name]
            if inp.is_linked:
                nt.links.new(inp.links[0].from_socket, em.inputs["Color"])
            else:
                dv = inp.default_value
                em.inputs["Color"].default_value = tuple(dv) if hasattr(dv, "__len__") else (dv, dv, dv, 1)
            old = out.inputs["Surface"].links[0].from_socket
            nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
            saved.append((nt, old, out, em))
        run(key, "EMIT")
        for nt, old, out, em in saved:
            nt.links.new(old, out.inputs["Surface"])
            nt.nodes.remove(em)
    emit_bake("BaseColor", "Base Color")
    run("Roughness", "ROUGHNESS")
    sc.render.bake.normal_space = "TANGENT"
    run("Normal", "NORMAL")
    emit_bake("Metallic", "Metallic")

    paths = {}
    for key in ("BaseColor", "Normal"):
        im = imgs[key]
        im.filepath_raw = os.path.join(TEX_DIR, f"Marshal_{key}.png")
        im.file_format = "PNG"
        im.save()
        paths[key] = im.filepath_raw
    met = np.array(imgs["Metallic"].pixels[:]).reshape(-1, 4)
    rgh = np.array(imgs["Roughness"].pixels[:]).reshape(-1, 4)
    ms = np.zeros_like(met)
    ms[:, 0] = met[:, 0]; ms[:, 1] = met[:, 0]; ms[:, 2] = met[:, 0]
    ms[:, 3] = 1.0 - rgh[:, 0]
    msi = bpy.data.images.new("Marshal_MetallicSmoothness", size, size, alpha=True)
    msi.colorspace_settings.name = "Non-Color"
    msi.pixels = ms.ravel().tolist()
    msi.filepath_raw = os.path.join(TEX_DIR, "Marshal_MetallicSmoothness.png")
    msi.file_format = "PNG"
    msi.save()

    # Replace every non-emissive slot with one textured material
    am = bpy.data.materials.new("MarshalArmor")
    am.use_nodes = True
    nt = am.node_tree
    b = nt.nodes["Principled BSDF"]
    def tex(img):
        t = nt.nodes.new("ShaderNodeTexImage"); t.image = img
        return t
    nt.links.new(tex(imgs["BaseColor"]).outputs["Color"], b.inputs["Base Color"])
    nt.links.new(tex(imgs["Roughness"]).outputs["Color"], b.inputs["Roughness"])
    nt.links.new(tex(imgs["Metallic"]).outputs["Color"], b.inputs["Metallic"])
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tex(imgs["Normal"]).outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    old = list(obj.data.materials)
    emit = [m for m in old if m.get("zcom_kind") == "emit"]
    new = [am] + emit
    remap = [new.index(m) if m in emit else 0 for m in old]
    idx = [remap[p.material_index] for p in obj.data.polygons]
    obj.data.materials.clear()
    for m in new:
        obj.data.materials.append(m)
    for p, i in zip(obj.data.polygons, idx):
        p.material_index = i
    arm.data.pose_position = "POSE"
    return paths


# ============================================================================ export / preview

def export_fbx(arm, body):
    e = bpy.data.objects.new("Front", None)
    e.location = (0, -1, 1)
    link(e)
    for o in bpy.data.objects:
        o.select_set(o in (arm, body, e))
    path = os.path.join(MODELS, "Marshal.fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH", "ARMATURE", "EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        mesh_smooth_type="FACE", use_mesh_modifiers=True, use_armature_deform_only=False,
        add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X", path_mode="STRIP",
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
    tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
    print(f"[zcom] exported {path} ({tris} tris, actions: {[a.name for a in bpy.data.actions]})")


def setup_gpu(samples):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        prefs.compute_device_type = "METAL"
        prefs.get_devices()
        for d in prefs.devices:
            d.use = True
        sc.cycles.device = "GPU"
    except Exception as ex:
        print("[zcom] GPU setup failed:", ex)
    sc.cycles.samples = samples


def preview_weapon(arm):
    """Placeholder carbine on the Weapon bone, for preview renders only (not exported)."""
    bm = bmesh.new()
    for c, sz in (((0, -0.12, 0.05), (0.05, 0.62, 0.1)), ((0, 0.0, -0.05), (0.035, 0.05, 0.12)), ((0, -0.26, -0.03), (0.04, 0.05, 0.08))):
        r = bmesh.ops.create_cube(bm, size=1.0)
        for v in r["verts"]:
            v.co = V((v.co.x * sz[0], v.co.y * sz[1], v.co.z * sz[2])) + V(c) + GRIP
    o = mesh_from_bm("PreviewGun", bm, bpy.data.materials["Graphite"] if "Graphite" in bpy.data.materials else None)
    o.vertex_groups.new(name="Weapon").add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
    o.parent = arm
    o.modifiers.new("Armature", "ARMATURE").object = arm
    return o


def previews(arm, tag=""):
    setup_render(24)
    bpy.context.scene.cycles.device = "CPU"      # Metal kernel recompiles after a bake have crashed Blender
    preview_weapon(arm)
    shots = [("front", "Idle", 0, (8, -25)), ("back", "Idle", 0, (10, 160)), ("side", "Run", 5, (4, 90)),
             ("run34", "Run", 14, (10, -40)), ("strafe", "StrafeL", 5, (10, -20)), ("iso", "Run", 3, (52, -40)),
             ("death", "Death", 50, (35, -60))]
    for t in arm.animation_data.nla_tracks:
        t.mute = True
    for name, act, fr, view in shots:
        arm.animation_data.action = bpy.data.actions[act]
        bpy.context.scene.frame_set(fr)
        render_views("Marine", {name: view}, tag=tag)


if __name__ == "__main__":
    M, J, body = build_all()
    tris = 0
    if STAGE == "model":
        for o, _ in PARTS:
            if o.modifiers:
                apply_mods(o)
        tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o, _ in PARTS)
        print(f"[zcom] marine parts={len(PARTS)} tris={tris}")
        setup_render(32)
        tag = ARGS[3] if len(ARGS) > 3 else ""
        render_views("Marine", {"front": (8, -25), "back": (10, 160), "side": (5, 90)}, tag=tag)
        print("[zcom] model previews done")
    else:
        arm = build_armature(J)
        mesh = skin(arm)
        mesh.modifiers["Armature"].show_viewport = False     # don't deform 25k verts on every IK update
        bake_actions(arm)
        mesh.modifiers["Armature"].show_viewport = True
        if STAGE != "nobake":
            bake_textures(mesh, arm)
        export_fbx(arm, mesh)
        previews(arm, ARGS[3] if len(ARGS) > 3 else "")
        print("[zcom] marine done")
