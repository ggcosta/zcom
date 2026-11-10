"""
ZCOM - Marshal library animation, retargeted onto the generated rig.

zcom_marine_gen.py animates the Marshal procedurally. This script adds clips taken from a free animation
library instead: the Quaternius "Universal Animation Library [Standard]" (CC0, quaternius.itch.io). The pack
itself is not in the repo - download it and pass the path of Unreal-Godot/UAL1_Standard.glb.

The rig is rebuilt with the generator's own code (the rest pose is deterministic), so the clips fit the
committed Marshal.fbx without re-exporting its mesh or textures. Output is an animation-only FBX:

    <models>/MarshalMoves.fbx      Death  <- Death01        whole body; the rifle drops out of the right hand
                                   Run    <- Jog_Fwd_Loop   hips / spine / legs; both hands stay on the rifle

  blender -b --factory-startup -P Tools/Blender/zcom_marine_mocap.py -- <models> <previews> \
      <sculpt.glb> Art/Reference/Codex/marshal_turnaround.png <UAL1_Standard.glb> [preview] [trellis]
  The sculpt must be the one Marshal.fbx was built from (the rig is fitted to it): the Hunyuan3D-2 sculpt for
  zcom_marine_gen.py, or the raw TRELLIS.2 GLB plus "trellis" for zcom_marine_trellis.py.

Blender axes: Z up, the character faces -Y, +X is the character's LEFT (same for both rigs).
"""
import bpy, math, os, sys, tempfile
import numpy as np
from mathutils import Vector, Matrix, Quaternion

ARGS = sys.argv[sys.argv.index("--") + 1:]
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import zcom_marine as zm          # reads ARGS[0], ARGS[1] (models, previews)
import zcom_marine_gen as gen     # reads ARGS[2], ARGS[3] (sculpt, turnaround)

V = Vector
UAL = os.path.abspath(ARGS[4])
PREVIEW = "preview" in ARGS[5:]
TRELLIS = "trellis" in ARGS[5:]     # the sculpt is a raw TRELLIS.2 GLB (see zcom_marine_trellis.py)
SIDES = (("L", "l", 1.0), ("R", "r", -1.0))
SRC_BONES = ["pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "Head"] + [
    f"{b}_{s}" for s in "lr" for b in ("clavicle", "upperarm", "lowerarm", "hand", "thigh", "calf", "foot", "ball")]
Rz = lambda deg: zm.Rq((0, 0, 1), deg)


# ============================================================================ source clips

class Source:
    """The library skeleton: rest pose and sampled clips, all in world space."""

    def __init__(self, path):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=path)
        self.objs = [o for o in bpy.data.objects if o not in before]
        self.arm = next(o for o in self.objs if o.type == "ARMATURE")
        zm.upd()
        W = self.arm.matrix_world
        self.rest = {n: W @ self.arm.data.bones[n].matrix_local for n in SRC_BONES}
        self.rest_q = {n: m.to_quaternion() for n, m in self.rest.items()}

    def length(self, action):
        f0, f1 = bpy.data.actions[action].frame_range
        return int(round(f1 - f0))

    def sample(self, action, frames):
        sc = bpy.context.scene
        self.arm.animation_data.action = bpy.data.actions[action]
        out = []
        for f in frames:
            sc.frame_set(int(math.floor(f)), subframe=f - math.floor(f))
            W = self.arm.matrix_world
            out.append({n: W @ self.arm.pose.bones[n].matrix for n in SRC_BONES})
        return out

    def delta(self, S, n):
        """Rotation of bone n away from its rest pose (world space)."""
        return S[n].to_quaternion() @ self.rest_q[n].inverted()

    def remove(self):
        for o in self.objs:
            bpy.data.objects.remove(o)


# ============================================================================ retarget

def rest_q(arm, n):
    return arm.data.bones[n].matrix_local.to_quaternion()


def set_rot(arm, n, q):
    """World orientation q (a full orientation, not a delta) for bone n; the head stays where its parent put it."""
    zm.set_world(arm, n, q.to_matrix())


