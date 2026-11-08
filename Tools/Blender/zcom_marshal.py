"""
ORBITAL MARSHAL - playable starter character (rigged + animated).

    blender -b --factory-startup -P Tools/Blender/zcom_marshal.py -- <models_out_dir> <previews_out_dir>

* Hard-surface armour built from bevelled / chamfered primitives, two-tone steel + graphite, cyan T-visor,
  oversized left pauldron with chevron, high collar, extraction backpack, hip scanner, chunky boots.
* Armature: Hips > Spine > Chest > Neck > Head, arms, legs (thigh / shin / foot), two 2-bone cape chains.
  Armour pieces are rigidly bone-parented; the cape is a skinned mesh so it bends smoothly.
* Baked looping actions exported as FBX takes: Idle, Run, RunBack (24 fps).

Blender coordinates: Z up, character faces -Y, +X is the character's LEFT. (Unity: x -> -x, z -> y, y -> -z.)
"""
import bpy, math, os, sys
from mathutils import Vector, Matrix, Quaternion

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import zcom_characters as zc  # helpers: prim, limb, bevel, subsurf, displace, deform, solidify, join, material, ...

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
MODELS = os.path.abspath(ARGS[0]) if len(ARGS) > 0 else os.path.abspath("Assets/ZCOM/Resources/Models")
PREVIEWS = os.path.abspath(ARGS[1]) if len(ARGS) > 1 else os.path.abspath("Tools/Blender/previews")
os.makedirs(MODELS, exist_ok=True)
os.makedirs(PREVIEWS, exist_ok=True)
rad = math.radians
V = Vector

prim, limb, bevel, subsurf, deform, solidify, join = zc.prim, zc.limb, zc.bevel, zc.subsurf, zc.deform, zc.solidify, zc.join


# ----------------------------------------------------------------------------- palette

def mats():
    return dict(
        steel=zc.material("Steel", (0.55, 0.58, 0.62), 0.65, 0.42),
        graphite=zc.material("Graphite", (0.17, 0.18, 0.2), 0.55, 0.45),
        fabric=zc.material("Fabric", (0.08, 0.085, 0.095), 0.0, 0.85),
        amber=zc.material("Amber", (0.91, 0.64, 0.23), 0.3, 0.45),
        rubber=zc.material("Rubber", (0.05, 0.05, 0.055), 0.1, 0.7),
        metal=zc.material("Metal", (0.3, 0.31, 0.34), 0.85, 0.35),
        visor=zc.material("Visor", (0.05, 0.35, 0.4), 0.0, 0.05, (0.25, 0.88, 0.94), 5),
        light=zc.material("Light", (0.05, 0.35, 0.4), 0.0, 0.2, (0.25, 0.88, 0.94), 4),
    )


def chamfer_box(name, loc, size, mat, cut=0.03, rot=(0, 0, 0), taper=None, taper_axis="Z", seg=1):
    """Faceted armour plate: box with a hard 1-segment bevel (reads as machined chamfers)."""
    o = prim("cube", name, loc, size, rot, mat=mat)
    bevel(o, cut, seg, 30)
    if taper:
        deform(o, "TAPER", taper, taper_axis)
    return o


def plate_arc(name, center, radius, height, start_deg, end_deg, thickness, mat, z_taper=0.0):
    """Curved armour plate (a section of a cylinder shell) around the Z axis, e.g. thigh / shin wraps."""
    import bmesh
    bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=height, vertices=32, location=center)
    o = bpy.context.active_object
    o.name = name
    zc.finalize(o)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    kill = []
    for f in bm.faces:
        c = f.calc_center_median()
        ang = math.degrees(math.atan2(c.y, c.x)) % 360
        inside = (start_deg <= ang <= end_deg) if start_deg <= end_deg else (ang >= start_deg or ang <= end_deg)
        if abs(f.normal.z) > 0.5 or not inside:
            kill.append(f)
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bm.to_mesh(o.data)
    bm.free()
    o.data.materials.append(mat)
    for p in o.data.polygons:
        p.use_smooth = True
    solidify(o, thickness)
    bevel(o, thickness * 0.35, 1, 30)
    if z_taper:
        deform(o, "TAPER", z_taper, "Z")
    return o


