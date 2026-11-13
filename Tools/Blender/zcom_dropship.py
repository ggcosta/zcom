"""
The operative's landed dropship ZC-07 "KESTREL" (the base: stash aboard, raids start down the ramp, boarding extracts)
-> Assets/ZCOM/Resources/Models/Dropship.fbx + Assets/ZCOM/Resources/Textures/Ship/<Object>_*.png

    blender -b --factory-startup -P Tools/Blender/zcom_dropship.py -- <models_dir> <previews_dir> [Object,Object,...]

Ship coordinates (Blender): origin on the ground under the hull's middle, nose towards -Y, deck top at z = DECK.
Exported in ship coordinates (place every object at the ship's origin) except the instanced parts:
  Engine (nacelle centred on its own origin, starboard; mirror on X for port), LandingGear (origin on the ground),
  StashLocker / StashDrawer (origin at their base, front -Y).

The hull and the interior are modelled together so the interior fits the hull's cross-section, then every part is
sliced at CUT_Z (deck + 1.45 m): Hull_Upper / Interior_Upper are what the game hides when the operative is inside
(XCOM-style cutaway, with dark section caps on the cut), Hull_Lower / Interior_Lower always stay.
Texturing is the same procedural bake as the ship kit (zcom_ship.py): worn paint, AO grime, dust, drip streaks,
bevelled-edge normals, hull plating seams and per-panel tone, hazard stripes, stencils and lights.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import zcom_ship as zs

V = Vector
rad = math.radians
box, cyl, ring, hull, pipe, text, bolts, prism = zs.box, zs.cyl, zs.ring, zs.hull, zs.pipe, zs.text, zs.bolts, zs.prism

DECK = 1.7
CUT_Z = DECK + 1.45
Y_FRONT, Y_REAR = -6.5, 8.6          # main fuselage
Y_BULK, Y_PART = -6.2, 0.4           # cockpit bulkhead, crew / hold partition
WALL_X = 3.55                        # interior lining
CEIL_Z = 5.05

# hull cross-section (right half): belly, belly edge, lower chine, upper chine, shoulder, roof
PROFILE = [(0.0, 0.95), (2.9, 0.95), (4.5, 2.1), (4.75, 3.6), (3.6, 5.4), (0.0, 5.75)]
SEG_KIND = ["hullDark", "hullDark", "hull", "hull", "hull"]

zs.KINDS.update({
    "hull":      dict(c=(0.26, 0.27, 0.285), m=0.35, r=0.5, wear=1.0, dust=1.2, panels=True, panel_size=(1.6, 2.4, 1.15)),
    "hullDark":  dict(c=(0.07, 0.072, 0.08), m=0.4, r=0.55, wear=0.9, dust=1.4, panels=True, panel_size=(1.2, 1.2, 0.9)),
    "engineGlow": dict(c=(0.05, 0.03, 0.01), m=0.0, r=0.4, wear=0.0, dust=0.0, e=(1.0, 0.45, 0.12), es=1.0),
    "lamp":      dict(c=(0.6, 0.62, 0.66), m=0.0, r=0.2, wear=0.0, dust=0.0, e=(0.85, 0.92, 1.0), es=1.0),
    "seat":      dict(c=(0.09, 0.085, 0.08), m=0.0, r=0.75, wear=0.3, dust=0.6),
    "strap":     dict(c=(0.55, 0.22, 0.04), m=0.0, r=0.8, wear=0.2, dust=0.6),
    "cap":       dict(c=(0.03, 0.03, 0.034), m=0.2, r=0.7, wear=0.0, dust=0.0, e=(0.9, 0.3, 0.06), es=0.035),
})
TEX = {"Hull_Upper": 4096, "Hull_Lower": 4096, "Interior_Lower": 4096, "Interior_Upper": 2048, "Engine": 2048,
       "Ramp": 2048, "RearDoor": 2048, "LandingGear": 1024, "Cockpit": 2048, "CargoStack": 2048, "StashLocker": 2048, "StashDrawer": 1024}


# ============================================================================ hull geometry

def section(y):
    """Cross-section at y (right half points). The nose drops and narrows towards a blunt armoured tip."""
    if y >= Y_FRONT:
        return list(PROFILE)
    # loft keys: (y, sx, sz, zc)
    keys = [(Y_FRONT, 1.0, 1.0, 3.0), (-9.0, 0.8, 0.74, 2.9), (-10.9, 0.44, 0.42, 2.6), (-11.6, 0.2, 0.24, 2.45)]
    for (y0, a0, b0, c0), (y1, a1, b1, c1) in zip(keys, keys[1:]):
        if y1 <= y <= y0:
            t = (y0 - y) / (y0 - y1)
            sx, sz, zc = a0 + (a1 - a0) * t, b0 + (b1 - b0) * t, c0 + (c1 - c0) * t
            return [(x * sx, zc + (z - zc) * sz) for x, z in PROFILE]
    return [(x * 0.2, 2.45 + (z - 2.45) * 0.24) for x, z in PROFILE]


def _offset(p, q, d):
    """Outward normal offset of the segment p->q (right half, outward = away from the centre line)."""
    dx, dz = q[0] - p[0], q[1] - p[1]
    n = V((dz, -dx)).normalized() if True else None
    # outward points away from (0, 3.2)
    mid = V(((p[0] + q[0]) / 2, (p[1] + q[1]) / 2))
    if (mid - V((0, 3.2))).dot(n) < 0:
        n = -n
    return n * d


def plate(seg, y0, y1, kind, thick=0.16, gap=0.025, out=0.0, side=1, shrink=0.0):
    """One armour plate on hull segment seg between y0 and y1 (y0 > y1 allowed), mirrored by side."""
    a, b = (y0, y1) if y0 < y1 else (y1, y0)
    a += gap; b -= gap
    pts = []
    for y in (a, b):
        sec = section(y)
        p, q = V(sec[seg]), V(sec[seg + 1])
        d = (q - p)
        L = d.length
        if L < 1e-4:
            continue
        p2, q2 = p + d * (gap + shrink) / L, q - d * (gap + shrink) / L
        n_out = _offset(sec[seg], sec[seg + 1], 1.0)
        for c in (p2, q2):
            for k in (out, out - thick):
                w = c + n_out * k
                pts.append((w.x * side, y, w.y))
    if len(pts) < 8:
        return None
    return hull(pts, kind, bevel=0.012)


def hull_body():
    """Main fuselage + nose: plating over a dark inner shell, belt ribs, rear frame."""
    ys = [Y_FRONT + i * 2.4 for i in range(7)]                    # -6.5 .. 7.9
    ys[-1] = 7.9
    nose = [Y_FRONT, -7.75, -9.0, -10.0, -10.9, -11.6]
    for side in (1, -1):
        for seg in range(5):
            if seg == 0 and side == -1:
                continue                                            # the belly centre strip is shared
            kind = SEG_KIND[seg]
            # inner shell (fills the plate seams)
            for i in range(len(ys) - 1):
                plate(seg, ys[i], ys[i + 1], "frame", thick=0.1, gap=0.0, out=-0.12, side=side)
            for i in range(len(nose) - 1):
                plate(seg, nose[i], nose[i + 1], "frame", thick=0.1, gap=0.0, out=-0.12, side=side)
            # main plates
            for i in range(len(ys) - 1):
                plate(seg, ys[i], ys[i + 1], kind, side=side)
            plate(seg, 7.9, Y_REAR, "frame", thick=0.3, gap=0.0, out=0.08, side=side)       # rear frame ring
            # nose plates (canopy glass replaces the upper ones between -9 and -6.5)
            for i in range(len(nose) - 1):
                y0, y1 = nose[i], nose[i + 1]
                if seg >= 3 and y0 > -9.05:
                    continue
                plate(seg, y0, y1, kind, side=side)
    # belly centre strip (seg 0 spans both sides): mirror it once more for the other half
    for i in range(6):
        pass
    for side in (-1,):
        for i in range(len(ys) - 1):
            plate(0, ys[i], ys[i + 1], "hullDark", side=side)
            plate(0, ys[i], ys[i + 1], "frame", thick=0.1, gap=0.0, out=-0.12, side=side)
        for i in range(len(nose) - 1):
            plate(0, nose[i], nose[i + 1], "hullDark", side=side)
    # belt ribs at the section joints
    for y in ys[1:-1]:
        for side in (1, -1):
            for seg in range(5):
                plate(seg, y - 0.04, y + 0.04, "frame", thick=0.1, gap=0.0, out=0.0, side=side, shrink=-0.02)
    # canopy frame: mullions + sill
    for side in (1, -1):
        for y in (-9.0, -7.75, Y_FRONT):
            for seg in (3, 4):
                plate(seg, y - 0.06, y + 0.06, "frame", thick=0.22, gap=0.0, out=0.05, side=side)
    # nose tip armour + sensor ball
    sec = section(-11.6)
    pts = []
    for x, z in sec:
        for s in (1, -1):
            pts += [(x * s, -11.6, z), (x * s * 0.55, -12.0, 2.45 + (z - 2.45) * 0.55)]
    hull(pts, "hullDark", bevel=0.03)
    cyl((0, -10.6, 1.95), 0.32, 0.4, "dark", verts=24)
    cyl((0, -10.6, 1.72), 0.26, 0.18, "glass", verts=24)
    # chin cannon housing (decorative)
    box((0, -9.6, 1.85), (1.2, 1.6, 0.5), "hullDark", bevel=0.05)
    for s in (-1, 1):
        cyl((s * 0.32, -10.7, 1.8), 0.07, 1.2, "steel", rot=(90, 0, 0), verts=12)


def canopy_glass():
    for side in (1, -1):
        for seg in (3, 4):
            for y0, y1 in ((-9.0, -7.75), (-7.75, Y_FRONT)):
                plate(seg, y0, y1, "glass", thick=0.04, gap=0.07, out=-0.04, side=side)


def hull_details():
    """Wings, tail fins, dorsal spine, vents, lights, lettering."""
    for s in (1, -1):
        # stub wing (root on the upper chine), anhedral tip, hazard leading edge
        root = [(4.55, 0.4, 3.15), (4.55, 5.6, 3.15), (4.55, 0.4, 3.62), (4.55, 5.6, 3.62)]
        tip = [(6.7, 1.6, 3.0), (6.7, 5.3, 3.0), (6.7, 1.6, 3.32), (6.7, 5.3, 3.32)]
        hull([(x * s, y, z) for x, y, z in root + tip], "hullDark", bevel=0.03)
        hull([(4.6 * s, 0.33, 3.4), (6.65 * s, 1.53, 3.18), (4.6 * s, 0.33, 3.5), (6.65 * s, 1.53, 3.25),
              (4.6 * s, 0.5, 3.4), (6.65 * s, 1.7, 3.18), (4.6 * s, 0.5, 3.5), (6.65 * s, 1.7, 3.25)], "hazard", bevel=0.01)
        box((5.6 * s, 4.6, 3.66), (1.4, 0.9, 0.08), "frame", rot=(0, -4 * s, 0), bevel=0.01)
        text("NO STEP", (5.6 * s, 3.2, 3.62), 0.16, "stencil", rot=(0, -4 * s, 180 if s > 0 else 0), depth=0.002)
        # tail fins (canted outward)
        fin = [(1.2, 4.3, 5.5), (1.2, 8.4, 5.4), (1.25, 7.0, 7.4), (1.25, 8.4, 7.6),
               (1.5, 4.3, 5.5), (1.5, 8.4, 5.4), (1.55, 7.0, 7.4), (1.55, 8.4, 7.6)]
        rot = Matrix.Rotation(rad(-18 * s), 3, "Y")
        pts = []
        for x, y, z in fin:
            v = rot @ V((x * s, y, z - 5.5)); pts.append((v.x, v.y, v.z + 5.5))
        hull(pts, "hull", bevel=0.03)
        v = rot @ V((1.6 * s, 8.3, 7.4 - 5.5)); cyl((v.x, v.y, v.z + 5.5), 0.06, 0.12, "red" if s < 0 else "green", verts=10)
        # running lights on the chines, landing lights under the belly
        cyl((4.62 * s, -5.8, 2.8), 0.06, 0.08, "red" if s < 0 else "green", rot=(0, 90, 0), verts=12)
        box((3.2 * s, -3.0, 0.88), (0.5, 0.3, 0.06), "lamp", bevel=0.01)
        box((3.2 * s, 6.5, 0.88), (0.5, 0.3, 0.06), "lamp", bevel=0.01)
        # side vents and RCS blocks
        for y in (-3.0, 3.6):
            box((4.66 * s, y, 2.9), (0.08, 1.4, 0.55), "dark", bevel=0.01)
            for i in range(5):
                box((4.72 * s, y, 2.7 + i * 0.1), (0.05, 1.3, 0.03), "frame", bevel=0.0)
        for y in (-6.0, 7.4):
            box((3.75 * s, y, 5.2), (0.5, 0.5, 0.35), "frame", rot=(0, -40 * s, 0), bevel=0.03)
            for k in range(2):
                cyl((3.95 * s, y - 0.12 + k * 0.24, 5.32), 0.06, 0.2, "dark", rot=(0, 50 * s, 0), verts=10)
        # side hatch (crew door, sealed) + lettering
        box((4.62 * s, -2.0, 2.85), (0.06, 1.3, 1.5), "hullDark", bevel=0.02)
        box((4.66 * s, -2.0, 3.65), (0.04, 1.0, 0.06), "amber", bevel=0.005)
        text("ZC-07", (4.68 * s, -4.4, 2.75), 0.6, "stencil", rot=(90, -5 * s, 90 * s), depth=0.004)
        text("KESTREL", (4.66 * s, -4.4, 2.33), 0.2, "stencil", rot=(90, -5 * s, 90 * s), depth=0.003)
        # layered side armour (sponsons) along the lower chine
        for y0, y1 in ((-5.9, -3.5), (-1.7, 0.7), (3.1, 5.5)):
            plate(2, y0, y1, "hullDark", thick=0.12, gap=0.05, out=0.12, side=s, shrink=0.15)
            plate(2, y0 + 0.3, y1 - 0.3, "hull", thick=0.06, gap=0.0, out=0.17, side=s, shrink=0.35)
        for y0, y1 in ((-4.1, -1.7), (0.7, 3.1)):
            plate(3, y0, y1, "hull", thick=0.08, gap=0.06, out=0.1, side=s, shrink=0.2)
        # belly VTOL thrusters
        for y in (-3.0, 4.2):
            cyl((2.0 * s, y, 0.86), 0.55, 0.2, "frame", verts=24)
            cyl((2.0 * s, y, 0.76), 0.42, 0.06, "engineGlow", verts=24)
        for k in range(5):
            box((4.62 * s, 4.6 + k * 0.2, 2.0), (0.04, 0.1, 0.12), "hazard", bevel=0.0)
    # dorsal spine, sensor dome, antennas
    box((0, 1.0, 5.85), (1.2, 9.0, 0.28), "hullDark", bevel=0.06)
    for y in (-2.5, 0.5, 3.5):
        box((0, y, 6.02), (0.9, 1.6, 0.1), "dark", bevel=0.02)
        for i in range(6):
            box((0, y - 0.6 + i * 0.24, 6.06), (0.8, 0.06, 0.06), "frame", bevel=0.0)
    cyl((0, -4.4, 5.95), 0.5, 0.3, "hullDark", verts=24)
    cyl((0, -4.4, 6.15), 0.42, 0.14, "glass", verts=24)
    cyl((0.6, 5.5, 6.6), 0.025, 1.6, "steel", verts=6, bevel=0.0)
    cyl((-0.6, 5.8, 6.4), 0.02, 1.2, "steel", verts=6, bevel=0.0)
    cyl((0, 7.6, 6.06), 0.1, 0.16, "red", verts=12)
    text("ZCOM", (0, -1.0, 5.995), 0.7, "stencil", rot=(0, 0, 0), depth=0.003)
    # rear door header lights + hazard surround
    for s in (1, -1):
        box((3.45 * s, Y_REAR + 0.05, 3.0), (0.25, 0.12, 2.6), "hazard", bevel=0.01)
        box((3.45 * s, Y_REAR + 0.12, 4.45), (0.2, 0.06, 0.12), "amber", bevel=0.005)
    box((0, Y_REAR + 0.05, 4.62), (7.0, 0.12, 0.28), "hazard", bevel=0.01)


def rear_wall():
    """Rear bulkhead above and around the ramp opening (opening: |x| < 3.3, deck .. 4.4)."""
    # header above the opening (z > 4.4) up to the roof profile
    hdr = [(3.3, 4.4), (4.24, 4.4), (3.6, 5.4), (0.0, 5.75)]
    pts = [(x * s, Y_REAR + dy, z) for x, z in hdr for s in (1, -1) for dy in (-0.15, 0.15)]
    hull(pts, "frame", bevel=0.02)
    # side jambs between the opening (|x| > 3.3) and the hull
    jamb = [(3.3, DECK), (3.94, DECK), (4.5, 2.1), (4.75, 3.6), (4.24, 4.4), (3.3, 4.4)]
    for s in (1, -1):
        hull([(x * s, Y_REAR + dy, z) for x, z in jamb for dy in (-0.15, 0.15)], "frame", bevel=0.02)
    # belly closure below the deck at the rear
    box((0, Y_REAR, 1.3), (6.2, 0.3, 0.7), "frame", bevel=0.02)


# ============================================================================ interior

def interior():
    D = DECK
    # deck substructure + side plates + grated centre channel
    box((0, (Y_BULK + Y_REAR) / 2, D - 0.2), (2 * WALL_X + 0.2, Y_REAR - Y_BULK, 0.4), "frame", bevel=0.0)
    y = Y_BULK
    k = 0
    while y < Y_REAR - 0.1:
        y1 = min(y + 2.0, Y_REAR)
        for s in (1, -1):
            cx = s * (0.72 + (WALL_X - 0.72) / 2)
            box((cx, (y + y1) / 2, D - 0.03), (WALL_X - 0.74, y1 - y - 0.04, 0.06), "deck", bevel=0.01)
            if (k + (s > 0)) % 2 == 0:
                n = 8
                for i in range(n):
                    for j in range(n):
                        px = cx - (WALL_X - 1.0) / 2 + (i + 0.5) * (WALL_X - 1.0) / n
                        py = y + 0.15 + (j + 0.5) * (y1 - y - 0.3) / n
                        box((px, py, D + 0.004), (0.07, 0.016, 0.008), "deck", rot=(0, 0, 45 if (i + j) % 2 else -45), bevel=0.0)
            for py in (y + 0.25, y1 - 0.25):
                cyl((s * (WALL_X - 0.25), py, D + 0.004), 0.06, 0.012, "steel", verts=12, bevel=0.002)     # tie-down
        y = y1
        k += 1
    # centre channel: pit, conduits, lights, grating
    box((0, (Y_BULK + Y_REAR) / 2, D - 0.34), (1.4, Y_REAR - Y_BULK, 0.06), "dark", bevel=0.0)
    for x, r, kd in ((-0.4, 0.08, "steel"), (0.0, 0.06, "trim"), (0.38, 0.09, "paint")):
        cyl((x, (Y_BULK + Y_REAR) / 2, D - 0.31 + r), r, Y_REAR - Y_BULK, kd, rot=(90, 0, 0), verts=12, bevel=0.0)
    yy = Y_BULK + 1.0
    while yy < Y_REAR - 0.5:
        box((0, yy, D - 0.3), (0.5, 0.12, 0.02), "cyan", bevel=0.002)
        yy += 2.0
    for s in (1, -1):
        box((s * 0.72, (Y_BULK + Y_REAR) / 2, D - 0.03), (0.06, Y_REAR - Y_BULK, 0.06), "frame", bevel=0.005)
    n = int((Y_REAR - Y_BULK) / 0.075)
    for i in range(n):
        box((0, Y_BULK + 0.04 + i * 0.075, D - 0.025), (1.38, 0.02, 0.05), "steel", bevel=0.0)
    for x in (-0.35, 0.35):
        box((x, (Y_BULK + Y_REAR) / 2, D - 0.035), (0.03, Y_REAR - Y_BULK, 0.04), "steel", bevel=0.0)
    # ramp threshold hazard band + exit stencil
    box((0, Y_REAR - 0.18, D + 0.003), (2 * WALL_X, 0.3, 0.008), "hazard", bevel=0.0)
    text("EXIT  RAMP", (-2.1, Y_REAR - 0.8, D + 0.005), 0.28, "stencil", rot=(0, 0, 180), depth=0.002)
    text("CARGO HOLD", (2.1, 4.0, D + 0.005), 0.3, "stencil", rot=(0, 0, 90), depth=0.002)

    # side lining: wainscot, panels, ribs, slanted ceiling, ceiling spine
    ribs = [Y_BULK + 0.15 + i * 1.85 for i in range(9) if Y_BULK + 0.15 + i * 1.85 < Y_REAR - 0.2]
    for s in (1, -1):
        x = s * WALL_X
        box((x, (Y_BULK + Y_REAR) / 2, 2.6), (0.14, Y_REAR - Y_BULK, 1.9), "frame", bevel=0.0)          # backing
        for i in range(len(ribs) - 1):
            y0, y1 = ribs[i] + 0.16, ribs[i + 1] - 0.16
            cy, L = (y0 + y1) / 2, y1 - y0
            box((x - s * 0.08, cy, D + 0.5), (0.06, L, 0.9), "paint", bevel=0.012)                        # wainscot
            box((x - s * 0.1, cy, D + 1.0), (0.04, L, 0.08), "trim", bevel=0.006)
            box((x - s * 0.08, cy, 3.0), (0.06, L, 1.1), "light", bevel=0.014)                             # upper panel
            box((x - s * 0.115, cy, 3.0), (0.02, L - 0.3, 0.8), "light", bevel=0.008)
            box((x - s * 0.12, cy, D + 1.12), (0.02, L * 0.7, 0.025), "cyan", bevel=0.002)                # light strip
        # slanted ceiling from the wall top to the spine
        for i in range(len(ribs) - 1):
            y0, y1 = ribs[i] + 0.16, ribs[i + 1] - 0.16
            hull([(x, y0, 3.6), (x, y1, 3.6), (s * 1.3, y0, CEIL_Z), (s * 1.3, y1, CEIL_Z),
                  (x - s * 0.08, y0, 3.55), (x - s * 0.08, y1, 3.55), (s * 1.3, y0, CEIL_Z - 0.08), (s * 1.3, y1, CEIL_Z - 0.08)], "paint", bevel=0.01)
        box((x, (Y_BULK + Y_REAR) / 2, 3.58), (0.18, Y_REAR - Y_BULK, 0.08), "frame", bevel=0.0)
        # ribs (wall + ceiling)
        for ry in ribs:
            prism([(x - s * 0.02, D), (x - s * 0.28, D), (x - s * 0.28, 3.55), (s * 1.3, CEIL_Z - 0.18), (s * 1.3, CEIL_Z), (x - s * 0.02, 3.62)][::s],
                  ry - 0.13, ry + 0.13, "frame", bevel=0.02) if False else None
            box((x - s * 0.15, ry, (D + 3.6) / 2), (0.28, 0.26, 3.6 - D), "frame", bevel=0.02)
            box((x - s * 0.29, ry, D + 0.25), (0.02, 0.27, 0.42), "hazard", bevel=0.0)
            hull([(x, ry - 0.13, 3.6), (x, ry + 0.13, 3.6), (s * 1.25, ry - 0.13, CEIL_Z), (s * 1.25, ry + 0.13, CEIL_Z),
                  (x - s * 0.3, ry - 0.13, 3.45), (x - s * 0.3, ry + 0.13, 3.45), (s * 1.25, ry - 0.13, CEIL_Z - 0.26), (s * 1.25, ry + 0.13, CEIL_Z - 0.26)], "frame", bevel=0.02)
            box((x - s * 0.3, ry, 2.55), (0.03, 0.12, 0.3), "amber", bevel=0.004)
        # overhead conduits along the upper wall
        for z, r, kd in ((3.75, 0.07, "steel"), (3.92, 0.05, "trim")):
            cyl((s * (WALL_X - 0.35), (Y_BULK + Y_REAR) / 2, z), r, Y_REAR - Y_BULK, kd, rot=(90, 0, 0), verts=12, bevel=0.0)
    # ceiling spine with light panels and the cargo rail
    box((0, (Y_BULK + Y_REAR) / 2, CEIL_Z + 0.02), (2.7, Y_REAR - Y_BULK, 0.1), "frame", bevel=0.0)
    for ry in ribs[:-1]:
        box((0, ry + 0.92, CEIL_Z - 0.05), (1.6, 1.2, 0.06), "lamp", bevel=0.01)
        box((0, ry + 0.92, CEIL_Z - 0.03), (1.75, 1.35, 0.06), "frame", bevel=0.01)
    box((0, (Y_PART + Y_REAR) / 2, CEIL_Z - 0.3), (0.3, Y_REAR - Y_PART, 0.25), "paint", bevel=0.02)

    # front bulkhead (cockpit door) and the crew / hold partition
    def bulkhead(y, door_w, door_h, label, kind="paint"):
        for s in (1, -1):
            hull([(s * door_w / 2, y - 0.15, D), (s * WALL_X, y - 0.15, D), (s * WALL_X, y - 0.15, 3.6), (s * 1.3, y - 0.15, CEIL_Z),
                  (s * door_w / 2, y - 0.15, CEIL_Z),
                  (s * door_w / 2, y + 0.15, D), (s * WALL_X, y + 0.15, D), (s * WALL_X, y + 0.15, 3.6), (s * 1.3, y + 0.15, CEIL_Z),
                  (s * door_w / 2, y + 0.15, CEIL_Z)], kind, bevel=0.02)
            box((s * (door_w / 2 + 0.1), y, (D + D + door_h) / 2), (0.2, 0.42, door_h), "frame", bevel=0.03)
            box((s * (door_w / 2 + 0.21), y - 0.2, D + door_h * 0.55), (0.03, 0.03, door_h * 0.8), "cyan", bevel=0.003)
            box((s * (door_w / 2 + 0.6), y - 0.17, D + 1.3), (0.3, 0.02, 0.4), "dark", bevel=0.01)
            box((s * (door_w / 2 + 0.6), y - 0.185, D + 1.34), (0.22, 0.01, 0.28), "screen", bevel=0.002)
            box((s * (door_w / 2 + 0.2), y, D + 0.2), (0.24, 0.46, 0.4), "hazard", bevel=0.01)
        hull([(-door_w / 2, y - 0.15, D + door_h), (door_w / 2, y - 0.15, D + door_h), (-door_w / 2, y - 0.15, CEIL_Z), (door_w / 2, y - 0.15, CEIL_Z),
              (-door_w / 2, y + 0.15, D + door_h), (door_w / 2, y + 0.15, D + door_h), (-door_w / 2, y + 0.15, CEIL_Z), (door_w / 2, y + 0.15, CEIL_Z)], kind, bevel=0.02)
        box((0, y, D + door_h + 0.08), (door_w + 0.4, 0.44, 0.16), "frame", bevel=0.03)
        box((0, y - 0.23, D + door_h + 0.08), (door_w * 0.7, 0.01, 0.03), "amber", bevel=0.002)
        text(label, (0, y - 0.24, D + door_h + 0.32), 0.14, "stencil", depth=0.002)
    bulkhead(Y_BULK, 1.5, 2.25, "FLIGHT DECK")
    bulkhead(Y_PART, 3.4, 2.5, "CREW BAY  /  CARGO HOLD", "light")

    # jump seats along both hold walls + starboard crew bay
    def jump_seats(s, y0, n):
        for i in range(n):
            y = y0 + i * 0.68
            x = s * (WALL_X - 0.12)
            box((x - s * 0.06, y, D + 1.0), (0.06, 0.6, 1.3), "frame", bevel=0.01)
            box((x - s * 0.3, y, D + 0.48), (0.46, 0.52, 0.09), "seat", bevel=0.02)
            box((x - s * 0.12, y, D + 0.95), (0.1, 0.5, 0.85), "seat", bevel=0.03)
            box((x - s * 0.13, y, D + 1.5), (0.12, 0.3, 0.22), "seat", bevel=0.03)
            for k in (-1, 1):
                box((x - s * 0.19, y + k * 0.14, D + 1.0), (0.02, 0.05, 0.8), "strap", bevel=0.0)
            box((x - s * 0.22, y, D + 0.78), (0.03, 0.12, 0.08), "steel", bevel=0.01)
            box((x - s * 0.45, y, D + 0.24), (0.04, 0.06, 0.48), "steel", bevel=0.005)
    jump_seats(1, 1.3, 9)
    jump_seats(-1, 4.6, 5)
    jump_seats(1, -5.4, 4)
    # crew bay: equipment lockers (port, forward of the stash) and a fold-down table
    for i in range(2):
        y = -5.55 + i * 0.64
        box((-(WALL_X - 0.32), y, D + 1.1), (0.6, 0.6, 2.2), "paint", bevel=0.015)
        box((-(WALL_X - 0.63), y, D + 1.15), (0.02, 0.5, 1.95), "light", bevel=0.008)
        box((-(WALL_X - 0.645), y + 0.18, D + 1.2), (0.02, 0.03, 0.3), "steel", bevel=0.003)
        for k in range(4):
            box((-(WALL_X - 0.645), y, D + 1.9 + k * 0.035), (0.004, 0.35, 0.015), "dark", bevel=0.0)
        box((-(WALL_X - 0.645), y - 0.15, D + 0.4), (0.01, 0.12, 0.05), "amber" if i else "green", bevel=0.002)
    box((1.6, -2.4, D + 0.78), (1.2, 2.0, 0.07), "light", bevel=0.015)
    box((1.6, -2.4, D + 0.4), (0.12, 0.12, 0.76), "frame", bevel=0.01)
    box((1.6, -2.4, D + 0.03), (0.6, 0.6, 0.05), "frame", bevel=0.01)
    box((1.6, -2.9, D + 0.83), (0.35, 0.5, 0.03), "screen", rot=(0, 0, 8), bevel=0.003)
    text("MISSION BRIEF", (1.6, -1.85, D + 0.818), 0.09, "stencil", rot=(0, 0, 180), depth=0.0015)
    # hold: wall stowage racks + ramp control panel
    for s, y in ((-1, 2.1), (-1, 3.5)):
        box((s * (WALL_X - 0.25), y, D + 2.0), (0.4, 1.1, 0.06), "frame", bevel=0.01)
        box((s * (WALL_X - 0.25), y - 0.3, D + 2.15), (0.32, 0.4, 0.24), "trim", bevel=0.02)
        box((s * (WALL_X - 0.25), y + 0.25, D + 2.15), (0.32, 0.45, 0.24), "paint", bevel=0.02)
    box((-(WALL_X - 0.18), Y_REAR - 0.6, D + 1.3), (0.2, 0.5, 0.7), "frame", bevel=0.02)
    box((-(WALL_X - 0.29), Y_REAR - 0.6, D + 1.4), (0.02, 0.36, 0.3), "screen", bevel=0.003)
    for k, kd in enumerate(("green", "amber", "red")):
        box((-(WALL_X - 0.29), Y_REAR - 0.74 + k * 0.14, D + 1.12), (0.03, 0.08, 0.08), kd, bevel=0.005)
    text("RAMP", (-(WALL_X - 0.285), Y_REAR - 0.6, D + 1.6), 0.08, "stencil", rot=(90, 0, 90), depth=0.0015)


# ============================================================================ interior instance (the hub)
# The interior is its own instance in the game (you never see the outside from in there), so it is roomier than the
# hull: 10.8 m wide, flight deck + crew bay + mid bay (room for future workstations) + cargo hold, 31 m from the
# viewscreens to the rear door. Same deck height and cut plane as the hull model.
IW = 5.4                     # half width
I_TOP, I_CEIL, I_SLANT = DECK + 2.5, DECK + 4.0, 3.6
I_NOSE, I_FRONT, I_PA, I_PB, I_REAR = -17.4, -13.0, -4.0, 4.5, 14.0
COCK_W = 3.4


def interior_big():
    D = DECK
    L = I_REAR - I_FRONT
    # ---------------- deck: side plates, grated centre channel, substructure
    # substructure: kept 7 cm under the plate tops and out of the grated channel (coplanar faces z-fight)
    for s in (1, -1):
        box((s * (0.84 + (IW + 0.2 - 0.84) / 2), (I_NOSE + I_REAR) / 2, D - 0.3), (IW + 0.2 - 0.84, I_REAR - I_NOSE + 0.3, 0.46), "frame", bevel=0.0)
    box((0, (I_NOSE + I_FRONT) / 2, D - 0.3), (1.7, I_FRONT - I_NOSE, 0.46), "frame", bevel=0.0)
    y, k = I_FRONT, 0
    while y < I_REAR - 0.1:
        y1 = min(y + 2.2, I_REAR)
        for s in (1, -1):
            for x0, x1 in ((0.8, 3.1), (3.1, IW)):
                cx = s * (x0 + x1) / 2
                box((cx, (y + y1) / 2, D - 0.03), (x1 - x0 - 0.04, y1 - y - 0.04, 0.06), "deck", bevel=0.01)
                if (k + (s > 0) + (x0 > 1)) % 2 == 0:
                    n = 6
                    for i in range(n):
                        for j in range(n):
                            px = cx - (x1 - x0 - 0.4) / 2 + (i + 0.5) * (x1 - x0 - 0.4) / n
                            py = y + 0.2 + (j + 0.5) * (y1 - y - 0.4) / n
                            box((px, py, D + 0.005), (0.07, 0.016, 0.01), "deck", rot=(0, 0, 45 if (i + j) % 2 else -45), bevel=0.0)
            cyl((s * (IW - 0.3), y + 0.3, D + 0.004), 0.06, 0.012, "steel", verts=12, bevel=0.002)
        y = y1
        k += 1
    box((0, (I_FRONT + I_REAR) / 2, D - 0.36), (1.6, L, 0.06), "dark", bevel=0.0)
    for x, r, kd in ((-0.45, 0.08, "steel"), (0.0, 0.06, "trim"), (0.42, 0.09, "paint")):
        cyl((x, (I_FRONT + I_REAR) / 2, D - 0.33 + r), r, L, kd, rot=(90, 0, 0), verts=12, bevel=0.0)
    yy = I_FRONT + 1.0
    while yy < I_REAR - 0.5:
        box((0, yy, D - 0.32), (0.6, 0.14, 0.02), "cyan", bevel=0.002)
        yy += 2.2
    for s in (1, -1):
        box((s * 0.8, (I_FRONT + I_REAR) / 2, D - 0.035), (0.06, L, 0.06), "frame", bevel=0.005)
    n = int(L / 0.075)
    for i in range(n):
        box((0, I_FRONT + 0.04 + i * 0.075, D - 0.04), (1.5, 0.02, 0.05), "steel", bevel=0.0)
    for x in (-0.4, 0.4):
        box((x, (I_FRONT + I_REAR) / 2, D - 0.055), (0.03, L, 0.04), "steel", bevel=0.0)

    # ---------------- outer walls: lining with ribs, slanted ceiling, ceiling spine with lamps
    ribs = [I_FRONT + 0.2 + i * 2.2 for i in range(int(L / 2.2) + 1) if I_FRONT + 0.2 + i * 2.2 < I_REAR - 0.2]
    for s in (1, -1):
        x = s * IW
        box((x + s * 0.12, (I_FRONT + I_REAR) / 2, (D + I_CEIL) / 2), (0.24, L, I_CEIL - D + 0.4), "frame", bevel=0.0)
        for i in range(len(ribs) - 1):
            y0, y1 = ribs[i] + 0.18, ribs[i + 1] - 0.18
            cy, Ly = (y0 + y1) / 2, y1 - y0
            box((x - s * 0.04, cy, D + 0.5), (0.06, Ly, 0.9), "paint", bevel=0.012)
            box((x - s * 0.06, cy, D + 1.0), (0.04, Ly, 0.08), "trim", bevel=0.006)
            box((x - s * 0.04, cy, D + 1.75), (0.06, Ly, 1.3), "light", bevel=0.014)
            box((x - s * 0.075, cy, D + 1.75), (0.02, Ly - 0.3, 1.0), "light", bevel=0.008)
            box((x - s * 0.08, cy, D + 1.12), (0.02, Ly * 0.7, 0.025), "cyan", bevel=0.002)
            hull([(x, y0, I_TOP), (x, y1, I_TOP), (s * I_SLANT, y0, I_CEIL), (s * I_SLANT, y1, I_CEIL),
                  (x - s * 0.08, y0, I_TOP - 0.05), (x - s * 0.08, y1, I_TOP - 0.05), (s * I_SLANT, y0, I_CEIL - 0.08), (s * I_SLANT, y1, I_CEIL - 0.08)], "paint", bevel=0.01)
        for ry in ribs:
            box((x - s * 0.15, ry, (D + I_TOP) / 2), (0.3, 0.3, I_TOP - D), "frame", bevel=0.02)
            box((x - s * 0.31, ry, D + 0.25), (0.02, 0.31, 0.42), "hazard", bevel=0.0)
            hull([(x, ry - 0.15, I_TOP), (x, ry + 0.15, I_TOP), (s * I_SLANT, ry - 0.15, I_CEIL), (s * I_SLANT, ry + 0.15, I_CEIL),
                  (x - s * 0.3, ry - 0.15, I_TOP - 0.15), (x - s * 0.3, ry + 0.15, I_TOP - 0.15), (s * I_SLANT, ry - 0.15, I_CEIL - 0.28), (s * I_SLANT, ry + 0.15, I_CEIL - 0.28)], "frame", bevel=0.02)
            box((x - s * 0.31, ry, D + 1.6), (0.03, 0.12, 0.3), "amber", bevel=0.004)
        for z, r, kd in ((I_TOP + 0.2, 0.08, "steel"), (I_TOP + 0.42, 0.055, "trim")):
            cyl((s * (IW - 0.4), (I_FRONT + I_REAR) / 2, z), r, L, kd, rot=(90, 0, 0), verts=12, bevel=0.0)
    box((0, (I_FRONT + I_REAR) / 2, I_CEIL + 0.04), (2 * I_SLANT + 0.2, L, 0.12), "frame", bevel=0.0)
    for ry in ribs[:-1]:
        for x in (-1.6, 1.6):
            box((x, ry + 1.1, I_CEIL - 0.05), (1.6, 1.3, 0.06), "lamp", bevel=0.01)
            box((x, ry + 1.1, I_CEIL - 0.03), (1.75, 1.45, 0.06), "frame", bevel=0.01)
    box((0, (I_PB + I_REAR) / 2, I_CEIL - 0.35), (0.35, I_REAR - I_PB, 0.3), "paint", bevel=0.02)      # cargo rail

    # ---------------- cross walls (full width, a door each)
    def cross_wall(y, half, door_w, door_h, label, kind="paint", lit=True):
        for s in (1, -1):
            hull([(s * door_w / 2, y - 0.15, D), (s * half, y - 0.15, D), (s * half, y - 0.15, I_TOP), (s * min(half, I_SLANT), y - 0.15, I_CEIL),
                  (s * door_w / 2, y - 0.15, I_CEIL),
                  (s * door_w / 2, y + 0.15, D), (s * half, y + 0.15, D), (s * half, y + 0.15, I_TOP), (s * min(half, I_SLANT), y + 0.15, I_CEIL),
                  (s * door_w / 2, y + 0.15, I_CEIL)], kind, bevel=0.02)
            box((s * (door_w / 2 + 0.12), y, D + door_h / 2), (0.24, 0.44, door_h), "frame", bevel=0.03)
            box((s * (door_w / 2 + 0.25), y - 0.21, D + door_h * 0.55), (0.03, 0.03, door_h * 0.8), "cyan" if lit else "amber", bevel=0.003)
            box((s * (door_w / 2 + 0.25), y + 0.21, D + door_h * 0.55), (0.03, 0.03, door_h * 0.8), "cyan" if lit else "amber", bevel=0.003)
            box((s * (door_w / 2 + 0.24), y, D + 0.2), (0.28, 0.48, 0.4), "hazard", bevel=0.01)
            for side in (-1, 1):
                box((s * (door_w / 2 + 0.75), y + side * 0.17, D + 1.3), (0.34, 0.02, 0.42), "dark", bevel=0.01)
                box((s * (door_w / 2 + 0.75), y + side * 0.185, D + 1.34), (0.26, 0.01, 0.3), "screen", bevel=0.002)
        hull([(-door_w / 2, y - 0.15, D + door_h), (door_w / 2, y - 0.15, D + door_h), (-door_w / 2, y - 0.15, I_CEIL), (door_w / 2, y - 0.15, I_CEIL),
              (-door_w / 2, y + 0.15, D + door_h), (door_w / 2, y + 0.15, D + door_h), (-door_w / 2, y + 0.15, I_CEIL), (door_w / 2, y + 0.15, I_CEIL)], kind, bevel=0.02)
        box((0, y, D + door_h + 0.08), (door_w + 0.48, 0.46, 0.16), "frame", bevel=0.03)
        for side, rot in ((-1, (90, 0, 0)), (1, (90, 0, 180))):
            box((0, y + side * 0.24, D + door_h + 0.08), (door_w * 0.7, 0.01, 0.03), "amber", bevel=0.002)
            text(label, (0, y + side * 0.245, D + door_h + 0.34), 0.15, "stencil", rot=rot, depth=0.002)
    cross_wall(I_FRONT, IW, 1.8, 2.3, "FLIGHT DECK")
    cross_wall(I_PA, IW, 3.6, 2.6, "MID BAY", "light")
    cross_wall(I_PB, IW, 4.2, 2.8, "CARGO HOLD")

    # ---------------- rear wall with the blast door (the exit)
    y = I_REAR
    for s in (1, -1):
        hull([(s * 3.3, y - 0.2, D), (s * IW, y - 0.2, D), (s * IW, y - 0.2, I_TOP), (s * I_SLANT, y - 0.2, I_CEIL), (s * 3.3, y - 0.2, I_CEIL),
              (s * 3.3, y + 0.25, D), (s * IW, y + 0.25, D), (s * IW, y + 0.25, I_TOP), (s * I_SLANT, y + 0.25, I_CEIL), (s * 3.3, y + 0.25, I_CEIL)], "frame", bevel=0.02)
        box((s * 3.55, y - 0.15, D + 1.2), (0.4, 0.12, 2.4), "hazard", bevel=0.01)
    hull([(-3.3, y - 0.2, 4.4), (3.3, y - 0.2, 4.4), (-3.3, y - 0.2, I_CEIL), (3.3, y - 0.2, I_CEIL),
          (-3.3, y + 0.25, 4.4), (3.3, y + 0.25, 4.4), (-3.3, y + 0.25, I_CEIL), (3.3, y + 0.25, I_CEIL)], "frame", bevel=0.02)
    for s in (1, -1):
        cx = s * 1.65
        box((cx, y, (D + 4.4) / 2), (3.26, 0.26, 4.4 - D), "hullDark", bevel=0.03)
        for zc, h in ((D + 0.55, 0.9), (D + 1.75, 1.2), (3.85, 0.8)):
            box((cx, y - 0.15, zc), (2.9, 0.06, h - 0.1), "hull", bevel=0.02)
        for kk in range(6):
            box((s * 0.18, y - 0.19, D + 0.25 + kk * 0.42), (0.3, 0.02, 0.2), "hazard", rot=(0, 30 * s, 0), bevel=0.0)
        box((s * 2.9, y - 0.19, D + 1.3), (0.06, 0.03, 1.4), "cyan", bevel=0.004)
        box((s * 0.7, y - 0.2, D + 1.15), (0.08, 0.06, 0.45), "steel", bevel=0.01)
    box((0, y - 0.12, 4.32), (6.8, 0.4, 0.16), "frame", bevel=0.03)
    text("EXIT  TO  SURFACE", (0, y - 0.33, 4.55), 0.18, "amber", depth=0.002)
    box((0, y - 0.45, D + 0.012), (6.6, 0.6, 0.008), "hazard", bevel=0.0)
    box((-3.95, y - 0.3, D + 1.3), (0.4, 0.14, 0.6), "frame", bevel=0.02)
    box((-3.95, y - 0.38, D + 1.4), (0.3, 0.01, 0.3), "screen", bevel=0.003)
    for kk, kd in enumerate(("green", "amber", "red")):
        box((-4.05 + kk * 0.1, y - 0.38, D + 1.13), (0.07, 0.02, 0.07), kd, bevel=0.005)

    # ---------------- flight deck (walkable): angled walls, viewscreens instead of a canopy
    for s in (1, -1):
        hull([(s * COCK_W, I_FRONT, D), (s * COCK_W, I_NOSE + 1.4, D), (s * 1.8, I_NOSE, D),
              (s * COCK_W, I_FRONT, I_TOP + 0.6), (s * COCK_W, I_NOSE + 1.4, I_TOP + 0.6), (s * 1.8, I_NOSE, I_TOP + 0.6),
              (s * (COCK_W + 0.3), I_FRONT, D), (s * (COCK_W + 0.3), I_NOSE + 1.3, D), (s * 1.9, I_NOSE - 0.3, D),
              (s * (COCK_W + 0.3), I_FRONT, I_TOP + 0.6), (s * (COCK_W + 0.3), I_NOSE + 1.3, I_TOP + 0.6), (s * 1.9, I_NOSE - 0.3, I_TOP + 0.6)], "paint", bevel=0.02)
        box((s * (COCK_W - 0.05), (I_FRONT + I_NOSE + 1.4) / 2, D + 1.0), (0.04, 3.0, 0.08), "trim", bevel=0.005)
    box((0, I_NOSE - 0.15, (D + I_TOP + 0.6) / 2), (3.8, 0.3, I_TOP + 0.6 - D), "frame", bevel=0.02)
    for x, w in ((-1.15, 1.1), (0.0, 1.1), (1.15, 1.1)):
        box((x, I_NOSE + 0.02, D + 2.0), (w - 0.06, 0.06, 1.3), "dark", bevel=0.01)
        box((x, I_NOSE + 0.06, D + 2.0), (w - 0.16, 0.02, 1.15), "screen", bevel=0.003)
    for s in (1, -1):
        hull([(s * COCK_W, I_NOSE + 1.4, D + 1.2), (s * 1.8, I_NOSE, D + 1.2), (s * COCK_W, I_NOSE + 1.4, D + 2.6), (s * 1.8, I_NOSE, D + 2.6),
              (s * (COCK_W - 0.05), I_NOSE + 1.45, D + 1.25), (s * 1.85, I_NOSE + 0.06, D + 1.25), (s * (COCK_W - 0.05), I_NOSE + 1.45, D + 2.55), (s * 1.85, I_NOSE + 0.06, D + 2.55)], "screen", bevel=0.003)
    box((0, (I_NOSE + I_FRONT) / 2, D + 0.1), (2 * COCK_W, I_FRONT - I_NOSE, 0.2), "deck", bevel=0.02)
    text("FLIGHT DECK", (0, I_FRONT - 0.6, D + 0.205), 0.26, "stencil", rot=(0, 0, 180), depth=0.002)
    for s in (1, -1):
        x = s * 1.0
        box((x, -15.5, D + 0.45), (0.2, 0.2, 0.5), "frame", bevel=0.02)
        box((x, -15.55, D + 0.75), (0.62, 0.62, 0.14), "seat", bevel=0.03)
        box((x, -15.22, D + 1.25), (0.58, 0.14, 0.95), "seat", rot=(-12, 0, 0), bevel=0.04)
        box((x, -15.14, D + 1.85), (0.36, 0.14, 0.26), "seat", bevel=0.04)
        for kk in (-1, 1):
            box((x + kk * 0.32, -15.55, D + 0.95), (0.08, 0.5, 0.06), "frame", bevel=0.01)
            box((x + kk * 0.15, -15.3, D + 1.3), (0.04, 0.03, 0.7), "strap", rot=(-12, 0, 0), bevel=0.0)
        cyl((x + s * 0.3, -15.85, D + 0.95), 0.03, 0.3, "steel", verts=8)
        box((x + s * 0.6, -15.7, D + 0.75), (0.3, 1.0, 0.5), "frame", bevel=0.02)
        box((x + s * 0.6, -15.7, D + 1.01), (0.24, 0.8, 0.02), "screen", bevel=0.002)
        # side stations
        box((s * (COCK_W - 0.45), -14.2, D + 0.55), (0.7, 1.6, 1.1), "frame", bevel=0.03)
        box((s * (COCK_W - 0.45), -14.2, D + 1.12), (0.55, 1.4, 0.03), "screen", rot=(0, -12 * s, 0), bevel=0.003)
    hull([(-2.2, -16.8, D + 0.2), (2.2, -16.8, D + 0.2), (-2.2, -16.35, D + 0.2), (2.2, -16.35, D + 0.2),
          (-2.0, -16.95, D + 1.15), (2.0, -16.95, D + 1.15), (-2.0, -16.6, D + 1.25), (2.0, -16.6, D + 1.25)], "frame", bevel=0.03)
    for x in (-1.35, -0.45, 0.45, 1.35):
        box((x, -16.62, D + 1.24), (0.75, 0.28, 0.02), "screen", rot=(-25, 0, 0), bevel=0.002)
    box((0, -16.0, D + 0.55), (0.45, 1.0, 0.7), "frame", bevel=0.03)

    # ---------------- crew bay: lockers, bunks, jump seats, briefing table (the stash is placed by the game)
    def jump_seats(s, y0, n):
        for i in range(n):
            yy = y0 + i * 0.68
            x = s * (IW - 0.12)
            box((x - s * 0.06, yy, D + 1.0), (0.06, 0.6, 1.3), "frame", bevel=0.01)
            box((x - s * 0.3, yy, D + 0.48), (0.46, 0.52, 0.09), "seat", bevel=0.02)
            box((x - s * 0.12, yy, D + 0.95), (0.1, 0.5, 0.85), "seat", bevel=0.03)
            box((x - s * 0.13, yy, D + 1.5), (0.12, 0.3, 0.22), "seat", bevel=0.03)
            for kk in (-1, 1):
                box((x - s * 0.19, yy + kk * 0.14, D + 1.0), (0.02, 0.05, 0.8), "strap", bevel=0.0)
            box((x - s * 0.45, yy, D + 0.24), (0.04, 0.06, 0.48), "steel", bevel=0.005)
    for i in range(3):
        yy = -12.4 + i * 0.64
        box((-(IW - 0.32), yy, D + 1.1), (0.6, 0.6, 2.2), "paint", bevel=0.015)
        box((-(IW - 0.63), yy, D + 1.15), (0.02, 0.5, 1.95), "light", bevel=0.008)
        box((-(IW - 0.645), yy + 0.18, D + 1.2), (0.02, 0.03, 0.3), "steel", bevel=0.003)
        for kk in range(4):
            box((-(IW - 0.645), yy, D + 1.9 + kk * 0.035), (0.004, 0.35, 0.015), "dark", bevel=0.0)
        box((-(IW - 0.645), yy - 0.15, D + 0.4), (0.01, 0.12, 0.05), "amber" if i % 2 else "green", bevel=0.002)
    for b in range(2):                                  # bunks along the starboard wall
        yy = -11.6 + b * 2.3
        x = IW - 0.55
        for zb in (D + 0.45, D + 1.65):
            box((x, yy, zb), (1.0, 2.1, 0.12), "frame", bevel=0.02)
            box((x, yy, zb + 0.12), (0.9, 2.0, 0.14), "seat", bevel=0.04)
            box((x, yy - 0.75, zb + 0.25), (0.7, 0.4, 0.14), "light", bevel=0.05)
        for dy in (-1.03, 1.03):
            box((x - 0.47, yy + dy, D + 1.1), (0.06, 0.06, 2.2), "steel", bevel=0.01)
        box((x - 0.5, yy, D + 2.35), (0.03, 1.6, 0.03), "trim", bevel=0.0)
    jump_seats(1, -6.6, 3)
    box((1.2, -8.4, D + 0.78), (1.4, 2.4, 0.07), "light", bevel=0.015)
    box((1.2, -8.4, D + 0.4), (0.14, 0.14, 0.76), "frame", bevel=0.01)
    box((1.2, -8.4, D + 0.03), (0.7, 0.7, 0.05), "frame", bevel=0.01)
    box((1.2, -9.0, D + 0.83), (0.4, 0.55, 0.03), "screen", rot=(0, 0, 8), bevel=0.003)
    text("MISSION BRIEF", (1.2, -7.35, D + 0.818), 0.09, "stencil", rot=(0, 0, 180), depth=0.0015)
    for s in (1, -1):
        box((1.2 + s * 1.05, -8.4, D + 0.3), (0.42, 0.42, 0.06), "seat", bevel=0.02)
        box((1.2 + s * 1.05, -8.4, D + 0.15), (0.08, 0.08, 0.3), "steel", bevel=0.005)

    # ---------------- mid bay: open floor with marked bays for future workstations, wall racks
    for i, (bx, by) in enumerate(((-3.0, -2.0), (-3.0, 2.0), (3.0, -2.0), (3.0, 2.0))):
        for s in (1, -1):
            box((bx + s * 1.1, by, D + 0.01), (0.08, 2.4, 0.008), "hazard", bevel=0.0)
            box((bx, by + s * 1.2, D + 0.01), (2.28, 0.08, 0.008), "hazard", bevel=0.0)
        text(f"BAY {'ABCD'[i]}", (bx, by, D + 0.017), 0.3, "stencil", rot=(0, 0, 90 if bx < 0 else -90), depth=0.002)
    for s in (1, -1):
        x = s * (IW - 0.2)
        for yy in (-2.0, 2.0):
            box((x, yy, D + 1.6), (0.12, 2.2, 1.6), "frame", bevel=0.02)
            box((x - s * 0.07, yy, D + 1.6), (0.02, 2.0, 1.4), "dark", bevel=0.005)
            for r in range(3):
                box((x - s * 0.15, yy, D + 1.05 + r * 0.5), (0.22, 2.0, 0.04), "steel", bevel=0.004)
            box((x - s * 0.22, yy - 0.5, D + 1.2), (0.25, 0.5, 0.25), "trim", bevel=0.02)
            box((x - s * 0.22, yy + 0.4, D + 1.7), (0.25, 0.7, 0.2), "paint", bevel=0.02)
    text("MID BAY", (0.0, 0.0, D + 0.017), 0.4, "stencil", rot=(0, 0, 90), depth=0.002)

    # ---------------- cargo hold: jump seats, stowage, markings
    jump_seats(1, 5.4, 12)
    jump_seats(-1, 9.0, 7)
    for yy in (5.6, 7.2):
        box((-(IW - 0.25), yy, D + 2.0), (0.4, 1.3, 0.06), "frame", bevel=0.01)
        box((-(IW - 0.25), yy - 0.35, D + 2.15), (0.32, 0.45, 0.24), "trim", bevel=0.02)
        box((-(IW - 0.25), yy + 0.3, D + 2.15), (0.32, 0.5, 0.24), "paint", bevel=0.02)
    text("CARGO HOLD", (2.6, 9.0, D + 0.017), 0.34, "stencil", rot=(0, 0, 90), depth=0.002)
    text("EXIT", (0.0, I_REAR - 1.4, D + 0.017), 0.4, "stencil", rot=(0, 0, 180), depth=0.002)


def cockpit():
    D = DECK
    box((0, -7.9, D + 0.1), (4.4, 3.4, 0.2), "deck", bevel=0.02)
    for s in (1, -1):
        x = s * 0.85
        # pilot seat
        box((x, -8.0, D + 0.45), (0.2, 0.2, 0.5), "frame", bevel=0.02)
        box((x, -8.05, D + 0.75), (0.62, 0.62, 0.14), "seat", bevel=0.03)
        box((x, -7.72, D + 1.25), (0.58, 0.14, 0.95), "seat", rot=(-12, 0, 0), bevel=0.04)
        box((x, -7.64, D + 1.85), (0.36, 0.14, 0.26), "seat", bevel=0.04)
        for k in (-1, 1):
            box((x + k * 0.32, -8.05, D + 0.95), (0.08, 0.5, 0.06), "frame", bevel=0.01)
            box((x + k * 0.15, -7.8, D + 1.3), (0.04, 0.03, 0.7), "strap", rot=(-12, 0, 0), bevel=0.0)
        # flight stick + side console
        cyl((x + s * 0.3, -8.35, D + 0.95), 0.03, 0.3, "steel", verts=8)
        box((x + s * 0.55, -8.2, D + 0.75), (0.3, 1.0, 0.5), "frame", bevel=0.02)
        box((x + s * 0.55, -8.2, D + 1.01), (0.24, 0.8, 0.02), "screen", bevel=0.002)
    # dashboard
    hull([(-2.0, -9.3, D + 0.2), (2.0, -9.3, D + 0.2), (-2.0, -8.85, D + 0.2), (2.0, -8.85, D + 0.2),
          (-1.8, -9.45, D + 1.15), (1.8, -9.45, D + 1.15), (-1.8, -9.1, D + 1.25), (1.8, -9.1, D + 1.25)], "frame", bevel=0.03)
    for x in (-1.25, -0.42, 0.42, 1.25):
        box((x, -9.12, D + 1.24), (0.7, 0.28, 0.02), "screen", rot=(-25, 0, 0), bevel=0.002)
    box((0, -8.6, D + 0.55), (0.45, 1.1, 0.7), "frame", bevel=0.03)
    for i in range(3):
        for j in range(4):
            box((-0.12 + i * 0.12, -8.9 + j * 0.15, D + 0.91), (0.07, 0.07, 0.02), "amber" if (i + j) % 3 == 0 else "light", bevel=0.005)
    box((0, -8.1, CEIL_Z - 0.4), (1.3, 1.2, 0.18), "frame", bevel=0.03)      # overhead panel


def cargo_stack():
    """Strapped crates on a pallet (crew bay / hold)."""
    D = DECK
    box((0, 0, D + 0.08), (1.6, 1.3, 0.16), "frame", bevel=0.02)
    box((-0.4, 0, D + 0.56), (0.75, 1.2, 0.8), "trim", bevel=0.03)
    box((0.42, -0.25, D + 0.46), (0.7, 0.7, 0.6), "hullDark", bevel=0.03)
    box((0.42, 0.33, D + 0.36), (0.7, 0.45, 0.4), "light", bevel=0.03)
    box((-0.4, 0.05, D + 1.2), (0.7, 0.8, 0.48), "paint", bevel=0.03)
    for k in (-0.35, 0.35):
        box((0, k, D + 0.75), (1.62, 0.05, 1.2), "strap", bevel=0.0) if False else None
        box((k - 0.05, 0, D + 1.46), (0.05, 1.25, 0.02), "strap", bevel=0.0)
    box((-0.78, 0, D + 0.75), (0.02, 1.0, 0.04), "strap", bevel=0.0)
    text("XN-04", (-0.4, -0.61, D + 0.6), 0.16, "dark", depth=0.002)
    box((0.42, -0.61, D + 0.55), (0.3, 0.012, 0.08), "hazard", bevel=0.0)


def ramp():
    D = DECK
    y0, y1 = Y_REAR, Y_REAR + 4.9
    W = 6.2
    slab = [(s * W / 2, y0, D - 0.02) for s in (1, -1)] + [(s * W / 2, y1, 0.03) for s in (1, -1)]
    slab += [(x, y, z - 0.22) for x, y, z in slab]
    hull(slab, "hullDark", bevel=0.03)
    L = math.hypot(y1 - y0, D - 0.05)
    ang = math.degrees(math.atan2(D - 0.05, y1 - y0))
    def on_ramp(t, x, h=0.0):
        return (x, y0 + (y1 - y0) * t, D - 0.02 + (0.03 - (D - 0.02)) * t + h)
    # grip bars, side hazard edges, hinge knuckles, toe lip, side rails/pistons, lights
    for i in range(18):
        t = (i + 0.5) / 18
        box(on_ramp(t, 0, 0.015), (W - 0.9, 0.06, 0.03), "steel", rot=(-ang, 0, 0), bevel=0.0)
    for s in (1, -1):
        c = on_ramp(0.5, s * (W / 2 - 0.22), 0.012)
        box(c, (0.4, L, 0.02), "hazard", rot=(-ang, 0, 0), bevel=0.0)
        c = on_ramp(0.5, s * (W / 2 + 0.03), -0.05)
        box(c, (0.14, L, 0.32), "frame", rot=(-ang, 0, 0), bevel=0.02)
        pipe([(s * 3.2, Y_REAR - 0.2, 3.3), (s * 3.2, Y_REAR + 1.8, 1.35)], 0.07, "frame")
        pipe([(s * 3.2, Y_REAR + 0.6, 2.53), (s * 3.2, Y_REAR + 2.6, 0.95)], 0.045, "steel")
        cyl(on_ramp(0.98, s * (W / 2 - 0.3), 0.06), 0.06, 0.06, "amber", verts=10)
    for x in (-2.4, -0.8, 0.8, 2.4):
        cyl((x, y0, D - 0.12), 0.14, 0.9, "frame", rot=(0, 90, 0), verts=16)
    box((0, y1 + 0.08, 0.06), (W, 0.25, 0.1), "frame", rot=(0, 0, 0), bevel=0.02)


def landing_gear():
    """One leg, origin on the ground under the foot; the hull belly attaches at z ~ 1.0."""
    cyl((0, 0, 0.07), 0.62, 0.14, "frame", verts=28, bevel=0.02)
    cyl((0, 0, 0.17), 0.45, 0.08, "hazardR", verts=28, bevel=0.01)
    box((0, 0, 0.3), (0.42, 0.42, 0.22), "frame", bevel=0.03)
    cyl((0, 0, 0.62), 0.12, 0.5, "steel", verts=16)
    cyl((0, 0, 1.05), 0.2, 0.55, "hullDark", verts=16)
    box((0, 0, 1.38), (0.6, 0.6, 0.18), "frame", bevel=0.03)
    pipe([(0.15, 0.14, 0.4), (0.25, 0.25, 0.9), (0.24, 0.3, 1.4)], 0.025, "trim")
    for s in (1, -1):
        pipe([(0.0, s * 0.12, 0.45), (0.0, s * 0.85, 1.36)], 0.05, "frame")
        pipe([(0.0, s * 0.1, 0.55), (0.0, s * 0.55, 1.05)], 0.035, "steel")
    box((0, -0.22, 1.1), (0.25, 0.03, 0.18), "hazard", bevel=0.0)


def engine():
    """Starboard nacelle around its own origin, axis along Y (intake -Y). Pylon on the inboard (-X) side."""
    R = 1.15
    cyl((0, 0, 0), R, 6.6, "hullDark", rot=(90, 0, 0), verts=32, bevel=0.01)
    for y in (-2.2, -0.4, 1.4):
        cyl((0, y, 0), R + 0.04, 0.12, "frame", rot=(90, 0, 0), verts=32, bevel=0.004)
    ring((0, -3.45, 0), R + 0.15, R - 0.25, 0.5, "frame", verts=40, rot=(90, 0, 0))
    ring((0, -3.62, 0), R + 0.1, R - 0.08, 0.16, "hazardR", verts=40, rot=(90, 0, 0))
    cyl((0, -3.1, 0), R - 0.22, 0.12, "dark", rot=(90, 0, 0), verts=32)
    cyl((0, -3.2, 0), 0.32, 0.4, "steel", rot=(90, 0, 0), verts=20, r2=0.12)
    for i in range(14):
        a = 360 * i / 14
        box((0, -3.15, 0), (0.07, 0.04, (R - 0.3) * 2), "steel", rot=(0, a, 0), bevel=0.0) if False else None
        ca, sa = math.cos(rad(a)), math.sin(rad(a))
        box((ca * 0.6, -3.16, sa * 0.6), (0.5, 0.03, 0.12), "steel", rot=(0, -a, 0), bevel=0.0)
    # exhaust: nozzle petals + glow
    cyl((0, 3.7, 0), R - 0.05, 0.9, "frame", rot=(90, 0, 0), verts=32, r2=R - 0.3)
    for i in range(12):
        a = 360 * i / 12
        ca, sa = math.cos(rad(a)), math.sin(rad(a))
        box((ca * (R - 0.25), 4.15, sa * (R - 0.25)), (0.42, 0.3, 0.05), "steel", rot=(0, -a + 90, 0), bevel=0.01)
    cyl((0, 4.0, 0), R - 0.4, 0.3, "engineGlow", rot=(90, 0, 0), verts=32)
    cyl((0, 3.95, 0), 0.3, 0.5, "dark", rot=(90, 0, 0), verts=16)
    # pylon, vents, light, markings
    box((-1.0, 1.4, 0.0), (0.6, 3.6, 0.5), "hullDark", bevel=0.05)
    for y in (-1.2, 0.5, 2.3):
        box((0, y, R + 0.02), (0.6, 0.9, 0.06), "dark", bevel=0.01)
        for k in range(4):
            box((0, y - 0.3 + k * 0.2, R + 0.06), (0.55, 0.05, 0.04), "frame", bevel=0.0)
    box((R + 0.02, 0.0, 0.0), (0.06, 2.0, 0.08), "trim", bevel=0.005)
    cyl((R * 0.7, 3.2, R * 0.7), 0.06, 0.06, "amber", rot=(0, 0, 0), verts=10)
    for k, z in ((1, R + 0.45), (-1, -(R + 0.35))):
        hull([(0, 1.6, k * R * 0.8), (0, 3.9, k * R * 0.8), (0, 3.4, z), (0, 4.0, z),
              (0.08, 1.6, k * R * 0.8), (0.08, 3.9, k * R * 0.8), (0.08, 3.4, z), (0.08, 4.0, z)], "hullDark", bevel=0.02)
    ring((0, 4.32, 0), R - 0.18, R - 0.32, 0.05, "engineGlow", verts=40, rot=(90, 0, 0))
    text("DANGER  INTAKE", (R * 0.72, -2.6, R * 0.72), 0.12, "stencil", rot=(90, -45, -90), depth=0.002)


def rear_door():
    """Closed blast door in the rear opening (|x| < 3.3, deck .. 4.4) at the top of the ramp: two armoured leaves."""
    D = DECK
    y = Y_REAR - 0.02
    for s in (1, -1):
        cx = s * 1.65
        box((cx, y, (D + 4.4) / 2), (3.26, 0.26, 4.4 - D), "hullDark", bevel=0.03)
        for zc, h in ((D + 0.55, 0.9), (D + 1.75, 1.2), (3.85, 0.8)):
            for side in (-1, 1):
                box((cx, y + side * 0.15, zc), (2.9, 0.06, h - 0.1), "hull", bevel=0.02)
        for side in (-1, 1):
            # hazard chevrons along the meeting edge, light bars, handles, view slit
            for k in range(6):
                box((s * 0.18, y + side * 0.19, D + 0.25 + k * 0.42), (0.3, 0.02, 0.2), "hazard", rot=(0, 30 * s, 0), bevel=0.0)
            box((s * 2.9, y + side * 0.19, D + 1.3), (0.06, 0.03, 1.4), "cyan", bevel=0.004)
            box((s * 0.7, y + side * 0.2, D + 1.15), (0.08, 0.06, 0.45), "steel", bevel=0.01)
            box((cx, y + side * 0.19, 3.3), (1.6, 0.03, 0.12), "dark", bevel=0.005)
            box((cx, y + side * 0.2, 3.3), (1.5, 0.01, 0.06), "amber", bevel=0.002)
        bolts([(cx + dx, y - 0.2, z) for dx in (-1.35, 1.35) for z in (D + 0.2, 2.6, 4.2)], "steel", r=0.03, h=0.03)
        bolts([(cx + dx, y + 0.2, z) for dx in (-1.35, 1.35) for z in (D + 0.2, 2.6, 4.2)], "steel", r=0.03, h=0.03, axis="+Y")
    box((0, y, 4.32), (6.6, 0.4, 0.16), "frame", bevel=0.03)
    box((0, y, D + 0.03), (6.6, 0.4, 0.06), "frame", bevel=0.01)
    for side, rot in ((-1, (90, 0, 0)), (1, (90, 0, 180))):
        text("KESTREL", (0, y + side * 0.2, 3.95), 0.22, "stencil", rot=rot, depth=0.002)
    # door control panel on the starboard jamb (outside) and port jamb (inside)
    for sx, side in ((1, 1), (-1, -1)):
        box((sx * 3.55, Y_REAR + side * 0.2, D + 1.2), (0.35, 0.08, 0.5), "frame", bevel=0.02)
        box((sx * 3.55, Y_REAR + side * 0.245, D + 1.25), (0.25, 0.01, 0.3), "screen", bevel=0.003)
        box((sx * 3.55, Y_REAR + side * 0.245, D + 1.02), (0.12, 0.01, 0.06), "green", bevel=0.002)


OBJECTS = [("hull", None), ("Engine", engine), ("LandingGear", landing_gear), ("Ramp", ramp), ("Cockpit", cockpit),
           ("CargoStack", cargo_stack), ("RearDoor", rear_door), ("StashLocker", zs.stash_locker), ("StashDrawer", zs.stash_drawer)]


# ============================================================================ cutaway split

def _cut(o, z, keep_below):
    me = o.data
    if "K_cap" not in [m.name for m in me.materials]:
        me.materials.append(zs.MATS["cap"]["mat"])
    cap_i = [m.name for m in me.materials].index("K_cap")
    bm = bmesh.new()
    bm.from_mesh(me)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=(0, 0, z), plane_no=(0, 0, 1),
                           clear_outer=keep_below, clear_inner=not keep_below)
    edges = [e for e in bm.edges if e.is_boundary]
    if edges:
        res = bmesh.ops.holes_fill(bm, edges=edges, sides=0)
        for f in res["faces"]:
            f.material_index = cap_i
            f.smooth = False
    bm.to_mesh(me)
    bm.free()


def split(parts, z):
    lower, upper = [], []
    for o in parts:
        o.data.transform(o.matrix_world)
        o.matrix_world = Matrix.Identity(4)
        zz = [v.co.z for v in o.data.vertices]
        if not zz:
            continue
        lo, hi = min(zz), max(zz)
        if hi <= z + 1e-4:
            lower.append(o)
        elif lo >= z - 1e-4:
            upper.append(o)
        else:
            o2 = o.copy(); o2.data = o.data.copy()
            bpy.context.scene.collection.objects.link(o2)
            _cut(o, z, True); _cut(o2, z, False)
            lower.append(o); upper.append(o2)
    return lower, upper


def build_group(name, fn_list):
    zs.PARTS.clear()
    for fn in fn_list:
        fn()
    parts = list(zs.PARTS)
    zs.PARTS.clear()
    lower, upper = split(parts, CUT_Z)
    out = []
    for n, lst in ((name + "_Lower", lower), (name + "_Upper", upper)):
        if not lst:
            continue
        zs.PARTS[:] = lst
        out.append(zs.finish(n))
    return out


# ============================================================================ preview

def preview(built):
    sc = bpy.context.scene
    zs.set_pass("normal", None)
    for o in bpy.data.objects:
        o.hide_render = o.name not in built
    eng = built.get("Engine")
    gear = built.get("LandingGear")
    for o in (eng, gear, built.get("StashLocker"), built.get("StashDrawer")):
        if o:
            o.hide_render = True
    def place(src, loc, scale=(1, 1, 1), yaw=0.0):
        o = src.copy(); o.data = src.data
        sc.collection.objects.link(o)
        o.location = V(loc); o.scale = V(scale); o.rotation_euler = (0, 0, rad(yaw)); o.hide_render = False
        return o
    if eng:
        place(eng, (7.55, 2.6, 3.25)); place(eng, (-7.55, 2.6, 3.25), (-1, 1, 1))
        for s in (1, -1):
            zs.box((s * 6.6, 2.9, 3.3), (0.7, 3.4, 0.45), "hullDark", bevel=0.04)     # preview-only pylon joint
    if gear:
        for x, y in ((3.0, -4.3), (-3.0, -4.3), (3.0, 5.0), (-3.0, 5.0)):
            place(gear, (x, y, 0))
    if "StashLocker" in built:
        place(built["StashLocker"], (-(WALL_X - 0.45), -2.6, DECK), yaw=90)
    if "CargoStack" in built:
        built["CargoStack"].location = (1.9, 2.2, 0)
    zs.PARTS.clear()
    # ground
    bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=40)
    me = bpy.data.meshes.new("Ground"); bm.to_mesh(me); bm.free()
    g = bpy.data.objects.new("Ground", me); sc.collection.objects.link(g)
    gm = bpy.data.materials.new("GroundMat"); gm.use_nodes = True
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.14, 0.12, 0.16, 1)
    gm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.95
    me.materials.append(gm)

    zs.setup_cycles(96)
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    try:
        sc.view_settings.view_transform = "AgX"; sc.view_settings.look = "AgX - Medium High Contrast"
    except Exception:
        pass
    w = bpy.data.worlds.new("Sky"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.05, 0.06, 0.09, 1)
    w.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0
    sun = bpy.data.lights.new("Sun", "SUN"); sun.energy = 3.2; sun.color = (1.0, 0.86, 0.7)
    so = bpy.data.objects.new("Sun", sun); sc.collection.objects.link(so); so.rotation_euler = (rad(50), 0, rad(-35))
    for loc, e, c in (((0, 4, 4.4), 260, (0.85, 0.92, 1.0)), ((0, -3, 4.4), 200, (0.85, 0.92, 1.0)), ((0, -8, 3.6), 80, (0.4, 0.8, 1.0))):
        l = bpy.data.lights.new("I", "POINT"); l.energy = e; l.color = c
        lo = bpy.data.objects.new("I", l); sc.collection.objects.link(lo); lo.location = loc
    cd = bpy.data.cameras.new("Cam"); cd.lens = 42
    cam = bpy.data.objects.new("Cam", cd); sc.collection.objects.link(cam); sc.camera = cam

    def shot(name, target, d, dist):
        d = V(d).normalized()
        cam.location = V(target) + d * dist
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = os.path.join(zs.PREVIEWS, f"Dropship_{name}.png")
        bpy.ops.render.render(write_still=True)
    shot("hero", (0, 0, 2.5), (-0.9, -0.75, 0.55), 30)
    shot("rear", (0, 6, 2.0), (0.7, 1.0, 0.45), 24)
    # cutaway: hide the upper halves, game camera angle
    for o in bpy.data.objects:
        if o.name.endswith("_Upper") or o.name == "CanopyGlass":
            o.hide_render = True
    for o in bpy.data.objects:     # the interior instance: interior parts only, no hull, no ground
        if o.type == "MESH":
            o.hide_render = not (o.name.startswith("Interior_Lower") or o.name in ("CargoStack",))
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.0, 0.0, 0.0, 1)
    so.hide_render = True
    for loc in ((0, -9, 5.0), (0, -1, 5.0), (0, 8, 5.0), (0, -15, 4.0), (3, 0, 4.5), (-3, 0, 4.5), (3, 9, 4.5), (-3, -9, 4.5)):
        l = bpy.data.lights.new("C", "POINT"); l.energy = 450; l.color = (0.85, 0.92, 1.0)
        lo = bpy.data.objects.new("C", l); sc.collection.objects.link(lo); lo.location = loc
    shot("instance", (0, -1.5, 1.8), (-0.55, -0.42, 0.9), 40)
    shot("instance_crew", (0, -9, 2.0), (-0.6, -0.3, 0.9), 15)
    print("[zcom] dropship previews written")


# ============================================================================ main

if __name__ == "__main__":
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    for k, p in zs.KINDS.items():
        zs.make_kind(k, p)
    built = {}
    for o in build_group("Hull", [hull_body, hull_details, rear_wall]):
        built[o.name] = o
    for o in build_group("Interior", [interior_big]):
        built[o.name] = o
    zs.PARTS.clear()
    canopy_glass()
    built["CanopyGlass"] = zs.finish("CanopyGlass")
    for name, fn in OBJECTS[1:]:
        zs.PARTS.clear()
        fn()
        built[name] = zs.finish(name)
    for n, o in built.items():
        print(f"[zcom] built {n}: {len(o.data.polygons)} faces")
    for name, o in built.items():
        if name.endswith("Glass"):
            zs.unwrap(o); zs.glass_material(o); continue
        if zs.ONLY and name not in zs.ONLY:
            ims = []
            for ch in ("BaseColor", "Normal", "MetallicSmoothness", "Emission"):
                p = os.path.join(zs.TEX_DIR, f"{name}_{ch}.png")
                ims.append(bpy.data.images.load(p) if os.path.exists(p) else None)
            zs.unwrap(o)
            if ims[0]:
                zs.preview_material(o, *ims)
            continue
        maps = zs.bake_piece(o, TEX.get(name, 2048))
        zs.preview_material(o, *maps)
    for o in bpy.data.objects:
        o.select_set(o.name in built)
    path = os.path.join(zs.MODELS, "Dropship.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             mesh_smooth_type="FACE", use_mesh_modifiers=True, bake_anim=False, path_mode="STRIP")
    print(f"[zcom] exported {path}")
    preview(built)
    print("[zcom] dropship done")