def limb_fix(arm, src, tgt, s_from, s_to):
    """The two rest poses differ (library: T-pose, straight legs; Marshal: arms down, legs apart). This is the
    rotation that lays the Marshal's rest bone along the library's, so equal deltas give equal directions."""
    b = arm.data.bones[tgt]
    dt = (b.tail_local - b.head_local).normalized()
    ds = (src.rest[s_to].translation - src.rest[s_from].translation).normalized()
    return dt.rotation_difference(ds)


def arm_fixes(arm, src):
    fix = {}
    for S, s, _ in SIDES:
        fix[f"Shoulder_{S}"] = (f"clavicle_{s}", limb_fix(arm, src, f"Shoulder_{S}", f"clavicle_{s}", f"upperarm_{s}"))
        fix[f"UpperArm_{S}"] = (f"upperarm_{s}", limb_fix(arm, src, f"UpperArm_{S}", f"upperarm_{s}", f"lowerarm_{s}"))
        fore = limb_fix(arm, src, f"ForeArm_{S}", f"lowerarm_{s}", f"hand_{s}")
        fix[f"ForeArm_{S}"] = (f"lowerarm_{s}", fore)
        fix[f"Hand_{S}"] = (f"hand_{s}", fore)
    return fix


def yaw_of(q):
    f = q @ V((0, -1, 0))
    return math.degrees(math.atan2(f.x, -f.y))


def pose_hips(arm, src, S, off, tilt=1.0, yaw=0.0):
    hq = rest_q(arm, "Hips")
    arm.pose.bones["Hips"].location = hq.inverted() @ (Rz(yaw) @ V(off))
    zm.set_fk(arm, "Hips", Rz(yaw) @ Quaternion().slerp(src.delta(S, "pelvis"), tilt))


def pose_legs(arm, src, S, ky=1.0, y_mid=0.0, kz=1.0, widen=0.03, leg_yaw=0.0, heading=0.0):
    """Legs by IK onto the library's foot path, so ground contact survives the different proportions.
    ky scales the stride about y_mid, kz the foot lift. For directional clips the stride runs along `heading`
    (degrees from forward, + = the character's left) while the feet, the stance width and the knees face `leg_yaw`."""
    bb = arm.data.bones
    RL, RH = Rz(leg_yaw), Rz(heading)
    for T, s, side in SIDES:
        ank0, ball0, toe0 = bb[f"Foot_{T}"].head_local, bb[f"Foot_{T}"].tail_local, bb[f"Toe_{T}"].tail_local
        heel0 = V((ank0.x, ank0.y + 0.07, ball0.z))
        sb, sb0 = S[f"ball_{s}"].translation, src.rest[f"ball_{s}"].translation
        qf = RL @ src.delta(S, f"foot_{s}")
        lateral = RL @ V((sb.x + side * widen, ball0.y - sb0.y + y_mid, 0.0))
        along = RH @ V((0.0, ky * (sb.y - y_mid), 0.0))
        ball = lateral + along + V((0.0, 0.0, ball0.z + kz * (sb.z - sb0.z)))
        ank = ball - qf @ (ball0 - ank0)
        # the Marshal's boot is longer than the library foot: lift until heel, ball and toe clear the floor
        low = min(ank.z + (qf @ (p - ank0)).z for p in (heel0, ball0, toe0))
        ank.z += max(0.0, ball0.z - low)
        hip = arm.pose.bones[f"Thigh_{T}"].head.copy()
        knee = RL @ (S[f"calf_{s}"].translation - S[f"thigh_{s}"].translation)
        d = (ank - hip).normalized()
        if (knee - d * knee.dot(d)).length < 0.02:
            knee = RL @ (src.delta(S, f"thigh_{s}") @ V((0, -1, 0)))
        w = zm.ik(arm, f"Thigh_{T}", f"Shin_{T}", ank, hip + knee, 1.0)
        zm.set_world(arm, f"Foot_{T}", qf.to_matrix() @ zm.rest3(arm, f"Foot_{T}"), w)
        zm.set_world(arm, f"Toe_{T}", (RL @ src.delta(S, f"ball_{s}")).to_matrix() @ zm.rest3(arm, f"Toe_{T}"))