def place(objs, loc, rot_deg):
    """Move a group of parts that was built around the origin into place (rotation in degrees, XYZ)."""
    from mathutils import Euler
    M = Matrix.Translation(V(loc)) @ Euler([rad(a) for a in rot_deg]).to_matrix().to_4x4()
    for o in objs:
        o.matrix_world = M @ o.matrix_world
    return objs


# ----------------------------------------------------------------------------- skeleton

# name: (head, tail, parent)
BONES = {
    "Hips": ((0, 0, 0.94), (0, 0, 1.06), None),
    "Spine": ((0, 0, 1.06), (0, 0, 1.24), "Hips"),
    "Chest": ((0, 0, 1.24), (0, 0, 1.5), "Spine"),
    "Neck": ((0, 0, 1.5), (0, 0, 1.6), "Chest"),
    "Head": ((0, 0, 1.6), (0, 0, 1.86), "Neck"),
    # Arms hold the carbine: right hand on the grip, left hand on the fore-grip (gun is on the right side).
    "UpperArm_R": ((-0.33, 0.0, 1.45), (-0.4, -0.06, 1.2), "Chest"),
    "ForeArm_R": ((-0.4, -0.06, 1.2), (-0.22, -0.3, 1.13), "UpperArm_R"),
    "UpperArm_L": ((0.33, 0.0, 1.45), (0.17, -0.36, 1.27), "Chest"),
    "ForeArm_L": ((0.17, -0.36, 1.27), (-0.17, -0.67, 1.21), "UpperArm_L"),
    "Thigh_L": ((0.13, 0, 0.93), (0.14, -0.02, 0.52), "Hips"),
    "Shin_L": ((0.14, -0.02, 0.52), (0.14, 0.03, 0.11), "Thigh_L"),
    "Foot_L": ((0.14, 0.03, 0.11), (0.14, -0.15, 0.04), "Shin_L"),
    "Thigh_R": ((-0.13, 0, 0.93), (-0.14, -0.02, 0.52), "Hips"),
    "Shin_R": ((-0.14, -0.02, 0.52), (-0.14, 0.03, 0.11), "Thigh_R"),
    "Foot_R": ((-0.14, 0.03, 0.11), (-0.14, -0.15, 0.04), "Shin_R"),
    "Cape_L1": ((0.12, 0.17, 1.46), (0.15, 0.3, 1.0), "Chest"),
    "Cape_L2": ((0.15, 0.3, 1.0), (0.17, 0.37, 0.58), "Cape_L1"),
    "Cape_R1": ((-0.12, 0.17, 1.46), (-0.15, 0.3, 1.0), "Chest"),
    "Cape_R2": ((-0.15, 0.3, 1.0), (-0.17, 0.37, 0.58), "Cape_R1"),
}


def build_armature():
    arm_data = bpy.data.armatures.new("MarshalRig")
    arm = bpy.data.objects.new("MarshalRig", arm_data)
    bpy.context.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones
    for name, (h, t, parent) in BONES.items():
        b = eb.new(name)
        b.head = V(h)
        b.tail = V(t)
        b.roll = 0.0
        if parent:
            b.parent = eb[parent]
            b.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


PARTS = []


def bind(obj, bone):
    """Rigid skinning: every vertex of this armour piece follows one bone 100%."""
    vg = obj.vertex_groups.new(name=bone)
    vg.add([v.index for v in obj.data.vertices], 1.0, "REPLACE")
    PARTS.append(obj)


# ----------------------------------------------------------------------------- geometry

