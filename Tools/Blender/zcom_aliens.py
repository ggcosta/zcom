"""
Xeno creatures (Art/Reference/Codex/env_aliens.png) -> Assets/ZCOM/Resources/Models/Alien_<Type>.fbx

    blender -b --factory-startup -P Tools/Blender/zcom_aliens.py -- <models_dir> <previews_dir>

Rigid insect anatomy (chitin segments never stretch): every part is weighted 100% to one bone.
Bones: Body (root, centre of mass) > Head > Mouth, Abdomen > Sac, Leg_<L|R><n>_A (hip->knee) > Leg_<L|R><n>_B (knee->foot),
Claw_<L|R>_A > Claw_<L|R>_B (Brute). Legs are animated in Unity by procedural foot planting + 2-bone IK (InsectLegs.cs).
Materials: AlienChitin (armour plates), AlienFlesh (wet tissue), AlienGlow (eyes / sacs), AlienClaw (bone tips).
Blender axes: Z up, creature faces -Y, origin on the ground under the body.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Quaternion, noise

ARGS = sys.argv[sys.argv.index("--") + 1:]
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import zcom_marine as zm

MODELS, PREVIEWS = zm.MODELS, zm.PREVIEWS
V = Vector
rad = math.radians
PARTS = []          # (obj, bone)
BONES = {}          # name -> (head, tail, parent)
M = {}


def mats():
    def mat(name, color, metal, rough, emit=None, strength=0.0):
        m = bpy.data.materials.new(name); m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = (*color, 1); b.inputs["Metallic"].default_value = metal; b.inputs["Roughness"].default_value = rough
        if emit:
            b.inputs["Emission Color"].default_value = (*emit, 1); b.inputs["Emission Strength"].default_value = strength
        return m
    M.update(chitin=mat("AlienChitin", (0.05, 0.025, 0.06), 0.2, 0.3), flesh=mat("AlienFlesh", (0.14, 0.02, 0.08), 0.0, 0.35),
             glow=mat("AlienGlow", (0.2, 0.5, 0.05), 0.0, 0.2, (0.45, 1.0, 0.15), 8), claw=mat("AlienClaw", (0.45, 0.36, 0.28), 0.1, 0.4))


def obj_from_bm(name, bm, m, bone, smooth=True):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    me.materials.append(M[m])
    for p in me.polygons:
        p.use_smooth = smooth
    PARTS.append((o, bone))
    return o


def ellip(name, c, r, m, bone, n_amp=0.0, n_scale=3.0, seg=20, rings=12, flat_bottom=0.0):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    for v in bm.verts:
        p = v.co.copy()
        if flat_bottom and p.z < -flat_bottom:
            p.z = -flat_bottom + (p.z + flat_bottom) * 0.3
        n = noise.noise(p * n_scale + V(c) * 7.0) if n_amp else 0.0
        v.co = V((p.x * r[0], p.y * r[1], p.z * r[2])) * (1 + n_amp * n) + V(c)
    return obj_from_bm(name, bm, m, bone)


def seg(name, a, b, r1, r2, m, bone, verts=10):
    a, b = V(a), V(b); d = b - a
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r1, radius2=max(r2, 0.0005), depth=d.length)
    q = V((0, 0, 1)).rotation_difference(d.normalized())
    for v in bm.verts:
        v.co = q @ v.co + (a + b) / 2
    return obj_from_bm(name, bm, m, bone)


def chain(name, pts, radii, m, bone, verts=10):
    for i in range(len(pts) - 1):
        seg(f"{name}{i}", pts[i], pts[i + 1], radii[i], radii[i + 1], m, bone, verts)
        if 0 < i:
            ellip(f"{name}J{i}", pts[i], (radii[i] * 1.05,) * 3, m, bone, seg=10, rings=6)


def plate(name, c, r, m, bone, theta=70.0, tilt=0.0, spikes=0, spike_len=0.15, yaw=0.0):
    """Faceted chitin plate: convex hull of a coarse ellipsoid cap (+ inner points), optional back spikes."""
    c = V(c)
    q = Quaternion(V((0, 0, 1)), rad(yaw)) @ Quaternion(V((1, 0, 0)), rad(tilt))
    pts = []
    for i in range(4):
        th = rad(theta * i / 3)
        n = 1 if i == 0 else 7
        for k in range(n):
            ph = 2 * math.pi * (k + 0.5 * (i % 2)) / n
            d = V((math.sin(th) * math.cos(ph), math.sin(th) * math.sin(ph), math.cos(th)))
            pts.append(c + q @ V((d.x * r[0], d.y * r[1], d.z * r[2])))
    pts.append(c + q @ V((0, 0, r[2] * 0.2)))
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in pts]
    res = bmesh.ops.convex_hull(bm, input=vs)
    kill = {g for g in res["geom_interior"] + res["geom_unused"] if isinstance(g, bmesh.types.BMVert)}
    if kill:
        bmesh.ops.delete(bm, geom=list(kill), context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = obj_from_bm(name, bm, m, bone)
    zm.harden(o, 0.008, 1, 30)
    for s in range(spikes):
        t = (s + 0.5) / spikes - 0.5
        base = c + q @ V((t * r[0] * 1.2, 0.0, r[2] * 0.85))
        tip = base + q @ V((t * 0.2, 0.35, 1.0)).normalized() * spike_len
        seg(f"{name}Spike{s}", base, tip, spike_len * 0.18, 0.0, "chitin", bone, 6)
    return o


def bone(name, head, tail, parent=None):
    BONES[name] = (V(head), V(tail), parent)


def leg(side, i, hip, knee, foot, r, m_upper="chitin", spikes=2):
    s = "L" if side > 0 else "R"
    a, b = f"Leg_{s}{i}_A", f"Leg_{s}{i}_B"
    bone(a, hip, knee, "Body"); bone(b, knee, foot, a)
    seg(f"{a}Mesh", hip, knee, r, r * 0.8, m_upper, a)
    fm = V(hip) + (V(knee) - V(hip)) * 0.55
    d = (V(knee) - V(hip))
    ellip(f"{a}Femur", fm, (r * 1.45, r * 1.45, r * 1.45), "chitin", a, 0.08, seg=10, rings=8)
    seg(f"{a}FemurPlate", V(hip) + d * 0.3, V(hip) + d * 0.8, r * 1.35, r * 1.1, "chitin", a, 6)
    ellip(f"{a}Knee", knee, (r * 0.95,) * 3, "chitin", b, seg=10, rings=6)
    mid = V(knee) + (V(foot) - V(knee)) * 0.82
    seg(f"{b}Mesh", knee, mid, r * 0.8, r * 0.45, "chitin", b)
    seg(f"{b}Claw", mid, foot, r * 0.45, 0.0, "claw", b, 8)
    for k in range(spikes):
        p = V(knee) + (V(foot) - V(knee)) * (0.25 + 0.25 * k)
        seg(f"{b}Spine{k}", p, p + V((side * 0.04, 0.0, 0.06)), r * 0.25, 0.0, "claw", b, 6)
    ellip(f"{a}Coxa", hip, (r * 1.3,) * 3, "flesh", "Body", seg=10, rings=6)


# =============================================================================== creatures

def skitter():
    c = V((0, 0.02, 0.42))
    bone("Body", c, c + V((0, -0.25, 0)))
    bone("Head", c + V((0, -0.28, 0.0)), c + V((0, -0.48, -0.02)), "Body")
    bone("Mouth", c + V((0, -0.5, -0.05)), c + V((0, -0.6, -0.05)), "Head")
    bone("Abdomen", c + V((0, 0.2, 0.0)), c + V((0, 0.55, 0.02)), "Body")
    ellip("Thorax", c, (0.24, 0.26, 0.13), "flesh", "Body", 0.08)
    ellip("Abdomen", c + V((0, 0.4, 0.02)), (0.22, 0.3, 0.15), "flesh", "Abdomen", 0.1)
    for k, y in enumerate((0.28, 0.4, 0.52)):
        ellip(f"GlowSlit{k}", c + V((0, y, 0.1)), (0.13, 0.035, 0.05), "glow", "Abdomen", seg=12, rings=6)
    ellip("Head", c + V((0, -0.36, -0.01)), (0.16, 0.15, 0.11), "chitin", "Head", 0.06)
    for x in (-0.07, 0.07):
        ellip(f"Eye{x}", c + V((x, -0.47, 0.04)), (0.03, 0.03, 0.03), "glow", "Head", seg=8, rings=6)
        ellip(f"Eye2{x}", c + V((x * 1.6, -0.43, 0.06)), (0.022, 0.022, 0.022), "glow", "Head", seg=8, rings=6)
        chain(f"Mandible{x}", [c + V((x * 1.1, -0.45, -0.04)), c + V((x * 1.8, -0.6, -0.08)), c + V((x * 0.6, -0.72, -0.12))],
              [0.035, 0.025, 0.003], "claw", "Head", 8)
    plate("PlateHead", c + V((0, -0.33, 0.05)), (0.18, 0.17, 0.09), "chitin", "Head", 65, spikes=0)
    for k, (y, sc) in enumerate(((-0.12, 1.0), (0.08, 1.05), (0.3, 0.95), (0.5, 0.85))):
        b = "Body" if y < 0.2 else "Abdomen"
        plate(f"Plate{k}", c + V((0, y, 0.06)), (0.24 * sc, 0.15, 0.1 * sc), "chitin", b, 70, tilt=-8, spikes=3, spike_len=0.16 * sc)
    for side in (1, -1):
        for i, (hy, fy, ky) in enumerate(((-0.15, -0.55, -0.32), (0.02, 0.05, 0.03), (0.18, 0.6, 0.38))):
            hip = c + V((side * 0.17, hy, -0.03))
            knee = V((side * 0.48, ky, 0.72))
            foot = V((side * 0.7, fy, 0.0))
            leg(side, i, hip, knee, foot, 0.04, spikes=2)
    return dict(height=0.9)


def spitter():
    c = V((0, 0.05, 1.05))
    bone("Body", c, c + V((0, -0.3, 0)))
    bone("Head", c + V((0, -0.4, 0.0)), c + V((0, -0.75, -0.1)), "Body")
    bone("Mouth", c + V((0, -0.95, -0.2)), c + V((0, -1.05, -0.22)), "Head")
    bone("Abdomen", c + V((0, 0.15, 0.1)), c + V((0, 0.5, 0.2)), "Body")
    bone("Sac", c + V((0, 0.3, 0.38)), c + V((0, 0.3, 0.7)), "Abdomen")
    ellip("Thorax", c, (0.26, 0.38, 0.22), "flesh", "Body", 0.08)
    plate("PlateThorax", c + V((0, -0.05, 0.08)), (0.28, 0.36, 0.17), "chitin", "Body", 75, tilt=-6, spikes=2, spike_len=0.22)
    # neck + head with tubular maw
    seg("Neck", c + V((0, -0.3, 0.02)), c + V((0, -0.6, -0.06)), 0.13, 0.11, "flesh", "Head")
    ellip("Head", c + V((0, -0.68, -0.08)), (0.15, 0.2, 0.13), "chitin", "Head", 0.05)
    plate("PlateHead", c + V((0, -0.66, -0.02)), (0.16, 0.22, 0.12), "chitin", "Head", 70, tilt=-15, spikes=1, spike_len=0.3)
    seg("Maw", c + V((0, -0.82, -0.14)), c + V((0, -0.98, -0.22)), 0.08, 0.1, "flesh", "Head", 12)
    ellip("MawGlow", c + V((0, -0.99, -0.22)), (0.07, 0.02, 0.07), "glow", "Head", seg=12, rings=6)
    for x in (-0.07, 0.07):
        ellip(f"Eye{x}", c + V((x, -0.82, -0.02)), (0.03, 0.03, 0.03), "glow", "Head", seg=8, rings=6)
        chain(f"Fang{x}", [c + V((x * 1.2, -0.95, -0.18)), c + V((x * 1.8, -1.05, -0.3)), c + V((x * 0.9, -1.0, -0.42))], [0.025, 0.018, 0.002], "claw", "Head", 8)
    # abdomen with the big acid sac (cluster of glowing bulbs held by chitin ribs)
    ellip("Abdomen", c + V((0, 0.35, 0.12)), (0.24, 0.32, 0.2), "flesh", "Abdomen", 0.1)
    sc = c + V((0, 0.3, 0.42))
    for k, off in enumerate((V((0, 0, 0)), V((0.17, 0.08, -0.03)), V((-0.17, 0.08, -0.03)), V((0.12, -0.15, 0.02)), V((-0.12, -0.15, 0.02)), V((0, 0.2, 0.05)))):
        ellip(f"Sac{k}", sc + off, (0.2, 0.22, 0.2), "glow", "Sac", 0.12, 4.0, seg=16, rings=10)
    for k in range(4):
        ang = rad(-60 + 40 * k)
        p0 = sc + V((math.sin(ang) * 0.32, -0.25, -0.12))
        chain(f"Rib{k}", [p0, sc + V((math.sin(ang) * 0.36, 0.0, 0.2)), sc + V((math.sin(ang) * 0.25, 0.3, 0.12))], [0.035, 0.03, 0.01], "chitin", "Sac", 8)
    for side in (1, -1):
        for i, (hy, fy, ky) in enumerate(((-0.2, -0.75, -0.45), (0.25, 0.65, 0.5))):
            hip = c + V((side * 0.2, hy, -0.08))
            knee = V((side * 0.62, ky, 1.55))
            foot = V((side * 0.72, fy, 0.0))
            leg(side, i, hip, knee, foot, 0.055, spikes=2)
    return dict(height=1.8)


def brute():
    c = V((0, 0.1, 1.35))
    bone("Body", c, c + V((0, -0.4, 0)))
    bone("Head", c + V((0, -0.65, -0.15)), c + V((0, -1.0, -0.25)), "Body")
    bone("Mouth", c + V((0, -1.05, -0.32)), c + V((0, -1.15, -0.34)), "Head")
    bone("Abdomen", c + V((0, 0.3, 0.0)), c + V((0, 0.8, 0.1)), "Body")
    ellip("Torso", c, (0.62, 0.62, 0.48), "flesh", "Body", 0.1)
    ellip("Rear", c + V((0, 0.55, 0.05)), (0.55, 0.5, 0.45), "flesh", "Abdomen", 0.1)
    for k, off in enumerate((V((0.35, 0.3, 0.1)), V((-0.35, 0.3, 0.1)), V((0.2, 0.75, 0.2)))):
        ellip(f"Core{k}", c + off, (0.18, 0.2, 0.16), "glow", "Abdomen", 0.12, seg=12, rings=8)
    # overlapping back armour, scale-like, with spikes
    rows = [(-0.45, 0.0, 0.7), (-0.1, 0.0, 0.78), (0.25, 0.0, 0.72), (0.6, 0.0, 0.6)]
    for k, (y, x, s) in enumerate(rows):
        b = "Body" if y < 0.3 else "Abdomen"
        plate(f"Back{k}", c + V((x, y, 0.28)), (0.7 * s / 0.75, 0.32, 0.3), "chitin", b, 72, tilt=-18, spikes=4, spike_len=0.35)
        for side in (1, -1):
            plate(f"Side{k}{side}", c + V((side * 0.48, y + 0.05, 0.05)), (0.3, 0.3, 0.26), "chitin", b, 75, tilt=-10, yaw=side * 70, spikes=1, spike_len=0.25)
    # head
    ellip("Head", c + V((0, -0.85, -0.2)), (0.3, 0.28, 0.24), "chitin", "Head", 0.06)
    plate("Brow", c + V((0, -0.82, -0.08)), (0.34, 0.3, 0.18), "chitin", "Head", 70, tilt=-25, spikes=2, spike_len=0.2)
    for x in (-0.1, 0.1):
        ellip(f"Eye{x}", c + V((x, -1.08, -0.15)), (0.035, 0.035, 0.035), "glow", "Head", seg=8, rings=6)
        chain(f"Tusk{x}", [c + V((x * 1.3, -1.05, -0.32)), c + V((x * 2.2, -1.25, -0.45)), c + V((x * 1.2, -1.3, -0.62))], [0.05, 0.035, 0.004], "claw", "Head", 8)
    # crushing claws (arms)
    for side in (1, -1):
        s = "L" if side > 0 else "R"
        sh, el, tip = c + V((side * 0.55, -0.55, 0.05)), V((side * 1.05, -1.35, 1.7)), V((side * 0.62, -2.0, 0.75))
        bone(f"Claw_{s}_A", sh, el, "Body"); bone(f"Claw_{s}_B", el, tip, f"Claw_{s}_A")
        seg(f"Claw{s}Upper", sh, el, 0.17, 0.13, "flesh", f"Claw_{s}_A")
        plate(f"Claw{s}Guard", (V(sh) + V(el)) / 2 + V((side * 0.08, 0, 0.1)), (0.22, 0.42, 0.16), "chitin", f"Claw_{s}_A", 70,
              tilt=-35, yaw=side * 25, spikes=2, spike_len=0.2)
        ellip(f"Claw{s}Elbow", el, (0.15, 0.15, 0.15), "chitin", f"Claw_{s}_B", 0.05, seg=10, rings=6)
        # scythe blade: curved chain from elbow to tip, thick plate behind
        mid = V(el) + (V(tip) - V(el)) * 0.5 + V((side * 0.12, -0.25, 0.1))
        chain(f"Claw{s}Blade", [el, mid, tip], [0.18, 0.12, 0.005], "claw", f"Claw_{s}_B", 10)
        plate(f"Claw{s}Plate", V(el) + (mid - V(el)) * 0.6, (0.16, 0.34, 0.12), "chitin", f"Claw_{s}_B", 70, tilt=-60, yaw=side * 20, spikes=2, spike_len=0.18)
    for side in (1, -1):
        for i, (hy, fy, ky) in enumerate(((-0.3, -0.55, -0.55), (0.45, 0.85, 0.8))):
            hip = c + V((side * 0.45, hy, -0.3))
            knee = V((side * 1.0, ky, 1.15))
            foot = V((side * 0.95, fy, 0.0))
            leg(side, i, hip, knee, foot, 0.14, spikes=2)
            plate(f"LegGuard{side}{i}", (V(hip) + V(knee)) / 2 + V((side * 0.06, 0, 0.12)), (0.2, 0.22, 0.14), "chitin", f"Leg_{'L' if side > 0 else 'R'}{i}_A", 70,
                  tilt=-30, yaw=side * 60, spikes=1, spike_len=0.15)
    return dict(height=2.6)


# =============================================================================== rig / export

def build_armature():
    ad = bpy.data.armatures.new("XenoRig")
    arm = bpy.data.objects.new("XenoRig", ad)
    bpy.context.scene.collection.objects.link(arm)
    for o in bpy.context.scene.objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = ad.edit_bones
    for name in BONES:
        h, t, p = BONES[name]
        b = eb.new(name); b.head, b.tail = h, t
    for name in BONES:
        p = BONES[name][2]
        if p:
            eb[name].parent = eb[p]
            eb[name].use_connect = (eb[p].tail - eb[name].head).length < 1e-4
        eb[name].align_roll(V((0, 0, 1)) if abs((BONES[name][1] - BONES[name][0]).normalized().z) < 0.9 else V((0, -1, 0)))
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def skin_and_join(arm, name):
    objs = []
    for o, b in PARTS:
        if o.modifiers:
            zm.apply_mods(o)
        o.vertex_groups.new(name=b).add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
        objs.append(o)
    with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs, selected_objects=objs, object=objs[0]):
        bpy.ops.object.join()
    body = objs[0]
    body.name = body.data.name = name
    body.data.transform(body.matrix_world); body.matrix_world = Matrix.Identity(4)
    for o in bpy.context.scene.objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=0.5, correct_aspect=False, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    body.parent = arm
    body.modifiers.new("Armature", "ARMATURE").object = arm
    return body


def export(arm, body, kind):
    e = bpy.data.objects.new("Front", None); e.location = (0, -1, 0.5); bpy.context.scene.collection.objects.link(e)
    for o in bpy.data.objects:
        o.select_set(o in (arm, body, e))
    path = os.path.join(MODELS, f"Alien_{kind}.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH", "ARMATURE", "EMPTY"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
                             mesh_smooth_type="FACE", use_mesh_modifiers=True, use_armature_deform_only=False, add_leaf_bones=True,
                             primary_bone_axis="Y", secondary_bone_axis="X", bake_anim=False, path_mode="STRIP")
    tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
    print(f"[zcom] exported {path} ({tris} tris, {len(BONES)} bones)")


if __name__ == "__main__":
    for kind, fn in (("Skitter", skitter), ("Spitter", spitter), ("Brute", brute)):
        zm.reset(); PARTS.clear(); BONES.clear(); M.clear()
        mats()
        info = fn()
        arm = build_armature()
        body = skin_and_join(arm, f"Alien_{kind}")
        export(arm, body, kind)
        zm.setup_render(24, (900, 700))
        bpy.context.scene.cycles.device = "CPU"
        hgt = info["height"]
        zm.render_views(f"Alien_{kind}", {"q34": (28, -30), "side": (8, 90)}, target=V((0, -0.1, hgt * 0.45)), dist=hgt * 3.4, lens=50)
    print("[zcom] aliens done")
