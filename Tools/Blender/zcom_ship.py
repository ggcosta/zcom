"""
Ship interior kit (the operative's frigate: hub between raids) -> Assets/ZCOM/Resources/Models/ShipKit.fbx
                                                               + Assets/ZCOM/Resources/Textures/Ship/<Piece>_*.png

    blender -b --factory-startup -P Tools/Blender/zcom_ship.py -- <models_dir> <previews_dir> [Piece,Piece,...]

Hard-surface pieces built from bevelled parts (hardened + weighted normals), then each piece gets its own unwrap and a
unique PBR texture set baked in Cycles from a procedural "worn military paint" shader:
  - ambient occlusion (piece-local) drives cavity grime, edge detection (Bevel node) drives chipped paint revealing
    bare steel, noise breaks up both; dust settles on up-facing surfaces; vertical grime streaks on walls;
  - the normal map carries rounded edges (Bevel node) plus fine dents, so the low-poly corners catch light;
  - hazard stripes (diagonal or radial), stencils (real text geometry) and lights (emission map) are baked in.
Every exported object has its origin at its bottom centre and its "front" towards -Y (the room side for walls).
Objects whose name ends in "Glass" are not baked (Unity gives them a transparent material).

Pieces: FloorPlate, FloorGrate, FloorMarked (4 x 4 m); Wall, WallPipes, WallWindow (+ WallWindowGlass), Rib, Corner
(4.2 m bulkheads, 2 m modules); CutWall, CutCorner, HullSkirt (the cutaway side: low sectioned wall, outer hull below the
deck); StashLocker + StashDrawer; LaunchPad + LaunchPadCore + LaunchClamp; PodGantry.
"""
import bpy, bmesh, math, os, sys
import numpy as np
from mathutils import Vector, Matrix, Euler

ARGS = sys.argv[sys.argv.index("--") + 1:]
MODELS = os.path.abspath(ARGS[0])
PREVIEWS = os.path.abspath(ARGS[1])
ONLY = set(ARGS[2].split(",")) if len(ARGS) > 2 and ARGS[2] else None
TEX_DIR = os.path.abspath(os.path.join(MODELS, "..", "Textures", "Ship"))
FONT_PATH = os.path.abspath(os.path.join(MODELS, "..", "Fonts", "Rajdhani-Bold.ttf"))
os.makedirs(TEX_DIR, exist_ok=True)
os.makedirs(PREVIEWS, exist_ok=True)
os.makedirs(MODELS, exist_ok=True)
V = Vector
rad = math.radians
H = 4.2          # bulkhead height
TEX = {"StashLocker": 2048, "FloorPlate": 2048, "FloorGrate": 2048, "FloorMarked": 2048, "LaunchPad": 2048,
       "PodGantry": 2048, "WallWindow": 2048, "Wall": 1536, "WallPipes": 1536, "HullSkirt": 1536}

# ============================================================================ material kinds
# colour (linear-ish albedo), metallic, roughness, wear (chipped paint at edges), emission colour, dust
KINDS = {
    "paint":   dict(c=(0.115, 0.122, 0.135), m=0.25, r=0.52, wear=1.0, dust=1.0),
    "deck":    dict(c=(0.085, 0.088, 0.095), m=0.3, r=0.6, wear=1.0, dust=1.4),
    "light":   dict(c=(0.26, 0.27, 0.285), m=0.2, r=0.48, wear=1.0, dust=1.0),
    "frame":   dict(c=(0.045, 0.045, 0.052), m=0.45, r=0.5, wear=0.6, dust=0.8),
    "steel":   dict(c=(0.42, 0.43, 0.45), m=1.0, r=0.36, wear=0.0, dust=0.5),
    "dark":    dict(c=(0.018, 0.018, 0.021), m=0.2, r=0.72, wear=0.0, dust=0.3),
    "rubber":  dict(c=(0.022, 0.022, 0.024), m=0.0, r=0.85, wear=0.0, dust=0.6),
    "trim":    dict(c=(0.62, 0.17, 0.025), m=0.1, r=0.5, wear=1.0, dust=1.0),
    "hazard":  dict(c=(0.75, 0.48, 0.03), m=0.1, r=0.55, wear=1.2, dust=1.0, stripes="diag"),
    "hazardR": dict(c=(0.75, 0.48, 0.03), m=0.1, r=0.55, wear=1.2, dust=1.0, stripes="radial"),
    "stencil": dict(c=(0.62, 0.62, 0.58), m=0.1, r=0.55, wear=1.4, dust=1.0),
    "insul":   dict(c=(0.42, 0.3, 0.1), m=0.0, r=0.9, wear=0.0, dust=0.4),
    "cut":     dict(c=(0.03, 0.03, 0.035), m=0.0, r=0.8, wear=0.0, dust=0.0, e=(0.9, 0.22, 0.05), es=0.5),
    "cyan":    dict(c=(0.02, 0.06, 0.08), m=0.0, r=0.2, wear=0.0, dust=0.0, e=(0.3, 0.85, 1.0), es=1.0),
    "amber":   dict(c=(0.08, 0.04, 0.01), m=0.0, r=0.3, wear=0.0, dust=0.0, e=(1.0, 0.55, 0.12), es=1.0),
    "red":     dict(c=(0.08, 0.01, 0.01), m=0.0, r=0.3, wear=0.0, dust=0.0, e=(1.0, 0.08, 0.04), es=1.0),
    "green":   dict(c=(0.01, 0.08, 0.02), m=0.0, r=0.3, wear=0.0, dust=0.0, e=(0.2, 1.0, 0.35), es=1.0),
    "screen":  dict(c=(0.01, 0.03, 0.04), m=0.0, r=0.1, wear=0.0, dust=0.0, e=(0.25, 0.75, 1.0), es=0.8, scan=True),
    "glass":   dict(c=(0.02, 0.05, 0.07), m=0.0, r=0.05, wear=0.0, dust=0.0),
}
MATS = {}


def _node(nt, kind, **props):
    n = nt.nodes.new(kind)
    for k, v in props.items():
        setattr(n, k, v)
    return n


def _sock(n, name, typ=None, out=False):
    socks = n.outputs if out else n.inputs
    for s in socks:
        if s.name == name and (typ is None or s.type == typ) and getattr(s, "enabled", True):
            return s
    for s in socks:
        if s.name == name and (typ is None or s.type == typ):
            return s
    raise KeyError(f"{n.bl_idname}.{name}")


def math_node(nt, op, a, b=None, clamp=False):
    n = _node(nt, "ShaderNodeMath", operation=op, use_clamp=clamp)
    for i, x in enumerate((a, b)):
        if x is None:
            continue
        if isinstance(x, (int, float)):
            n.inputs[i].default_value = x
        else:
            nt.links.new(x, n.inputs[i])
    return n.outputs[0]


def mix_rgb(nt, fac, a, b, blend="MIX"):
    n = _node(nt, "ShaderNodeMix", data_type="RGBA", blend_type=blend, clamp_result=True)
    fi = _sock(n, "Factor", "VALUE")
    if isinstance(fac, (int, float)):
        fi.default_value = fac
    else:
        nt.links.new(fac, fi)
    for name, x in (("A", a), ("B", b)):
        s = _sock(n, name, "RGBA")
        if isinstance(x, (tuple, list)):
            s.default_value = (*x[:3], 1)
        else:
            nt.links.new(x, s)
    return _sock(n, "Result", "RGBA", out=True)