def build_body(m, arm):
    S, G, F, A, R, M, VIS, L = m["steel"], m["graphite"], m["fabric"], m["amber"], m["rubber"], m["metal"], m["visor"], m["light"]

    # ---------------- head
    h = []
    shell = prim("ico", "helmet", (0, 0.015, 1.745), (0.33, 0.37, 0.36), mat=S, sub=2, smooth=False)
    h.append(shell)
    h.append(chamfer_box("helmet_ridge", (0, 0.03, 1.915), (0.07, 0.26, 0.03), G, 0.01))
    h.append(chamfer_box("helmet_stripe", (-0.08, 0.0, 1.895), (0.04, 0.22, 0.02), A, 0.006, rot=(0, 18, 0)))
    h.append(chamfer_box("helmet_rim", (0, 0.02, 1.6), (0.3, 0.34, 0.06), G, 0.02))
    h.append(chamfer_box("helmet_brow", (0, -0.165, 1.79), (0.26, 0.05, 0.045), S, 0.015, rot=(-14, 0, 0)))
    h.append(chamfer_box("faceplate", (0, -0.16, 1.69), (0.25, 0.06, 0.17), G, 0.02, rot=(6, 0, 0), taper=-0.45))
    h.append(chamfer_box("visor_bar", (0, -0.192, 1.735), (0.2, 0.012, 0.034), VIS, 0.004))
    h.append(chamfer_box("visor_stem", (0, -0.192, 1.69), (0.032, 0.012, 0.1), VIS, 0.004))
    h.append(chamfer_box("chin", (0, -0.15, 1.62), (0.18, 0.07, 0.06), S, 0.015, rot=(18, 0, 0)))
    for s in (-1, 1):
        h.append(chamfer_box("cheek", (s * 0.155, -0.06, 1.69), (0.04, 0.16, 0.16), G, 0.015, rot=(0, 0, s * 8)))
        h.append(chamfer_box("earpod", (s * 0.17, 0.03, 1.74), (0.05, 0.12, 0.12), M, 0.015))
        h.append(chamfer_box("rearvent", (s * 0.06, 0.18, 1.7), (0.05, 0.03, 0.1), G, 0.008))
    h.append(prim("cyl", "antenna", (0.2, 0.06, 1.88), (0.012, 0.012, 0.32), mat=M))
    h.append(chamfer_box("antenna_base", (0.2, 0.05, 1.74), (0.04, 0.06, 0.08), G, 0.008))
    head = join(h, "M_Head", BONES["Head"][0], 32)
    bind(head, "Head")

    # ---------------- chest / torso (incl. collar, shoulders, backpack)
    c = []
    c.append(zc.subsurf(chamfer_box("torso", (0, 0.02, 1.33), (0.44, 0.3, 0.38), G, 0.06), 1))
    plate = chamfer_box("chestplate", (0, -0.13, 1.37), (0.46, 0.08, 0.3), S, 0.04, rot=(8, 0, 0), taper=0.22)
    c.append(plate)
    c.append(chamfer_box("chest_center", (0, -0.175, 1.36), (0.14, 0.04, 0.22), G, 0.015, rot=(8, 0, 0)))
    for s in (-1, 1):
        c.append(chamfer_box("chestlight", (s * 0.12, -0.192, 1.43), (0.08, 0.01, 0.016), L, 0.003, rot=(10, 0, -s * 8)))
        c.append(chamfer_box("pec", (s * 0.15, -0.16, 1.4), (0.13, 0.04, 0.15), S, 0.02, rot=(10, 0, -s * 8)))
    for i in range(3):
        c.append(chamfer_box(f"ab{i}", (0, -0.13, 1.16 - i * 0.055), (0.24 - i * 0.02, 0.06, 0.045), G, 0.012))
    # high collar ring
    collar = prim("cyl", "collar", (0, 0.0, 1.53), (0.3, 0.28, 0.12), mat=S, verts=12)
    bevel(collar, 0.02, 1, 30)
    c.append(collar)
    c.append(chamfer_box("collar_front", (0, -0.13, 1.56), (0.22, 0.04, 0.1), G, 0.015, rot=(-15, 0, 0)))
    # right (small) shoulder plate
    c.append(chamfer_box("shoulder_R", (-0.33, 0.0, 1.5), (0.18, 0.24, 0.1), S, 0.025, rot=(0, 22, 0)))
    c.append(chamfer_box("shoulder_R2", (-0.37, 0.0, 1.44), (0.12, 0.22, 0.08), G, 0.02, rot=(0, 35, 0)))
    # LEFT oversized command pauldron: big amber double chevron on the top surface (reads from the iso camera)
    pd = [chamfer_box("pauldron", (0, 0, 0), (0.32, 0.38, 0.12), S, 0.05),
          chamfer_box("pauldron_lip", (0.1, 0, -0.09), (0.16, 0.36, 0.09), G, 0.03, rot=(0, -20, 0))]
    for k, off in enumerate((-0.07, 0.03)):
        for sgn in (-1, 1):
            pd.append(chamfer_box("chevron", (0.0 + 0.0, off + 0.0 + 0.045, 0.063), (0.035, 0.15, 0.012), A, 0.004, rot=(0, 0, sgn * 52)))
            pd[-1].location.x += sgn * 0.045
            pd[-1].location.y += off * 0 - 0.03 + (0.0 if k == 0 else 0.0)
    # (rebuild chevrons precisely as two V shapes pointing forward)
    for o in pd[2:]:
        bpy.data.objects.remove(o)
    pd = pd[:2]
    for k, yoff in enumerate((-0.08, 0.02)):
        for sgn in (-1, 1):
            pd.append(chamfer_box("chevron", (sgn * 0.05, yoff + 0.04, 0.063), (0.03, 0.15, 0.012), A, 0.004, rot=(0, 0, sgn * 50)))
    pd.append(chamfer_box("pauldron_trim", (0.15, -0.14, -0.02), (0.02, 0.08, 0.04), L, 0.004))
    place(pd, (0.37, 0.0, 1.53), (0, -22, 0))
    c.extend(pd)
    # backpack
    c.append(chamfer_box("pack", (0, 0.25, 1.3), (0.38, 0.18, 0.46), G, 0.04))
    c.append(chamfer_box("pack_frame", (0, 0.3, 1.3), (0.42, 0.1, 0.5), M, 0.02))
    c.append(chamfer_box("pack_lid", (0, 0.26, 1.55), (0.34, 0.16, 0.06), S, 0.02))
    c.append(chamfer_box("pack_light", (0, 0.351, 1.3), (0.035, 0.01, 0.24), L, 0.004))
    c.append(chamfer_box("pack_light_frame", (0, 0.347, 1.3), (0.08, 0.01, 0.3), G, 0.006))
    for s in (-1, 1):
        can = prim("cyl", "canister", (s * 0.22, 0.26, 1.24), (0.08, 0.08, 0.3), mat=S, verts=16)
        bevel(can, 0.01, 1, 30)
        c.append(can)
        c.append(prim("cyl", "canister_band", (s * 0.22, 0.26, 1.24), (0.085, 0.085, 0.03), mat=A, verts=16))
    c.append(prim("cyl", "pack_handle", (0, 0.26, 1.6), (0.02, 0.02, 0.16), (0, 90, 0), mat=M))
    chest = join(c, "M_Chest", BONES["Chest"][0], 32)
    bind(chest, "Chest")

    # ---------------- hips: pelvis, belt, pouches, scanner, tassets
    p = []
    p.append(zc.subsurf(chamfer_box("pelvis", (0, 0, 0.95), (0.36, 0.25, 0.18), G, 0.04), 1))
    p.append(chamfer_box("belt", (0, 0, 1.02), (0.4, 0.29, 0.06), M, 0.012))
    p.append(chamfer_box("buckle", (0, -0.15, 1.02), (0.08, 0.02, 0.05), A, 0.006))
    for x in (0.1, 0.17):
        p.append(chamfer_box("pouch", (x, -0.13, 0.97), (0.06, 0.06, 0.09), F, 0.01))
    p.append(chamfer_box("pouch_back", (0, 0.15, 0.98), (0.16, 0.06, 0.09), F, 0.01))
    p.append(chamfer_box("scanner", (-0.21, -0.07, 0.95), (0.05, 0.13, 0.12), G, 0.012))
    p.append(chamfer_box("scanner_screen", (-0.237, -0.07, 0.955), (0.005, 0.09, 0.08), L, 0.002))
    for s in (-1, 1):
        p.append(chamfer_box("tasset", (s * 0.17, -0.06, 0.86), (0.12, 0.05, 0.14), S, 0.02, rot=(8, 0, s * 10)))
    hips = join(p, "M_Hips", BONES["Hips"][0], 32)
    bind(hips, "Hips")

    # ---------------- legs
    for s, side in ((1, "L"), (-1, "R")):
        x = s * 0.135
        th = [zc.subsurf(chamfer_box("thigh_suit", (x, -0.005, 0.72), (0.16, 0.17, 0.4), G, 0.04), 1),
              chamfer_box("thigh_plate", (x, -0.085, 0.74), (0.14, 0.04, 0.26), S, 0.025, rot=(4, 0, 0), taper=-0.2),
              chamfer_box("thigh_side", (x + s * 0.085, 0.0, 0.73), (0.025, 0.12, 0.22), G, 0.008),
              chamfer_box("thigh_strap", (x, 0.0, 0.62), (0.175, 0.185, 0.03), M, 0.006)]
        if s < 0:
            th.append(chamfer_box("thigh_mark", (x - 0.088, -0.02, 0.8), (0.006, 0.05, 0.06), A, 0.002))
        thigh = join(th, f"M_Thigh_{side}", BONES[f"Thigh_{side}"][0], 32)
        bind(thigh, f"Thigh_{side}")

        sh = [zc.subsurf(chamfer_box("calf", (x, 0.015, 0.32), (0.13, 0.15, 0.36), G, 0.035), 1),
              bevel(prim("cyl", "knee", (x, -0.1, 0.51), (0.17, 0.17, 0.07), (82, 0, 0), mat=S, verts=6, smooth=False), 0.015, 1, 30),
              chamfer_box("knee_core", (x, -0.135, 0.51), (0.05, 0.02, 0.05), G, 0.01),
              chamfer_box("shin", (x, -0.07, 0.3), (0.13, 0.06, 0.28), S, 0.03, taper=-0.25),
              chamfer_box("shin_rib", (x, -0.1, 0.3), (0.035, 0.02, 0.2), G, 0.006),
              chamfer_box("ankle", (x, 0.0, 0.15), (0.14, 0.15, 0.06), M, 0.012)]
        shin = join(sh, f"M_Shin_{side}", BONES[f"Shin_{side}"][0], 32)
        bind(shin, f"Shin_{side}")

        ft = [chamfer_box("boot", (x, -0.045, 0.075), (0.15, 0.28, 0.13), G, 0.035),
              chamfer_box("boot_toe", (x, -0.16, 0.07), (0.145, 0.07, 0.1), S, 0.025),
              chamfer_box("boot_cuff", (x, 0.02, 0.15), (0.155, 0.17, 0.05), S, 0.015),
              chamfer_box("sole", (x, -0.045, 0.016), (0.165, 0.3, 0.032), R, 0.01),
              chamfer_box("heel", (x, 0.07, 0.04), (0.15, 0.07, 0.06), R, 0.01)]
        foot = join(ft, f"M_Foot_{side}", BONES[f"Foot_{side}"][0], 32)
        bind(foot, f"Foot_{side}")

    # ---------------- arms (posed on the rifle)
    for side in ("L", "R"):
        sh, el = V(BONES[f"UpperArm_{side}"][0]), V(BONES[f"UpperArm_{side}"][1])
        hand = V(BONES[f"ForeArm_{side}"][1])
        d1, d2 = (el - sh).normalized(), (hand - el).normalized()
        up = [limb("upper", sh, el, 0.07, 0.062, G),
              limb("upper_plate", sh + d1 * 0.06, el - d1 * 0.05, 0.08, 0.072, S),
              prim("sphere", "elbow", el, (0.13, 0.13, 0.13), mat=G)]
        upper = join(up, f"M_UpperArm_{side}", tuple(sh), 32)
        bind(upper, f"UpperArm_{side}")
        fo = [limb("fore", el, hand, 0.062, 0.055, G),
              limb("bracer", el + d2 * 0.05, hand - d2 * 0.06, 0.078, 0.07, S),
              limb("bracer_band", el + d2 * 0.12, el + d2 * 0.15, 0.081, 0.081, A if side == "R" else M),
              zc.subsurf(chamfer_box("glove", tuple(hand), (0.09, 0.11, 0.09), G, 0.02), 1),
              chamfer_box("knuckles", tuple(hand + d2 * 0.04 + V((0, 0, 0.03))), (0.08, 0.05, 0.04), M, 0.01)]
        fore = join(fo, f"M_ForeArm_{side}", tuple(el), 32)
        bind(fore, f"ForeArm_{side}")


