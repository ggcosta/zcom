"""
ZCOM energy weapons (Art/Reference/Codex/prop_*.png) -> Assets/ZCOM/Resources/Models/Weapon_<Kind>.fbx

    blender -b --factory-startup -P Tools/Blender/zcom_weapons.py -- <models_dir> <previews_dir>

Conventions (Blender): the right-hand palm on the pistol grip is the origin, forward is -Y, up is +Z.
The left hand cups the handguard at (0, -0.26, 0) (matches the Marshal rig's Weapon bone).
Each FBX contains:
  Body          - all hard-surface parts (material slots: WpnSteel, WpnGraphite, WpnRubber, WpnAmber, WpnCeramic, WpnPad)
  Glow          - emissive parts tinted by the weapon colour in Unity (material WpnGlow)
  Spinner_*     - emissive coils/rings that spin around their local forward axis while firing
  Muzzle, Vent, Cell, Front - empties used by the Gun component
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import zcom_marine as zm   # material(), harden(), mesh_from_bm(), apply_mods(), render helpers

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
MODELS = os.path.abspath(ARGS[0]) if len(ARGS) > 0 else os.path.abspath("Assets/ZCOM/Resources/Models")
PREVIEWS = os.path.abspath(ARGS[1]) if len(ARGS) > 1 else os.path.abspath("Tools/Blender/previews")
zm.PREVIEWS = PREVIEWS
V = Vector
rad = math.radians

BODY, GLOW, SPIN = [], [], []


def mats():
    m = zm.material
    return dict(
        steel=m("WpnSteel", (0.2, 0.205, 0.215), 0.7, 0.4, "armor", wear=1.0),
        graphite=m("WpnGraphite", (0.035, 0.037, 0.042), 0.4, 0.45, "armor", wear=0.6),
        rubber=m("WpnRubber", (0.018, 0.018, 0.02), 0.0, 0.75, "armor", wear=0.2),
        amber=m("WpnAmber", (0.78, 0.36, 0.045), 0.15, 0.5, "armor", wear=0.6),
        ceramic=m("WpnCeramic", (0.62, 0.62, 0.6), 0.05, 0.42, "armor", wear=0.8),
        pad=m("WpnPad", (0.06, 0.05, 0.04), 0.0, 0.85, "fabric", bump=0.3),
        glow=m("WpnGlow", (0.3, 0.9, 1.0), 0.0, 0.2, "emit", emit=(0.3, 0.9, 1.0), strength=8.0),
    )


def _finish(o, mat, kind, bevel, segs=2):
    o.data.materials.append(mat)
    zm.harden(o, bevel, segs, 30)
    zm.apply_mods(o)
    (GLOW if kind == "glow" else SPIN[-1][1] if kind == "spin" else BODY).append(o)
    return o


def box(name, c, size, mat, kind="body", rot=(0, 0, 0), bevel=0.004, taper=None, chamfer=None):
    """Bevelled box. taper=(sx, sz) scales the -Y (front) end; chamfer cuts the top-front edge."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        p = V((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        if taper and v.co.y < 0:
            p.x *= taper[0]; p.z *= taper[1]
        if chamfer and v.co.y < 0 and v.co.z > 0:
            p.y += chamfer
        v.co = p
    o = zm.mesh_from_bm(name, bm)
    o.rotation_euler = Euler([rad(a) for a in rot])
    o.location = c
    return _finish(o, mat, kind, bevel)


