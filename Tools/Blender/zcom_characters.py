"""
ZCOM character generator for Blender (run headless):

    blender -b -P Tools/Blender/zcom_characters.py -- <models_out_dir> <previews_out_dir> [only_name]

Builds the operative and the three aliens procedurally (bevelled hard-surface armour, subdivided organic forms,
noise-displaced alien skin), splits them into the parts the game animates, exports one FBX per character and
renders a preview image of each.

Conventions
  * Blender: Z up, characters face -Y (Blender "front"), 1 unit = 1 metre, feet on Z = 0.
  * Object names are what the game looks up: Leg_*, Arm_*, Sac, Core, Mouth, Front (marker).
  * Pivots (object origins) sit at the joints the game rotates around (hips, shoulders).
  * Material names are remapped to the game's URP materials in Unity (Armor, Suit, Skin, Glow, ...).
"""
import bpy, math, os, sys
from mathutils import Vector, Matrix

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
MODELS = os.path.abspath(ARGS[0]) if len(ARGS) > 0 else os.path.abspath("Assets/ZCOM/Resources/Models")
PREVIEWS = os.path.abspath(ARGS[1]) if len(ARGS) > 1 else os.path.abspath("Tools/Blender/previews")
ONLY = ARGS[2] if len(ARGS) > 2 else None
os.makedirs(MODELS, exist_ok=True)
os.makedirs(PREVIEWS, exist_ok=True)
rad = math.radians


# ----------------------------------------------------------------------------- scene / material helpers

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, color, metal=0.0, rough=0.5, emit=None, strength=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    m.metallic = metal
    m.roughness = rough
    try:
        m.use_nodes = True
    except Exception:
        pass
    if m.node_tree:
        b = m.node_tree.nodes.get("Principled BSDF")
        if b:
            b.inputs["Base Color"].default_value = (*color, 1.0)
            b.inputs["Metallic"].default_value = metal
            b.inputs["Roughness"].default_value = rough
            if emit:
                b.inputs["Emission Color"].default_value = (*emit, 1.0)
                b.inputs["Emission Strength"].default_value = strength
    return m


def finalize(o):
    """Apply rotation/scale into the mesh but keep the object's location as its origin
    (so deform/displace modifiers work around the part's own centre)."""
    bpy.context.view_layer.update()
    m = o.matrix_world.copy()
    loc = m.to_translation()
    o.data.transform(Matrix.Translation(-loc) @ m)
    o.matrix_world = Matrix.Translation(loc)


def prim(kind, name, loc=(0, 0, 0), scale=(1, 1, 1), rot=(0, 0, 0), mat=None, smooth=True, **kw):
    if kind == "cube":
        bpy.ops.mesh.primitive_cube_add(size=1)
    elif kind == "sphere":
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.5, segments=kw.get("seg", 24), ring_count=kw.get("rings", 14))
    elif kind == "ico":
        bpy.ops.mesh.primitive_ico_sphere_add(radius=0.5, subdivisions=kw.get("sub", 2))
    elif kind == "cyl":
        bpy.ops.mesh.primitive_cylinder_add(radius=0.5, depth=1, vertices=kw.get("verts", 24))
    elif kind == "cone":
        bpy.ops.mesh.primitive_cone_add(radius1=0.5, radius2=kw.get("r2", 0.0), depth=1, vertices=kw.get("verts", 16))
    elif kind == "torus":
        bpy.ops.mesh.primitive_torus_add(major_radius=0.5, minor_radius=kw.get("minor", 0.1),
                                         major_segments=kw.get("major_seg", 32), minor_segments=kw.get("minor_seg", 10))
    o = bpy.context.active_object
    o.name = name
    o.location = loc
    o.rotation_euler = [rad(a) for a in rot]
    o.scale = scale
    finalize(o)
    if mat:
        o.data.materials.append(mat)
    for p in o.data.polygons:
        p.use_smooth = smooth
    return o