def build_cape(m, arm):
    """Two skinned cloth panels hanging from the back of the shoulders, with a faded chevron stripe."""
    import bmesh
    objs = []
    for s, side in ((1, "L"), (-1, "R")):
        top, mid, bot = V(BONES[f"Cape_{side}1"][0]), V(BONES[f"Cape_{side}1"][1]), V(BONES[f"Cape_{side}2"][1])
        me = bpy.data.meshes.new(f"Cape_{side}")
        o = bpy.data.objects.new(f"M_Cape_{side}", me)
        bpy.context.collection.objects.link(o)
        bm = bmesh.new()
        cols, rows = 14, 30
        width_top, width_bot = 0.17, 0.23
        verts = []
        for r in range(rows + 1):
            t = r / rows
            # follow the bone chain
            centre = top.lerp(mid, t * 2) if t <= 0.5 else mid.lerp(bot, (t - 0.5) * 2)
            w = width_top + (width_bot - width_top) * t
            row = []
            for c_ in range(cols + 1):
                u = c_ / cols - 0.5
                xoff = u * w + s * 0.02
                yoff = 0.02 * math.cos(u * math.pi) - 0.02  # slight curl
                row.append(bm.verts.new(centre + V((xoff, yoff, 0))))
            verts.append(row)
        for r in range(rows):
            for c_ in range(cols):
                bm.faces.new((verts[r][c_], verts[r][c_ + 1], verts[r + 1][c_ + 1], verts[r + 1][c_]))
        # slightly uneven hem
        for c_, v in enumerate(verts[-1]):
            v.co.z += 0.012 * math.sin(c_ * 0.9 + s)
        bm.to_mesh(me)
        bm.free()
        me.materials.append(m["fabric"])
        for poly in me.polygons:
            poly.use_smooth = True

        # Inverted-V chevron stripe as its own thin strip lying on the cloth (crisp edges, no stair-stepping).
        def surf(u, t):
            centre = top.lerp(mid, t * 2) if t <= 0.5 else mid.lerp(bot, (t - 0.5) * 2)
            w = width_top + (width_bot - width_top) * t
            return centre + V((u * w + s * 0.02, 0.02 * math.cos(u * math.pi) - 0.02 + 0.007, 0))
        sme = bpy.data.meshes.new(f"CapeStripe_{side}")
        so = bpy.data.objects.new(f"M_CapeStripe_{side}", sme)
        bpy.context.collection.objects.link(so)
        sbm = bmesh.new()
        su, rowsv = 20, []
        for i in range(su + 1):
            u = -0.5 + i / su
            d = 0.5 - s * u                    # 0 at the spine edge, 1 at the outer edge
            tl = 0.5 + 0.2 * d
            rowsv.append((sbm.verts.new(surf(u, tl - 0.022)), sbm.verts.new(surf(u, tl + 0.022))))
        for i in range(su):
            a0, a1 = rowsv[i]
            b0, b1 = rowsv[i + 1]
            sbm.faces.new((a0, b0, b1, a1))
        sbm.to_mesh(sme)
        sbm.free()
        sme.materials.append(m["steel"])
        for poly in sme.polygons:
            poly.use_smooth = True
        solidify(so, 0.004)
        zc.bake(so)
        sg1, sg2 = so.vertex_groups.new(name=f"Cape_{side}1"), so.vertex_groups.new(name=f"Cape_{side}2")
        for v in sme.vertices:
            t = max(0.0, min(1.0, (top.z - v.co.z) / (top.z - bot.z)))
            w2 = max(0.0, min(1.0, (t - 0.3) / 0.4))
            sg1.add([v.index], 1.0 - w2, "REPLACE")
            sg2.add([v.index], w2, "REPLACE")
        PARTS.append(so)
        solidify(o, 0.012)
        zc.bake(o)
        # skin weights along the chain
        g1, g2 = o.vertex_groups.new(name=f"Cape_{side}1"), o.vertex_groups.new(name=f"Cape_{side}2")
        span = top.z - bot.z
        for v in me.vertices:
            t = max(0.0, min(1.0, (top.z - v.co.z) / span))
            w2 = max(0.0, min(1.0, (t - 0.3) / 0.4))
            g1.add([v.index], 1.0 - w2, "REPLACE")
            g2.add([v.index], w2, "REPLACE")
        PARTS.append(o)
        objs.append(o)
    return objs