def world_q(arm, n):
    return arm.pose.bones[n].matrix.to_quaternion()


# ============================================================================ Run

def anim_run(arm, src, name="Run", N=19, stride=0.70, lift=0.8, bounce=0.45, sink=0.4, lean=0.5, chest_yaw=0.25, phase=0.7,
             heading=0.0, leg_yaw=0.0, reverse=False):
    """Jog_Fwd_Loop, retimed to N frames with the stride shortened to `stride` of the library's (which bounds
    2.5 m per step - too long for a marine in heavy armour). `lean`, `sink` and `bounce` tone down the
    sprinter's forward pitch, crouch and vertical travel: the Marshal aims a rifle while he runs. `phase` is
    the library frame at which the left foot lands, so the clip stays in step with the procedural strafes it
    is blended with.
    Directional variants reuse the same cycle so every direction of the locomotion blend has one rhythm and
    phase: `reverse` plays it backwards (backpedal), `heading` / `leg_yaw` turn the stride and the legs towards a
    strafe while the chest keeps the rifle on the aim."""
    Ns = src.length("Jog_Fwd_Loop")
    sgn = -1.0 if reverse else 1.0
    frames = [(phase + sgn * fr * Ns / N) % Ns for fr in range(N + 1)]
    clip = src.sample("Jog_Fwd_Loop", frames)
    p0 = src.rest["pelvis"].translation
    offs = [S["pelvis"].translation - p0 for S in clip[:-1]]
    mean = sum(offs, V()) / len(offs)
    y_mid = sum(S[f"ball_{s}"].translation.y for S in clip[:-1] for s in "lr") / (2 * N)
    zm.new_action(arm, name)
    for fr, S in enumerate(clip):
        zm.reset_pose(arm)
        o = S["pelvis"].translation - p0
        pose_hips(arm, src, S, (o.x, o.y, sink * mean.z + bounce * (o.z - mean.z)), tilt=lean * (0.4 if reverse else 1.0), yaw=leg_yaw)
        zm.upd()
        qc = src.delta(S, "spine_03")
        yaw = yaw_of(qc)
        swing = Quaternion().slerp(qc @ Rz(-yaw), lean)                # the clip's lean without its twist, toned down
        if reverse:
            swing = Quaternion().slerp(swing, 0.3)                     # no sprinter's forward lean when backing up
        qc = swing @ Rz(zm.STANCE_YAW + chest_yaw * yaw)               # rifle stance yaw instead of the arm swing
        set_rot(arm, "Spine", world_q(arm, "Hips").slerp(qc @ rest_q(arm, "Spine"), 0.45))
        set_rot(arm, "Chest", qc @ rest_q(arm, "Chest"))
        set_rot(arm, "Neck", src.delta(S, "neck_01") @ rest_q(arm, "Neck"))
        set_rot(arm, "Head", src.delta(S, "Head") @ rest_q(arm, "Head"))
        p = fr / N
        zm.pose_weapon_arms(arm, sway_yaw=1.5 * math.sin(2 * math.pi * p), sway_pitch=-5 + 1.8 * math.cos(4 * math.pi * p))
        pose_legs(arm, src, S, ky=stride, y_mid=y_mid, kz=lift, widen=0.06,   # armoured thighs need the room
                  leg_yaw=leg_yaw, heading=heading)
        zm.key_all(arm, fr)
    speed = 5.0 * stride / (N / zm.FPS)      # the library clip covers 5.0 m per cycle
    print(f"[mocap] {name}: {N} frames, {5.0 * stride:.2f} m per cycle, native speed {speed:.2f} m/s (clip hips {mean.z:+.3f} m)")
    return speed


# ============================================================================ Idle

