using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Upper-body acting layered over the Marshal's locomotion clips, so actions read on the character and not only
    /// on the gun and the HUD. Everything is procedural (no extra animation clips), applied after the Animator:
    ///  - Reload: the left hand leaves the handguard, pulls the spent cell, fetches a fresh one from the belt
    ///    (it is visible in the hand), slams it home in time with the gun's own reload events, and returns.
    ///  - Grenade: the left arm cocks back with a grenade in the hand while the throw is being aimed, then whips
    ///    forward on release.
    ///  - Hit: spine, chest and head flinch away from the blow.
    ///  - Dash: the body tucks into the dash and leaves a short trail of after-images.
    /// The right arm is never touched: it keeps the gun on target the whole time.
    /// Runs after PlayerController (aim twist, recoil) and before SpringBones (cape).
    /// </summary>
    [DefaultExecutionOrder(20)]
    public class HeroActions : MonoBehaviour
    {
        // Left-hand targets in the aim frame (x right, y up, z towards the cursor), metres from the feet.
        static readonly Vector3 ThrowCocked = new Vector3(-0.42f, 1.52f, -0.2f);
        static readonly Vector3 ThrowEnd = new Vector3(-0.1f, 1.55f, 0.62f);
        const float ThrowSweep = 0.11f, ThrowHold = 0.08f, ThrowReturn = 0.3f;

        PlayerController pc;
        Transform spine, chest, neck, head, upperArm, foreArm, hand, hips;
        bool ready;

        Vector3 flinch, flinchVel;          // world XZ, degrees
        float windup;                       // 0..1 grenade arm cocked
        float throwT = -1f;                 // seconds since release, <0 when idle
        Vector3 throwFrom;                  // aim-frame hand position at release
        float chestYaw, chestYawVel;
        float dashAmt;
        Vector3 handOffset;                 // smoothed world offset of the left wrist from its animated position
        Vector3 lastTarget;
        float ghostT;

        Transform cellProp, heldGrenade;
        MeshFilter cellMf;
        MeshRenderer cellMr;
        SkinnedMeshRenderer[] skins;

        public void Init(PlayerController owner)
        {
            pc = owner;
            if (!pc || !pc.animator) return;
            var root = pc.animator.transform;
            spine = pc.spine ? pc.spine : CharacterModels.Find(root, "Spine");
            chest = pc.torso ? pc.torso : CharacterModels.Find(root, "Chest");
            hips = pc.hips ? pc.hips : CharacterModels.Find(root, "Hips");
            neck = CharacterModels.Find(root, "Neck");
            head = CharacterModels.Find(root, "Head");
            upperArm = CharacterModels.Find(root, "UpperArm_L");
            foreArm = CharacterModels.Find(root, "ForeArm_L");
            hand = CharacterModels.Find(root, "Hand_L");
            skins = root.GetComponentsInChildren<SkinnedMeshRenderer>();
            ready = spine && chest;
        }

        /// <summary>Flinch away from a blow travelling along <paramref name="dir"/>.</summary>
        public void Hit(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = -transform.forward;
            flinchVel = Vector3.ClampMagnitude(flinchVel + dir.normalized * 320f, 460f);
        }

        /// <summary>The grenade has just left the hand.</summary>
        public void Throw()
        {
            if (!ready) return;
            throwFrom = Quaternion.Inverse(AimRotation()) * (lastTarget - transform.position);
            // A quick tap never cocked the arm; start the swing from behind the shoulder anyway so it reads as a throw.
            throwFrom = Vector3.Lerp(ThrowCocked, throwFrom, windup);
            throwT = 0f;
            chestYawVel += 520f;
        }

        Quaternion AimRotation()
        {
            Vector3 d = pc.AimDir; d.y = 0f;
            return d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d) : transform.rotation;
        }

        static void Bend(Transform t, Vector3 axis, float degrees)
        {
            if (t) t.rotation = Quaternion.AngleAxis(degrees, axis) * t.rotation;
        }

        static float Seg(float p, float a, float b) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, p));

        void LateUpdate()
        {
            if (!ready || !pc) return;
            bool alive = pc.health == null || pc.health.IsAlive;
            if (!alive)
            {
                if (cellProp) cellProp.gameObject.SetActive(false);
                if (heldGrenade) heldGrenade.gameObject.SetActive(false);
                return;
            }
            float dt = Time.deltaTime;
            Vector3 up = Vector3.up, right = transform.right, fwd = transform.forward;

            // ---- hit flinch
            Spring.Step(ref flinch, ref flinchVel, Vector3.zero, 170f, 13f, dt);
            float deg = flinch.magnitude;
            if (deg > 0.05f)
            {
                Vector3 axis = Vector3.Cross(up, flinch / deg);
                Bend(spine, axis, deg * 0.45f);
                Bend(chest, axis, deg * 0.55f);
                Bend(head, axis, deg * 0.5f);
            }

            // ---- dash tuck
            dashAmt = Mathf.MoveTowards(dashAmt, pc.IsDashing ? 1f : 0f, dt * (pc.IsDashing ? 16f : 7f));
            if (dashAmt > 0.01f)
            {
                Vector3 axis = Vector3.Cross(up, pc.DashDir);
                Bend(spine, axis, 11f * dashAmt);
                Bend(head, axis, -8f * dashAmt);   // eyes stay up
            }
            if (pc.IsDashing)
            {
                ghostT -= dt;
                if (ghostT <= 0f) { ghostT = 0.045f; SpawnGhost(); }
            }
            else ghostT = 0f;

            // ---- grenade: wind-up twist and release whip on the chest
            bool aiming = pc.grenades && pc.grenades.IsAiming;
            windup = Mathf.MoveTowards(windup, aiming ? 1f : 0f, dt / (aiming ? 0.14f : 0.2f));
            Spring.Step(ref chestYaw, ref chestYawVel, -11f * windup, 160f, 16f, dt);
            if (Mathf.Abs(chestYaw) > 0.05f) Bend(chest, up, chestYaw);

            // ---- left arm
            bool reloading = pc.gun && pc.gun.isActiveAndEnabled && pc.gun.IsReloading;
            float p = reloading ? pc.gun.ReloadProgress : 0f;
            if (reloading)
            {
                float look = Seg(p, 0.04f, 0.22f) * (1f - Seg(p, 0.76f, 0.95f));
                Bend(neck, right, 6f * look);
                Bend(head, right, 11f * look);
            }

            bool showCell = false;
            if (upperArm && foreArm && hand)
            {
                Vector3 anim = hand.position;
                Vector3 target = anim;
                Vector3 pole = -right * 0.7f - up - fwd * 0.2f;

                if (reloading)
                {
                    var cell = pc.gun.CellPart;
                    Vector3 well = (cell ? cell.position : pc.gun.transform.position + Vector3.down * 0.08f) + Vector3.down * 0.05f - right * 0.02f;
                    Vector3 pulled = well + Vector3.down * 0.08f;
                    Vector3 belt = (hips ? hips.position : transform.position + up) + right * -0.25f + up * 0.03f + fwd * 0.07f;
                    if (p < 0.16f) target = Vector3.Lerp(anim, well, Seg(p, 0f, 0.16f));                   // reach for the spent cell
                    else if (p < 0.24f) target = Vector3.Lerp(well, pulled, Seg(p, 0.16f, 0.24f));           // yank it (the gun drops it at 0.2)
                    else if (p < 0.44f) target = Vector3.Lerp(pulled, belt, Seg(p, 0.24f, 0.44f));           // down to the belt
                    else if (p < 0.5f) target = belt;
                    else if (p < 0.67f) target = Vector3.Lerp(belt, pulled, Seg(p, 0.5f, 0.67f)) - right * (0.06f * Mathf.Sin(Mathf.InverseLerp(0.5f, 0.67f, p) * Mathf.PI));
                    else if (p < 0.72f) target = Vector3.Lerp(pulled, well, Seg(p, 0.67f, 0.72f));           // slam home (the gun seats it at 0.7)
                    else if (p < 0.82f) target = well;
                    else target = Vector3.Lerp(well, anim, Seg(p, 0.82f, 1f));
                    showCell = p > 0.46f && p < 0.7f;
                }

                Quaternion aim = AimRotation();
                if (windup > 0.001f)
                {
                    target = Vector3.Lerp(target, transform.position + aim * ThrowCocked, Mathf.SmoothStep(0f, 1f, windup));
                    pole = Vector3.Lerp(pole, -right - up * 0.6f - fwd * 0.5f, windup);
                }
                if (throwT >= 0f)
                {
                    throwT += dt;
                    float k = Mathf.Clamp01(throwT / ThrowSweep);
                    Vector3 local = Vector3.Lerp(throwFrom, ThrowEnd, Ease.OutCubic(k)) + Vector3.up * (0.14f * Mathf.Sin(k * Mathf.PI));
                    float back = Mathf.SmoothStep(0f, 1f, (throwT - ThrowSweep - ThrowHold) / ThrowReturn);
                    target = Vector3.Lerp(transform.position + aim * local, target, back);
                    if (throwT > ThrowSweep + ThrowHold + ThrowReturn) throwT = -1f;
                }

                // A short lag hides every hand-off between poses (and an interrupted reload).
                handOffset = Vector3.Lerp(handOffset, target - anim, 1f - Mathf.Exp(-34f * dt));
                lastTarget = anim + handOffset;
                if (handOffset.sqrMagnitude > 1e-6f) SolveArm(upperArm, foreArm, hand, lastTarget, pole);
            }

            UpdateCellProp(showCell);
            UpdateHeldGrenade(windup > 0.35f && throwT < 0f);
        }

        /// <summary>The grenade sits in the left palm while the throw is aimed; the real one spawns on release.</summary>
        void UpdateHeldGrenade(bool show)
        {
            if (!show || !hand || !foreArm)
            {
                if (heldGrenade) heldGrenade.gameObject.SetActive(false);
                return;
            }
            if (!heldGrenade)
            {
                heldGrenade = new GameObject("HeldGrenade").transform;
                heldGrenade.SetParent(transform, false);
                Grenade.BuildVisual(heldGrenade);
            }
            if (!heldGrenade.gameObject.activeSelf) heldGrenade.gameObject.SetActive(true);
            Vector3 along = (hand.position - foreArm.position).normalized;
            heldGrenade.SetPositionAndRotation(hand.position + along * 0.1f, hand.rotation);
        }

        /// <summary>Two-bone IK: bends the elbow to the right reach, swings the arm onto the target, then rolls the
        /// elbow around the shoulder-wrist line towards <paramref name="pole"/>.</summary>
        static void SolveArm(Transform upper, Transform fore, Transform hand, Vector3 target, Vector3 pole)
        {
            Vector3 a = upper.position, b = fore.position, c = hand.position;
            float l1 = (b - a).magnitude, l2 = (c - b).magnitude;
            if (l1 < 1e-4f || l2 < 1e-4f) return;
            float d = Mathf.Clamp((target - a).magnitude, Mathf.Abs(l1 - l2) + 0.01f, l1 + l2 - 0.004f);

            float want = Mathf.Acos(Mathf.Clamp((l1 * l1 + l2 * l2 - d * d) / (2f * l1 * l2), -1f, 1f)) * Mathf.Rad2Deg;
            float have = Vector3.Angle(a - b, c - b);
            Vector3 axis = Vector3.Cross(a - b, c - b);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(a - b, pole);
            if (axis.sqrMagnitude < 1e-8f) return;
            fore.rotation = Quaternion.AngleAxis(want - have, axis.normalized) * fore.rotation;

            upper.rotation = Quaternion.FromToRotation(hand.position - a, target - a) * upper.rotation;

            Vector3 n = (hand.position - a).normalized;
            Vector3 cur = Vector3.ProjectOnPlane(fore.position - a, n), des = Vector3.ProjectOnPlane(pole, n);
            if (cur.sqrMagnitude > 1e-6f && des.sqrMagnitude > 1e-6f)
                upper.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(cur, des, n), n) * upper.rotation;
        }

        /// <summary>The fresh cell carried from the belt to the gun: a copy of the gun's own cell part, held in the hand.</summary>
        void UpdateCellProp(bool show)
        {
            var cell = show && pc.gun ? pc.gun.CellPart : null;
            var srcMf = cell ? cell.GetComponent<MeshFilter>() : null;
            var srcMr = cell ? cell.GetComponent<MeshRenderer>() : null;
            if (!srcMf || !srcMr || !hand || !foreArm)
            {
                if (cellProp && cellProp.gameObject.activeSelf) cellProp.gameObject.SetActive(false);
                return;
            }
            if (!cellProp)
            {
                var go = new GameObject("ReloadCell");
                cellProp = go.transform;
                cellProp.SetParent(transform, false);
                cellMf = go.AddComponent<MeshFilter>();
                cellMr = go.AddComponent<MeshRenderer>();
                cellMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (!cellProp.gameObject.activeSelf) cellProp.gameObject.SetActive(true);
            cellMf.sharedMesh = srcMf.sharedMesh;
            cellMr.sharedMaterial = srcMr.sharedMaterial;
            Vector3 palm = hand.position + (hand.position - foreArm.position).normalized * 0.085f;
            cellProp.SetPositionAndRotation(palm, cell.rotation);
            cellProp.localScale = cell.lossyScale;
        }

        void SpawnGhost()
        {
            if (skins == null || skins.Length == 0) return;
            var go = new GameObject("DashGhost");
            var ghost = go.AddComponent<DashGhost>();
            ghost.Begin(new Color(0.2f, 0.62f, 1f), 0.55f, 0.24f);
            foreach (var smr in skins)
            {
                if (!smr || !smr.enabled || !smr.gameObject.activeInHierarchy) continue;
                var mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                ghost.Add(mesh, smr.transform.position, smr.transform.rotation);
            }
        }
    }

    /// <summary>A frozen, glowing copy of a skinned character that fades out (dash after-image).</summary>
    public class DashGhost : MonoBehaviour
    {
        Material mat;
        Color color;
        float life = 0.25f, t;
        readonly System.Collections.Generic.List<Mesh> meshes = new System.Collections.Generic.List<Mesh>();
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public void Begin(Color c, float intensity, float seconds)
        {
            color = Mats.HDR(c, intensity);
            life = Mathf.Max(0.05f, seconds);
            mat = Mats.Additive(null, color, false);
        }

        public void Add(Mesh mesh, Vector3 pos, Quaternion rot)
        {
            meshes.Add(mesh);
            var part = new GameObject("Part");
            part.transform.SetParent(transform, false);
            part.transform.SetPositionAndRotation(pos, rot);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = part.AddComponent<MeshRenderer>();
            var mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(t / life);
            if (mat) mat.SetColor(ColorId, color * (k * k));
            if (t >= life) Destroy(gameObject);
        }

        void OnDestroy()
        {
            foreach (var m in meshes) if (m) Destroy(m);
            if (mat) Destroy(mat);
        }
    }
}
