using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Twin-stick style controller: WASD relative to the camera, body faces the cursor, dash with i-frames.
    /// Also drives the procedural rig (leg swing, bob, lean, recoil, squash, death fall).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController I { get; private set; }

        [Header("Movement")]
        public float moveSpeed = 5.8f;
        public float acceleration = 26f;
        public float deceleration = 32f;
        public float turnSpeed = 1500f;
        [Tooltip("Seconds for the lower body to catch up with the aim; the torso tracks the cursor immediately.")]
        public float bodyTurnTime = 0.075f;

        [Header("Dash")]
        public float dashSpeed = 22f;
        public float dashDuration = 0.17f;
        public float dashCooldown = 0.7f;

        [Header("Aim")]
        public float aimHeight = 1.2f;

        [Header("Rig")]
        public Transform visual;
        public Transform torso;
        public Transform legL;
        public Transform legR;

        [Header("Animated model (optional)")]
        [Tooltip("Animator on the Blender character; drives Idle / Run / RunBack via the 'Move' parameter.")]
        public Animator animator;
        [Tooltip("Bones twisted at runtime so the legs follow the movement direction while the torso keeps aiming.")]
        public Transform hips, spine;

        [Header("Components")]
        public Gun gun;
        public GrenadeThrower grenades;
        public PlayerHealth health;
        public Inventory inventory;

        CharacterController cc;
        Vector3 velocity, push;
        float vy;
        Vector3 aimDir = Vector3.forward, aimPoint;
        float dashT, dashCdT;
        Vector3 dashDir;
        float dashLean;
        HeroActions actions;
        float walkPhase;
        Vector3 recoil, recoilVel;
        Vector3 squash = Vector3.one, squashVel;
        float twist, twistVel;
        float deathT = -1f;
        float trailT;
        Vector2 moveBlend;
        float yawVel, aimTwist;
        Vector3 prevVel, accelSmooth, accelVel;
        float bank, bankVel, lastYaw;
        Transform footL, footR, upperArmR;
        bool footDownL = true, footDownR = true;
        float rigKickBack, rigKickBackVel, rigKickPitch, rigKickPitchVel, rigKickYaw, rigKickYawVel;
        static readonly int MoveXId = Animator.StringToHash("MoveX");
        static readonly int MoveZId = Animator.StringToHash("MoveZ");
        static readonly int DeadId = Animator.StringToHash("Dead");

        public Vector3 AimPoint => aimPoint;
        public Vector3 AimDir => aimDir;
        public Vector3 Velocity => velocity;
        public bool IsDashing => dashT > 0f;
        public bool IsMoving => new Vector2(velocity.x, velocity.z).sqrMagnitude > 0.6f;
        public float DashCooldown01 => Mathf.Clamp01(dashCdT / dashCooldown);
        public bool CanAct => deathT < 0f && (health == null || health.IsAlive) && !GameFeel.Paused && !(GameManager.I && GameManager.I.IsExtracted);
        public Vector3 Chest => transform.position + Vector3.up * aimHeight;

        void Awake()
        {
            I = this;
            cc = GetComponent<CharacterController>();
            if (!gun) gun = GetComponentInChildren<Gun>();
            if (!grenades) grenades = GetComponent<GrenadeThrower>();
            if (!health) health = GetComponent<PlayerHealth>();
            if (!inventory) inventory = GetComponent<Inventory>();
            aimDir = transform.forward;
            aimPoint = transform.position + aimDir * 5f + Vector3.up * aimHeight;
            if (animator)
            {
                // Reload, throw, flinch and dash acting on the animated model.
                actions = GetComponent<HeroActions>();
                if (!actions) actions = gameObject.AddComponent<HeroActions>();
                actions.Init(this);
            }
        }

        /// <summary>World direction of the current (or last) dash.</summary>
        public Vector3 DashDir => dashDir;

        void Update()
        {
            float dt = Time.deltaTime;
            var input = PlayerInputSource.Current;
            var cam = CameraRig.I ? CameraRig.I.Cam : Camera.main;

            if (deathT >= 0f)
            {
                AnimateDeath(dt);
                return;
            }

            bool uiOpen = inventory && inventory.PanelOpen;
            if (cam && !uiOpen) UpdateAim(input, cam);

            Vector3 move = Vector3.zero;
            if (cam)
            {
                Vector3 fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
                Vector3 right = cam.transform.right; right.y = 0f; right.Normalize();
                move = Vector3.ClampMagnitude(fwd * input.move.y + right * input.move.x, 1f);
            }

            dashCdT -= dt;
            if (input.dash && !uiOpen && dashCdT <= 0f && dashT <= 0f) StartDash(move);

            if (dashT > 0f)
            {
                dashT -= dt;
                velocity = dashDir * dashSpeed;
                trailT -= dt;
                if (trailT <= 0f)
                {
                    trailT = 0.012f;
                    Vfx.Dots(transform.position + Vector3.up * Random.Range(0.4f, 1.6f) + Random.insideUnitSphere * 0.2f,
                        -dashDir * 1.5f + Random.insideUnitSphere * 0.5f, new Color(0.4f, 0.9f, 1f), Random.Range(0.15f, 0.3f), 0.35f);
                }
                if (dashT <= 0f) velocity = dashDir * moveSpeed;
            }
            else
            {
                Vector3 target = move * moveSpeed;
                float rate = move.sqrMagnitude > 0.01f ? acceleration : deceleration;
                velocity = Vector3.MoveTowards(velocity, target, rate * dt);
            }

            if (cc.isGrounded && vy < 0f) vy = -2f; else vy += Physics.gravity.y * dt;
            push = Vector3.Lerp(push, Vector3.zero, 1f - Mathf.Exp(-10f * dt));
            cc.Move((velocity + push + Vector3.up * vy) * dt);

            // Lower body turns with a short critically-damped lag; the residual is taken up by the spine in LateUpdate.
            float targetYaw = Mathf.Atan2(aimDir.x, aimDir.z) * Mathf.Rad2Deg;
            float yaw = animator ? Mathf.SmoothDampAngle(transform.eulerAngles.y, targetYaw, ref yawVel, bodyTurnTime, 2200f, dt)
                                 : Mathf.MoveTowardsAngle(transform.eulerAngles.y, targetYaw, turnSpeed * dt);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            aimTwist = Mathf.DeltaAngle(yaw, targetYaw);

            AnimateRig(dt);
        }

        void UpdateAim(PlayerInputFrame input, Camera cam)
        {
            Ray ray = cam.ScreenPointToRay(input.pointer);
            var plane = new Plane(Vector3.up, new Vector3(0f, aimHeight, 0f));
            if (plane.Raycast(ray, out float d))
            {
                aimPoint = ray.GetPoint(d);
                Vector3 to = aimPoint - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.5f) aimDir = to.normalized;
            }
        }

        void StartDash(Vector3 move)
        {
            dashDir = move.sqrMagnitude > 0.01f ? move.normalized : aimDir;
            dashT = dashDuration;
            dashCdT = dashCooldown;
            squash = new Vector3(0.8f, 1.15f, 1.25f);
            ProcAudio.Play("dash", 0.5f, 0.08f);
            Vfx.Ring(transform.position + Vector3.up * 0.05f, 0.3f, 1.6f, new Color(0.3f, 0.8f, 1f) * 2f, 0.25f);
        }

        void AnimateRig(float dt)
        {
            if (!visual) return;
            Vector3 planar = new Vector3(velocity.x, 0f, velocity.z);
            float speed = planar.magnitude;
            float amt = Mathf.Clamp01(speed / moveSpeed);
            Vector3 localVel = transform.InverseTransformDirection(planar);

            if (animator)
            {
                AnimateBody(dt, localVel, speed, amt);
                return;
            }

            walkPhase += dt * speed * 2.1f;
            float s = Mathf.Sin(walkPhase);
            // Swing legs along the actual movement direction (works for strafing/backpedalling too).
            Vector3 swingAxis = localVel.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, localVel.normalized) : Vector3.right;
            if (legL) legL.localRotation = Quaternion.AngleAxis(s * 38f * amt, swingAxis);
            if (legR) legR.localRotation = Quaternion.AngleAxis(-s * 38f * amt, swingAxis);

            Spring.Step(ref recoil, ref recoilVel, Vector3.zero, 320f, 24f, dt);
            Spring.Step(ref squash, ref squashVel, Vector3.one, 260f, 14f, dt);
            Spring.Step(ref twist, ref twistVel, 0f, 200f, 18f, dt);

            float bob = Mathf.Abs(Mathf.Cos(walkPhase)) * 0.07f * amt;
            float breathe = Mathf.Sin(Time.time * 2.2f) * 0.012f;
            visual.localPosition = new Vector3(0f, bob + breathe, 0f) + recoil;

            float leanMul = IsDashing ? 2.2f : 1.4f;
            float pitchLean = Mathf.Clamp(localVel.z * leanMul, -14f, 14f);
            float rollLean = Mathf.Clamp(-localVel.x * leanMul, -14f, 14f);
            visual.localRotation = Quaternion.Euler(pitchLean, twist, rollLean);
            visual.localScale = squash;
        }

        /// <summary>Animated-model path: 2D directional locomotion blend with playback matched to ground speed (no foot
        /// sliding), lean into acceleration and turns, footsteps from the animated feet.</summary>
        void AnimateBody(float dt, Vector3 localVel, float speed, float amt)
        {
            if (!footL)
            {
                footL = CharacterModels.Find(animator.transform, "Foot_L");
                footR = CharacterModels.Find(animator.transform, "Foot_R");
                upperArmR = CharacterModels.Find(animator.transform, "UpperArm_R");
            }
            Vector2 target = new Vector2(localVel.x, localVel.z) / Mathf.Max(0.1f, moveSpeed);
            if (target.sqrMagnitude > 1f) target.Normalize();
            if (speed < 0.2f) target = Vector2.zero;
            moveBlend = Vector2.Lerp(moveBlend, target, 1f - Mathf.Exp(-9f * dt));
            animator.SetFloat(MoveXId, moveBlend.x);
            animator.SetFloat(MoveZId, moveBlend.y);

            // stride sync: each clip covers a fixed distance per cycle (Run 5.53 m/s, RunBack and strafes 4.89 at 1x)
            Vector2 dir = moveBlend.sqrMagnitude > 0.001f ? moveBlend.normalized : Vector2.up;
            float native = dir.y >= 0f ? Mathf.Lerp(4.89f, 5.53f, dir.y * dir.y) : 4.89f;
            float m = Mathf.Clamp01(moveBlend.magnitude);
            float sync = speed > 0.5f ? Mathf.Clamp(speed / (native * Mathf.Max(m, 0.35f)), 0.8f, 1.5f) : 1f;
            animator.speed = IsDashing ? 1.7f : Mathf.Lerp(1f, sync, Mathf.Clamp01((m - 0.3f) / 0.4f));

            // lean into acceleration (body weight) and bank into turns
            Vector3 planarVel = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 accel = (planarVel - prevVel) / Mathf.Max(dt, 1e-4f);
            prevVel = planarVel;
            Spring.Step(ref accelSmooth, ref accelVel, accel, 90f, 16f, dt);
            Vector3 la = transform.InverseTransformDirection(accelSmooth);
            float yawNow = transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(lastYaw, yawNow) / Mathf.Max(dt, 1e-4f);
            lastYaw = yawNow;
            Spring.Step(ref bank, ref bankVel, Mathf.Clamp(-yawRate * 0.012f * Mathf.Clamp01(speed / 3f), -7f, 7f), 120f, 18f, dt);
            // Dashing throws the whole body into the direction of travel (not just forwards).
            dashLean = Mathf.MoveTowards(dashLean, IsDashing ? 1f : 0f, dt * (IsDashing ? 14f : 6f));
            Vector3 ld = transform.InverseTransformDirection(dashDir) * (16f * dashLean);
            float pitch = Mathf.Clamp(la.z * 0.32f, -9f, 9f) + ld.z;
            float roll = Mathf.Clamp(-la.x * 0.32f, -8f, 8f) + bank - ld.x;

            Spring.Step(ref recoil, ref recoilVel, Vector3.zero, 320f, 24f, dt);
            Spring.Step(ref twist, ref twistVel, 0f, 200f, 18f, dt);
            visual.localPosition = new Vector3(recoil.x, 0f, recoil.z) * 0.5f;
            visual.localRotation = Quaternion.Euler(pitch, twist, roll);
            visual.localScale = Vector3.one;

            Spring.Step(ref rigKickBack, ref rigKickBackVel, 0f, 520f, 30f, dt);
            Spring.Step(ref rigKickPitch, ref rigKickPitchVel, 0f, 420f, 26f, dt);
            Spring.Step(ref rigKickYaw, ref rigKickYawVel, 0f, 380f, 24f, dt);
        }

        /// <summary>Weapon recoil on the upper body (gun, hands and arms move together): back push, muzzle climb, twist.</summary>
        public void RigKick(float back, float pitch)
        {
            rigKickBackVel -= back * 22f;
            rigKickPitchVel -= pitch * 22f;
            rigKickYawVel += Random.Range(-1f, 1f) * pitch * 9f;
        }

        void Footsteps()
        {
            if (!footL || !footR || deathT >= 0f) return;
            float baseY = transform.position.y;
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            void Check(Transform f, ref bool down)
            {
                float h = f.position.y - baseY;
                if (down && h > 0.2f) down = false;
                else if (!down && h < 0.15f)
                {
                    down = true;
                    if (speed > 1.5f && !IsDashing)
                    {
                        float k = Mathf.Clamp01(speed / moveSpeed);
                        ProcAudio.Play("step", 0.16f + 0.18f * k, 0.12f, 0.9f + Random.Range(0f, 0.15f));
                        Vector3 p = new Vector3(f.position.x, baseY + 0.05f, f.position.z);
                        for (int i = 0; i < 3; i++)
                            Vfx.Dots(p + Random.insideUnitSphere * 0.08f, (Random.insideUnitSphere + Vector3.up * 0.6f - velocity.normalized * 0.4f) * 0.9f,
                                new Color(0.55f, 0.52f, 0.6f, 0.5f), Random.Range(0.08f, 0.16f), Random.Range(0.35f, 0.6f));
                    }
                }
            }
            Check(footL, ref footDownL);
            Check(footR, ref footDownR);
        }

        void LateUpdate()
        {
            if (!animator || deathT >= 0f) return;
            Vector3 up = Vector3.up;
            // aim offset: spine/chest take up the lag between the legs and the cursor so the gun is always on target
            if (spine) spine.rotation = Quaternion.AngleAxis(aimTwist * 0.4f, up) * spine.rotation;
            if (torso)
            {
                torso.rotation = Quaternion.AngleAxis(aimTwist * 0.6f + rigKickYaw, up) * torso.rotation;
                Vector3 right = visual ? visual.right : transform.right;
                torso.rotation = Quaternion.AngleAxis(rigKickPitch, right) * torso.rotation;
                torso.position += (visual ? visual.forward : transform.forward) * Mathf.Clamp(rigKickBack, -0.08f, 0.02f);
            }
            if (spine) spine.rotation = Quaternion.AngleAxis(rigKickPitch * 0.35f, visual ? visual.right : transform.right) * spine.rotation;
            Footsteps();
        }

        void AnimateDeath(float dt)
        {
            deathT += dt;
            if (!visual) return;
            if (animator) { if (cc.enabled && !cc.isGrounded) cc.Move(Vector3.down * 4f * dt); return; }   // Death clip plays
            float k = Ease.OutCubic(deathT / 0.6f);
            visual.localRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-86f, 0f, 12f), k);
            visual.localPosition = Vector3.Lerp(Vector3.zero, new Vector3(0f, 0.15f, 0f), k);
            if (cc.enabled && !cc.isGrounded) cc.Move(Vector3.down * 4f * dt);
        }

        public void Recoil(Vector3 worldDir, float amount)
        {
            recoil += transform.InverseTransformDirection(worldDir) * amount;
        }

        public void PlayThrow()
        {
            if (actions) actions.Throw();
            else twistVel -= 900f;
            squash = new Vector3(1.08f, 0.94f, 1.08f);
        }

        public void AddPush(Vector3 impulse)
        {
            impulse.y = 0f;
            push += impulse;
        }

        /// <param name="dir">World direction the blow was travelling in.</param>
        public void HitReact(Vector3 dir)
        {
            squash = new Vector3(1.15f, 0.85f, 1.15f);
            if (actions) actions.Hit(dir);
        }

        public void OnDeath()
        {
            deathT = 0f;
            velocity = Vector3.zero;
            if (animator) { animator.speed = 1f; animator.SetTrigger(DeadId); }
            if (gun) gun.enabled = false;
            if (grenades) grenades.CancelAim();
        }
    }
}