def skin_all(arm):
    """Merge every part into one skinned mesh driven by the armature."""
    body = join(list(PARTS), "M_Marshal", (0, 0, 0), 32)
    body.parent = arm
    mod = body.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    return body


# ----------------------------------------------------------------------------- animation

def rest_quat(arm, bone):
    return arm.data.bones[bone].matrix_local.to_quaternion()


def key_rot(arm, bone, frame, axis, deg):
    """Rotate a bone by deg around an armature-space axis (relative to its rest pose), and key it."""
    pb = arm.pose.bones[bone]
    pb.rotation_mode = "QUATERNION"
    rq = rest_quat(arm, bone)
    world = Quaternion(V(axis), rad(deg))
    pb.rotation_quaternion = rq.inverted() @ world @ rq
    pb.keyframe_insert("rotation_quaternion", frame=frame)


def key_rot_multi(arm, bone, frame, rots):
    pb = arm.pose.bones[bone]
    pb.rotation_mode = "QUATERNION"
    rq = rest_quat(arm, bone)
    q = Quaternion()
    for axis, deg in rots:
        q = Quaternion(V(axis), rad(deg)) @ q
    pb.rotation_quaternion = rq.inverted() @ q @ rq
    pb.keyframe_insert("rotation_quaternion", frame=frame)