def limb(name, a, b, r1, r2, mat, verts=16):
    """Tapered segment between two points (radius r1 at a, r2 at b)."""
    a, b = Vector(a), Vector(b)
    d = b - a
    bpy.ops.mesh.primitive_cone_add(radius1=r1, radius2=r2, depth=d.length, vertices=verts, end_fill_type="TRIFAN")
    o = bpy.context.active_object
    o.name = name
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    o.location = (a + b) / 2
    finalize(o)
    o.data.materials.append(mat)
    for p in o.data.polygons:
        p.use_smooth = True
    return o


def bevel(o, w=0.02, seg=3, angle=35):
    m = o.modifiers.new("Bevel", "BEVEL")
    m.width = w
    m.segments = seg
    m.limit_method = "ANGLE"
    m.angle_limit = rad(angle)
    return o


def subsurf(o, lv=2):
    m = o.modifiers.new("Subsurf", "SUBSURF")
    m.levels = lv
    m.render_levels = lv
    return o


_tex_count = [0]


def displace(o, strength=0.03, size=0.2):
    _tex_count[0] += 1
    t = bpy.data.textures.new(f"noise{_tex_count[0]}", "CLOUDS")
    t.noise_scale = size
    t.noise_depth = 2
    m = o.modifiers.new("Displace", "DISPLACE")
    m.texture = t
    m.texture_coords = "GLOBAL"
    m.strength = strength
    m.mid_level = 0.5
    return o


def deform(o, method, amount, axis="Z"):
    m = o.modifiers.new(method, "SIMPLE_DEFORM")
    m.deform_method = method
    if method in ("TAPER", "STRETCH"):
        m.factor = amount
    else:
        m.angle = rad(amount)
    m.deform_axis = axis
    return o


def solidify(o, t):
    m = o.modifiers.new("Solidify", "SOLIDIFY")
    m.thickness = t
    return o


def bake(o):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev)
    o.modifiers.clear()
    o.data = me


def join(objs, name, origin=(0, 0, 0), sharp_angle=40):
    for o in objs:
        bake(o)
    with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs, selected_objects=objs):
        bpy.ops.object.join()
    o = objs[0]
    o.name = name
    o.data.name = name
    o.data.transform(o.matrix_world)
    o.matrix_world = Matrix.Identity(4)
    for p in o.data.polygons:
        p.use_smooth = True
    try:
        o.data.set_sharp_from_angle(angle=rad(sharp_angle))
    except Exception:
        pass
    p = Vector(origin)
    o.data.transform(Matrix.Translation(-p))
    o.location = p
    return o


def empty(name, loc):
    e = bpy.data.objects.new(name, None)
    e.location = loc
    bpy.context.collection.objects.link(e)
    return e


def mirror_x(v):
    return (-v[0], v[1], v[2])


# ----------------------------------------------------------------------------- export / preview

def export(name):
    for o in bpy.data.objects:
        o.select_set(o.type in ("MESH", "EMPTY"))
    path = os.path.join(MODELS, f"{name}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False,
        use_custom_props=False)
    tris = sum(len(p.vertices) - 2 for o in bpy.data.objects if o.type == "MESH" for p in o.data.polygons)
    print(f"[zcom] exported {path}  ({tris} triangles)")