def anim_idle(arm, src, name="Idle", clip_name="Idle_Loop", amount=1.4):
    """The rifle stance of zcom_marine.anim_idle (staggered feet, lower body turned, head on the aim) brought to
    life by the library idle: its breathing, weight shift and head drift drive the stance's hips, chest and head
    (relative to the clip's first frame, scaled by `amount`). The feet stay planted where the stance puts them."""
    N = src.length(clip_name)
    clip = src.sample(clip_name, range(N + 1))
    S0 = clip[0]

    def motion(S, bone):
        e = (src.delta(S, bone) @ src.delta(S0, bone).inverted()).to_euler("XYZ")
        return math.degrees(e.x) * amount, math.degrees(e.y) * amount, math.degrees(e.z) * amount

    p0 = S0["pelvis"].translation
    zm.new_action(arm, name)
    for fr, S in enumerate(clip):
        zm.reset_pose(arm)
        o = (S["pelvis"].translation - p0) * amount
        hp, hr, hy = motion(S, "pelvis")
        cp, cr, cy = motion(S, "spine_03")
        _, _, ny = motion(S, "Head")
        zm.pose_torso(arm, (o.x, o.y, -0.035 + o.z), -10 + hy, (1, 0, 0), 2.0 + hp, zm.STANCE_YAW - 4 + cy,
                      chest_pitch=cp, roll=cr, head_yaw=18 + ny)
        zm.pose_weapon_arms(arm, sway_yaw=0.5 * cy, sway_pitch=-9 + 0.5 * cp, drop=0.03)
        zm.pose_legs(arm, {"L": (V((0.15, -0.17, 0.03)), 12, 0, 0), "R": (V((-0.135, 0.03, 0.03)), -22, 0, 0)}, V((0, -1, 0)))
        zm.key_all(arm, fr)
    print(f"[mocap] {name}: {N} frames ({N / zm.FPS:.1f} s) from {clip_name}")


# ============================================================================ Death

REL, FALL = 12, 13        # frame at which the right hand lets go of the rifle; frames it takes to hit the floor


def swing_arm(arm, side, z_t):
    """Swings a whole arm about its shoulder (elbow bend untouched) until the wrist is at height z_t."""
    zm.upd()
    names = [f"{b}_{side}" for b in ("UpperArm", "ForeArm", "Hand")]
    sh = arm.pose.bones[names[0]].head.copy()
    v = arm.pose.bones[names[2]].head - sh
    L = v.length
    dz = max(-L, min(L, z_t - sh.z))
    h = V((v.x, v.y, 0.0))
    h = h.normalized() if h.length > 1e-4 else V((0, 1, 0))
    q = v.rotation_difference(h * math.sqrt(max(L * L - dz * dz, 0.0)) + V((0, 0, dz)))
    qs = [q @ world_q(arm, n) for n in names]
    for n, qn in zip(names, qs):
        set_rot(arm, n, qn)