def key_loc(arm, bone, frame, offset):
    pb = arm.pose.bones[bone]
    rq = rest_quat(arm, bone)
    pb.location = rq.inverted() @ V(offset)
    pb.keyframe_insert("location", frame=frame)


X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)
# Legs swing about +X: negative = forward (character faces -Y).


def new_action(arm, name):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    if not arm.animation_data:
        arm.animation_data_create()
    arm.animation_data.action = act
    return act


def pose_cycle(arm, frame, phase, mode):
    """mode: 'idle', 'run', 'back'."""
    s, c = math.sin(phase), math.cos(phase)
    if mode == "idle":
        b = math.sin(phase)
        key_loc(arm, "Hips", frame, (0, 0, -0.008 + 0.006 * b))
        key_rot(arm, "Spine", frame, X, -1.0 * b)
        key_rot(arm, "Chest", frame, X, 1.5 * b)
        key_rot(arm, "Head", frame, Z, 2.0 * math.sin(phase * 0.5))
        for side in ("L", "R"):
            key_rot(arm, f"Thigh_{side}", frame, X, 0)
            key_rot(arm, f"Shin_{side}", frame, X, 0)
            key_rot(arm, f"Foot_{side}", frame, X, 0)
            key_rot(arm, f"Cape_{side}1", frame, X, 9 + 2.5 * math.sin(phase + (0.8 if side == "L" else 0)))
            key_rot(arm, f"Cape_{side}2", frame, X, 3 + 3 * math.sin(phase + 1.2 + (0.8 if side == "L" else 0)))
        key_rot(arm, "UpperArm_R", frame, X, 1.2 * b)
        key_rot(arm, "UpperArm_L", frame, X, 1.2 * b)
        return

    d = 1.0 if mode == "run" else -1.0           # backwards: legs travel the other way
    amp = 38 if mode == "run" else 28
    lean = 7 if mode == "run" else -3
    for side, off in (("L", 0.0), ("R", math.pi)):
        ph = phase + off
        f = math.sin(ph)                          # +1 = leg forward
        swing = math.cos(ph) * d                  # >0 while the leg travels forward (swing phase)
        thigh = -amp * f
        knee = 8 + 62 * max(0.0, swing) ** 1.3 + 10 * max(0.0, -f)
        foot = -0.45 * thigh - 0.35 * knee + 6
        key_rot(arm, f"Thigh_{side}", frame, X, thigh)
        key_rot(arm, f"Shin_{side}", frame, X, knee)
        key_rot(arm, f"Foot_{side}", frame, X, foot)
    key_loc(arm, "Hips", frame, (0, 0, -0.03 + 0.03 * math.cos(2 * phase)))
    key_rot_multi(arm, "Hips", frame, [(Z, 6 * s * d), (Y, 3 * s)])
    key_rot_multi(arm, "Spine", frame, [(Z, -6 * s * d), (X, -lean)])
    key_rot(arm, "Chest", frame, X, -2 + 1.5 * math.cos(2 * phase))
    key_rot(arm, "Head", frame, X, 3 + 1.0 * math.cos(2 * phase))
    key_rot(arm, "UpperArm_R", frame, X, 2 * math.cos(2 * phase))
    key_rot(arm, "UpperArm_L", frame, X, 2 * math.cos(2 * phase))
    lift = 26 if mode == "run" else 8
    for side, off in (("L", 0.0), ("R", 1.3)):
        key_rot(arm, f"Cape_{side}1", frame, X, lift + 7 * math.sin(2 * phase + off))
        key_rot(arm, f"Cape_{side}2", frame, X, lift * 0.6 + 10 * math.sin(2 * phase + off + 1.1))