def preview(name, views=((35, -30), (35, 150))):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    sh = scene.display.shading
    sh.light = "STUDIO"
    sh.color_type = "MATERIAL"
    sh.show_cavity = True
    sh.cavity_type = "BOTH"
    sh.show_shadows = True
    sh.show_specular_highlight = True
    scene.render.resolution_x = 900
    scene.render.resolution_y = 900
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("w")
    scene.world = world
    world.color = (0.05, 0.06, 0.09)

    mins = Vector((1e9, 1e9, 1e9)); maxs = Vector((-1e9, -1e9, -1e9))
    for o in bpy.data.objects:
        if o.type != "MESH":
            continue
        for v in o.bound_box:
            w = o.matrix_world @ Vector(v)
            mins = Vector(map(min, mins, w)); maxs = Vector(map(max, maxs, w))
    center = (mins + maxs) / 2
    size = max((maxs - mins).x, (maxs - mins).y, (maxs - mins).z)

    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = size * 1.25
    cam = bpy.data.objects.new("cam", cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam
    for i, (elev, azim) in enumerate(views):
        d = Vector((math.sin(rad(azim)) * math.cos(rad(elev)), -math.cos(rad(azim)) * math.cos(rad(elev)), math.sin(rad(elev))))
        cam.location = center + d * size * 3
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(PREVIEWS, f"{name}_{i}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


# ----------------------------------------------------------------------------- OPERATIVE

def build_operative():
    armor = material("Armor", (0.78, 0.8, 0.84), 0.3, 0.35)
    suit = material("Suit", (0.1, 0.11, 0.13), 0.2, 0.6)
    accent = material("Accent", (0.9, 0.42, 0.08), 0.2, 0.45)
    visor = material("Visor", (0.05, 0.4, 0.55), 0.0, 0.05, (0.2, 0.85, 1.0), 4)
    neon = material("Neon", (0.05, 0.4, 0.55), 0.0, 0.2, (0.2, 0.85, 1.0), 4)
    metal = material("Metal", (0.22, 0.23, 0.26), 0.85, 0.35)

    # ---- torso (static)
    t = []
    t.append(subsurf(bevel(prim("cube", "pelvis", (0, 0, 0.92), (0.36, 0.24, 0.18), mat=suit), 0.04, 2), 1))
    t.append(subsurf(prim("cube", "abdomen", (0, 0.01, 1.08), (0.32, 0.22, 0.22), mat=suit), 2))
    for i, z in enumerate((1.02, 1.1)):
        for s in (-1, 1):
            t.append(bevel(prim("cube", "abplate", (s * 0.065, -0.115, z), (0.11, 0.03, 0.065), mat=armor), 0.012, 2))
    t.append(bevel(prim("cube", "belt", (0, 0, 0.985), (0.4, 0.28, 0.06), mat=metal), 0.015, 2))
    for s in (-1, 1):
        t.append(bevel(prim("cube", "pouch", (s * 0.17, -0.1, 0.94), (0.08, 0.06, 0.09), mat=accent), 0.012, 2))
    t.append(bevel(prim("cube", "buckle", (0, -0.145, 0.985), (0.08, 0.02, 0.05), mat=neon), 0.006, 1))
    chest = bevel(prim("cube", "chest", (0, 0.0, 1.33), (0.5, 0.3, 0.36), mat=suit), 0.06, 3)
    deform(chest, "TAPER", 0.25, "Z")
    t.append(chest)
    plate = bevel(prim("cube", "chestplate", (0, -0.13, 1.36), (0.48, 0.1, 0.32), (10, 0, 0), mat=armor), 0.04, 3)
    deform(plate, "TAPER", 0.35, "Z")
    t.append(plate)
    t.append(bevel(prim("cube", "pec_l", (0.12, -0.18, 1.38), (0.17, 0.04, 0.13), (12, 0, -8), mat=armor), 0.02, 2))
    t.append(bevel(prim("cube", "pec_r", (-0.12, -0.18, 1.38), (0.17, 0.04, 0.13), (12, 0, 8), mat=armor), 0.02, 2))
    t.append(bevel(prim("cube", "chestlight", (0, -0.19, 1.29), (0.14, 0.012, 0.02), mat=neon), 0.004, 1))
    t.append(bevel(prim("cube", "backplate", (0, 0.13, 1.35), (0.44, 0.06, 0.32), mat=armor), 0.03, 2))
    t.append(subsurf(prim("cyl", "collar", (0, 0, 1.52), (0.24, 0.22, 0.08), mat=suit), 1))
    for s in (-1, 1):
        pad = subsurf(bevel(prim("cube", "shoulder", (s * 0.33, 0, 1.47), (0.2, 0.26, 0.14), (0, s * -18, 0), mat=armor), 0.03, 2), 1)
        t.append(pad)
        t.append(bevel(prim("cube", "padstripe", (s * 0.4, -0.02, 1.47), (0.03, 0.22, 0.05), (0, s * -18, 0), mat=accent), 0.008, 1))
    # backpack
    t.append(bevel(prim("cube", "pack", (0, 0.24, 1.3), (0.4, 0.17, 0.46), mat=metal), 0.035, 3))
    t.append(bevel(prim("cube", "packlid", (0, 0.25, 1.53), (0.36, 0.15, 0.06), mat=armor), 0.02, 2))
    for s in (-1, 1):
        t.append(prim("cyl", "tube", (s * 0.13, 0.33, 1.28), (0.06, 0.06, 0.34), mat=neon))
        t.append(prim("cyl", "tubecap", (s * 0.13, 0.33, 1.46), (0.08, 0.08, 0.04), mat=metal))
        t.append(prim("cyl", "tubecap2", (s * 0.13, 0.33, 1.1), (0.08, 0.08, 0.04), mat=metal))
    for i in range(4):
        t.append(bevel(prim("cube", "fin", (0, 0.33, 1.18 + i * 0.05), (0.12, 0.02, 0.025), mat=suit), 0.005, 1))
    t.append(prim("cyl", "antenna", (-0.14, 0.27, 1.75), (0.012, 0.012, 0.42), mat=metal))
    t.append(prim("sphere", "antennatip", (-0.14, 0.27, 1.96), (0.03, 0.03, 0.03), mat=accent))
    join(t, "Torso", (0, 0, 0.9))

    # ---- head (static)
    h = []
    helmet = subsurf(prim("cube", "helmet", (0, 0.015, 1.73), (0.33, 0.37, 0.35), mat=armor), 2)
    h.append(helmet)
    h.append(subsurf(prim("cube", "crest", (0, 0.04, 1.9), (0.06, 0.3, 0.06), mat=accent), 1))
    h.append(subsurf(bevel(prim("cube", "faceplate", (0, -0.13, 1.66), (0.24, 0.1, 0.14), (-12, 0, 0), mat=armor), 0.02, 2), 1))
    for s in (-1, 1):
        h.append(subsurf(prim("cyl", "ear", (s * 0.17, 0.02, 1.72), (0.11, 0.11, 0.05), (0, 90, 0), mat=metal), 1))
        h.append(prim("cyl", "earlight", (s * 0.198, 0.02, 1.72), (0.05, 0.05, 0.01), (0, 90, 0), mat=neon))
    # curved visor: half a cylinder shell
    bpy.ops.mesh.primitive_cylinder_add(radius=0.178, depth=0.1, vertices=48, location=(0, 0.02, 1.745))
    v = bpy.context.active_object
    v.name = "visor"
    finalize(v)
    import bmesh
    bm = bmesh.new(); bm.from_mesh(v.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().y > -0.075 or abs(f.normal.z) > 0.5], context="FACES")
    bm.to_mesh(v.data); bm.free()
    v.data.materials.append(visor)
    solidify(v, 0.015)
    for p in v.data.polygons:
        p.use_smooth = True
    h.append(v)
    h.append(bevel(prim("cube", "chin", (0, -0.11, 1.62), (0.16, 0.08, 0.06), mat=suit), 0.02, 2))
    h.append(subsurf(prim("cyl", "neck", (0, 0, 1.58), (0.13, 0.13, 0.1), mat=suit), 1))
    join(h, "Head", (0, 0, 1.56))

    # ---- legs (animated around the hip)
    for side, nm in ((1, "Leg_L"), (-1, "Leg_R")):
        x = side * 0.12
        L = []
        L.append(subsurf(bevel(prim("cube", "thigh", (x, 0, 0.7), (0.15, 0.16, 0.36), mat=suit), 0.03, 2), 1))
        L.append(bevel(prim("cube", "thighplate", (x + side * 0.015, -0.075, 0.72), (0.13, 0.04, 0.24), (4, 0, 0), mat=armor), 0.02, 2))
        L.append(bevel(prim("cube", "thighside", (x + side * 0.08, 0, 0.73), (0.03, 0.12, 0.2), mat=accent), 0.01, 1))
        L.append(subsurf(bevel(prim("cube", "knee", (x, -0.085, 0.49), (0.12, 0.08, 0.12), mat=armor), 0.02, 2), 1))
        L.append(subsurf(prim("cube", "calf", (x, 0.01, 0.3), (0.12, 0.14, 0.32), mat=suit), 1))
        shin = bevel(prim("cube", "shin", (x, -0.045, 0.3), (0.13, 0.08, 0.3), mat=armor), 0.025, 3)
        deform(shin, "TAPER", -0.3, "Z")
        L.append(shin)
        L.append(bevel(prim("cube", "boot", (x, -0.04, 0.075), (0.14, 0.26, 0.13), mat=metal), 0.03, 3))
        L.append(bevel(prim("cube", "toecap", (x, -0.15, 0.07), (0.13, 0.06, 0.1), mat=armor), 0.02, 2))
        L.append(bevel(prim("cube", "sole", (x, -0.04, 0.012), (0.15, 0.28, 0.024), mat=suit), 0.008, 1))
        L.append(prim("cube", "bootlight", (x + side * 0.072, -0.02, 0.1), (0.005, 0.12, 0.012), mat=neon))
        join(L, nm, (x, 0, 0.9))

    # ---- arms (static, holding the rifle: right hand on the grip, left hand on the fore-grip)
    a = []
    for s, sh, el, hand in ((-1, (-0.36, 0.0, 1.46), (-0.37, -0.1, 1.2), (-0.22, -0.3, 1.13)),
                            (1, (0.36, 0.0, 1.46), (0.3, -0.33, 1.27), (0.17, -0.66, 1.21))):
        a.append(limb("upper", sh, el, 0.075, 0.065, suit))
        a.append(prim("sphere", "elbow", el, (0.13, 0.13, 0.13), mat=suit))
        a.append(limb("fore", el, hand, 0.065, 0.055, suit))
        mid = (Vector(el) + Vector(hand)) / 2
        d = (Vector(hand) - Vector(el)).normalized()
        b = limb("bracer", Vector(el) + d * 0.04, Vector(hand) - d * 0.05, 0.077, 0.068, armor)
        a.append(b)
        a.append(subsurf(bevel(prim("cube", "glove", hand, (0.09, 0.11, 0.09), mat=metal), 0.02, 2), 1))
        a.append(subsurf(prim("cube", "upperplate", (Vector(sh) + Vector(el)) / 2 + Vector((s * 0.035, 0, 0)), (0.06, 0.12, 0.14), mat=armor), 1))
    join(a, "Arms", (0, 0, 1.3))

    empty("Front", (0, -1, 1))


# ----------------------------------------------------------------------------- SKITTER

def alien_mats(prefix, skin, shell, glow, sac):
    return (material("Skin", skin, 0.1, 0.35), material("Shell", shell, 0.3, 0.25),
            material("Glow", (glow[0] * 0.3, glow[1] * 0.3, glow[2] * 0.3), 0, 0.2, glow, 5),
            material("Sac", (sac[0] * 0.3, sac[1] * 0.3, sac[2] * 0.3), 0, 0.15, sac, 3))


def build_skitter():
    skin, shell, glow, sac = alien_mats("Skitter", (0.24, 0.08, 0.32), (0.09, 0.04, 0.13), (0.4, 1.0, 0.35), (1.0, 0.25, 0.85))
    b = []
    thorax = displace(subsurf(prim("cube", "thorax", (0, 0.02, 0.55), (0.46, 0.66, 0.3), mat=skin), 2), 0.03, 0.12)
    b.append(thorax)
    for i in range(4):
        y = -0.22 + i * 0.16
        seg = subsurf(bevel(prim("cube", f"plate{i}", (0, y, 0.71 - abs(i - 1.5) * 0.02), (0.5 - abs(i - 1.5) * 0.06, 0.2, 0.08), (8, 0, 0), mat=shell), 0.03, 2), 1)
        b.append(seg)
        b.append(prim("cone", f"spike{i}", (0, y + 0.02, 0.8), (0.06, 0.08, 0.14), (-25, 0, 0), mat=shell))
    b.append(displace(subsurf(prim("cube", "head", (0, -0.42, 0.53), (0.32, 0.28, 0.22), mat=shell), 2), 0.015, 0.1))
    b.append(subsurf(prim("cube", "brow", (0, -0.49, 0.62), (0.3, 0.12, 0.06), (15, 0, 0), mat=shell), 1))
    for s in (-1, 1):
        m = prim("cone", "mandible", (s * 0.09, -0.6, 0.44), (0.07, 0.07, 0.26), (110, 0, s * 25), mat=shell)
        deform(m, "BEND", s * 40, "Z")
        b.append(m)
        for k, (ex, ez, r) in enumerate(((0.07, 0.6, 0.06), (0.12, 0.56, 0.045), (0.035, 0.66, 0.04))):
            b.append(prim("sphere", "eye", (s * ex, -0.555, ez), (r, r, r), mat=glow))
    abdomen = displace(subsurf(prim("cube", "abdomen", (0, 0.4, 0.62), (0.42, 0.42, 0.36), (15, 0, 0), mat=sac), 2), 0.02, 0.1)
    b.append(abdomen)
    for k in range(3):
        b.append(subsurf(prim("cube", f"abshell{k}", (0, 0.32 + k * 0.1, 0.8 - k * 0.04), (0.36 - k * 0.05, 0.12, 0.05), (15 + k * 15, 0, 0), mat=shell), 1))
    join(b, "Body", (0, 0, 0.55))

    for i, (s, y) in enumerate([(s, y) for s in (-1, 1) for y in (-0.22, 0.0, 0.22)]):
        hip = Vector((s * 0.2, y, 0.55))
        spread = (y / 0.22) * 0.25
        knee = Vector((s * 0.55, y + spread * 0.6, 0.86))
        foot = Vector((s * 0.82, y + spread, 0.0))
        L = [limb("coxa", hip, knee, 0.055, 0.042, shell),
             prim("sphere", "joint", knee, (0.085, 0.085, 0.085), mat=shell),
             limb("tibia", knee, foot + Vector((0, 0, 0.06)), 0.042, 0.02, shell),
             prim("cone", "claw", foot + Vector((0, 0, 0.03)), (0.035, 0.035, 0.08), (180, 0, 0), mat=skin)]
        join(L, f"Leg_{i}", tuple(hip))
    empty("Front", (0, -1, 0.55))


# ----------------------------------------------------------------------------- SPITTER

def build_spitter():
    skin, shell, glow, sac = alien_mats("Spitter", (0.13, 0.27, 0.24), (0.05, 0.11, 0.11), (1.0, 0.9, 0.2), (0.55, 1.0, 0.15))
    b = []
    torso = displace(subsurf(prim("cube", "torso", (0, 0, 1.18), (0.38, 0.32, 0.66), mat=skin), 2), 0.03, 0.15)
    deform(torso, "TAPER", -0.35, "Z")
    b.append(torso)
    for k in range(5):
        z = 0.98 + k * 0.11
        r = prim("torus", f"rib{k}", (0, -0.02, z), (0.4 - abs(k - 2) * 0.04, 0.34 - abs(k - 2) * 0.03, 0.45), mat=shell, minor=0.07)
        import bmesh
        bm = bmesh.new(); bm.from_mesh(r.data)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().y > -0.02], context="FACES")
        bm.to_mesh(r.data); bm.free()
        b.append(subsurf(r, 1))
    b.append(subsurf(prim("cube", "pelvis", (0, 0, 0.88), (0.34, 0.3, 0.16), mat=shell), 2))
    b.append(displace(subsurf(prim("cube", "spine", (0, 0.14, 1.2), (0.12, 0.1, 0.62), mat=shell), 2), 0.02, 0.06))
    b.append(subsurf(prim("cyl", "neck", (0, -0.06, 1.55), (0.14, 0.14, 0.16), (15, 0, 0), mat=skin), 1))
    head = displace(subsurf(prim("cube", "skull", (0, -0.13, 1.72), (0.26, 0.46, 0.24), (-10, 0, 0), mat=shell), 2), 0.012, 0.08)
    deform(head, "TAPER", -0.4, "Y")
    b.append(head)
    for k in range(4):
        b.append(prim("cone", f"crest{k}", (0, 0.04 + k * 0.07, 1.86 - k * 0.03), (0.05, 0.09, 0.18), (-40 - k * 10, 0, 0), mat=shell))
    for k, (ex, ez, r) in enumerate(((0.08, 1.77, 0.05), (-0.08, 1.77, 0.05), (0.0, 1.82, 0.04))):
        b.append(prim("sphere", "eye", (ex, -0.33, ez), (r, r, r), mat=glow))
    b.append(prim("cube", "mouthglow", (0, -0.34, 1.66), (0.12, 0.02, 0.03), mat=sac))
    for s in (-1, 1):
        b.append(limb("tendril", (s * 0.18, -0.05, 1.42), (s * 0.34, -0.25, 1.1), 0.04, 0.015, skin))
    join(b, "Body", (0, 0, 0.0))

    sacs = [displace(subsurf(prim("cube", "sac", (0, 0.3, 1.38), (0.46, 0.44, 0.52), (20, 0, 0), mat=sac), 2), 0.04, 0.09)]
    for k in range(5):
        a = k * 72
        sacs.append(limb("vein", (math.sin(rad(a)) * 0.12, 0.18, 1.55), (math.sin(rad(a)) * 0.22, 0.42, 1.2), 0.02, 0.012, skin))
    join(sacs, "Sac", (0, 0.3, 1.38))

    for i in range(3):
        a = rad(i * 120 + 60)
        dirv = Vector((math.sin(a), -math.cos(a), 0))
        hip = Vector((0, 0, 0.88)) + dirv * 0.14
        knee = hip + dirv * 0.45 + Vector((0, 0, 0.12))
        foot = hip + dirv * 0.62 + Vector((0, 0, -0.84))
        L = [limb("thigh", hip, knee, 0.07, 0.05, shell), prim("sphere", "knee", knee, (0.1, 0.1, 0.1), mat=shell),
             limb("shin", knee, foot + Vector((0, 0, 0.05)), 0.05, 0.022, skin),
             prim("cone", "claw", foot + Vector((0, 0, 0.03)), (0.05, 0.05, 0.09), (180, 0, 0), mat=shell)]
        join(L, f"Leg_{i}", tuple(hip))
    empty("Mouth", (0, -0.45, 1.62))
    empty("Front", (0, -1, 1.2))