def anim_death(arm, src, body, name="Death"):
    N = src.length("Death01")
    clip = src.sample("Death01", range(N + 1))
    fix = arm_fixes(arm, src)
    p0 = src.rest["pelvis"].translation
    low = part_mask(body, ("Hips", "Thigh_L", "Thigh_R"))
    high = part_mask(body, ("Spine", "Chest", "Neck", "Head", "Shoulder_L", "Shoulder_R"))
    rel = {}

    def pose(fr, S, lift, arch):
        zm.reset_pose(arm)
        o = S["pelvis"].translation - p0
        pose_hips(arm, src, S, (o.x, o.y, o.z + lift))
        zm.upd()
        a = zm.smoothstep(fr / 10)                 # out of the rifle stance into the clip
        qs = src.delta(S, "spine_01").slerp(src.delta(S, "spine_02"), 0.5)
        q_sp = qs @ Rz(zm.STANCE_YAW * 0.45 * (1 - a)) @ rest_q(arm, "Spine")
        q_ch = src.delta(S, "spine_03") @ Rz(zm.STANCE_YAW * (1 - a)) @ rest_q(arm, "Chest")
        set_rot(arm, "Spine", q_sp)
        set_rot(arm, "Chest", q_ch)
        rise = lift                                # how far the shoulders end up above the clip's
        if arch > 0:
            # the back bends up off the floor about the hips' side-to-side axis: he comes to rest on his pack
            pb = arm.pose.bones
            z0 = pb["Chest"].tail.z
            ax = world_q(arm, "Hips") @ rest_q(arm, "Hips").inverted() @ V((1, 0, 0))
            sgn = 1.0 if ax.cross(pb["Chest"].tail - pb["Spine"].head).z > 0 else -1.0
            set_rot(arm, "Spine", zm.Rq(ax, sgn * arch * 0.5) @ q_sp)
            set_rot(arm, "Chest", zm.Rq(ax, sgn * arch) @ q_ch)
            rise += pb["Chest"].tail.z - z0
        set_rot(arm, "Neck", src.delta(S, "neck_01") @ rest_q(arm, "Neck"))
        set_rot(arm, "Head", src.delta(S, "Head") @ rest_q(arm, "Head"))
        # arms: start on the rifle, then follow the clip
        zm.pose_weapon_arms(arm, sway_pitch=-9, drop=0.03)
        hold = {n: world_q(arm, n) for n in fix}
        grip = arm.pose.bones["Hand_R"].matrix.inverted() @ arm.pose.bones["Weapon"].matrix
        for S_, _, _ in SIDES:
            zm.upd()
            for b in ("Shoulder", "UpperArm", "ForeArm", "Hand"):
                n = f"{b}_{S_}"
                sb, q = fix[n]
                rest = world_q(arm, n) if b == "Shoulder" else hold[n]
                set_rot(arm, n, rest.slerp(src.delta(S, sb) @ q @ rest_q(arm, n), a))
            if rise * a > 0.005:                   # raised shoulders: let the hands fall back to the floor
                zm.upd()
                wz = arm.pose.bones[f"Hand_{S_}"].head.z
                swing_arm(arm, S_, max(min(wz, 0.07), wz - rise * a))
        # rifle: rigid in the right hand, then it drops and comes to rest flat on the ground beside him
        zm.upd()
        m = arm.pose.bones["Hand_R"].matrix @ grip
        if fr == REL:
            rel["m"] = m.copy()
        elif fr > REL:
            t = min(1.0, (fr - REL) / FALL)
            p_r, q_r = rel["m"].translation, rel["m"].to_quaternion()
            wq = rest_q(arm, "Weapon")
            q_g = Rz(yaw_of(q_r @ wq.inverted()) + 20) @ zm.Rq((0, 1, 0), 90) @ wq
            p_g = V((p_r.x - 0.25, p_r.y - 0.1, 0.045))
            p = p_r.lerp(p_g, t)
            p.z = p_r.z + (p_g.z - p_r.z) * t * t
            m = Matrix.Translation(p) @ q_r.slerp(q_g, zm.smoothstep(t)).to_matrix().to_4x4()
        zm.set_world(arm, "Weapon", m.to_3x3(), m.translation)
        pose_legs(arm, src, S)

    # first pass: the library body is slimmer and carries no backpack. Raise the hips until the armour clears
    # the floor, then bend the back up until the pack does.
    lift, arch = [], []
    for fr, S in enumerate(clip):
        pose(fr, S, 0.0, 0.0)
        h = max(0.0, -min_z(body, low))
        if h > 0:
            pose(fr, S, h, 0.0)
        a = 0.0
        if min_z(body, high) < -0.005:
            a_lo, a = 0.0, 70.0
            for _ in range(7):
                mid = (a_lo + a) / 2
                pose(fr, S, h, mid)
                if min_z(body, high) < -0.005:
                    a_lo = mid
                else:
                    a = mid
        lift.append(h)
        arch.append(a)

    def smooth(x):
        x = [max(x[max(0, i - 2):i + 3]) for i in range(len(x))]
        return [sum(x[max(0, i - 3):i + 4]) / len(x[max(0, i - 3):i + 4]) for i in range(len(x))]
    lift, arch = smooth(lift), smooth(arch)
    print("[mocap] death hip lift  " + " ".join(f"{x:.2f}" for x in lift[::6]))
    print("[mocap] death back arch " + " ".join(f"{x:.0f}" for x in arch[::6]))
    zm.new_action(arm, name)
    for fr, S in enumerate(clip):
        pose(fr, S, lift[fr], arch[fr])
        zm.key_all(arm, fr)
    print(f"[mocap] {name}: {N} frames")


