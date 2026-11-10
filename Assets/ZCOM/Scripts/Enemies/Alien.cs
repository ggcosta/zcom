using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Zcom
{
    public enum AlienType { Skitter, Spitter, Brute }

    /// <summary>
    /// One component for all alien types. Telegraph -> attack -> recover loop, with readable wind-ups:
    ///  Skitter: fast melee lunge. Spitter: keeps distance, lobs acid. Brute: slow tank with a ground slam.
    /// All motion is procedural (bob, leg cycles, squash/stretch, hit flash, spawn pop).
    /// </summary>
    public class Alien : MonoBehaviour, IDamageable
    {
        public static readonly List<Alien> All = new List<Alien>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); }

        [Header("Type")]
        public AlienType type;
        public int scoreValue = 10;
        [Range(0, 1)] public float healthDropChance = 0.1f;

        [Header("Stats")]
        public float maxHealth = 40f;
        public float moveSpeed = 6f;
        public float agentRadius = 0.5f;
        public float attackRange = 2.4f;
        public float attackDamage = 9f;
        public float telegraphTime = 0.3f;
        public float attackCooldown = 1f;
        public float recoverTime = 0.45f;
        public float knockbackResist = 1f;
        [Header("Skitter")]
        public float lungeSpeed = 15f;
        public float lungeTime = 0.2f;
        [Header("Spitter")]
        public float preferredRange = 10f;
        public float projectileSpeed = 12f;
        [Header("Brute")]
        public float slamRadius = 3.4f;

        [Header("Rig")]
        public Transform body;
        public Transform sac;
        public Transform mouth;
        public Transform[] legs;
        public Transform[] arms;
        public float legSwing = 30f;
        public float stepFrequency = 10f;
        public float bobAmount = 0.06f;
        public float deathSize = 1f;

        [Header("Look")]
        public Material skinMaterial;
        [Tooltip("Carapace material: the chips knocked off by hits and the plates left by a death. Falls back to the skin.")]
        public Material shellMaterial;
        public Material glowMaterial;
        public Color gooColor = new Color(0.4f, 1f, 0.3f);
        public Color glowColor = new Color(0.4f, 1f, 0.3f);

        enum State { Spawning, Idle, Chase, Telegraph, Attack, Recover, Stagger, Dead }

        [Header("Camp behaviour")]
        [Tooltip("Notices the player within this range (with line of sight), or always within 4m.")]
        public float aggroRange = 11f;
        [Tooltip("Gives up and walks home when this far from home and the player is out of reach.")]
        public float leashRange = 28f;
        public float guardRadius = 6f;
        AlienCamp camp;
        Vector3 home;
        bool hasHome;
        float wanderT;

        /// <summary>True once the alien has noticed the player (not idling / going home).</summary>
        public bool IsAlerted => state != State.Idle && state != State.Dead && state != State.Spawning;
        public AlienCamp Camp => camp;

        public float Health { get; private set; }
        public bool IsAlive => state != State.Dead && Health > 0f;
        public float Health01 => maxHealth > 0f ? Mathf.Clamp01(Health / maxHealth) : 0f;
        /// <summary>The hit capsule on the root (carcasses are told to ignore it).</summary>
        public Collider BodyCollider => col;
        public float LastHitTime { get; private set; } = -100f;
        /// <summary>Revealed by the Marshal's scan pulse until this time (shown through walls on the HUD).</summary>
        public float RevealedUntil { get; set; } = -100f;

        State state;
        float stateT, cooldownT, repathT;
        float phase, animT;
        float flashT;
        bool flashDirty;
        float strafeSign = 1f;
        Vector3 knockVel;
        Vector3 attackDir;
        bool attackHit;
        Vector3 squash = Vector3.one, squashVel;
        // Hit reaction: the body rocks away from each shot (flinch), and enough damage in a short window staggers it.
        Vector3 flinch, flinchVel;
        float staggerMeter, staggerImmuneT, staggerDur;
        Vector3 bodyBasePos, sacBaseScale;
        Quaternion[] legBase, armBase;
        NavMeshAgent agent;
        Collider col;
        Renderer[] rends;
        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public void Init(bool dormant = false)
        {
            Health = maxHealth;
            All.Add(this);
            mpb = new MaterialPropertyBlock();
            col = GetComponent<Collider>();
            AlienCorpse.IgnoreAll(col);
            rends = body ? body.GetComponentsInChildren<Renderer>() : GetComponentsInChildren<Renderer>();
            bodyBasePos = body ? body.localPosition : Vector3.zero;
            sacBaseScale = sac ? sac.localScale : Vector3.one;
            legBase = new Quaternion[legs != null ? legs.Length : 0];
            for (int i = 0; i < legBase.Length; i++) legBase[i] = legs[i].localRotation;
            armBase = new Quaternion[arms != null ? arms.Length : 0];
            for (int i = 0; i < armBase.Length; i++) armBase[i] = arms[i].localRotation;
            strafeSign = Random.value < 0.5f ? -1f : 1f;
            phase = Random.value * 10f;

            if (NavMesh.SamplePosition(transform.position, out var hit, 4f, NavMesh.AllAreas))
                transform.position = hit.position;
            agent = gameObject.AddComponent<NavMeshAgent>();
            agent.speed = moveSpeed;
            agent.acceleration = 40f;
            agent.angularSpeed = 0f;
            agent.updateRotation = false;
            agent.radius = agentRadius;
            agent.height = 2f;
            agent.stoppingDistance = 0.2f;
            agent.autoBraking = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
            agent.avoidancePriority = Random.Range(30, 70);
            SetStopped(true);

            stateT = 0f;
            cooldownT = Random.Range(0.2f, 0.8f);
            home = transform.position;
            if (dormant)
            {
                state = State.Idle;
                SetStopped(false);
                if (agent) agent.speed = moveSpeed * 0.3f;
                wanderT = Random.Range(0.5f, 4f);
            }
            else
            {
                state = State.Spawning;
                if (body) body.localScale = Vector3.one * 0.01f;
            }
        }

        /// <summary>Ties the alien to a camp: it guards the area around <paramref name="homePos"/>.</summary>
        public void SetHome(AlienCamp owner, Vector3 homePos, float radius)
        {
            camp = owner;
            home = homePos;
            hasHome = true;
            guardRadius = radius;
        }

        /// <summary>Notice the player. Called on sight, on damage, or by a camp-mate's alarm.</summary>
        public void Alert(bool fromCamp = false)
        {
            if (state != State.Idle) return;
            state = State.Chase;
            stateT = 0f;
            repathT = 0f;
            cooldownT = Mathf.Max(cooldownT, Random.Range(0.3f, 0.8f));
            if (agent) agent.speed = moveSpeed;
            SetStopped(false);
            squash = new Vector3(0.85f, 1.25f, 0.85f);
            if (Hud.I) Hud.I.WorldText(transform.position + Vector3.up * (1.8f + deathSize * 0.5f), "!", new Color(1f, 0.3f, 0.2f), 46);
            if (!fromCamp)
            {
                if (type == AlienType.Brute) ProcAudio.Play("roar", 0.55f, 0.06f, 0.85f);
                ProcAudio.Play("screech", 0.4f, 0.1f, type == AlienType.Brute ? 0.6f : 1.1f);
                if (camp) camp.Alarm(this);
            }
        }

        void GoHome()
        {
            state = State.Idle;
            stateT = 0f;
            if (agent) agent.speed = moveSpeed * 0.6f;
            SetStopped(false);
            SetDestination(home);
            wanderT = 4f;
        }

        void Idle(PlayerController player, bool playerAlive, float dist, float dt)
        {
            wanderT -= dt;
            if (wanderT <= 0f)
            {
                wanderT = Random.Range(2.5f, 6f);
                if (agent) agent.speed = moveSpeed * 0.3f;
                Vector2 r = Random.insideUnitCircle * guardRadius;
                Vector3 dest = home + new Vector3(r.x, 0f, r.y);
                if (NavMesh.SamplePosition(dest, out var hit, 2f, NavMesh.AllAreas)) SetDestination(hit.position);
            }
            FaceMovement(dt);
            if (playerAlive && (dist < 4f || (dist < aggroRange && HasLineOfSight(player)))) Alert();
        }

        void OnDestroy() { All.Remove(this); }

        bool AgentReady => agent && agent.enabled && agent.isOnNavMesh;

        void SetStopped(bool stopped)
        {
            if (!AgentReady) return;
            agent.isStopped = stopped;
            if (stopped) agent.velocity = Vector3.zero;
        }

        void SetDestination(Vector3 p)
        {
            if (AgentReady) agent.SetDestination(p);
        }

        void Update()
        {
            if (state == State.Dead) return;
            float dt = Time.deltaTime;
            stateT += dt;
            animT += dt;
            cooldownT -= dt;
            staggerImmuneT -= dt;
            staggerMeter = Mathf.Max(0f, staggerMeter - 0.3f * dt);

            var player = PlayerController.I;
            bool playerAlive = player && player.CanAct;
            Vector3 toPlayer = player ? player.transform.position - transform.position : Vector3.forward;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;

            switch (state)
            {
                case State.Spawning:
                    if (stateT >= 0.6f) { state = State.Chase; stateT = 0f; SetStopped(false); }
                    break;
                case State.Idle:
                    Idle(player, playerAlive, dist, dt);
                    break;
                case State.Chase:
                    Chase(player, playerAlive, toPlayer, dist, dt);
                    break;
                case State.Telegraph:
                    if (playerAlive && type != AlienType.Brute && dist > 0.1f) attackDir = toPlayer / dist;
                    Face(attackDir, 900f, dt);
                    if (stateT >= telegraphTime) BeginAttack(player, toPlayer, dist);
                    break;
                case State.Attack:
                    UpdateAttack(player, dt);
                    break;
                case State.Recover:
                    if (stateT >= recoverTime) { state = State.Chase; stateT = 0f; SetStopped(false); }
                    break;
                case State.Stagger:
                    if (stateT >= staggerDur) { state = State.Chase; stateT = 0f; repathT = 0f; SetStopped(false); }
                    break;
            }

            if (knockVel.sqrMagnitude > 0.01f)
            {
                if (AgentReady) agent.Move(knockVel * dt);
                knockVel = Vector3.Lerp(knockVel, Vector3.zero, 1f - Mathf.Exp(-9f * dt));
            }

            Animate(dt);
            UpdateFlash(dt);
        }

        void Chase(PlayerController player, bool playerAlive, Vector3 toPlayer, float dist, float dt)
        {
            repathT -= dt;
            if (!playerAlive)
            {
                if (hasHome) { GoHome(); return; }
                // Player is down: mill about.
                if (repathT <= 0f)
                {
                    repathT = Random.Range(1.5f, 3f);
                    Vector2 r = Random.insideUnitCircle * 6f;
                    SetDestination(transform.position + new Vector3(r.x, 0, r.y));
                }
                FaceMovement(dt);
                return;
            }

            // Leash: chased too far from home and the player got away.
            if (hasHome && dist > 16f && (transform.position - home).magnitude > leashRange) { GoHome(); return; }

            Vector3 dirToPlayer = dist > 0.01f ? toPlayer / dist : transform.forward;
            if (type == AlienType.Spitter)
            {
                bool los = HasLineOfSight(player);
                if (dist < attackRange && dist > 2.5f && los && cooldownT <= 0f) { StartTelegraph(dirToPlayer); return; }
                if (repathT <= 0f)
                {
                    repathT = 0.3f;
                    Vector3 dest;
                    if (!los || dist > preferredRange + 1.5f) dest = player.transform.position;
                    else if (dist < preferredRange - 2.5f) dest = transform.position - dirToPlayer * 4f;
                    else dest = transform.position + Vector3.Cross(Vector3.up, dirToPlayer) * (3f * strafeSign);
                    if (Random.value < 0.05f) strafeSign = -strafeSign;
                    SetDestination(dest);
                }
                Face(dirToPlayer, 540f, dt);
            }
            else
            {
                if (dist < attackRange && cooldownT <= 0f && HasLineOfSight(player)) { StartTelegraph(dirToPlayer); return; }
                if (repathT <= 0f)
                {
                    repathT = 0.15f;
                    SetDestination(player.transform.position);
                }
                if (dist < 4f) Face(dirToPlayer, 720f, dt); else FaceMovement(dt);
            }
        }

        bool HasLineOfSight(PlayerController player)
        {
            Vector3 a = transform.position + Vector3.up * 1.1f;
            Vector3 b = player.transform.position + Vector3.up * 1.1f;
            if (!Physics.Linecast(a, b, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.GetComponentInParent<PlayerController>() != null || hit.collider.GetComponentInParent<Alien>() != null;
        }

        void Face(Vector3 dir, float degPerSec, float dt)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), degPerSec * dt);
        }

        void FaceMovement(float dt)
        {
            if (AgentReady && agent.velocity.sqrMagnitude > 0.2f) Face(agent.velocity, 540f, dt);
        }

        void StartTelegraph(Vector3 dir)
        {
            state = State.Telegraph;
            stateT = 0f;
            attackDir = dir;
            SetStopped(true);
            switch (type)
            {
                case AlienType.Skitter:
                    ProcAudio.Play("screech", 0.3f, 0.15f, 1.35f);
                    break;
                case AlienType.Spitter:
                    ProcAudio.Play("screech", 0.3f, 0.1f, 0.9f);
                    break;
                case AlienType.Brute:
                    ProcAudio.Play("screech", 0.5f, 0.05f, 0.55f);
                    Vfx.Telegraph(transform.position, slamRadius, telegraphTime, new Color(1f, 0.25f, 0.1f));
                    break;
            }
        }

        void BeginAttack(PlayerController player, Vector3 toPlayer, float dist)
        {
            state = State.Attack;
            stateT = 0f;
            attackHit = false;
            cooldownT = attackCooldown;
            switch (type)
            {
                case AlienType.Skitter:
                    squash = new Vector3(0.8f, 1.1f, 1.4f);
                    break;
                case AlienType.Spitter:
                    {
                        Vector3 from = mouth ? mouth.position : transform.position + Vector3.up * 1.5f;
                        Vector3 target = player ? player.transform.position + Vector3.up * 1f : from + transform.forward * 5f;
                        if (player) target += player.Velocity * Mathf.Clamp(dist / projectileSpeed, 0f, 1f) * 0.6f;
                        Vector3 dir = target - from;
                        dir.y = 0f;
                        AlienProjectile.Spawn(from, dir.normalized, projectileSpeed, attackDamage);
                        ProcAudio.Play("spit", 0.5f, 0.1f);
                        if (sac) sac.localScale = sacBaseScale * 0.6f;
                        squash = new Vector3(1.2f, 0.85f, 1.2f);
                        break;
                    }
                case AlienType.Brute:
                    {
                        Vector3 p = transform.position;
                        Vfx.Shockwave(p, slamRadius * 1.15f, new Color(1f, 0.45f, 0.2f) * 2.5f, 0.35f);
                        Vfx.Dust(p, slamRadius);
                        ProcAudio.Play("slam", 0.8f, 0.05f);
                        squash = new Vector3(1.25f, 0.75f, 1.25f);
                        if (player)
                        {
                            float pd = Vector3.Distance(player.transform.position, p);
                            GameFeel.Shake(Mathf.Lerp(0.5f, 0.1f, pd / 15f));
                            if (pd <= slamRadius + 0.4f)
                            {
                                Vector3 away = player.transform.position - p; away.y = 0f;
                                player.health.TakeDamage(new DamageInfo
                                {
                                    amount = attackDamage, point = player.Chest, direction = away.normalized, knockback = 14f,
                                });
                            }
                        }
                        break;
                    }
            }
        }

        void UpdateAttack(PlayerController player, float dt)
        {
            if (type == AlienType.Skitter)
            {
                if (stateT < lungeTime)
                {
                    if (AgentReady) agent.Move(attackDir * lungeSpeed * dt);
                    if (!attackHit && player && player.CanAct)
                    {
                        Vector3 d = player.transform.position - transform.position; d.y = 0f;
                        if (d.magnitude < 1.25f)
                        {
                            attackHit = true;
                            player.health.TakeDamage(new DamageInfo
                            {
                                amount = attackDamage, point = player.Chest, direction = attackDir, knockback = 7f,
                            });
                        }
                    }
                    return;
                }
            }
            else if (stateT < 0.12f) return;
            state = State.Recover;
            stateT = 0f;
        }

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return;
            LastHitTime = Time.time;
            if (state == State.Idle) Alert();
            Health -= info.amount;
            flashT = 0.09f;
            squash = info.quiet ? Vector3.Lerp(squash, new Vector3(1.08f, 0.93f, 1.08f), 0.5f) : new Vector3(1.22f, 0.82f, 1.22f);

            Vector3 kdir = info.direction; kdir.y = 0f;
            if (kdir.sqrMagnitude > 0.0001f) kdir.Normalize();
            knockVel += kdir * info.knockback * knockbackResist;

            // Flinch: the body rocks away from the shot; heavy types barely move.
            Vector3 local = transform.InverseTransformDirection(kdir);
            float rock = (info.quiet ? 2.5f : info.crit ? 15f : 9f) * Mathf.Lerp(0.45f, 1f, knockbackResist);
            flinchVel += new Vector3(local.z, 0f, -local.x) * (rock * 22f);

            if (info.quiet)
            {
                if (Random.value < 0.35f) Vfx.Goo(info.point, -kdir + Vector3.up * 0.3f, gooColor, 2);
                if (Random.value < 0.08f) BloodStain(kdir, 0.4f);
            }
            else
            {
                Vfx.Goo(info.point, -kdir + Vector3.up * 0.3f, gooColor, info.crit ? 14 : 7);
                Vfx.ChitinChips(info.point, -kdir + Vector3.up * 0.6f, shellMaterial ? shellMaterial : skinMaterial, deathSize,
                    info.crit ? 4 : Random.value < 0.5f ? 2 : 1);
                if (info.crit || Random.value < 0.45f) BloodStain(kdir, info.crit ? 0.9f : 0.55f);
                float pitch = type == AlienType.Brute ? 0.7f : type == AlienType.Spitter ? 1f : 1.25f;
                ProcAudio.Play("hit_alien", 0.32f, 0.12f, pitch);
                if (info.crit) ProcAudio.Play("chitin", 0.3f, 0.15f, pitch);
                if (Hud.I) Hud.I.DamageNumber(info.point + Vector3.up * 0.4f, info.amount, info.crit);
            }

            // Enough damage in a short window breaks its stride.
            staggerMeter += info.amount / Mathf.Max(1f, maxHealth) * (info.crit ? 1.5f : 1f);
            if (Health > 0f && staggerImmuneT <= 0f && staggerMeter >= StaggerThreshold) Stagger(kdir);

            // Explosions interrupt wind-ups (except the brute's, which is a commitment).
            if (info.explosive && type != AlienType.Brute && (state == State.Telegraph || state == State.Chase))
            {
                state = State.Recover;
                stateT = 0f;
                SetStopped(true);
            }

            if (Health <= 0f) Die(info);
        }

        /// <summary>Share of max health, taken within a second or so, that staggers this type.</summary>
        float StaggerThreshold => type == AlienType.Brute ? 0.16f : type == AlienType.Spitter ? 0.35f : 0.4f;

        /// <summary>
        /// Breaks the alien's stride: it stops, buckles and sheds shell for a moment. Committed attacks (a skitter
        /// already lunging, the brute's slam) play out, and a short immunity window prevents stun-locking.
        /// </summary>
        void Stagger(Vector3 kdir)
        {
            bool brute = type == AlienType.Brute;
            staggerMeter = 0f;
            staggerImmuneT = brute ? 3f : 1.4f;

            Vector3 local = transform.InverseTransformDirection(kdir);
            flinchVel += new Vector3(local.z, 0f, -local.x) * (brute ? 220f : 420f);
            squash = new Vector3(1.3f, 0.72f, 1.3f);
            Vector3 chest = transform.position + Vector3.up * (0.7f * deathSize);
            Vfx.ChitinChips(chest, -kdir + Vector3.up, shellMaterial ? shellMaterial : skinMaterial, deathSize * 1.3f, brute ? 6 : 4);
            Vfx.Goo(chest, -kdir + Vector3.up * 0.5f, gooColor, 10);
            BloodStain(kdir, 0.9f);
            float pitch = brute ? 0.6f : type == AlienType.Spitter ? 0.9f : 1.15f;
            ProcAudio.Play("alien_pain", brute ? 0.5f : 0.4f, 0.12f, pitch);
            ProcAudio.Play("chitin", 0.4f, 0.15f, brute ? 0.75f : 1.1f);

            if (state == State.Attack || state == State.Spawning || (brute && state == State.Telegraph)) return;
            state = State.Stagger;
            stateT = 0f;
            staggerDur = brute ? 0.28f : 0.45f;
            cooldownT = Mathf.Max(cooldownT, 0.25f);
            SetStopped(true);
        }

        /// <summary>Acid blood lands on the ground behind the alien, along the line of the shot.</summary>
        void BloodStain(Vector3 kdir, float size)
        {
            float reach = Mathf.Max(0.8f, deathSize);
            Vector3 p = transform.position + kdir * (Random.Range(0.5f, 1.7f) * reach) + Random.insideUnitSphere * 0.35f;
            Color c = gooColor * 0.6f; c.a = 0.85f;
            Vfx.Splat(p, size * Random.Range(0.7f, 1.3f) * Mathf.Lerp(0.85f, 1.4f, Mathf.InverseLerp(0.9f, 1.8f, deathSize)), c, 24f);
        }

        void Die(DamageInfo info)
        {
            bool brute = type == AlienType.Brute;
            Vector3 moveVel = AgentReady ? agent.velocity : Vector3.zero;
            state = State.Dead;
            All.Remove(this);
            if (agent) agent.enabled = false;
            if (col) col.enabled = false;

            // Gunfire leaves a carcass (a ragdoll of the same model); explosions blow the alien to pieces.
            if (sac) sac.localScale = sacBaseScale;
            bool carcass = !info.explosive && AlienCorpse.TryCreate(this, info, knockVel + moveVel);

            Vector3 center = transform.position + Vector3.up * (0.6f * deathSize);
            Vfx.AlienDeath(center, deathSize, gooColor, glowColor, skinMaterial, glowMaterial, info.direction, info.explosive, shellMaterial, carcass);
            ProcAudio.Play("alien_death", brute ? 0.8f : 0.5f, 0.1f, brute ? 0.6f : type == AlienType.Spitter ? 0.9f : 1.2f);
            ProcAudio.Play("gib", brute ? 0.7f : 0.45f, 0.12f, brute ? 0.7f : 1f);
            GameFeel.HitStop(brute ? 0.13f : 0.045f, 0.05f);
            GameFeel.Shake(brute ? 0.5f : 0.16f);

            if (GameManager.I) GameManager.I.OnAlienKilled(this);
            float drop = brute ? 0.8f : healthDropChance;
            if (Random.value < drop) HealthOrb.Spawn(transform.position + Vector3.up * 0.8f);
            LootTable.AlienDrop(type, transform.position);
            if (carcass)
            {
                // The GameObject lives on as the corpse; only the living parts go.
                if (agent) Destroy(agent);
                if (col) Destroy(col);
                Destroy(this);
            }
            else Destroy(gameObject);
        }

        void Animate(float dt)
        {
            if (!body) return;
            float speed = AgentReady ? agent.velocity.magnitude : 0f;
            if (state == State.Attack && type == AlienType.Skitter) speed = lungeSpeed;
            float moveAmt = Mathf.Clamp01(speed / Mathf.Max(0.1f, moveSpeed));
            phase += dt * stepFrequency * (0.25f + moveAmt);

            float tele = state == State.Telegraph ? Mathf.Clamp01(stateT / telegraphTime) : 0f;

            for (int i = 0; i < legBase.Length; i++)
            {
                float offset = (i % 2 == 0) ? 0f : Mathf.PI;
                float swing = Mathf.Sin(phase + offset) * legSwing * moveAmt;
                Quaternion q = type == AlienType.Skitter ? Quaternion.Euler(0f, swing, Mathf.Abs(Mathf.Cos(phase + offset)) * 12f * moveAmt)
                                                         : Quaternion.Euler(swing, 0f, 0f);
                legs[i].localRotation = legBase[i] * q;
            }

            for (int i = 0; i < armBase.Length; i++)
            {
                float raise = 0f;
                if (state == State.Telegraph) raise = -110f * Ease.OutCubic(tele);
                else if (state == State.Attack) raise = Mathf.Lerp(-110f, 20f, Mathf.Clamp01(stateT / 0.08f));
                else if (state == State.Recover) raise = Mathf.Lerp(20f, 0f, Mathf.Clamp01(stateT / recoverTime));
                else raise = Mathf.Sin(phase + i * Mathf.PI) * 18f * moveAmt;
                arms[i].localRotation = armBase[i] * Quaternion.Euler(raise, 0f, 0f);
            }

            if (sac)
            {
                float swell = state == State.Telegraph ? Mathf.Lerp(1f, 1.6f, tele) + Mathf.Sin(animT * 40f) * 0.05f * tele : 1f + Mathf.Sin(animT * 3f) * 0.05f;
                sac.localScale = Vector3.Lerp(sac.localScale, sacBaseScale * swell, 1f - Mathf.Exp(-14f * dt));
            }

            float bob = Mathf.Abs(Mathf.Sin(phase)) * bobAmount * moveAmt + Mathf.Sin(animT * 2.5f) * 0.02f;
            Vector3 pose = Vector3.zero;
            Vector3 poseScale = Vector3.one;
            if (state == State.Telegraph)
            {
                if (type == AlienType.Skitter) { pose = new Vector3(0, -0.15f, -0.15f) * tele; poseScale = Vector3.Lerp(Vector3.one, new Vector3(1.15f, 0.8f, 0.95f), tele); }
                else if (type == AlienType.Brute) { pose = new Vector3(0, 0.25f, -0.1f) * tele; poseScale = Vector3.Lerp(Vector3.one, new Vector3(0.95f, 1.12f, 0.95f), tele); }
                else poseScale = Vector3.Lerp(Vector3.one, new Vector3(0.92f, 1.08f, 0.92f), tele);
                pose += new Vector3(Mathf.Sin(animT * 70f), 0, Mathf.Cos(animT * 63f)) * 0.025f * tele; // shiver
            }
            else if (state == State.Stagger)
            {
                // buckles towards the ground and shudders, then picks itself back up
                float k = 1f - Mathf.Clamp01(stateT / Mathf.Max(0.01f, staggerDur));
                pose = new Vector3(Mathf.Sin(animT * 58f) * 0.035f, -0.14f * deathSize, 0f) * k;
                poseScale = Vector3.Lerp(Vector3.one, new Vector3(1.08f, 0.88f, 1.08f), k);
            }

            float spawn = state == State.Spawning ? Ease.OutBack(stateT / 0.6f) : 1f;
            Vector3 rise = state == State.Spawning ? Vector3.down * (1f - Ease.OutCubic(stateT / 0.6f)) * 0.8f : Vector3.zero;

            Spring.Step(ref squash, ref squashVel, Vector3.one, 300f, 14f, dt);
            body.localPosition = bodyBasePos + Vector3.up * bob + pose + rise;
            body.localScale = Vector3.Scale(Vector3.Scale(squash, poseScale), Vector3.one * Mathf.Max(0.01f, spawn));

            // Lean into movement, and rock away from hits.
            Spring.Step(ref flinch, ref flinchVel, Vector3.zero, 220f, 16f, dt);
            flinch = Vector3.ClampMagnitude(flinch, 30f);
            Vector3 localVel = AgentReady ? transform.InverseTransformDirection(agent.velocity) : Vector3.zero;
            body.localRotation = Quaternion.Euler(Mathf.Clamp(localVel.z * 2f, -12f, 12f) + flinch.x, 0f, Mathf.Clamp(-localVel.x * 2f, -12f, 12f) + flinch.z);
        }

        void UpdateFlash(float dt)
        {
            Color emission = Color.black;
            bool active = false;
            if (flashT > 0f)
            {
                flashT -= dt;
                emission = Color.white * (6f * Mathf.Clamp01(flashT / 0.09f) + 0.5f);
                active = true;
            }
            else if (state == State.Telegraph)
            {
                float tele = Mathf.Clamp01(stateT / telegraphTime);
                float pulse = 0.5f + 0.5f * Mathf.Sin(stateT * Mathf.Lerp(18f, 50f, tele));
                emission = new Color(1f, 0.12f, 0.05f) * (0.6f + 2.5f * tele * pulse);
                active = true;
            }

            if (active)
            {
                mpb.SetColor(EmissionId, emission);
                foreach (var r in rends) if (r) r.SetPropertyBlock(mpb);
                flashDirty = true;
            }
            else if (flashDirty)
            {
                foreach (var r in rends) if (r) r.SetPropertyBlock(null);
                flashDirty = false;
            }
        }
    }
}