# ----------------------------------------------------------------------------- BRUTE

def build_brute():
    skin, shell, glow, core = alien_mats("Brute", (0.34, 0.1, 0.07), (0.18, 0.18, 0.2), (1.0, 0.35, 0.1), (1.0, 0.45, 0.1))
    shell.metallic = 0.6
    b = []
    torso = displace(subsurf(prim("cube", "torso", (0, 0.05, 1.5), (1.25, 0.95, 1.05), (12, 0, 0), mat=skin), 2), 0.06, 0.25)
    b.append(torso)
    b.append(displace(subsurf(prim("cube", "belly", (0, -0.15, 1.15), (0.85, 0.6, 0.5), mat=skin), 2), 0.03, 0.15))
    for k in range(4):
        pl = subsurf(bevel(prim("cube", f"back{k}", (0, 0.32 + k * 0.06, 2.05 - k * 0.22), (1.05 - k * 0.12, 0.35, 0.1), (60 + k * 8, 0, 0), mat=shell), 0.04, 2), 1)
        b.append(pl)
        for s in (-1, 1):
            b.append(prim("cone", "spike", (s * (0.32 - k * 0.04), 0.5 + k * 0.08, 2.12 - k * 0.22), (0.1, 0.1, 0.3), (-35, 0, s * 15), mat=shell))
    for s in (-1, 1):
        b.append(subsurf(prim("cube", "pauldron", (s * 0.7, 0.05, 2.02), (0.55, 0.68, 0.3), (0, s * 22, 0), mat=shell), 2))
        b.append(subsurf(prim("cube", "pauldron2", (s * 0.78, 0.05, 1.86), (0.45, 0.6, 0.22), (0, s * 35, 0), mat=shell), 2))
        b.append(prim("cone", "horn", (s * 0.85, 0.05, 2.25), (0.14, 0.14, 0.35), (0, s * 30, 0), mat=shell))
    b.append(displace(subsurf(prim("cube", "head", (0, -0.55, 1.9), (0.5, 0.45, 0.4), mat=shell), 2), 0.02, 0.1))
    b.append(subsurf(prim("cube", "jaw", (0, -0.68, 1.72), (0.42, 0.3, 0.16), (-15, 0, 0), mat=skin), 1))
    for k in range(4):
        x = (k - 1.5) * 0.11
        b.append(prim("sphere", "eye", (x, -0.78, 1.95 + (k % 2) * 0.04), (0.07, 0.07, 0.07), mat=glow))
    for s in (-1, 1):
        b.append(prim("cone", "tusk", (s * 0.17, -0.82, 1.68), (0.06, 0.06, 0.2), (70, 0, 0), mat=material("Bone", (0.85, 0.8, 0.7), 0, 0.4)))
    b.append(subsurf(prim("cube", "pelvis", (0, 0.05, 1.0), (0.8, 0.55, 0.3), mat=shell), 2))
    join(b, "Body", (0, 0, 0.0))

    c = [displace(prim("ico", "core", (0, -0.52, 1.42), (0.36, 0.25, 0.36), mat=core, sub=3), 0.04, 0.08)]
    for k in range(6):
        a = rad(k * 60)
        c.append(prim("cone", "coreclaw", (math.cos(a) * 0.24, -0.5, 1.42 + math.sin(a) * 0.24), (0.06, 0.06, 0.16), (90, 0, 0), mat=shell))
    join(c, "Core", (0, -0.52, 1.42))

    for side, nm in ((1, "Leg_L"), (-1, "Leg_R")):
        hip = Vector((side * 0.42, 0.05, 1.0))
        knee = Vector((side * 0.52, -0.12, 0.52))
        ankle = Vector((side * 0.5, 0.08, 0.14))
        L = [subsurf(limb("thigh", hip, knee, 0.26, 0.2, skin, 16), 1),
             subsurf(bevel(prim("cube", "kneeplate", knee + Vector((0, -0.12, 0.02)), (0.3, 0.16, 0.3), mat=shell), 0.04, 2), 1),
             subsurf(limb("shin", knee, ankle, 0.19, 0.15, shell, 16), 1),
             subsurf(bevel(prim("cube", "foot", (side * 0.5, -0.08, 0.08), (0.34, 0.5, 0.16), mat=skin), 0.04, 2), 1)]
        for k in (-1, 0, 1):
            L.append(prim("cone", "toeclaw", (side * 0.5 + k * 0.1, -0.36, 0.06), (0.07, 0.07, 0.16), (100, 0, 0), mat=shell))
        join(L, nm, tuple(hip))

    for side, nm in ((1, "Arm_L"), (-1, "Arm_R")):
        sh = Vector((side * 0.92, 0.0, 1.85))
        el = Vector((side * 1.08, -0.25, 1.25))
        wr = Vector((side * 1.0, -0.45, 0.62))
        A = [subsurf(limb("upper", sh, el, 0.22, 0.2, skin, 16), 1),
             prim("sphere", "elbow", el, (0.38, 0.38, 0.38), mat=skin),
             subsurf(limb("fore", el, wr, 0.24, 0.3, shell, 16), 1)]
        for k in range(3):
            A.append(subsurf(bevel(prim("cube", "foreplate", el + (wr - el) * (0.25 + k * 0.25) + Vector((side * 0.12, -0.12, 0)), (0.22, 0.3, 0.14), (0, side * 20, 0), mat=shell), 0.03, 2), 1))
        A.append(subsurf(bevel(prim("cube", "fist", wr + Vector((0, -0.05, -0.18)), (0.46, 0.48, 0.44), mat=shell), 0.05, 2), 2))
        for k in (-1, 0, 1):
            A.append(prim("cone", "knuckle", wr + Vector((k * 0.13, -0.3, -0.18)), (0.08, 0.08, 0.16), (90, 0, 0), mat=shell))
        join(A, nm, tuple(sh))
    empty("Front", (0, -1, 1.5))


# ----------------------------------------------------------------------------- main

BUILDERS = {"Operative": build_operative, "Skitter": build_skitter, "Spitter": build_spitter, "Brute": build_brute}

if __name__ == "__main__":
    for name, fn in BUILDERS.items():
        if ONLY and name != ONLY:
            continue
        reset()
        fn()
        export(name)
        preview(name)
    print("[zcom] done")