def prism(name, c, profile, depth, mat, kind="body", axis="X", bevel=0.004):
    """Extrude a 2D profile [(y, z), ...] (side view) by depth along X - the classic hard-surface move."""
    bm = bmesh.new()
    vs0 = [bm.verts.new(V((-depth / 2, y, z))) for y, z in profile]
    vs1 = [bm.verts.new(V((depth / 2, y, z))) for y, z in profile]
    n = len(profile)
    bm.faces.new(vs0[::-1]); bm.faces.new(vs1)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((vs0[i], vs0[j], vs1[j], vs1[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = zm.mesh_from_bm(name, bm)
    o.location = c
    return _finish(o, mat, kind, bevel)


def tube(name, c, r, length, mat, kind="body", verts=8, r2=None, bevel=0.003, rot=(90, 0, 0)):
    """Cylinder along Y (default)."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r, radius2=r if r2 is None else r2, depth=length)
    o = zm.mesh_from_bm(name, bm)
    o.rotation_euler = Euler([rad(a) for a in rot])
    o.location = c
    return _finish(o, mat, kind, bevel)


def ring(name, c, r, width, thick, mat, kind="glow", verts=16):
    bm = bmesh.new()
    outer = bmesh.ops.create_circle(bm, segments=verts, radius=r + thick / 2)["verts"]
    inner = bmesh.ops.create_circle(bm, segments=verts, radius=r - thick / 2)["verts"]
    for i in range(verts):
        j = (i + 1) % verts
        bm.faces.new((outer[i], outer[j], inner[j], inner[i]))
    f = bmesh.ops.extrude_face_region(bm, geom=bm.faces[:])
    for v in [g for g in f["geom"] if isinstance(g, bmesh.types.BMVert)]:
        v.co.z += width
    for v in bm.verts:
        v.co.z -= width / 2
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = zm.mesh_from_bm(name, bm)
    o.rotation_euler = (rad(90), 0, 0)
    o.location = c
    return _finish(o, mat, kind, 0.002)


def helix(name, c, radius, length, turns, wire, mat, phase=0.0):
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = wire
    cu.bevel_resolution = 2
    sp = cu.splines.new("POLY")
    n = int(turns * 16)
    sp.points.add(n)
    for i in range(n + 1):
        t = i / n
        a = phase + 2 * math.pi * turns * t
        sp.points[i].co = (radius * math.cos(a), -length / 2 + length * t, radius * math.sin(a), 1)
    o = bpy.data.objects.new(name, cu)
    bpy.context.scene.collection.objects.link(o)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
    bpy.data.objects.remove(o)
    mo = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(mo)
    mo.location = c
    me.materials.append(mat)
    SPIN[-1][1].append(mo)
    return mo


def spinner(name, center):
    SPIN.append((name, [], V(center)))


def empty(name, loc):
    e = bpy.data.objects.new(name, None)
    e.location = loc
    bpy.context.scene.collection.objects.link(e)
    return e


def join(objs, name, origin=(0, 0, 0)):
    for o in objs:
        o.data.transform(o.matrix_world)
        o.matrix_world = Matrix.Identity(4)
    with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs, selected_objects=objs, object=objs[0]):
        bpy.ops.object.join()
    o = objs[0]
    o.name = o.data.name = name
    o.data.transform(Matrix.Translation(-V(origin)))
    o.location = origin
    return o


# =============================================================================== ION CARBINE

def ion(M):
    st, gr, rb, am, gl = M["steel"], M["graphite"], M["rubber"], M["amber"], M["glow"]
    # receiver: long angular body (side profile), steel side plates over a graphite core
    prism("Receiver", (0, 0, 0), [(0.3, 0.05), (0.3, 0.16), (0.12, 0.17), (0.06, 0.155), (-0.12, 0.155), (-0.18, 0.13), (-0.3, 0.13), (-0.3, 0.04), (-0.1, 0.03), (0.0, 0.04)], 0.056, gr, bevel=0.005)
    for x in (-1, 1):
        prism(f"SidePlate{x}", (0.031 * x, 0, 0), [(0.27, 0.07), (0.27, 0.15), (0.12, 0.155), (0.07, 0.14), (-0.05, 0.14), (-0.13, 0.12), (-0.13, 0.075), (0.02, 0.065)], 0.008, st, bevel=0.003)
        prism(f"FrontPlate{x}", (0.031 * x, 0, 0), [(-0.15, 0.125), (-0.29, 0.125), (-0.29, 0.07), (-0.16, 0.065)], 0.008, st, bevel=0.003)
        box(f"SideSlit{x}", (0.036 * x, -0.07, 0.115), (0.003, 0.08, 0.01), gl, "glow", bevel=0.001)
        box(f"Amber{x}", (0.036 * x, 0.2, 0.12), (0.003, 0.05, 0.012), am, rot=(35, 0, 0), bevel=0.001)
        box(f"Amber2{x}", (0.036 * x, 0.225, 0.12), (0.003, 0.05, 0.012), am, rot=(35, 0, 0), bevel=0.001)
        box(f"AmberFront{x}", (0.036 * x, -0.17, 0.085), (0.003, 0.05, 0.014), am, bevel=0.001)
    # stock + butt pad
    prism("Stock", (0, 0, 0), [(0.3, 0.16), (0.36, 0.165), (0.37, -0.04), (0.3, -0.05), (0.2, -0.02), (0.2, 0.04)], 0.05, gr, bevel=0.005)
    box("ButtPad", (0, 0.375, 0.06), (0.054, 0.025, 0.22), rb, bevel=0.006)
    # pistol grip (raked) + trigger guard frame
    prism("Grip", (0, 0, 0), [(0.0, 0.045), (0.045, 0.045), (0.06, -0.08), (0.04, -0.1), (0.0, -0.095), (-0.012, -0.02)], 0.036, rb, bevel=0.006)
    prism("GuardBottom", (0, 0, 0), [(-0.1, 0.01), (0.12, 0.0), (0.13, -0.018), (-0.08, -0.008)], 0.03, gr, bevel=0.004)
    box("Trigger", (0, -0.035, 0.015), (0.008, 0.012, 0.03), st, rot=(20, 0, 0), bevel=0.002)
    # energy cell behind the grip (bullpup)
    box("CellFrameTop", (0, 0.1, 0.06), (0.07, 0.07, 0.02), st, bevel=0.004)
    box("CellFrameBot", (0, 0.1, -0.045), (0.07, 0.07, 0.02), st, bevel=0.004)
    tube("CellGlass", (0, 0.1, 0.008), 0.028, 0.09, gl, "glow", verts=10, rot=(0, 0, 0))
    for x in (-1, 1):
        box(f"CellBar{x}", (0.03 * x, 0.1, 0.008), (0.008, 0.012, 0.1), st, bevel=0.002)
    # handguard (left hand) under the barrel
    prism("Handguard", (0, 0, 0), [(-0.14, 0.04), (-0.34, 0.04), (-0.36, 0.015), (-0.33, -0.005), (-0.16, -0.005)], 0.05, gr, bevel=0.005)
    for i in range(5):
        box(f"Grooves{i}", (0, -0.19 - i * 0.03, -0.008), (0.046, 0.01, 0.008), rb, bevel=0.002)
    # barrel shroud with vented rings
    tube("Shroud", (0, -0.45, 0.095), 0.04, 0.26, gr, verts=8, bevel=0.004)
    for i in range(4):
        box(f"Vent{i}", (0, -0.36 - i * 0.05, 0.095), (0.088, 0.018, 0.03), M["graphite"], bevel=0.002)
    spinner("Spinner_Rings", (0, -0.43, 0.095))
    for i in range(3):
        ring(f"Coil{i}", (0, -0.37 - i * 0.065, 0.095), 0.043, 0.012, 0.008, gl, "spin")
    tube("Muzzle", (0, -0.6, 0.095), 0.036, 0.05, st, verts=8, bevel=0.004)
    box("MuzzleBrake", (0, -0.632, 0.095), (0.07, 0.03, 0.06), gr, bevel=0.006)
    # top rail + optic
    box("Rail", (0, -0.02, 0.165), (0.03, 0.26, 0.016), gr, bevel=0.002)
    for i in range(9):
        box(f"RailTooth{i}", (0, -0.13 + i * 0.027, 0.176), (0.032, 0.012, 0.008), gr, bevel=0.001)
    box("OpticMount", (0, 0.02, 0.185), (0.03, 0.05, 0.03), st, bevel=0.004)
    box("Optic", (0, 0.01, 0.225), (0.05, 0.12, 0.05), st, taper=(0.9, 0.9), bevel=0.008)
    box("OpticLens", (0, -0.052, 0.225), (0.034, 0.006, 0.032), gl, "glow", bevel=0.002)
    box("OpticAmber", (0.026, 0.04, 0.235), (0.003, 0.03, 0.01), am, rot=(30, 0, 0), bevel=0.001)
    return dict(muzzle=(0, -0.66, 0.095), vent=(0, -0.2, 0.15), cell=(0, 0.1, 0.0))


# =============================================================================== NOVA SCATTER

def nova(M):
    st, gr, rb, am, gl = M["steel"], M["graphite"], M["rubber"], M["amber"], M["glow"]
    prism("Body", (0, 0, 0), [(0.15, 0.04), (0.15, 0.17), (-0.05, 0.19), (-0.32, 0.19), (-0.36, 0.17), (-0.36, 0.04), (-0.1, 0.035)], 0.08, gr, bevel=0.006)
    for x in (-1, 1):
        prism(f"Armor{x}", (0.043 * x, 0, 0), [(-0.25, 0.18), (-0.47, 0.18), (-0.5, 0.15), (-0.5, 0.05), (-0.47, 0.03), (-0.3, 0.03), (-0.25, 0.06)], 0.012, st, bevel=0.004)
        prism(f"RearArmor{x}", (0.043 * x, 0, 0), [(0.12, 0.08), (0.12, 0.165), (0.02, 0.17), (0.0, 0.11), (0.04, 0.08)], 0.01, st, bevel=0.003)
        for i in range(2):
            box(f"Hazard{x}{i}", (0.05 * x, -0.36 - i * 0.03, 0.105), (0.003, 0.016, 0.07), am, rot=(25, 0, 0), bevel=0.001)
        box(f"Cyan{x}", (0.05 * x, 0.06, 0.12), (0.003, 0.05, 0.014), gl, "glow", bevel=0.001)
    # hexagonal muzzle housing with recessed plasma core
    tube("MuzzleHousing", (0, -0.47, 0.11), 0.085, 0.09, st, verts=6, bevel=0.006, rot=(90, 0, 30))
    tube("MuzzleInner", (0, -0.515, 0.11), 0.06, 0.012, gr, verts=6, bevel=0.003, rot=(90, 0, 30))
    tube("PlasmaCore", (0, -0.515, 0.11), 0.05, 0.016, gl, "glow", verts=12, bevel=0.002)
    # rotating drum chamber with glowing slots
    tube("DrumFrontRim", (0, -0.13, 0.11), 0.078, 0.016, st, verts=8, bevel=0.003)
    tube("DrumRearRim", (0, -0.27, 0.11), 0.078, 0.016, st, verts=8, bevel=0.003)
    spinner("Spinner_Drum", (0, -0.2, 0.11))
    tube("Drum", (0, -0.2, 0.11), 0.072, 0.12, gr, "spin", verts=8, bevel=0.004)
    for k in range(8):
        a = 2 * math.pi * (k + 0.5) / 8
        box(f"DrumSlot{k}", (math.cos(a) * 0.067, -0.2, 0.11 + math.sin(a) * 0.067), (0.008, 0.07, 0.016), gl, "spin", rot=(0, -math.degrees(a), 0), bevel=0.001)
    # pump handguard
    tube("PumpCore", (0, -0.26, -0.005), 0.032, 0.2, rb, verts=10, bevel=0.003)
    for i in range(6):
        ring(f"PumpRib{i}", (0, -0.19 - i * 0.028, -0.005), 0.034, 0.012, 0.006, rb, "body")
    box("PumpClampF", (0, -0.36, 0.02), (0.06, 0.02, 0.07), st, bevel=0.004)
    box("PumpClampR", (0, -0.165, 0.02), (0.06, 0.02, 0.07), st, bevel=0.004)
    # grip, trigger, stock, carry handle
    prism("Grip", (0, 0, 0), [(0.0, 0.045), (0.05, 0.045), (0.07, -0.08), (0.05, -0.105), (0.0, -0.1), (-0.012, -0.02)], 0.04, rb, bevel=0.006)
    prism("Guard", (0, 0, 0), [(-0.07, 0.04), (-0.07, 0.0), (0.02, -0.01), (0.03, 0.04)], 0.012, gr, bevel=0.002)
    prism("Stock", (0, 0, 0), [(0.15, 0.16), (0.33, 0.17), (0.37, 0.15), (0.37, -0.03), (0.3, -0.04), (0.22, 0.05), (0.15, 0.06)], 0.05, gr, bevel=0.006)
    box("StockPlate", (0, 0.29, 0.11), (0.056, 0.06, 0.06), st, bevel=0.004)
    box("StockAmber", (0.029, 0.29, 0.11), (0.003, 0.04, 0.012), am, rot=(35, 0, 0), bevel=0.001)
    box("ButtPad", (0, 0.375, 0.07), (0.058, 0.02, 0.21), rb, bevel=0.006)
    box("HandleL", (0, -0.13, 0.22), (0.016, 0.016, 0.05), gr, bevel=0.003)
    box("HandleR", (0, 0.03, 0.22), (0.016, 0.016, 0.05), gr, bevel=0.003)
    box("HandleTop", (0, -0.05, 0.245), (0.018, 0.18, 0.016), gr, bevel=0.003)
    box("TopCyan", (0, -0.29, 0.193), (0.024, 0.07, 0.004), gl, "glow", bevel=0.001)
    return dict(muzzle=(0, -0.53, 0.11), vent=(0, -0.2, 0.19), cell=(0, -0.2, 0.11))


# =============================================================================== HELIX BEAM

def helix_beam(M):
    st, gr, rb, am, gl, ce, pad = M["steel"], M["graphite"], M["rubber"], M["amber"], M["glow"], M["ceramic"], M["pad"]
    # receiver: ceramic shell over graphite frame
    prism("Frame", (0, 0, 0), [(0.18, 0.06), (0.18, 0.13), (-0.02, 0.15), (-0.2, 0.15), (-0.24, 0.12), (-0.24, 0.03), (-0.05, 0.03)], 0.05, gr, bevel=0.005)
    for x in (-1, 1):
        prism(f"Shell{x}", (0.03 * x, 0, 0), [(0.16, 0.07), (0.16, 0.14), (0.0, 0.155), (-0.12, 0.155), (-0.2, 0.13), (-0.2, 0.08), (-0.05, 0.065)], 0.012, ce, bevel=0.004)
        box(f"Amber{x}", (0.037 * x, 0.05, 0.12), (0.003, 0.05, 0.014), am, rot=(35, 0, 0), bevel=0.001)
        box(f"Amber2{x}", (0.037 * x, 0.08, 0.12), (0.003, 0.05, 0.014), am, rot=(35, 0, 0), bevel=0.001)
        box(f"Cyan{x}", (0.037 * x, -0.1, 0.13), (0.003, 0.04, 0.01), gl, "glow", bevel=0.001)
    # capacitor block with 3 cells (under the receiver, ahead of the grip)
    box("Capacitor", (0, -0.12, -0.01), (0.06, 0.12, 0.08), gr, bevel=0.006)
    for i in range(3):
        tube(f"CapCell{i}", (0.03, -0.155 + i * 0.03, -0.01), 0.011, 0.06, gl, "glow", verts=8, rot=(0, 0, 0))
        tube(f"CapCellR{i}", (-0.03, -0.155 + i * 0.03, -0.01), 0.011, 0.06, gl, "glow", verts=8, rot=(0, 0, 0))
    for i in range(3):
        box(f"CapVent{i}", (0.031, -0.075 + 0.0, 0.012 - i * 0.018), (0.003, 0.03, 0.008), rb, bevel=0.001)
    # twin emitter rails with spinning helix coils
    for k, z in enumerate((0.125, 0.05)):
        tube(f"Rail{k}", (0, -0.5, z), 0.012, 0.56, st, verts=8)
        for i, y in enumerate((-0.27, -0.5, -0.73)):
            box(f"Clamp{k}{i}", (0, y, z), (0.07, 0.025, 0.05), gr, bevel=0.004)
        spinner(f"Spinner_Helix{k}", (0, -0.5, z))
        helix(f"Helix{k}a", (0, -0.5, z), 0.024, 0.48, 3.5, 0.0045, gl, 0.0)
        helix(f"Helix{k}b", (0, -0.5, z), 0.024, 0.48, 3.5, 0.0045, gl, math.pi)
    box("RailSpine", (0, -0.5, 0.0875), (0.02, 0.5, 0.02), gr, bevel=0.003)
    # split-prong emitter
    for z, s in ((0.15, 1), (0.025, -1)):
        prism(f"Prong{s}", (0, 0, 0), [(-0.76, z - 0.02 * s), (-0.95, z + 0.0 * s), (-0.95, z + 0.012 * s), (-0.8, z + 0.035 * s), (-0.76, z + 0.03 * s)], 0.04, ce, bevel=0.003)
        box(f"ProngGlow{s}", (0, -0.9, z + 0.008 * s), (0.044, 0.04, 0.006), gl, "glow", bevel=0.001)
    tube("EmitterCore", (0, -0.8, 0.0875), 0.022, 0.06, st, verts=8)
    tube("EmitterTip", (0, -0.84, 0.0875), 0.012, 0.03, gl, "glow", verts=8)
    # grip, guard, skeleton stock with cheek pad
    prism("Grip", (0, 0, 0), [(0.0, 0.04), (0.045, 0.04), (0.06, -0.08), (0.04, -0.1), (0.0, -0.095), (-0.012, -0.02)], 0.036, rb, bevel=0.006)
    prism("Guard", (0, 0, 0), [(-0.06, 0.035), (-0.06, -0.005), (0.02, -0.012), (0.03, 0.035)], 0.012, gr, bevel=0.002)
    prism("StockTop", (0, 0, 0), [(0.18, 0.13), (0.46, 0.145), (0.48, 0.12), (0.18, 0.1)], 0.04, ce, bevel=0.004)
    prism("StockStrut", (0, 0, 0), [(0.18, 0.06), (0.22, 0.06), (0.46, -0.04), (0.42, -0.05)], 0.03, gr, bevel=0.004)
    box("ButtPad", (0, 0.49, 0.05), (0.05, 0.025, 0.21), rb, bevel=0.006)
    box("CheekPad", (0, 0.36, 0.16), (0.05, 0.13, 0.03), pad, bevel=0.01)
    # long scope
    box("ScopeMount", (0, -0.02, 0.17), (0.03, 0.06, 0.03), gr, bevel=0.003)
    tube("Scope", (0, -0.02, 0.215), 0.026, 0.24, ce, verts=12, bevel=0.003)
    tube("ScopeBell", (0, -0.14, 0.215), 0.034, 0.04, gr, verts=12, bevel=0.003)
    tube("ScopeLens", (0, -0.161, 0.215), 0.026, 0.004, gl, "glow", verts=12, bevel=0.001)
    tube("ScopeTurret", (0, -0.01, 0.245), 0.014, 0.025, gr, verts=10, rot=(0, 0, 0))
    return dict(muzzle=(0, -0.96, 0.0875), vent=(0, -0.12, 0.05), cell=(0, -0.12, -0.03))


# =============================================================================== export

def build(kind, fn):
    zm.reset()
    BODY.clear(); GLOW.clear(); SPIN.clear()
    M = mats()
    pts = fn(M)
    body = join(BODY, "Body")
    objs = [body]
    if GLOW:
        objs.append(join(GLOW, "Glow"))
    for name, parts, center in SPIN:
        objs.append(join(parts, name, center))
    for n, p in (("Muzzle", pts["muzzle"]), ("Vent", pts["vent"]), ("Cell", pts["cell"]), ("Front", (0, -1, 0))):
        objs.append(empty(n, p))
    for o in bpy.data.objects:
        o.select_set(o in objs)
    path = os.path.join(MODELS, f"Weapon_{kind}.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=True, bake_anim=False)
    tris = sum(len(p.vertices) - 2 for o in objs if o.type == "MESH" for p in o.data.polygons)
    print(f"[zcom] exported {path} ({tris} tris)")
    floor = zm.setup_render(48, (1200, 700))
    floor.location.z = -0.25
    zm.render_views(f"Weapon_{kind}", {"side": (0, 90), "q34": (20, 55)}, target=V((0, -0.2, 0.08)), dist=2.4, lens=55)


if __name__ == "__main__":
    for kind, fn in (("IonCarbine", ion), ("NovaScatter", nova), ("HelixBeam", helix_beam)):
        build(kind, fn)
    print("[zcom] weapons done")
