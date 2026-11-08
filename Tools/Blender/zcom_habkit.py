"""
Modular colony habitat kit (Art/Reference/Codex/env_hab.png) -> Assets/ZCOM/Resources/Models/HabKit.fbx

    blender -b --factory-startup -P Tools/Blender/zcom_habkit.py -- <models_dir> <previews_dir>

One FBX, one object per kit piece (origin at the bottom centre; the outward face looks down -Y, i.e. +Z in Unity):
  WallPanel, WallWindow (2 m wide, 2.7 m tall), Pillar (corner), Rib (between modules), DoorFrame (3 m, 2.4 m opening),
  DoorSteps, RoofTile (2 x 2 m), RoofVent, RoofPipes, RoofDish, RoofBeacon, Lamp, Grate.
Materials (mapped in Unity): HabWall (light insulated panels), HabFrame (dark purple-grey structure), HabRoof,
HabTrim (orange), HabGlass (cyan emissive), HabAmber (amber emissive), HabRed (red emissive), HabGrate.
UVs are cube-projected at 2 m so the tiling metal textures line up across pieces.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler

ARGS = sys.argv[sys.argv.index("--") + 1:]
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import zcom_marine as zm

MODELS, PREVIEWS = zm.MODELS, zm.PREVIEWS
V = Vector
rad = math.radians
M = {}


def mat(name, color, metal=0.3, rough=0.5, emit=None, strength=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = strength
    return m


def mats():
    M.update(
        wall=mat("HabWall", (0.55, 0.56, 0.6), 0.25, 0.5),
        frame=mat("HabFrame", (0.1, 0.09, 0.13), 0.5, 0.45),
        roof=mat("HabRoof", (0.07, 0.07, 0.08), 0.4, 0.6),
        trim=mat("HabTrim", (0.8, 0.33, 0.05), 0.2, 0.5),
        glass=mat("HabGlass", (0.02, 0.15, 0.2), 0.0, 0.1, (0.25, 0.85, 1.0), 6),
        amber=mat("HabAmber", (0.3, 0.15, 0.02), 0.0, 0.3, (1.0, 0.6, 0.15), 6),
        red=mat("HabRed", (0.3, 0.02, 0.02), 0.0, 0.3, (1.0, 0.1, 0.05), 8),
        grate=mat("HabGrate", (0.12, 0.12, 0.13), 0.6, 0.45),
    )


PIECE = []


def B(name, loc, size, m, rot=(0, 0, 0), bevel=0.012, segs=2, taper=None):
    zm.PARTS.clear()
    o = zm.box(name, V(loc), size, M[m], None, rot=rot, bevel=bevel, segs=segs, taper=taper)
    PIECE.append(o)
    return o


def C(name, loc, r, depth, m, rot=(0, 0, 0), verts=16, bevel=0.006, r2=None):
    zm.PARTS.clear()
    o = zm.cyl(name, V(loc), r, depth, M[m], None, rot=rot, verts=verts, bevel=bevel, r2=r2)
    PIECE.append(o)
    return o


def finish(name, offset=(0, 0, 0)):
    """Apply modifiers, join the current parts into one object, cube-project UVs."""
    objs = list(PIECE)
    PIECE.clear()
    for o in objs:
        zm.apply_mods(o)
        o.data.transform(o.matrix_world)
        o.matrix_world = Matrix.Identity(4)
    with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs, selected_objects=objs, object=objs[0]):
        bpy.ops.object.join()
    o = objs[0]
    o.name = o.data.name = name
    for ob in bpy.context.scene.objects:
        ob.select_set(False)
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=2.0, correct_aspect=False, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    o.location = V(offset)          # spread pieces out for the preview only (exported at their own origin)
    return o


def wall(name, window):
    B("Core", (0, 0, 1.35), (2.0, 0.24, 2.7), "wall", bevel=0.01)
    B("Plinth", (0, -0.005, 0.1), (2.02, 0.32, 0.2), "frame", bevel=0.02)
    B("Trim", (0, -0.01, 0.36), (2.0, 0.29, 0.2), "trim", bevel=0.01)
    B("Cap", (0, 0, 2.66), (2.04, 0.32, 0.12), "frame", bevel=0.02)
    # proud insulated panels with seams
    B("PanelLow", (0, -0.128, 0.92), (1.78, 0.03, 0.78), "wall", bevel=0.012)
    if window:
        B("WinFrame", (0, -0.13, 1.82), (1.5, 0.06, 0.5), "frame", bevel=0.02)
        B("WinGlass", (0, -0.145, 1.82), (1.32, 0.04, 0.34), "glass", bevel=0.01)
        B("WinSill", (0, -0.17, 1.55), (1.52, 0.09, 0.05), "frame", bevel=0.01)
    else:
        for x in (-0.45, 0.45):
            B(f"PanelUp{x}", (x, -0.128, 1.86), (0.86, 0.03, 1.0), "wall", bevel=0.012)
        B("Hatch", (0.55, -0.15, 0.95), (0.36, 0.03, 0.3), "frame", bevel=0.01)
        B("HatchHandle", (0.55, -0.168, 0.95), (0.16, 0.02, 0.03), "wall", bevel=0.005)
    for x in (-0.85, 0.85):
        for z in (0.55, 1.33, 2.48):
            C(f"Bolt{x}{z}", (x, -0.135, z), 0.022, 0.03, "frame", rot=(90, 0, 0), verts=8, bevel=0.003)


def pillar():
    B("Body", (0, 0, 1.5), (0.5, 0.5, 3.0), "frame", bevel=0.07, segs=1)
    B("Foot", (0, 0, 0.13), (0.64, 0.64, 0.26), "frame", bevel=0.05, segs=1)
    B("Top", (0, 0, 2.96), (0.6, 0.6, 0.18), "frame", bevel=0.04, segs=1)
    for d in ((0, -0.255), (-0.255, 0), (0.255, 0), (0, 0.255)):
        rot = (0, 0, 90) if d[0] != 0 else (0, 0, 0)
        B(f"Lamp{d}", (d[0], d[1], 2.25), (0.12, 0.04, 0.18), "amber", rot=rot, bevel=0.01)
    B("Shackle", (0, -0.26, 2.9), (0.14, 0.05, 0.1), "frame", bevel=0.01)


def rib():
    B("Body", (0, -0.03, 1.48), (0.3, 0.42, 2.96), "frame", bevel=0.05, segs=1)
    B("Foot", (0, -0.05, 0.14), (0.38, 0.5, 0.28), "frame", bevel=0.04, segs=1)
    B("Lamp", (0, -0.25, 2.3), (0.1, 0.03, 0.14), "amber", bevel=0.01)


def door():
    for x in (-1.35, 1.35):
        B(f"Jamb{x}", (x, -0.02, 1.35), (0.3, 0.42, 2.7), "frame", bevel=0.05, segs=1)
        B(f"Glow{x}", (x * 0.89, -0.2, 1.15), (0.05, 0.05, 2.3), "glass", bevel=0.01)
    B("Header", (0, -0.02, 2.55), (3.0, 0.42, 0.42), "frame", bevel=0.05, segs=1)
    B("GlowTop", (0, -0.2, 2.31), (2.4, 0.05, 0.05), "glass", bevel=0.01)
    B("Sign", (0, -0.24, 2.58), (0.7, 0.03, 0.12), "glass", bevel=0.01)
    B("Panel", (1.62, -0.24, 1.25), (0.16, 0.04, 0.26), "frame", bevel=0.01)
    B("PanelGlow", (1.62, -0.265, 1.29), (0.1, 0.01, 0.1), "glass", bevel=0.003)
    for x in (-1.6, 1.6):
        B(f"Light{x}", (x, -0.24, 2.0), (0.12, 0.05, 0.16), "amber", bevel=0.01)
    B("Threshold", (0, -0.05, 0.03), (2.4, 0.46, 0.06), "grate", bevel=0.01)


def steps():
    for i in range(3):
        B(f"Step{i}", (0, -0.35 - i * 0.3, 0.18 - i * 0.06), (2.2, 0.32, 0.36 - i * 0.12), "grate", bevel=0.02)
    for x in (-1.15, 1.15):
        B(f"Post{x}a", (x, -0.4, 0.55), (0.06, 0.06, 0.8), "trim", bevel=0.01)
        B(f"Post{x}b", (x, -1.0, 0.45), (0.06, 0.06, 0.6), "trim", bevel=0.01)
        B(f"Rail{x}", (x, -0.7, 0.88), (0.06, 0.66, 0.06), "trim", rot=(-12, 0, 0), bevel=0.01)


def roof_tile():
    B("Slab", (0, 0, 0.09), (2.0, 2.0, 0.18), "roof", bevel=0.01)
    for x, y, sx, sy in ((-0.5, -0.5, 0.9, 0.9), (0.5, 0.5, 0.9, 0.9), (0.5, -0.5, 0.9, 0.9), (-0.5, 0.5, 0.9, 0.9)):
        B(f"Plate{x}{y}", (x, y, 0.19), (sx, sy, 0.03), "roof", bevel=0.012)


def roof_vent():
    B("Base", (0, 0, 0.12), (1.3, 1.0, 0.24), "frame", bevel=0.03)
    B("Box", (0, 0, 0.45), (1.1, 0.85, 0.5), "frame", bevel=0.04, taper=(0.92, 0.92))
    for i in range(6):
        B(f"Slat{i}", (0, -0.43, 0.32 + i * 0.065), (0.8, 0.03, 0.025), "roof", bevel=0.005)
    B("Glow", (0, -0.43, 0.7), (0.5, 0.02, 0.04), "glass", bevel=0.005)
    for x in (-0.4, 0.4):
        B(f"Hazard{x}", (x, -0.505, 0.12), (0.2, 0.01, 0.12), "trim", bevel=0.004)


def roof_pipes():
    for k, x in enumerate((-0.18, 0.18)):
        C(f"PipeA{k}", (x, 0, 0.22), 0.09, 2.4, "wall", rot=(90, 0, 0), verts=12, bevel=0.004)
        C(f"PipeUp{k}", (x, 1.2, 0.5), 0.09, 0.6, "wall", verts=12, bevel=0.004)
        for y in (-0.9, 0.0, 0.9):
            C(f"Clamp{k}{y}", (x, y, 0.22), 0.11, 0.06, "frame", rot=(90, 0, 0), verts=12, bevel=0.004)
            B(f"Saddle{k}{y}", (x, y, 0.06), (0.24, 0.1, 0.12), "frame", bevel=0.01)
        for y in (-0.5, 0.5):
            C(f"Band{k}{y}", (x, y, 0.22), 0.095, 0.05, "trim", rot=(90, 0, 0), verts=12, bevel=0.003)


def roof_dish():
    B("Mount", (0, 0, 0.15), (0.7, 0.7, 0.3), "frame", bevel=0.05)
    B("MountGlow", (0, -0.355, 0.15), (0.3, 0.01, 0.05), "glass", bevel=0.003)
    C("Neck", (0, 0, 0.45), 0.08, 0.35, "frame", verts=10)
    C("Dish", (0, -0.05, 0.85), 0.55, 0.16, "wall", rot=(-55, 0, 0), verts=24, bevel=0.01, r2=0.18)
    C("Feed", (0, -0.28, 1.05), 0.03, 0.4, "frame", rot=(-55, 0, 0), verts=8)
    C("Antenna", (0.45, 0.2, 0.9), 0.015, 1.6, "frame", verts=6)


def roof_beacon():
    C("Base", (0, 0, 0.1), 0.14, 0.2, "frame", verts=10)
    C("Post", (0, 0, 0.4), 0.05, 0.5, "frame", verts=8)
    C("Lamp", (0, 0, 0.75), 0.09, 0.2, "red", verts=12)
    C("Cap", (0, 0, 0.88), 0.1, 0.05, "frame", verts=12)


def lamp():
    B("Foot", (0, 0, 0.12), (0.55, 0.55, 0.24), "frame", bevel=0.05)
    B("Post", (0, 0, 1.9), (0.2, 0.2, 3.6), "frame", bevel=0.03)
    B("Band", (0, 0, 1.0), (0.24, 0.24, 0.12), "trim", bevel=0.01)
    B("Arm", (0, -0.45, 3.62), (0.12, 1.0, 0.12), "frame", bevel=0.02)
    B("Head", (0, -0.9, 3.55), (0.36, 0.5, 0.16), "frame", bevel=0.03)
    B("Light", (0, -0.9, 3.465), (0.28, 0.4, 0.02), "amber", bevel=0.005)
    B("Box", (0, 0.13, 1.4), (0.18, 0.1, 0.3), "frame", bevel=0.02)
    B("BoxGlow", (0, 0.18, 1.45), (0.08, 0.01, 0.06), "glass", bevel=0.003)


def grate():
    B("Frame", (0, 0, 0.05), (2.2, 2.3, 0.1), "frame", bevel=0.02)
    B("Mesh", (0, 0, 0.1), (1.9, 2.0, 0.02), "grate", bevel=0.005)
    for i in range(9):
        B(f"Bar{i}", (-0.8 + i * 0.2, 0, 0.115), (0.04, 2.0, 0.02), "frame", bevel=0.004)
    for x in (-1.05, 1.05):
        B(f"Hazard{x}", (x, 0, 0.105), (0.08, 2.26, 0.015), "trim", bevel=0.004)


if __name__ == "__main__":
    zm.reset()
    mats()
    pieces = [("WallPanel", lambda: wall("WallPanel", False)), ("WallWindow", lambda: wall("WallWindow", True)),
              ("Pillar", pillar), ("Rib", rib), ("DoorFrame", door), ("DoorSteps", steps), ("RoofTile", roof_tile),
              ("RoofVent", roof_vent), ("RoofPipes", roof_pipes), ("RoofDish", roof_dish), ("RoofBeacon", roof_beacon),
              ("Lamp", lamp), ("Grate", grate)]
    objs = []
    for i, (n, fn) in enumerate(pieces):
        fn()
        objs.append(finish(n))
    for o in bpy.data.objects:
        o.select_set(o in objs)
    path = os.path.join(MODELS, "HabKit.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             mesh_smooth_type="FACE", use_mesh_modifiers=True, bake_anim=False, path_mode="STRIP")
    print(f"[zcom] exported {path} pieces={[o.name for o in objs]}")
    # preview: assemble a small building
    def place(name, loc, yaw):
        src = bpy.data.objects[name]
        o = src.copy(); o.data = src.data
        bpy.context.scene.collection.objects.link(o)
        o.location = V(loc); o.rotation_euler = (0, 0, rad(yaw)); o.hide_render = False
        return o
    for o in objs:
        o.hide_render = True
    w, d = 8, 6
    for i, x in enumerate((-3, -1, 1, 3)):
        place("WallWindow" if i in (1, 2) else "WallPanel", (x, -d / 2, 0), 0)
        place("WallPanel", (x, d / 2, 0), 180)
    for y in (-2, 0, 2):
        place("WallWindow" if y == 0 else "WallPanel", (-w / 2, y, 0), -90)
        place("WallPanel", (w / 2, y, 0), 90)
    for x in (-2, 0, 2):
        place("Rib", (x, -d / 2, 0), 0)
    for c in ((-4, -3), (4, -3), (-4, 3), (4, 3)):
        place("Pillar", (c[0], c[1], 0), 0)
    for x in range(-3, 4, 2):
        for y in range(-2, 3, 2):
            place("RoofTile", (x, y, 2.72), 0)
    place("RoofVent", (0.5, 0.5, 2.92), 0); place("RoofDish", (-2.5, -1.5, 2.92), 30); place("RoofBeacon", (3, 2, 2.92), 0)
    place("RoofPipes", (1.8, -0.5, 2.92), 90); place("Lamp", (6, -5, 0), 200); place("Grate", (0, -6, 0), 0)
    zm.setup_render(32, (1100, 760))
    bpy.context.scene.cycles.device = "CPU"
    zm.render_views("HabKit", {"q34": (35, -35)}, target=V((0, -1, 1.2)), dist=22, lens=50)
    print("[zcom] habkit done")