def part_mask(body, keep):
    """Per mesh: the vertices that mostly follow one of the bones in `keep`."""
    masks = {}
    for o in body:
        names = {g.index: g.name for g in o.vertex_groups}
        m = np.zeros(len(o.data.vertices), bool)
        for v in o.data.vertices:
            if v.groups:
                g = max(v.groups, key=lambda e: e.weight)
                m[v.index] = names[g.group] in keep
        masks[o.name] = m
    return masks


def min_z(body, masks):
    dg = bpy.context.evaluated_depsgraph_get()
    dg.update()
    z = 1e9
    for o in body:
        me = o.evaluated_get(dg).data
        co = np.empty(len(me.vertices) * 3, "f")
        me.vertices.foreach_get("co", co)
        zs = co[2::3]
        if len(zs) == len(masks[o.name]) and masks[o.name].any():
            z = min(z, float(zs[masks[o.name]].min()))
    return z


# ============================================================================ export / previews

def export(arm, name="MarshalMoves.fbx"):
    """gen.export() with no meshes writes Marshal.fbx - send it to a temp folder and keep the settings identical."""
    keep = gen.MODELS
    with tempfile.TemporaryDirectory() as tmp:
        gen.MODELS = tmp
        try:
            gen.export(arm, [])
        finally:
            gen.MODELS = keep
        dst = os.path.join(zm.MODELS, name)
        if os.path.exists(dst):
            os.remove(dst)
        os.replace(os.path.join(tmp, "Marshal.fbx"), dst)
    print(f"[mocap] exported {dst}")


def strips(arm, names, K=10, res=300, views=((90, 4), (35, 22), (0, 80)), tag=""):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light, sh.color_type, sh.single_color = "STUDIO", "SINGLE", (0.72, 0.74, 0.78)
    sh.show_shadows, sh.show_cavity = True, True
    sc.render.resolution_x = sc.render.resolution_y = res
    if "PrevCam" not in bpy.data.objects:
        bpy.ops.mesh.primitive_plane_add(size=30)
        cd = bpy.data.cameras.new("PrevCam")
        cd.type, cd.ortho_scale = "ORTHO", 2.7
        zm.link(bpy.data.objects.new("PrevCam", cd))
    cam = sc.camera = bpy.data.objects["PrevCam"]
    tmp = os.path.join(zm.PREVIEWS, "_strip.png")
    for n in names:
        act = bpy.data.actions[n]
        arm.animation_data.action = act
        f0, f1 = act.frame_range
        rows = []
        for az, el in views:
            row = []
            for i in range(K):
                f = f0 + (f1 - f0) * i / (K - 1)
                sc.frame_set(int(f), subframe=f - int(f))
                d = V((math.sin(math.radians(az)) * math.cos(math.radians(el)), -math.cos(math.radians(az)) * math.cos(math.radians(el)), math.sin(math.radians(el))))
                t = V((0, 0.25 if n.endswith("Death") else 0, 0.9))
                cam.location = t + d * 8
                cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
                sc.render.filepath = tmp
                bpy.ops.render.render(write_still=True)
                im = bpy.data.images.load(tmp)
                row.append(np.array(im.pixels[:]).reshape(im.size[1], im.size[0], 4))
                bpy.data.images.remove(im)
            rows.append(np.concatenate(row, axis=1))
        full = np.concatenate(rows[::-1], axis=0)
        im = bpy.data.images.new("strip", full.shape[1], full.shape[0])
        im.pixels = full.ravel().tolist()
        im.filepath_raw, im.file_format = os.path.join(zm.PREVIEWS, f"Mocap_{n}{tag}.png"), "PNG"
        im.save()
        bpy.data.images.remove(im)
        print(f"[mocap] preview Mocap_{n}{tag}.png")
    if os.path.exists(tmp):
        os.remove(tmp)