def bake_actions(arm):
    scene = bpy.context.scene
    scene.render.fps = 24
    specs = [("Idle", 48, "idle"), ("Run", 16, "run"), ("RunBack", 16, "back")]
    acts = []
    for name, frames, mode in specs:
        act = new_action(arm, name)
        for fr in range(frames + 1):
            phase = 2 * math.pi * fr / frames
            pose_cycle(arm, fr, phase, mode)
        acts.append((act, frames))
    # Leave Idle on the armature; push the others to NLA so the FBX exporter sees all of them.
    arm.animation_data.action = None
    for act, frames in acts:
        track = arm.animation_data.nla_tracks.new()
        track.name = act.name
        strip = track.strips.new(act.name, 0, act)
        strip.mute = False
    scene.frame_start, scene.frame_end = 0, 48
    return acts


# ----------------------------------------------------------------------------- export / preview

def export_fbx():
    for o in bpy.data.objects:
        o.select_set(o.type in ("MESH", "ARMATURE", "EMPTY"))
    path = os.path.join(MODELS, "Marshal.fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH", "ARMATURE", "EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        mesh_smooth_type="FACE", use_mesh_modifiers=True, use_armature_deform_only=False,
        add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
    tris = sum(len(p.vertices) - 2 for o in bpy.data.objects if o.type == "MESH" for p in o.data.polygons)
    print(f"[zcom] exported {path} ({tris} tris, actions: {[a.name for a in bpy.data.actions]})")


def preview(arm):
    """Stills: rest pose front 3/4 and back 3/4, plus a run frame from the side."""
    shots = [("Marshal_front", None, 0, (20, -35)), ("Marshal_back", None, 0, (20, 150)),
             ("Marshal_run_side", "Run", 4, (5, -90)), ("Marshal_iso", "Run", 10, (50, -45))]
    for name, action, frame, view in shots:
        arm.animation_data.action = bpy.data.actions[action] if action else None
        for t in arm.animation_data.nla_tracks:
            t.mute = True
        if not action:
            for pb in arm.pose.bones:
                pb.rotation_quaternion = Quaternion()
                pb.location = V()
        bpy.context.scene.frame_set(frame)
        zc.preview(name, views=(view,))
    for t in arm.animation_data.nla_tracks:
        t.mute = False
    arm.animation_data.action = None


if __name__ == "__main__":
    zc.PREVIEWS = PREVIEWS
    zc.reset()
    m = mats()
    arm = build_armature()
    build_body(m, arm)
    build_cape(m, arm)
    skin_all(arm)
    zc.empty("Front", (0, -1, 1))
    bake_actions(arm)
    export_fbx()
    preview(arm)
    print("[zcom] marshal done")