def make_kind(name, p):
    """Bake material: emits albedo / metallic / smoothness / emission on demand; a BSDF carries the detail normal."""
    m = bpy.data.materials.new("K_" + name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = _node(nt, "ShaderNodeOutputMaterial")
    tc = _node(nt, "ShaderNodeTexCoord")
    obj = tc.outputs["Object"]
    geo = _node(nt, "ShaderNodeNewGeometry")

    # edges: shading normal of a bevelled surface diverges from the true normal near edges
    bev = _node(nt, "ShaderNodeBevel", samples=8); bev.inputs["Radius"].default_value = 0.018
    dot = _node(nt, "ShaderNodeVectorMath", operation="DOT_PRODUCT")
    nt.links.new(bev.outputs[0], dot.inputs[0]); nt.links.new(geo.outputs["Normal"], dot.inputs[1])
    edge = math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", 1.0, _sock(dot, "Value", out=True)), 9.0, clamp=True)

    ao_n = _node(nt, "ShaderNodeAmbientOcclusion", samples=16, only_local=True)
    ao_n.inputs["Distance"].default_value = 0.45
    ao = ao_n.outputs["AO"]

    def noise(scale, detail=6.0, rough=0.55, coords=None, dim="3D"):
        n = _node(nt, "ShaderNodeTexNoise", noise_dimensions=dim)
        n.inputs["Scale"].default_value = scale
        n.inputs["Detail"].default_value = detail
        n.inputs["Roughness"].default_value = rough
        nt.links.new(coords or obj, n.inputs["Vector"])
        return n.outputs["Fac"]

    def scaled(sx, sy, sz):
        mp = _node(nt, "ShaderNodeMapping")
        mp.inputs["Scale"].default_value = (sx, sy, sz)
        nt.links.new(obj, mp.inputs["Vector"])
        return mp.outputs[0]

    n_big = noise(1.6, 6, 0.6)
    n_mid = noise(7.0, 5, 0.6)
    n_fine = noise(38.0, 3, 0.5)
    streak = noise(1.0, 4, 0.5, scaled(14, 14, 0.9))      # vertical drip streaks

    base = tuple(p["c"])
    col = None
    if p.get("stripes"):
        sep = _node(nt, "ShaderNodeSeparateXYZ"); nt.links.new(obj, sep.inputs[0])
        if p["stripes"] == "diag":
            s = math_node(nt, "ADD", math_node(nt, "ADD", sep.outputs[0], sep.outputs[1]), sep.outputs[2])
            s = math_node(nt, "MULTIPLY", s, 3.2)
        else:
            s = math_node(nt, "MULTIPLY", math_node(nt, "ARCTAN2", sep.outputs[1], sep.outputs[0]), 24 / (2 * math.pi))
        band = math_node(nt, "GREATER_THAN", math_node(nt, "FRACT", s), 0.5)
        col = mix_rgb(nt, band, base, (0.02, 0.02, 0.022))
    cur = col if col is not None else None

    def as_col(x):
        if x is None:
            rgb = _node(nt, "ShaderNodeRGB"); rgb.outputs[0].default_value = (*base, 1); return rgb.outputs[0]
        return x
    cur = as_col(cur)
    # subtle paint mottling
    mott = math_node(nt, "ADD", 0.84, math_node(nt, "MULTIPLY", n_big, 0.32))
    cur = mix_rgb(nt, 1.0, cur, _rgb_from_float(nt, mott), "MULTIPLY")
    seam = None
    if p.get("panels"):
        # hull plating: straight seams on an object-space grid (panels sx x sy x sz metres) + a tone shift per panel
        sizes = p.get("panel_size", (1.6, 2.4, 1.15))
        sep_o = _sep(nt, obj)
        seam = None
        cell = []
        for ax, size in zip(("X", "Y", "Z"), sizes):
            c = math_node(nt, "DIVIDE", _sock(sep_o, ax, out=True), size)
            f = math_node(nt, "FRACT", c)
            d = math_node(nt, "MULTIPLY", math_node(nt, "MINIMUM", f, math_node(nt, "SUBTRACT", 1.0, f)), size)   # metres to the seam
            line = math_node(nt, "SUBTRACT", 1.0, math_node(nt, "DIVIDE", d, 0.014), clamp=True)
            seam = line if seam is None else math_node(nt, "MAXIMUM", seam, line)
            cell.append(math_node(nt, "FLOOR", c))
        comb = _node(nt, "ShaderNodeCombineXYZ")
        for k in range(3):
            nt.links.new(cell[k], comb.inputs[k])
        wn = _node(nt, "ShaderNodeTexWhiteNoise", noise_dimensions="3D")
        nt.links.new(comb.outputs[0], wn.inputs["Vector"])
        tone = math_node(nt, "ADD", 0.86, math_node(nt, "MULTIPLY", _sock(wn, "Value", out=True), 0.26))
        cur = mix_rgb(nt, 1.0, cur, _rgb_from_float(nt, tone), "MULTIPLY")
        cur = mix_rgb(nt, math_node(nt, "MULTIPLY", seam, 0.75), cur, _scale_col(nt, cur, 0.3))
    # vertical grime streaks on vertical faces
    sepn = _node(nt, "ShaderNodeSeparateXYZ"); nt.links.new(geo.outputs["Normal"], sepn.inputs[0])
    upf = math_node(nt, "ABSOLUTE", sepn.outputs[2])
    vert = math_node(nt, "SUBTRACT", 1.0, upf, clamp=True)
    st = math_node(nt, "MULTIPLY", math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", streak, 0.45, clamp=True), 2.2, clamp=True), vert)
    cur = mix_rgb(nt, math_node(nt, "MULTIPLY", st, 0.45), cur, _scale_col(nt, cur, 0.55))

    # chipped paint at edges -> bare steel
    wear = None
    if p.get("wear", 0) > 0:
        w = math_node(nt, "ADD", math_node(nt, "MULTIPLY", edge, 1.15 * p["wear"]), math_node(nt, "MULTIPLY", n_mid, 0.75))
        w = math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", w, 1.02), 7.0, clamp=True)
        wear = w
        cur = mix_rgb(nt, wear, cur, (0.36, 0.365, 0.38))
    # cavity grime from AO
    cav = math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", 1.0, ao), 1.25, clamp=True)
    cav = math_node(nt, "ADD", cav, math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", n_mid, 0.5), 0.35), clamp=True)
    cur = mix_rgb(nt, math_node(nt, "MULTIPLY", cav, 0.7), cur, _scale_col(nt, cur, 0.25))
    # dust on up-facing surfaces
    if p.get("dust", 0) > 0:
        up = math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", sepn.outputs[2], 0.65, clamp=True), 2.8, clamp=True)
        d = math_node(nt, "MULTIPLY", math_node(nt, "MULTIPLY", up, n_big), 0.55 * p["dust"], clamp=True)
        d = math_node(nt, "ADD", d, math_node(nt, "MULTIPLY", cav, 0.25 * p["dust"]), clamp=True)
        cur = mix_rgb(nt, d, cur, (0.2, 0.19, 0.185))
    albedo = cur

    metal = p["m"]
    met = _const(nt, metal)
    if wear is not None:
        met = math_node(nt, "ADD", math_node(nt, "MULTIPLY", wear, 1.0 - metal), metal)
    smooth0 = 1.0 - p["r"]
    smo = math_node(nt, "ADD", smooth0, math_node(nt, "MULTIPLY", math_node(nt, "SUBTRACT", n_mid, 0.5), 0.18))
    if wear is not None:
        smo = math_node(nt, "ADD", smo, math_node(nt, "MULTIPLY", wear, 0.62 - smooth0))
    smo = math_node(nt, "SUBTRACT", smo, math_node(nt, "MULTIPLY", cav, 0.25), clamp=True)

    if "e" in p:
        e = _node(nt, "ShaderNodeRGB"); e.outputs[0].default_value = (*p["e"], 1)
        emi = e.outputs[0]
        k = p.get("es", 1.0)
        if p.get("scan"):     # screens: scanlines + soft UI blocks
            sl = math_node(nt, "GREATER_THAN", math_node(nt, "FRACT", math_node(nt, "MULTIPLY", _sock(_sep(nt, obj), "Z", out=True), 90.0)), 0.35)
            blk = math_node(nt, "GREATER_THAN", noise(9.0, 1, 0.3), 0.48)
            k_node = math_node(nt, "MULTIPLY", math_node(nt, "ADD", math_node(nt, "MULTIPLY", sl, 0.35), 0.4), math_node(nt, "ADD", math_node(nt, "MULTIPLY", blk, 0.6), 0.4))
            emi = mix_rgb(nt, 1.0, emi, _rgb_from_float(nt, math_node(nt, "MULTIPLY", k_node, k)), "MULTIPLY")
        else:
            emi = _scale_col(nt, emi, k)
    else:
        rgb = _node(nt, "ShaderNodeRGB"); rgb.outputs[0].default_value = (0, 0, 0, 1); emi = rgb.outputs[0]

    # detail normal: rounded edges + dents
    bev2 = _node(nt, "ShaderNodeBevel", samples=8); bev2.inputs["Radius"].default_value = 0.012
    bump = _node(nt, "ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.35; bump.inputs["Distance"].default_value = 0.004
    hgt = math_node(nt, "ADD", n_fine, math_node(nt, "MULTIPLY", n_mid, 0.5))
    if seam is not None:
        hgt = math_node(nt, "SUBTRACT", hgt, math_node(nt, "MULTIPLY", seam, 1.6))
    nt.links.new(hgt, bump.inputs["Height"])
    nt.links.new(bev2.outputs[0], bump.inputs["Normal"])

    emit_node = _node(nt, "ShaderNodeEmission")
    bsdf = _node(nt, "ShaderNodeBsdfPrincipled")
    nt.links.new(bump.outputs[0], bsdf.inputs["Normal"])
    # flat preview values (used when a piece has no baked textures yet)
    bsdf.inputs["Base Color"].default_value = (*base, 1)
    bsdf.inputs["Metallic"].default_value = p["m"]
    bsdf.inputs["Roughness"].default_value = p["r"]
    if "e" in p:
        bsdf.inputs["Emission Color"].default_value = (*p["e"], 1)
        bsdf.inputs["Emission Strength"].default_value = 4.0 * p.get("es", 1.0)
    MATS[name] = dict(mat=m, out=out, emit=emit_node, bsdf=bsdf, albedo=albedo, metal=met, smooth=smo, emission=emi)
    return m


def _const(nt, v):
    n = _node(nt, "ShaderNodeValue"); n.outputs[0].default_value = v; return n.outputs[0]


def _sep(nt, vec):
    s = _node(nt, "ShaderNodeSeparateXYZ"); nt.links.new(vec, s.inputs[0]); return s


def _rgb_from_float(nt, f):
    c = _node(nt, "ShaderNodeCombineColor")
    for i in range(3):
        nt.links.new(f, c.inputs[i])
    return c.outputs[0]


def _scale_col(nt, col, k):
    return mix_rgb(nt, 1.0, col, (k, k, k), "MULTIPLY")


def set_pass(which, image):
    for name, d in MATS.items():
        nt = d["mat"].node_tree
        for l in list(d["out"].inputs["Surface"].links):
            nt.links.remove(l)
        for l in list(d["emit"].inputs["Color"].links):
            nt.links.remove(l)
        if which == "normal":
            nt.links.new(d["bsdf"].outputs[0], d["out"].inputs["Surface"])
        else:
            src = d[which]
            nt.links.new(src, d["emit"].inputs["Color"])
            nt.links.new(d["emit"].outputs[0], d["out"].inputs["Surface"])
        img = nt.nodes.get("BAKE")
        if img is None:
            img = nt.nodes.new("ShaderNodeTexImage"); img.name = "BAKE"
        img.image = image
        nt.nodes.active = img


# ============================================================================ geometry helpers
PARTS = []


def link(o):
    bpy.context.scene.collection.objects.link(o)
    return o


def _mesh(name, bm, kind, smooth=True):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    o = link(bpy.data.objects.new(name, me))
    me.materials.append(MATS[kind]["mat"])
    for p in me.polygons:
        p.use_smooth = smooth
    return o


def _harden(o, width, segs=2, angle=35):
    if width > 0:
        b = o.modifiers.new("Bevel", "BEVEL")
        b.width = width; b.segments = segs; b.limit_method = "ANGLE"; b.angle_limit = rad(angle)
        b.harden_normals = True; b.miter_outer = "MITER_ARC"
        o.modifiers.new("WN", "WEIGHTED_NORMAL").keep_sharp = True
    else:
        for p in o.data.polygons:
            p.use_smooth = False
    PARTS.append(o)
    return o


def box(loc, size, kind, rot=(0, 0, 0), bevel=0.01, segs=2, taper=None, name="B"):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = V((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        if taper and v.co.z > 0:
            v.co.x *= taper[0]; v.co.y *= taper[1]
    o = _mesh(name, bm, kind)
    o.rotation_euler = Euler([rad(a) for a in rot]); o.location = V(loc)
    return _harden(o, bevel, segs)


def cyl(loc, r, depth, kind, rot=(0, 0, 0), verts=24, bevel=0.004, r2=None, name="C"):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=verts, radius1=r, radius2=r2 if r2 is not None else r, depth=depth)
    o = _mesh(name, bm, kind)
    o.rotation_euler = Euler([rad(a) for a in rot]); o.location = V(loc)
    return _harden(o, bevel, 2, 30)


def ring(loc, r_out, r_in, depth, kind, verts=64, bevel=0.006, rot=(0, 0, 0), name="R"):
    """Flat annulus (tube) along Z."""
    bm = bmesh.new()
    outer = [bm.verts.new((r_out * math.cos(2 * math.pi * i / verts), r_out * math.sin(2 * math.pi * i / verts), 0)) for i in range(verts)]
    inner = [bm.verts.new((r_in * math.cos(2 * math.pi * i / verts), r_in * math.sin(2 * math.pi * i / verts), 0)) for i in range(verts)]
    for i in range(verts):
        j = (i + 1) % verts
        bm.faces.new((outer[i], outer[j], inner[j], inner[i]))
    res = bmesh.ops.extrude_face_region(bm, geom=bm.faces[:])
    for v in [g for g in res["geom"] if isinstance(g, bmesh.types.BMVert)]:
        v.co.z += depth
    for v in bm.verts:
        v.co.z -= depth / 2
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = _mesh(name, bm, kind)
    o.rotation_euler = Euler([rad(a) for a in rot]); o.location = V(loc)
    return _harden(o, bevel, 2, 30)


def prism(pts2d, y0, y1, kind, bevel=0.01, name="P"):
    """Extrude a convex/concave polygon given in the XZ plane (list of (x, z)) from y0 to y1."""
    bm = bmesh.new()
    a = [bm.verts.new((x, y0, z)) for x, z in pts2d]
    b = [bm.verts.new((x, y1, z)) for x, z in pts2d]
    bm.faces.new(a)
    bm.faces.new(list(reversed(b)))
    n = len(pts2d)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = _mesh(name, bm, kind)
    return _harden(o, bevel, 2, 30)


def hull(pts, kind, bevel=0.01, name="H"):
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in pts]
    res = bmesh.ops.convex_hull(bm, input=vs)
    kill = {g for g in res["geom_interior"] + res["geom_unused"] if isinstance(g, bmesh.types.BMVert)}
    if kill:
        bmesh.ops.delete(bm, geom=list(kill), context="VERTS")
    bmesh.ops.dissolve_limit(bm, angle_limit=rad(3), verts=bm.verts[:], edges=bm.edges[:])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = _mesh(name, bm, kind)
    return _harden(o, bevel, 2, 30)


def pipe(points, r, kind, res=12, name="Pipe"):
    """Tube along a polyline with rounded bends (curve bevel -> mesh)."""
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"; cu.bevel_depth = r; cu.bevel_resolution = 3; cu.use_fill_caps = True
    sp = cu.splines.new("BEZIER")
    sp.bezier_points.add(len(points) - 1)
    for bp, p in zip(sp.bezier_points, points):
        bp.co = V(p); bp.handle_left_type = bp.handle_right_type = "AUTO"
    # sharpen to polyline with small fillets: vector handles, then a bevel on the mesh gives the bends
    for bp in sp.bezier_points:
        bp.handle_left_type = bp.handle_right_type = "VECTOR"
    cu.resolution_u = 2
    o = link(bpy.data.objects.new(name, cu))
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
    bpy.data.objects.remove(o); bpy.data.curves.remove(cu)
    mo = link(bpy.data.objects.new(name, me))
    me.materials.clear(); me.materials.append(MATS[kind]["mat"])
    for p in me.polygons:
        p.use_smooth = True
    PARTS.append(mo)
    return mo


_FONT = None


def text(s, loc, size, kind, rot=(90, 0, 0), depth=0.003, align="CENTER", name="Txt"):
    """Stencil lettering as thin geometry. Default rot stands the text up facing -Y."""
    global _FONT
    if _FONT is None:
        _FONT = bpy.data.fonts.load(FONT_PATH) if os.path.exists(FONT_PATH) else None
    cu = bpy.data.curves.new(name, "FONT")
    cu.body = s; cu.size = size; cu.extrude = depth; cu.align_x = align; cu.align_y = "CENTER"
    if _FONT:
        cu.font = _FONT
    o = link(bpy.data.objects.new(name, cu))
    o.rotation_euler = Euler([rad(a) for a in rot]); o.location = V(loc)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
    mw = o.matrix_world.copy()
    bpy.data.objects.remove(o); bpy.data.curves.remove(cu)
    mo = link(bpy.data.objects.new(name, me))
    mo.matrix_world = mw
    me.materials.clear(); me.materials.append(MATS[kind]["mat"])
    for p in me.polygons:
        p.use_smooth = False
    PARTS.append(mo)
    return mo


def bolts(points, kind="steel", r=0.022, h=0.02, axis="-Y"):
    rot = {"-Y": (90, 0, 0), "+Y": (-90, 0, 0), "Z": (0, 0, 0), "-X": (0, 90, 0), "+X": (0, -90, 0)}[axis]
    for p in points:
        cyl(p, r, h, kind, rot=rot, verts=8, bevel=0.004)


def apply_mods(o):
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    o.modifiers.clear()
    old = o.data; o.data = me
    if old.users == 0:
        bpy.data.meshes.remove(old)


def finish(name):
    objs = list(PARTS); PARTS.clear()
    for o in objs:
        apply_mods(o)
        o.data.transform(o.matrix_world)
        o.matrix_world = Matrix.Identity(4)
    for o in bpy.context.scene.objects:
        o.select_set(False)
    with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs, selected_objects=objs, object=objs[0]):
        bpy.ops.object.join()
    o = objs[0]
    o.name = o.data.name = name
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    return o


# ============================================================================ pieces

def floor_base(skip=()):
    """4 x 4 m deck tile, walking surface at z = 0. skip: plate indices left out (0..3)."""
    box((0, 0, -0.17), (4.0, 4.0, 0.22), "frame", bevel=0.01)
    plates = [(-1, -1), (1, -1), (-1, 1), (1, 1)]
    for i, (sx, sy) in enumerate(plates):
        if i in skip:
            continue
        cx, cy = sx * 1.0, sy * 1.0
        box((cx, cy, -0.035), (1.955, 1.955, 0.07), "deck", bevel=0.012)
        # raised inner field + perimeter bolts
        box((cx, cy, 0.0), (1.62, 1.62, 0.012), "deck", bevel=0.004)
        pts = []
        for t in (-0.88, -0.3, 0.3, 0.88):
            pts += [(cx + t, cy - 0.89, 0.0), (cx + t, cy + 0.89, 0.0), (cx - 0.89, cy + t, 0.0), (cx + 0.89, cy + t, 0.0)]
        bolts(pts, "steel", r=0.018, h=0.016, axis="Z")
    return plates


def tread(cx, cy, n=11, span=1.5):
    """Diamond-plate texture: alternating raised lozenges."""
    step = span / n
    for i in range(n):
        for j in range(n):
            x = cx - span / 2 + (i + 0.5) * step
            y = cy - span / 2 + (j + 0.5) * step
            box((x, y, 0.009), (0.075, 0.018, 0.008), "deck", rot=(0, 0, 45 if (i + j) % 2 else -45), bevel=0.0)


def floor_plate():
    floor_base()
    tread(-1, 1); tread(1, -1)
    # inset light channel along the centre seam
    box((0, 0, -0.012), (0.06, 3.9, 0.02), "dark", bevel=0.0)
    for y in (-1.5, -0.5, 0.5, 1.5):
        box((0, y, -0.004), (0.035, 0.7, 0.01), "cyan", bevel=0.002)
    # recessed lift handles
    for (x, y) in ((-1.6, -1.0), (1.0, 1.6)):
        box((x, y, -0.002), (0.22, 0.06, 0.01), "dark", bevel=0.003)


def floor_grate():
    floor_base(skip=(0, 1, 2, 3))
    # service pit under a grating with pipes and cable runs
    box((0, 0, -0.55), (3.6, 3.6, 0.1), "dark", bevel=0.01)
    for x in (-1.85, 1.85):
        box((x, 0, -0.27), (0.18, 3.9, 0.54), "frame", bevel=0.015)
    for y in (-1.85, 1.85):
        box((0, y, -0.27), (3.9, 0.18, 0.54), "frame", bevel=0.015)
    for i, (y, r, k) in enumerate(((-1.1, 0.13, "steel"), (-0.7, 0.09, "trim"), (0.2, 0.15, "paint"), (0.75, 0.07, "steel"), (1.2, 0.1, "steel"))):
        cyl((0, y, -0.5 + r), r, 3.7, k, rot=(0, 90, 0), verts=16)
        for x in (-1.2, 0.0, 1.2):
            box((x, y, -0.5 + r * 0.5), (0.1, r * 2.4, r), "frame", bevel=0.01)
    for x in (-0.9, 0.9):
        box((x, -0.25, -0.46), (0.25, 0.35, 0.18), "frame", bevel=0.02)
        box((x, -0.43, -0.42), (0.06, 0.01, 0.04), "amber", bevel=0.002)
    for x in (-1.4, -0.3, 0.6, 1.5):
        box((x, 1.55, -0.49), (0.12, 0.12, 0.02), "cyan", bevel=0.002)
    # grating: frame + bars
    box((0, 0, -0.03), (3.7, 3.7, 0.06), "frame", bevel=0.01) if False else None
    for s in (-1, 1):
        box((s * 1.78, 0, -0.03), (0.14, 3.7, 0.06), "frame", bevel=0.008)
        box((0, s * 1.78, -0.03), (3.7, 0.14, 0.06), "frame", bevel=0.008)
        box((s * 1.86, 0, -0.005), (0.035, 3.72, 0.012), "hazard", bevel=0.002)
        box((0, s * 1.86, -0.005), (3.72, 0.035, 0.012), "hazard", bevel=0.002)
    n = 46
    for i in range(n):
        y = -1.68 + i * (3.36 / (n - 1))
        box((0, y, -0.025), (3.42, 0.022, 0.05), "steel", bevel=0.0)
    for x in (-1.15, -0.4, 0.4, 1.15):
        box((x, 0, -0.04), (0.03, 3.42, 0.04), "steel", bevel=0.0)


def floor_marked():
    floor_plate()
    # walkway stripes + arrow + lettering painted on the deck (proud geometry, 1 mm)
    for x in (-1.85, 1.85):
        box((x, 0, 0.0075), (0.09, 3.9, 0.002), "hazard", bevel=0.0)
    for k, y in enumerate((-1.0, 0.4)):
        pts = [(-0.35, y), (0.12, y), (0.12, y - 0.18), (0.55, y + 0.22), (0.12, y + 0.62), (0.12, y + 0.44), (-0.35, y + 0.44)]
        bm = bmesh.new()
        vs = [bm.verts.new((px - 0.95, py, 0.007)) for px, py in pts]
        bm.faces.new(vs)
        res = bmesh.ops.extrude_face_region(bm, geom=bm.faces[:])
        for v in [g for g in res["geom"] if isinstance(g, bmesh.types.BMVert)]:
            v.co.z += 0.002
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        o = _mesh("Arrow", bm, "stencil", smooth=False)
        PARTS.append(o)
    text("DROP BAY", (0.75, -1.05, 0.0075), 0.36, "stencil", rot=(0, 0, 90), depth=0.002)
    text("DECK 02", (0.75, 1.2, 0.0075), 0.28, "stencil", rot=(0, 0, 90), depth=0.002)


# ---------------------------------------------------------------- bulkheads (inner face at y = 0, room towards -Y)

def wall_shell(window=False):
    # structural core behind the face
    if window:
        box((0, 0.32, 0.45), (2.0, 0.6, 0.9), "frame", bevel=0.0)
        box((0, 0.32, 3.75), (2.0, 0.6, 0.9), "frame", bevel=0.0)
        for x in (-0.94, 0.94):
            box((x, 0.32, 2.1), (0.12, 0.6, 2.4), "frame", bevel=0.0)
    else:
        box((0, 0.32, H / 2), (2.0, 0.6, H), "frame", bevel=0.0)
    # kick plate with vent louvres
    box((0, -0.07, 0.28), (2.0, 0.16, 0.56), "paint", bevel=0.012)
    box((0, -0.16, 0.06), (2.0, 0.04, 0.12), "frame", bevel=0.006)
    for x in (-0.55, 0.55):
        box((x, -0.152, 0.3), (0.62, 0.012, 0.28), "dark", bevel=0.002)
        for i in range(6):
            box((x, -0.16, 0.19 + i * 0.045), (0.6, 0.02, 0.018), "frame", rot=(-35, 0, 0), bevel=0.0)
    # trim band + light strip
    box((0, -0.05, 2.2), (2.0, 0.12, 0.14), "trim", bevel=0.01)
    box((0, -0.115, 2.2), (1.7, 0.012, 0.025), "cyan", bevel=0.002)
    # angled upper hull panel (leans into the room)
    if not window:
        box((0, -0.14, 2.98), (1.94, 0.09, 1.4), "paint", rot=(14, 0, 0), bevel=0.014)
        box((0, -0.2, 2.98), (1.5, 0.03, 1.0), "paint", rot=(14, 0, 0), bevel=0.01)
        for x in (-0.82, 0.82):
            for z in (2.45, 3.5):
                yy = -0.2 - (z - 2.98) * math.tan(rad(14))
                cyl((x, yy, z), 0.02, 0.02, "steel", rot=(104, 0, 0), verts=8, bevel=0.003)
    # pipe run and cable tray (continuous across modules)
    for z, y, r, k in ((3.78, -0.34, 0.075, "steel"), (3.96, -0.26, 0.05, "paint")):
        cyl((0, y, z), r, 2.0, k, rot=(0, 90, 0), verts=16, bevel=0.0)
    cyl((0.62, -0.34, 3.78), 0.082, 0.12, "trim", rot=(0, 90, 0), verts=16, bevel=0.003)
    for x in (-0.6, 0.6):
        box((x, -0.18, 3.85), (0.08, 0.36, 0.36), "frame", bevel=0.01)
    box((0, -0.25, 4.12), (2.0, 0.42, 0.04), "frame", bevel=0.004)
    for i, y in enumerate((-0.38, -0.3, -0.22, -0.14)):
        cyl((0, y, 4.17), 0.028 if i % 2 else 0.035, 2.0, "rubber", rot=(0, 90, 0), verts=10, bevel=0.0)
    box((0, 0.1, H + 0.06), (2.0, 0.84, 0.12), "frame", bevel=0.01)


def wall_panels():
    for x in (-0.49, 0.49):
        box((x, -0.035, 1.38), (0.93, 0.07, 1.5), "paint", bevel=0.014)
        box((x, -0.075, 1.38), (0.74, 0.02, 1.26), "paint", bevel=0.008)
        bolts([(x + dx, -0.072, z) for dx in (-0.42, 0.42) for z in (0.7, 2.06)], "steel", r=0.016, h=0.016)


def wall():
    wall_shell()
    wall_panels()
    text("C-02", (-0.49, -0.086, 1.85), 0.16, "stencil", depth=0.002)


def wall_pipes():
    wall_shell()
    wall_panels()
    # vertical conduits, a junction box and a valve
    for x, r in ((-0.15, 0.055), (0.0, 0.045), (0.15, 0.06)):
        cyl((x, -0.17, 1.6), r, 3.2, "steel" if r != 0.045 else "trim", verts=12, bevel=0.0)
        for z in (0.75, 1.9, 2.95):
            box((x, -0.12, z), (r * 2.6, 0.12, 0.06), "frame", bevel=0.006)
    box((0.58, -0.17, 1.28), (0.42, 0.2, 0.56), "frame", bevel=0.02)
    box((0.58, -0.275, 1.28), (0.34, 0.02, 0.46), "light", bevel=0.008)
    box((0.48, -0.29, 1.44), (0.05, 0.01, 0.05), "amber", bevel=0.002)
    box((0.56, -0.29, 1.44), (0.05, 0.01, 0.05), "green", bevel=0.002)
    text("PWR 4B", (0.62, -0.288, 1.2), 0.08, "dark", depth=0.002)
    for k in range(3):
        box((0.58, -0.288, 1.06 + k * 0.035), (0.26, 0.004, 0.012), "hazard", bevel=0.0)
    cyl((-0.6, -0.26, 1.15), 0.12, 0.05, "red", rot=(90, 0, 0), verts=20, bevel=0.006)
    cyl((-0.6, -0.2, 1.15), 0.035, 0.14, "steel", rot=(90, 0, 0), verts=10)


def wall_window():
    wall_shell(window=True)
    # armoured frame (deep jambs with chamfered inner faces)
    W, Z0, Z1 = 1.72, 0.92, 3.3
    box((0, -0.06, Z0 - 0.06), (2.0, 0.2, 0.16), "paint", bevel=0.014)
    box((0, -0.12, Z0 - 0.04), (1.9, 0.12, 0.05), "frame", bevel=0.006)
    box((0, -0.06, Z1 + 0.08), (2.0, 0.2, 0.2), "paint", bevel=0.014)
    for s in (-1, 1):
        box((s * (W / 2 + 0.08), -0.06, (Z0 + Z1) / 2), (0.16, 0.2, Z1 - Z0 + 0.3), "paint", bevel=0.014)
        prism([(s * W / 2, Z0), (s * (W / 2 - 0.12), Z0 + 0.12), (s * (W / 2 - 0.12), Z1 - 0.12), (s * W / 2, Z1)][::s], 0.02, 0.62, "frame", bevel=0.006)
    prism([(-W / 2, Z1), (W / 2, Z1), (W / 2 - 0.12, Z1 - 0.12), (-W / 2 + 0.12, Z1 - 0.12)], 0.02, 0.62, "frame", bevel=0.006)
    prism([(-W / 2 + 0.12, Z0 + 0.12), (W / 2 - 0.12, Z0 + 0.12), (W / 2, Z0), (-W / 2, Z0)], 0.02, 0.62, "frame", bevel=0.006)
    # mullion + outer armour louvres (partly open)
    box((0, 0.42, (Z0 + Z1) / 2), (0.09, 0.12, Z1 - Z0), "frame", bevel=0.01)
    for z in (Z1 - 0.06,):
        box((0, 0.66, z), (W + 0.2, 0.08, 0.3), "paint", rot=(-38, 0, 0), bevel=0.012)
    box((0, 0.66, Z0 + 0.05), (W + 0.2, 0.08, 0.24), "paint", rot=(38, 0, 0), bevel=0.012)
    # status lights + label
    for s in (-1, 1):
        box((s * 0.95, -0.17, Z1 + 0.08), (0.05, 0.012, 0.1), "amber", bevel=0.002)
    text("OBS-1", (0, -0.165, Z1 + 0.1), 0.11, "stencil", depth=0.002)
    # angled upper panel above the window
    box((0, -0.14, 3.65), (1.94, 0.09, 0.45), "paint", rot=(14, 0, 0), bevel=0.014)


def wall_window_glass():
    box((0, 0.32, 2.11), (1.72, 0.02, 2.38), "glass", bevel=0.0)


def rib():
    """Structural frame standing proud of the bulkhead at module joints."""
    prism([(-0.2, 0), (0.2, 0), (0.2, 2.3), (0.17, H), (-0.17, H), (-0.2, 2.3)], -0.42, 0.05, "frame", bevel=0.02)
    box((0, -0.2, 0.15), (0.5, 0.56, 0.3), "frame", bevel=0.03)
    box((0, -0.2, 0.42), (0.44, 0.47, 0.24), "hazard", bevel=0.01)
    box((0, -0.43, 1.4), (0.26, 0.05, 1.5), "paint", bevel=0.012)
    box((0, -0.455, 2.55), (0.12, 0.03, 0.34), "amber", bevel=0.006)
    box((0, -0.44, 2.55), (0.2, 0.03, 0.44), "frame", bevel=0.006)
    # hydraulic strut
    cyl((0, -0.5, 3.15), 0.05, 1.1, "frame", rot=(-18, 0, 0), verts=12)
    cyl((0, -0.6, 3.5), 0.03, 0.8, "steel", rot=(-18, 0, 0), verts=10)
    bolts([(x, -0.43, z) for x in (-0.09, 0.09) for z in (0.75, 2.05)], "steel", r=0.018, h=0.02)
    text("07", (0, -0.457, 1.9), 0.14, "stencil", depth=0.002)


def corner():
    prism([(-0.45, 0), (0.45, 0), (0.45, H), (-0.45, H)], -0.45, 0.45, "frame", bevel=0.03)
    box((0, 0, 0.3), (0.96, 0.96, 0.6), "hazard", bevel=0.02)
    box((-0.25, -0.25, 2.4), (0.5, 0.5, 0.1), "trim", bevel=0.01)
    for z in (1.2, 3.2):
        box((-0.32, -0.32, z), (0.4, 0.4, 0.06), "steel", bevel=0.01)


# ---------------------------------------------------------------- cutaway side (seen from the outside by the camera)

CUT_H = 1.15


def cut_wall():
    """Sectioned bulkhead: interior panel, insulation, structure and outer armour, with the hull face outside (+Y)."""
    box((0, 0.03, CUT_H / 2), (2.0, 0.06, CUT_H), "paint", bevel=0.006)
    box((0, 0.17, CUT_H / 2 - 0.01), (2.0, 0.22, CUT_H - 0.02), "insul", bevel=0.0)
    for x in (-0.7, 0.0, 0.7):
        box((x, 0.17, CUT_H / 2), (0.08, 0.22, CUT_H), "frame", bevel=0.004)
    box((0, 0.35, CUT_H / 2), (2.0, 0.14, CUT_H), "frame", bevel=0.004)
    box((0, 0.48, CUT_H / 2 - 0.02), (2.0, 0.12, CUT_H - 0.04), "paint", bevel=0.01)
    # outer armour plates (camera side)
    for x in (-0.5, 0.5):
        box((x, 0.565, 0.55), (0.96, 0.06, 0.95), "paint", bevel=0.015)
        box((x, 0.6, 0.55), (0.7, 0.02, 0.62), "paint", bevel=0.008)
        bolts([(x + dx, 0.6, z) for dx in (-0.42, 0.42) for z in (0.14, 0.96)], "steel", r=0.016, h=0.016, axis="+Y")
    # section cap: dark cut face with thin glowing edge lines
    box((0, 0.3, CUT_H + 0.004), (2.0, 0.58, 0.008), "dark", bevel=0.0)
    for y in (0.012, 0.585):
        box((0, y, CUT_H + 0.01), (2.0, 0.018, 0.006), "cut", bevel=0.0)
    # interior side: low trim + light
    box((0, -0.02, 0.18), (2.0, 0.06, 0.36), "frame", bevel=0.006)
    box((0, -0.05, 0.42), (1.6, 0.012, 0.02), "cyan", bevel=0.002)


def cut_corner():
    box((0, 0, CUT_H / 2), (0.62, 0.62, CUT_H), "frame", bevel=0.01)
    box((0, 0, CUT_H + 0.004), (0.62, 0.62, 0.008), "dark", bevel=0.0)
    box((0.12, 0.12, 0.55), (0.42, 0.42, 0.9), "paint", bevel=0.02)
    box((0.27, 0.27, 0.9), (0.1, 0.1, 0.1), "red", bevel=0.01)


def hull_skirt():
    """Outer hull below deck level, outside the cutaway wall (inner edge at y = 0.62, deck at z = 0)."""
    segs = [(0.62, 0.0), (1.1, -0.7), (1.25, -1.6), (0.95, -2.9), (0.2, -4.0)]
    for i in range(len(segs) - 1):
        (y0, z0), (y1, z1) = segs[i], segs[i + 1]
        mid = ((y0 + y1) / 2, (z0 + z1) / 2)
        ln = math.hypot(y1 - y0, z1 - z0)
        ang = math.degrees(math.atan2(y1 - y0, -(z1 - z0)))   # tilt from vertical
        for x in (-0.5, 0.5):
            box((x, mid[0], mid[1]), (0.985, 0.12, ln + 0.02), "paint", rot=(-ang, 0, 0), bevel=0.015)
        box((0, mid[0] - 0.07 * math.cos(rad(ang)), mid[1]), (2.0, 0.1, ln), "frame", rot=(-ang, 0, 0), bevel=0.0)
    # rib flanges and a band of running lights
    for x in (-0.99, 0.99):
        for i in range(len(segs) - 1):
            (y0, z0), (y1, z1) = segs[i], segs[i + 1]
            ln = math.hypot(y1 - y0, z1 - z0); ang = math.degrees(math.atan2(y1 - y0, -(z1 - z0)))
            box((x, (y0 + y1) / 2 + 0.05, (z0 + z1) / 2), (0.06, 0.12, ln + 0.04), "frame", rot=(-ang, 0, 0), bevel=0.008)
    box((0, 1.2, -1.1), (1.6, 0.08, 0.06), "trim", rot=(-9, 0, 0), bevel=0.006)
    cyl((0.62, 1.27, -1.3), 0.05, 0.05, "red", rot=(-90, 0, 0), verts=12)
    box((-0.4, 1.24, -2.1), (0.6, 0.08, 0.32), "dark", rot=(8, 0, 0), bevel=0.01)
    for i in range(4):
        box((-0.4, 1.27, -2.22 + i * 0.08), (0.56, 0.05, 0.025), "frame", rot=(8, 0, 0), bevel=0.0)
    text("ZC-07", (0.25, 1.33, -1.6), 0.3, "stencil", rot=(90, 0, 180), depth=0.003)


# ---------------------------------------------------------------- stash locker (back against the wall at y = +0.45)

def stash_locker():
    W, D, HT = 3.0, 0.9, 2.7
    # carcass
    for s in (-1, 1):
        box((s * (W / 2 - 0.09), 0, HT / 2), (0.18, D, HT), "frame", bevel=0.02)
        box((s * (W / 2 - 0.09), -D / 2 + 0.02, 1.25), (0.2, 0.06, 2.1), "hazard", bevel=0.008)
        box((s * (W / 2 + 0.03), 0.05, 1.35), (0.06, 0.6, 2.2), "paint", bevel=0.01)
        bolts([(s * (W / 2 + 0.065), y, z) for y in (-0.15, 0.25) for z in (0.5, 1.4, 2.3)], "steel", r=0.018, h=0.02, axis="+X" if s > 0 else "-X")
    box((0, 0, HT - 0.09), (W, D, 0.18), "frame", bevel=0.02)
    box((0, -0.02, 0.1), (W - 0.1, D - 0.1, 0.2), "dark", bevel=0.01)
    box((0, 0.42, HT / 2), (W, 0.06, HT), "frame", bevel=0.0)
    box((0, 0.0, 0.205), (W - 0.36, D - 0.06, 0.03), "steel", bevel=0.004)
    # header sign
    box((0, -0.42, HT - 0.09), (2.1, 0.06, 0.2), "dark", bevel=0.008)
    text("STASH  //  SECURE STORAGE", (0, -0.452, HT - 0.09), 0.12, "amber", depth=0.002)
    for s in (-1, 1):
        box((s * 1.18, -0.45, HT - 0.09), (0.12, 0.02, 0.05), "cyan", bevel=0.002)
    # drawer columns: 2 left, console, 2 right
    cols = [-1.1, -0.48, 0.48, 1.1]
    rows = [0.47, 1.0, 1.53, 2.06]
    cw, rh = 0.6, 0.5
    for ci, x in enumerate(cols):
        for ri, z in enumerate(rows):
            box((x, -0.36, z), (cw + 0.02, 0.1, rh + 0.02), "dark", bevel=0.004)     # opening shadow
            if (ci, ri) == (1, 1):
                continue        # the animated drawer sits here (StashDrawer)
            drawer_front(x, z, cw, rh, f"{'ABCD'[ci]}{ri + 1}", ri)
    # centre console column
    box((0, -0.18, 1.3), (0.34, 0.5, 2.25), "frame", bevel=0.02)
    box((0, -0.43, 1.62), (0.3, 0.04, 0.36), "dark", rot=(-8, 0, 0), bevel=0.01)
    box((0, -0.455, 1.62), (0.25, 0.01, 0.3), "screen", rot=(-8, 0, 0), bevel=0.002)
    box((0, -0.44, 1.12), (0.22, 0.03, 0.26), "dark", bevel=0.01)
    box((0, -0.457, 1.12), (0.16, 0.006, 0.2), "cyan", bevel=0.002)       # palm scanner
    for i in range(3):
        for j in range(3):
            box((-0.06 + i * 0.06, -0.445, 0.72 + j * 0.06), (0.045, 0.02, 0.045), "light", bevel=0.004)
    box((0, -0.44, 2.12), (0.24, 0.02, 0.05), "amber", bevel=0.003)
    text("SEC-7", (0, -0.443, 2.22), 0.07, "stencil", depth=0.002)
    # conduits up into the bulkhead
    for x, r, k in ((-1.0, 0.07, "steel"), (-0.82, 0.05, "rubber"), (0.9, 0.08, "trim"), (1.08, 0.045, "rubber")):
        pipe([(x, 0.1, HT - 0.05), (x, 0.1, HT + 0.5), (x, 0.42, HT + 0.9), (x, 0.42, 4.15)], r, k)
        box((x, 0.1, HT + 0.02), (r * 3, r * 3, 0.06), "frame", bevel=0.008)
    box((-0.2, 0.25, HT + 0.12), (0.9, 0.35, 0.2), "frame", bevel=0.02)
    box((-0.2, 0.07, HT + 0.12), (0.7, 0.02, 0.06), "amber", bevel=0.003)


def drawer_front(x, z, w, h, label, row, y=-0.4):
    box((x, y, z), (w - 0.02, 0.06, h - 0.02), "light" if row % 2 == 0 else "paint", bevel=0.014)
    box((x, y - 0.035, z - 0.02), (w - 0.14, 0.012, h - 0.16), "paint" if row % 2 == 0 else "light", bevel=0.006)
    box((x, y - 0.06, z + h / 2 - 0.09), (w - 0.18, 0.035, 0.04), "steel", bevel=0.008)      # handle
    for s in (-1, 1):
        box((x + s * (w / 2 - 0.1), y - 0.05, z + h / 2 - 0.09), (0.035, 0.03, 0.05), "frame", bevel=0.005)
    box((x - w / 2 + 0.11, y - 0.044, z - h / 2 + 0.1), (0.12, 0.006, 0.07), "stencil", bevel=0.002)
    text(label, (x - w / 2 + 0.11, y - 0.048, z - h / 2 + 0.1), 0.06, "dark", depth=0.0015)
    box((x + w / 2 - 0.09, y - 0.042, z - h / 2 + 0.1), (0.04, 0.006, 0.025), "green" if (hash(label) % 3) else "amber", bevel=0.002)


def stash_drawer():
    """The drawer that slides out when the stash opens: front at y = -0.4 (closed), tray behind it."""
    w, h = 0.6, 0.5
    drawer_front(0, 0, w, h, "B2", 1, y=0.0)
    box((0, 0.38, -0.05), (w - 0.06, 0.72, 0.02), "steel", bevel=0.004)
    for s in (-1, 1):
        box((s * (w / 2 - 0.04), 0.38, 0.02), (0.02, 0.72, 0.16), "frame", bevel=0.004)
    box((0, 0.73, 0.02), (w - 0.06, 0.02, 0.16), "frame", bevel=0.004)
    # contents: sealed cases + an ingot
    box((-0.12, 0.25, 0.04), (0.22, 0.3, 0.16), "trim", bevel=0.015)
    box((0.13, 0.3, 0.02), (0.2, 0.18, 0.12), "light", bevel=0.012)
    box((0.12, 0.56, 0.0), (0.24, 0.12, 0.08), "amber", bevel=0.01)
    box((-0.1, 0.58, 0.01), (0.18, 0.2, 0.1), "frame", bevel=0.01)


# ---------------------------------------------------------------- drop bay

def launch_pad():
    cyl((0, 0, -0.1), 3.05, 0.2, "frame", verts=72, bevel=0.01)
    ring((0, 0, 0.02), 3.0, 2.72, 0.04, "hazardR", verts=72)
    ring((0, 0, 0.035), 2.72, 2.42, 0.07, "deck", verts=72)
    ring((0, 0, 0.0), 2.42, 1.25, 0.04, "frame", verts=72)
    # iris leaves
    for i in range(10):
        a = 2 * math.pi * i / 10
        pts = []
        for r, da in ((1.2, 0.0), (2.4, 0.08), (2.4, 0.62), (1.2, 0.56)):
            for z in (0.0, 0.035):
                pts.append((r * math.cos(a + da), r * math.sin(a + da), z))
        hull(pts, "steel" if i % 2 else "paint", bevel=0.006)
    # light studs around the rim
    for i in range(24):
        a = 2 * math.pi * (i + 0.5) / 24
        cyl((2.57 * math.cos(a), 2.57 * math.sin(a), 0.075), 0.035, 0.02, "cyan", verts=10, bevel=0.003)
    # four clamp bases (arms are LaunchClamp)
    for i in range(4):
        a = 2 * math.pi * (i + 0.5) / 4
        c, s = math.cos(a), math.sin(a)
        box((2.86 * c, 2.86 * s, 0.12), (0.62, 0.5, 0.24), "frame", rot=(0, 0, math.degrees(a)), bevel=0.03)
        box((3.0 * c, 3.0 * s, 0.25), (0.1, 0.36, 0.04), "amber", rot=(0, 0, math.degrees(a)), bevel=0.004)
    text("DROP 01", (0, -2.86, 0.045), 0.18, "stencil", rot=(0, 0, 0), depth=0.002)


def launch_pad_core():
    cyl((0, 0, 0.0), 1.25, 0.06, "dark", verts=64, bevel=0.006)
    ring((0, 0, 0.035), 1.18, 1.02, 0.02, "cyan", verts=64, bevel=0.002)
    ring((0, 0, 0.035), 0.72, 0.62, 0.02, "cyan", verts=48, bevel=0.002)
    cyl((0, 0, 0.03), 0.4, 0.03, "steel", verts=32, bevel=0.004)
    for i in range(6):
        a = 2 * math.pi * i / 6
        box((0.86 * math.cos(a), 0.86 * math.sin(a), 0.04), (0.2, 0.05, 0.02), "cyan", rot=(0, 0, math.degrees(a)), bevel=0.002)


def launch_clamp():
    """One docking clamp arm (origin at its hinge, pointing towards -X = pad centre)."""
    box((0, 0, 0.15), (0.34, 0.36, 0.3), "frame", bevel=0.02)
    cyl((0, 0, 0.3), 0.12, 0.42, "steel", rot=(90, 0, 0), verts=16)
    box((-0.45, 0, 0.62), (1.0, 0.24, 0.18), "paint", rot=(0, -38, 0), bevel=0.02)
    box((-0.45, 0, 0.62), (0.7, 0.26, 0.06), "hazard", rot=(0, -38, 0), bevel=0.006)
    box((-0.9, 0, 0.88), (0.3, 0.3, 0.22), "frame", bevel=0.03)
    box((-1.05, 0, 0.78), (0.12, 0.26, 0.32), "rubber", bevel=0.02)
    box((-0.95, -0.155, 0.92), (0.08, 0.01, 0.05), "amber", bevel=0.002)
    cyl((-0.3, 0.17, 0.42), 0.04, 0.6, "steel", rot=(0, -60, 0), verts=10)


def pod_gantry():
    """Cantilever crane on the east bulkhead holding the drop pod over the pad. Wall face at y = 0, arm towards -Y."""
    reach, top = 4.6, 5.6
    for x in (-1.0, 1.0):
        prism([(x - 0.22, 0), (x + 0.22, 0), (x + 0.22, top + 0.5), (x - 0.22, top + 0.5)], -0.5, 0.0, "frame", bevel=0.02)
        box((x, -0.27, 0.4), (0.5, 0.56, 0.8), "hazard", bevel=0.015)
        box((x, -0.52, 2.6), (0.08, 0.03, 0.5), "amber", bevel=0.004)
    # truss arm: two chords + diagonals on each side
    for x in (-0.55, 0.55):
        box((x, -reach / 2 - 0.2, top), (0.16, reach + 0.6, 0.18), "paint", bevel=0.015)
        box((x, -reach / 2 - 0.2, top - 0.8), (0.16, reach + 0.2, 0.16), "paint", bevel=0.015)
        n = 5
        for i in range(n):
            y0 = -0.2 - i * (reach / n)
            y1 = y0 - reach / n
            ang = math.degrees(math.atan2(0.8, reach / n)) * (1 if i % 2 else -1)
            box((x, (y0 + y1) / 2, top - 0.4), (0.07, math.hypot(0.8, reach / n), 0.07), "steel", rot=(ang, 0, 0), bevel=0.0)
        box((x * 1.18, -reach / 2, top - 0.4), (0.02, reach * 0.8, 0.12), "hazard", bevel=0.003)
    for i in range(6):
        box((0, -0.2 - i * reach / 5, top - 0.8), (1.25, 0.1, 0.1), "frame", bevel=0.01)
        box((0, -0.2 - i * reach / 5, top), (1.25, 0.1, 0.1), "frame", bevel=0.01)
    # winch head over the pad + cables + clamp ring
    box((0, -reach, top - 0.25), (1.6, 1.2, 0.7), "paint", bevel=0.04)
    box((0, -reach, top + 0.15), (1.3, 0.9, 0.14), "frame", bevel=0.02)
    cyl((0, -reach, top - 0.25), 0.3, 1.7, "steel", rot=(0, 90, 0), verts=24)
    box((0, -reach - 0.61, top - 0.25), (0.6, 0.02, 0.12), "amber", bevel=0.003)
    text("HOIST 2", (0, -reach - 0.615, top - 0.05), 0.14, "stencil", depth=0.002)
    for i in range(4):
        a = 2 * math.pi * (i + 0.5) / 4
        cx, cy = 0.55 * math.cos(a), -reach + 0.55 * math.sin(a)
        cyl((cx, cy, top - 1.15), 0.025, 1.3, "steel", verts=8, bevel=0.0)
    ring((0, -reach, top - 1.85), 0.95, 0.72, 0.16, "paint", verts=48)
    for i in range(6):
        a = 2 * math.pi * i / 6
        box((0.84 * math.cos(a), -reach + 0.84 * math.sin(a), top - 1.95), (0.16, 0.16, 0.2), "frame", bevel=0.02)
    for i in range(3):
        box((0, -1.3 - i * 1.2, top - 0.92), (0.4, 0.15, 0.04), "amber", bevel=0.004)


PIECES = [
    ("FloorPlate", floor_plate), ("FloorGrate", floor_grate), ("FloorMarked", floor_marked),
    ("Wall", wall), ("WallPipes", wall_pipes), ("WallWindow", wall_window), ("WallWindowGlass", wall_window_glass),
    ("Rib", rib), ("Corner", corner),
    ("CutWall", cut_wall), ("CutCorner", cut_corner), ("HullSkirt", hull_skirt),
    ("StashLocker", stash_locker), ("StashDrawer", stash_drawer),
    ("LaunchPad", launch_pad), ("LaunchPadCore", launch_pad_core), ("LaunchClamp", launch_clamp),
    ("PodGantry", pod_gantry),
]


# ============================================================================ bake

def setup_cycles(samples):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    prefs = bpy.context.preferences.addons["cycles"].preferences
    for kind in ("METAL", "OPTIX", "CUDA", "HIP", "ONEAPI"):
        try:
            prefs.compute_device_type = kind
        except TypeError:
            continue
        prefs.get_devices()
        devs = [d for d in prefs.devices if d.type == kind]
        if devs:
            for d in prefs.devices:
                d.use = d.type == kind
            sc.cycles.device = "GPU"
            break
    sc.cycles.samples = samples
    sc.cycles.use_denoising = False


def unwrap(o):
    for ob in bpy.context.scene.objects:
        ob.select_set(False)
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=rad(58), island_margin=0.004, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.004, rotate=True)
    bpy.ops.object.mode_set(mode="OBJECT")


def bake_piece(o, size):
    unwrap(o)
    setup_cycles(24)
    sc = bpy.context.scene
    sc.render.bake.margin = 8
    used = {s.material.name[2:] for s in o.material_slots if s.material}
    imgs = {}
    for ch in ("albedo", "metal", "smooth", "emission", "normal"):
        if ch == "emission" and not any("e" in KINDS[k] for k in used):
            continue
        im = bpy.data.images.new(f"{o.name}_{ch}", size, size, alpha=False, float_buffer=False)
        if ch != "albedo" and ch != "emission":
            im.colorspace_settings.name = "Non-Color"
        set_pass(ch, im)
        if ch == "normal":
            sc.render.bake.normal_space = "TANGENT"
            bpy.ops.object.bake(type="NORMAL", margin=8, use_clear=True)
        else:
            bpy.ops.object.bake(type="EMIT", margin=8, use_clear=True)
        imgs[ch] = im
        print(f"[zcom] {o.name}: baked {ch}")

    def save(name, im=None, arr=None, noncolor=False):
        if arr is not None:
            im = bpy.data.images.new(name, size, size, alpha=True)
            if noncolor:
                im.colorspace_settings.name = "Non-Color"
            im.pixels.foreach_set(arr.astype(np.float32).ravel())
        im.filepath_raw = os.path.join(TEX_DIR, name + ".png"); im.file_format = "PNG"; im.save()
        return im
    base = save(f"{o.name}_BaseColor", imgs["albedo"])
    nrm = save(f"{o.name}_Normal", imgs["normal"])
    px = lambda im: np.array(im.pixels[:], dtype=np.float32).reshape(-1, 4)
    ms = np.zeros((size * size, 4), np.float32)
    mt = px(imgs["metal"])[:, 0]
    ms[:, 0] = ms[:, 1] = ms[:, 2] = np.clip(mt, 0, 1)
    ms[:, 3] = np.clip(px(imgs["smooth"])[:, 0], 0.03, 0.85)
    msi = save(f"{o.name}_MetallicSmoothness", arr=ms, noncolor=True)
    emi = save(f"{o.name}_Emission", imgs["emission"]) if "emission" in imgs else None
    return base, nrm, msi, emi


def preview_material(o, base, nrm, msi, emi):
    m = bpy.data.materials.new("P_" + o.name); m.use_nodes = True
    nt = m.node_tree
    p = nt.nodes["Principled BSDF"]
    def tex(im, noncolor=False):
        t = nt.nodes.new("ShaderNodeTexImage"); t.image = im
        if noncolor:
            im.colorspace_settings.name = "Non-Color"
        return t
    nt.links.new(tex(base).outputs["Color"], p.inputs["Base Color"])
    nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(tex(nrm, True).outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs[0], p.inputs["Normal"])
    ms = tex(msi, True)
    sep = nt.nodes.new("ShaderNodeSeparateColor"); nt.links.new(ms.outputs["Color"], sep.inputs[0])
    nt.links.new(sep.outputs[0], p.inputs["Metallic"])
    inv = nt.nodes.new("ShaderNodeMath"); inv.operation = "SUBTRACT"; inv.inputs[0].default_value = 1.0
    nt.links.new(ms.outputs["Alpha"], inv.inputs[1]); nt.links.new(inv.outputs[0], p.inputs["Roughness"])
    if emi:
        nt.links.new(tex(emi).outputs["Color"], p.inputs["Emission Color"]); p.inputs["Emission Strength"].default_value = 6.0
    o.data.materials.clear(); o.data.materials.append(m)


def glass_material(o):
    m = bpy.data.materials.new("P_Glass"); m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (0.6, 0.85, 1.0, 1)
    p.inputs["Roughness"].default_value = 0.02
    p.inputs["Transmission Weight"].default_value = 1.0
    o.data.materials.clear(); o.data.materials.append(m)


# ============================================================================ preview scene

def place(name, loc, yaw=0.0):
    src = bpy.data.objects[name]
    o = src.copy(); o.data = src.data
    bpy.context.scene.collection.objects.link(o)
    o.location = V(loc); o.rotation_euler = (0, 0, rad(yaw)); o.hide_render = False
    return o


def preview(built):
    sc = bpy.context.scene
    set_pass("normal", None)
    for o in built.values():
        o.hide_render = True
    have = lambda n: n in built
    # deck 16 x 12 (Blender X east, Y north). North wall at y = 6 (room side faces -Y -> rotate 180),
    # east wall at x = 8, cutaway walls south (y = -6) and west (x = -8).
    for i, x in enumerate((-6, -2, 2, 6)):
        for j, y in enumerate((-4, 0, 4)):
            n = "FloorGrate" if (i, j) == (1, 0) else "FloorMarked" if (i, j) == (2, 1) else "FloorPlate"
            if have(n):
                place(n, (x, y, 0), 90 * ((i + j) % 4))
    xs = [-7, -5, -3, -1, 1, 3, 5, 7]
    for k, x in enumerate(xs):
        n = "WallWindow" if k in (3, 4) else "WallPipes" if k == 6 else "Wall"
        if have(n):
            place(n, (x, 6, 0), 0)
        if n == "WallWindow" and have("WallWindowGlass"):
            place("WallWindowGlass", (x, 6, 0), 0)
        if have("CutWall"):
            place("CutWall", (x, -6, 0), 180)
    for k, y in enumerate((-5, -3, -1, 1, 3, 5)):
        if have("Wall"):
            place("Wall", (8, y, 0), -90)
        if have("CutWall"):
            place("CutWall", (-8, y, 0), 90)
    for x in (-6, -2, 2, 6):
        if have("Rib"):
            place("Rib", (x, 6, 0), 0)
    for y in (-4, 0, 4):
        if have("Rib"):
            place("Rib", (8, y, 0), -90)
    if have("Corner"):
        place("Corner", (7.55, 5.55, 0), 0)
    if have("CutCorner"):
        place("CutCorner", (-8.3, -6.3, 0), 0)
    if have("HullSkirt"):
        for x in xs:
            place("HullSkirt", (x, -6, 0), 180)
        for y in (-5, -3, -1, 1, 3, 5):
            place("HullSkirt", (-8, y, 0), 90)
    if have("StashLocker"):
        place("StashLocker", (-4.0, 5.55, 0), 0)
        if have("StashDrawer"):
            place("StashDrawer", (-4.0 - 0.48, 5.55 - 0.4 - 0.45, 1.0), 0)
    if have("LaunchPad"):
        place("LaunchPad", (3.4, -0.5, 0))
        if have("LaunchPadCore"):
            place("LaunchPadCore", (3.4, -0.5, 0))
        if have("LaunchClamp"):
            for i in range(4):
                a = 2 * math.pi * (i + 0.5) / 4
                place("LaunchClamp", (3.4 + 2.86 * math.cos(a), -0.5 + 2.86 * math.sin(a), 0.2), math.degrees(a))
    if have("PodGantry"):
        place("PodGantry", (8, -0.5, 0), -90)

    sc.render.engine = "CYCLES"
    setup_cycles(96)
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    try:
        sc.view_settings.view_transform = "AgX"; sc.view_settings.look = "AgX - Medium High Contrast"
    except Exception:
        pass
    w = bpy.data.worlds.new("Space"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.01, 0.012, 0.02, 1)
    w.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0

    def light(kind, loc, energy, color, size=1.0, rot=None, spot=None):
        l = bpy.data.lights.new("L", kind); l.energy = energy; l.color = color
        if kind == "AREA":
            l.size = size
        if kind == "SPOT":
            l.spot_size = rad(spot or 100); l.spot_blend = 0.6
        o = link(bpy.data.objects.new("L", l)); o.location = V(loc)
        if rot:
            o.rotation_euler = Euler([rad(a) for a in rot])
        return o
    for x in (-5, 0, 5):
        for y in (-2.5, 2.5):
            light("AREA", (x, y, 5.5), 450, (0.85, 0.92, 1.0), 2.0)
    light("POINT", (-4, 4.6, 2.2), 120, (1.0, 0.62, 0.25))
    light("POINT", (4, -0.5, 1.0), 200, (0.3, 0.8, 1.0))
    light("SUN", (0, 0, 10), 1.2, (1.0, 0.85, 0.7), rot=(50, 0, 30))
    cd = bpy.data.cameras.new("Cam"); cd.lens = 50
    cam = link(bpy.data.objects.new("Cam", cd)); sc.camera = cam
    # game camera: pitch 52 deg, looking from the south-west (yaw 45)
    pitch, yaw, dist = 52, 45, 30
    target = V((0, 0, 0.8))
    d = V((-math.sin(rad(yaw)) * math.cos(rad(pitch)), -math.cos(rad(yaw)) * math.cos(rad(pitch)), math.sin(rad(pitch))))
    cam.location = target + d * dist
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = os.path.join(PREVIEWS, "ShipKit_room.png")
    bpy.ops.render.render(write_still=True)
    # close-up on the stash
    target = V((-4, 4.8, 1.3)); dist = 9; pitch = 30
    d = V((-math.sin(rad(yaw)) * math.cos(rad(pitch)), -math.cos(rad(yaw)) * math.cos(rad(pitch)), math.sin(rad(pitch))))
    d = V((0.25, -1, 0.45)).normalized()
    cam.location = target + d * dist
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = os.path.join(PREVIEWS, "ShipKit_stash.png")
    bpy.ops.render.render(write_still=True)
    print("[zcom] previews written")


# ============================================================================ main

if __name__ == "__main__":
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    for k, p in KINDS.items():
        make_kind(k, p)
    built = {}
    for name, fn in PIECES:
        fn()
        o = finish(name)
        built[name] = o
        print(f"[zcom] built {name}: {len(o.data.polygons)} faces")
    export_names = [n for n, _ in PIECES]
    for name in export_names:
        o = built[name]
        if name.endswith("Glass"):
            unwrap(o); glass_material(o); continue
        if ONLY and name not in ONLY:
            # reuse previously baked textures for the preview
            ims = []
            for ch in ("BaseColor", "Normal", "MetallicSmoothness", "Emission"):
                p = os.path.join(TEX_DIR, f"{name}_{ch}.png")
                ims.append(bpy.data.images.load(p) if os.path.exists(p) else None)
            unwrap(o)
            if ims[0]:
                preview_material(o, *ims)
            continue
        maps = bake_piece(o, TEX.get(name, 1024))
        preview_material(o, *maps)
    if True:
        for o in bpy.data.objects:
            o.select_set(o.name in built)
        path = os.path.join(MODELS, "ShipKit.fbx")
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                                 apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                                 mesh_smooth_type="FACE", use_mesh_modifiers=True, bake_anim=False, path_mode="STRIP")
        print(f"[zcom] exported {path}")
    preview(built)
    print("[zcom] ship kit done")