if __name__ == "__main__":
    zm.reset()
    sc = bpy.context.scene
    sc.render.fps = zm.FPS
    # the generator's rig, built the same way (mesh only for floor contact and previews - it is not exported)
    if TRELLIS:
        import zcom_marine_trellis as zt
        hi, tex, raw = zt.load_trellis(gen.GLB)
        bpy.data.objects.remove(tex)
    else:
        hi, raw = gen.load_sculpt()
    lo = gen.decimate(hi)
    J = gen.fit_joints(hi)
    co = gen.co_array(hi)
    back_y = float(np.percentile(co[(np.abs(co[:, 2] - 1.3) < 0.02) & (np.abs(co[:, 0]) < 0.12)][:, 1], 98))
    cape_y = gen.cape_line(hi)
    bpy.data.objects.remove(hi)
    pack = gen.build_backpack(back_y)
    if TRELLIS:
        zt.use_hold()
    else:
        zm.GRIP = V((-0.065, -0.25, 1.17))
        zm.STANCE_YAW = -22.0
    arm = gen.build_rig(J, cape_y)
    (zt.skin_weights if TRELLIS else gen.skin_weights)(lo, arm, cape_y, J)
    body = [lo, pack]
    for o in body:
        gen.attach(o, arm)
    zm.init_neutral(arm)

    src = Source(UAL)
    for o in src.objs:
        o.hide_render = True
    if PREVIEW:
        clip = src.sample("Jog_Fwd_Loop", range(src.length("Jog_Fwd_Loop") + 1))
        for f, S in enumerate(clip):
            l, r, p = S["ball_l"].translation, S["ball_r"].translation, S["pelvis"].translation
            print(f"[mocap] jog f{f:02d} L y {l.y:+.2f} z {l.z:.2f} | R y {r.y:+.2f} z {r.z:.2f} | pelvis z {p.z:.3f} yaw {yaw_of(src.delta(S, 'pelvis')):+.0f} chest yaw {yaw_of(src.delta(S, 'spine_03')):+.0f}")
    anim_death(arm, src, body)
    speed = anim_run(arm, src)
    # one cycle for every direction (Unity blends them by normalised time, so they share length and phase)
    anim_run(arm, src, "RunBack", reverse=True, stride=0.62, lift=0.65, bounce=0.35)
    anim_run(arm, src, "StrafeL", heading=90.0, leg_yaw=40.0, stride=0.62, lift=0.7)
    anim_run(arm, src, "StrafeR", heading=-90.0, leg_yaw=-40.0, stride=0.62, lift=0.7)
    anim_idle(arm, src)
    if PREVIEW:
        anim_run(arm, src, "RunB", N=17, stride=0.62)
        anim_run(arm, src, "RunFull", N=28, stride=1.0, lift=1.0, bounce=1.0, sink=1.0, lean=1.0)
    src.remove()
    keep = ("Death", "Run", "RunBack", "StrafeL", "StrafeR", "Idle")
    extra = [a.name for a in bpy.data.actions if a.name in ("RunB", "RunFull")]
    for a in list(bpy.data.actions):
        if a.name not in keep and a.name not in extra:
            bpy.data.actions.remove(a)
    if not PREVIEW:
        arm.animation_data.action = None
        for n in keep:
            tr = arm.animation_data.nla_tracks.new()
            tr.name = n
            tr.strips.new(n, 0, bpy.data.actions[n])
        sc.frame_start, sc.frame_end = 0, 90
        export(arm)
    else:
        zm.anim_gait(arm, "ProcRun", 16, (0, -1), 5.6, 0.36, 0.2, 8, 0, 0, crouch=0.03, bounce=0.03)
        zm.preview_weapon(arm)
        strips(arm, ["Death"])
        strips(arm, ["Death"], K=5, res=600, views=((90, 4), (35, 30), (-140, 35)), tag="_big")
        strips(arm, ["Run", "RunB", "RunFull", "ProcRun"], K=8, res=375, views=((90, 4), (0, 8)), tag="_cmp")
        strips(arm, ["Run", "RunBack", "StrafeL", "StrafeR"], K=8, res=375, views=((90, 4), (0, 60)), tag="_dir")
        strips(arm, ["Idle"], K=8, res=375, views=((-30, 8), (90, 4)), tag="_idle")
    print("[mocap] done")
