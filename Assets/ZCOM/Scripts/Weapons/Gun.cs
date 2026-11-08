using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Holds and fires the equipped energy weapon (stats from <see cref="WeaponDefs"/>).
    /// Bolt weapons: a ray finds the target, a visible plasma bolt flies there, damage lands on arrival.
    /// Beam weapons: continuous laser (core + glow + twin helix strands) that ticks damage and drains the cell.
    /// Feel: spring recoil, spread bloom, energy flashes, spinning emitter rings, glowing cores, vents,
    /// cell ejection on recharge, camera kick/shake, hit-stop.
    /// </summary>
    public class Gun : MonoBehaviour
    {
        [Tooltip("Empty transform the weapon model is built under.")]
        public Transform model;
        public WeaponKind startingWeapon = WeaponKind.IonCarbine;

        const float SwapTime = 0.35f;

        PlayerController owner;
        WeaponDef def;
        WeaponParts parts;
        readonly Dictionary<WeaponKind, int> energy = new Dictionary<WeaponKind, int>();

        float nextFireTime;
        float lastShotTime = -10f;
        float bloom, heat;
        bool reloading;
        float reloadStart;
        bool cellDropped, cellInserted;
        float autoReloadAt = -1f;
        float nextEmptyClick;
        float swapT;

        Vector3 modelBasePos;
        Quaternion modelBaseRot;
        float kickZ, kickZVel, kickPitch, kickPitchVel, kickRoll, kickRollVel;
        float spinAngle;
        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        struct PendingHit
        {
            public IDamageable target;
            public DamageInfo info;
            public float time;
            public bool environment;
            public Vector3 normal;
        }
        readonly List<PendingHit> pending = new List<PendingHit>();

        // Beam state
        LineRenderer beamCore, beamGlow, helixA, helixB;
        Light beamLight;
        AudioSource beamAudio;
        bool beaming;
        float beamTick;
        float beamNumbersT;
        readonly Dictionary<IDamageable, (float amount, Vector3 point)> beamAccum = new Dictionary<IDamageable, (float, Vector3)>();
        const int HelixPoints = 120;
        readonly Vector3[] helixBuf = new Vector3[HelixPoints];

        public WeaponDef Current => def;
        public int Ammo => def != null && energy.TryGetValue(def.kind, out var a) ? a : 0;
        public int MagazineSize => def != null ? def.cellSize : 0;
        public bool IsReloading => reloading;
        public bool IsSwapping => swapT > 0f;
        public bool IsBeaming => beaming;
        public float ReloadProgress => reloading ? Mathf.Clamp01((Time.time - reloadStart) / def.rechargeTime) : 0f;
        public float CurrentSpread => def == null ? 0f : Mathf.Min(Mathf.Max(def.maxSpread, def.baseSpread), def.baseSpread + bloom + (owner && owner.IsMoving ? def.moveSpread : 0f));

        void Awake()
        {
            owner = GetComponentInParent<PlayerController>();
            mpb = new MaterialPropertyBlock();
            if (!model)
            {
                model = new GameObject("GunModel").transform;
                model.SetParent(transform, false);
            }
            modelBasePos = model.localPosition;
            modelBaseRot = model.localRotation;
            BuildBeam();
        }

        void Start()
        {
            if (def == null && !(owner && owner.inventory)) Equip(startingWeapon, false);
        }

        // ------------------------------------------------------------------ equip

        /// <summary>Swap to a weapon: rebuilds the model, keeps each weapon's cell charge.</summary>
        public void Equip(WeaponKind kind, bool animate = true)
        {
            if (def != null && def.kind == kind) return;
            StopBeam();
            def = WeaponDefs.Get(kind);
            if (!energy.ContainsKey(kind)) energy[kind] = def.cellSize;
            ClearModel();
            parts = WeaponModels.Build(kind, model);

            reloading = false;
            autoReloadAt = -1f;
            bloom = heat = 0f;
            lastShotTime = -10f;
            nextFireTime = Time.time + (animate ? SwapTime * 0.8f : 0f);
            if (animate)
            {
                swapT = SwapTime;
                ProcAudio.Play("equip", 0.5f, 0.05f);
            }
            if (Ammo == 0) autoReloadAt = Time.time + SwapTime;
            ApplyBeamColors();
        }

        /// <summary>No weapon in the loadout: hands empty.</summary>
        public void Unequip()
        {
            StopBeam();
            def = null;
            reloading = false;
            ClearModel();
            parts = default;
        }

        void ClearModel()
        {
            for (int i = model.childCount - 1; i >= 0; i--)
            {
                var c = model.GetChild(i).gameObject;
                c.SetActive(false);
                Destroy(c);
            }
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            ProcessPending();
            if (def == null) return;
            float dt = Time.deltaTime;
            var input = PlayerInputSource.Current;
            bloom = Mathf.MoveTowards(bloom, 0f, def.spreadRecovery * dt);
            heat = Mathf.MoveTowards(heat, 0f, dt * 0.8f);
            swapT = Mathf.Max(0f, swapT - dt);
            bool uiOpen = owner && owner.inventory && owner.inventory.PanelOpen;
            bool canAct = owner && owner.CanAct && swapT <= 0f && !uiOpen;

            if (reloading) UpdateReload();
            else if (canAct)
            {
                if (autoReloadAt > 0f && Time.time >= autoReloadAt) { autoReloadAt = -1f; StartReload(); }
                else if (input.reload && Ammo < def.cellSize) StartReload();
            }

            if (def.beam) UpdateBeam(canAct && !reloading && input.fire && Ammo > 0, input.fire && canAct && !reloading, dt);
            else if (canAct && !reloading && input.fire && Time.time >= nextFireTime)
            {
                if (Ammo > 0)
                {
                    float interval = 1f / def.fireRate;
                    if (Time.time - nextFireTime > interval) nextFireTime = Time.time;
                    nextFireTime += interval;
                    FireBolts();
                }
                else EmptyClick();
            }

            AnimateModel(dt);
            UpdateGlows(dt);
        }

        void EmptyClick()
        {
            if (Time.time < nextEmptyClick) return;
            nextEmptyClick = Time.time + 0.25f;
            ProcAudio.Play("empty", 0.5f);
            StartReload();
        }

        // ------------------------------------------------------------------ bolts

        void FireBolts()
        {
            energy[def.kind] = Ammo - 1;
            lastShotTime = Time.time;
            heat = Mathf.Min(1f, heat + 0.12f);
            Vector3 aim = owner.AimDir;
            float spread = CurrentSpread;
            bloom = Mathf.Min(bloom + def.spreadPerShot, def.maxSpread);

            // Rays start at the body centre (never inside cover) at muzzle height.
            Vector3 origin = owner.transform.position;
            origin.y = parts.muzzle.position.y;
            Vector3 muzzlePos = parts.muzzle.position;

            var hits = new Dictionary<IDamageable, (float amount, Vector3 point, float dist, int count)>();
            for (int p = 0; p < def.pellets; p++)
            {
                float yaw = def.pellets > 1 ? Random.Range(-spread, spread) : (Random.value - Random.value) * spread;
                Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * aim;
                Vector3 end = origin + dir * def.range;

                var rayHits = Physics.RaycastAll(origin, dir, def.range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                System.Array.Sort(rayHits, (a, b) => a.distance.CompareTo(b.distance));
                int pierceLeft = def.pierce;
                foreach (var h in rayHits)
                {
                    var target = h.collider.GetComponentInParent<IDamageable>();
                    if (target is PlayerHealth) continue;
                    if (target != null)
                    {
                        if (!target.IsAlive) continue;
                        hits.TryGetValue(target, out var acc);
                        if (acc.count == 0) { acc.point = h.point; acc.dist = h.distance; }
                        acc.amount += def.damage;
                        acc.count++;
                        hits[target] = acc;
                        end = h.point;
                        if (pierceLeft-- <= 0) break;
                        continue;
                    }
                    end = h.point;
                    pending.Add(new PendingHit { environment = true, time = Time.time + h.distance / def.boltSpeed, normal = h.normal,
                        info = new DamageInfo { point = h.point, direction = dir } });
                    break;
                }
                Vfx.Bolt(muzzlePos, end, def.color, def.boltWidth, def.boltLength, def.boltSpeed);
            }

            foreach (var kv in hits)
            {
                bool crit = Random.value < def.critChance;
                var acc = kv.Value;
                pending.Add(new PendingHit
                {
                    target = kv.Key,
                    time = Time.time + acc.dist / def.boltSpeed,
                    info = new DamageInfo
                    {
                        amount = acc.amount * (crit ? def.critMultiplier : 1f),
                        point = acc.point,
                        direction = aim,
                        knockback = def.knockback * Mathf.Sqrt(acc.count),
                        crit = crit,
                    },
                });
            }

            Vfx.EnergyMuzzle(muzzlePos, aim, def.color, def.flashScale);
            if (parts.vent && def.pellets > 1) Vfx.Vent(parts.vent.position, Vector3.up + owner.transform.right * 0.5f, def.color);
            ProcAudio.Play(def.sound, def.soundVolume, 0.07f);
            spinBoost = 1f;

            bool rigged = owner.animator;
            float gunShare = rigged ? 0.25f : 1f;              // on the rigged marine the arms carry the recoil
            kickZ = Mathf.Max(kickZ - def.kickBack * gunShare, -0.35f);
            kickPitch = Mathf.Max(kickPitch - def.kickPitch * gunShare, -35f);
            kickRollVel += Random.Range(-160f, 160f) * (def.kickPitch / 10f) * gunShare;
            if (rigged) owner.RigKick(def.kickBack, def.kickPitch);
            owner.Recoil(-aim, def.playerRecoil);
            ProcAudio.Play("thump", Mathf.Clamp(def.soundVolume * (def.pellets > 1 ? 0.9f : 0.55f), 0.2f, 0.8f), 0.06f, def.pellets > 1 ? 0.8f : 1.05f);
            // heat flakes / vapour off the gun, glowing for a moment
            Vector3 side = Vector3.Cross(Vector3.up, aim).normalized;
            for (int i = 0; i < (def.pellets > 1 ? 4 : 1); i++)
                Vfx.Dots(muzzlePos - aim * 0.25f + Vector3.up * 0.05f, (side * Random.Range(1.5f, 3f) + Vector3.up * Random.Range(1f, 2.5f)) * (Random.value < 0.5f ? 1f : -1f) * 0.6f + Vector3.up,
                    Color.Lerp(def.color, Color.white, 0.5f), Random.Range(0.03f, 0.06f), Random.Range(0.25f, 0.45f));
            Vfx.Dots(muzzlePos + aim * 0.1f, aim * 1.2f + Vector3.up * 0.6f, new Color(0.7f, 0.75f, 0.8f, 0.35f), Random.Range(0.18f, 0.28f), 0.5f);
            GameFeel.Shake(def.shake);
            GameFeel.Kick(-aim, def.cameraKick);
            if (def.hitStopPerShot > 0f) GameFeel.HitStop(def.hitStopPerShot, 0.15f);

            if (Ammo == 0) autoReloadAt = Time.time + 0.2f;
        }

        /// <summary>Applies bolt impacts when the bolt visually arrives.</summary>
        void ProcessPending()
        {
            bool hitAny = false, killed = false, crit = false;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                if (Time.time < p.time) continue;
                pending.RemoveAt(i);
                Color c = def != null ? def.color : Color.white;
                if (p.environment)
                {
                    Vfx.EnergyImpact(p.info.point, p.normal, c, 1f);
                    Vfx.Glow(p.info.point + p.normal * 0.03f, Mats.HDR(Color.Lerp(c, new Color(1f, 0.55f, 0.2f), 0.5f), 2.2f), 0.35f, 0.7f);   // hot ember
                    continue;
                }
                if (p.target == null || (p.target is Object o && o == null) || !p.target.IsAlive) continue;
                p.target.TakeDamage(p.info);
                Vfx.EnergyImpact(p.info.point, -p.info.direction, c, 0.7f);
                hitAny = true;
                crit |= p.info.crit;
                if (!p.target.IsAlive) killed = true;
            }
            if (hitAny && Hud.I) Hud.I.Hitmarker(killed, crit);
        }

        // ------------------------------------------------------------------ beam

        void BuildBeam()
        {
            beamGlow = MakeBeamLine("BeamGlow", 0.7f);
            beamCore = MakeBeamLine("BeamCore", 0.16f);
            helixA = MakeBeamLine("HelixA", 0.045f);
            helixB = MakeBeamLine("HelixB", 0.045f);
            helixA.positionCount = helixB.positionCount = HelixPoints;

            var lg = new GameObject("BeamLight");
            lg.transform.SetParent(transform, false);
            beamLight = lg.AddComponent<Light>();
            beamLight.type = LightType.Point;
            beamLight.range = 5f;
            beamLight.shadows = LightShadows.None;
            beamLight.enabled = false;

            beamAudio = gameObject.AddComponent<AudioSource>();
            beamAudio.playOnAwake = false;
            beamAudio.loop = true;
            beamAudio.spatialBlend = 0f;
            beamAudio.volume = 0.4f;
            beamAudio.clip = ProcAudio.GetClip("beam_loop");
            SetBeamVisible(false);
        }

        LineRenderer MakeBeamLine(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Mats.Cached("BeamMat", () => Mats.Additive(ProcTex.BeamStrip, Mats.HDR(Color.white, 4f)));
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = width;
            lr.numCapVertices = 4;
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        void ApplyBeamColors()
        {
            if (def == null || !beamCore) return;
            Color c = def.color;
            beamGlow.startColor = beamGlow.endColor = new Color(c.r, c.g, c.b, 0.9f);
            Color core = Color.Lerp(c, Color.white, 0.6f);
            beamCore.startColor = beamCore.endColor = core;
            Color strand = Color.Lerp(c, Color.white, 0.15f);
            helixA.startColor = helixA.endColor = strand;
            helixB.startColor = helixB.endColor = strand;
            beamLight.color = c;
        }

        void SetBeamVisible(bool v)
        {
            beamCore.enabled = beamGlow.enabled = helixA.enabled = helixB.enabled = v;
            beamLight.enabled = v;
        }

        void StopBeam()
        {
            if (!beaming) return;
            beaming = false;
            SetBeamVisible(false);
            if (beamAudio) beamAudio.Stop();
            ProcAudio.Play("beam_stop", 0.45f, 0.05f);
            FlushBeamNumbers();
        }

        void UpdateBeam(bool firing, bool triedToFire, float dt)
        {
            if (!firing)
            {
                StopBeam();
                if (triedToFire && Ammo <= 0) EmptyClick();
                return;
            }
            if (!beaming)
            {
                beaming = true;
                beamTick = 0f;
                SetBeamVisible(true);
                beamAudio.Play();
                ProcAudio.Play(def.sound, def.soundVolume, 0.05f);
            }
            lastShotTime = Time.time;
            heat = Mathf.Min(1f, heat + dt * 0.35f);
            spinBoost = 1f;

            Vector3 aim = owner.AimDir;
            Vector3 origin = owner.transform.position;
            origin.y = parts.muzzle.position.y;
            Vector3 muzzle = parts.muzzle.position;

            // Who does the beam touch this frame?
            var touched = new List<(IDamageable target, Vector3 point)>();
            Vector3 end = origin + aim * def.range;
            Vector3 endNormal = -aim;
            bool hitWall = false;
            var rayHits = Physics.RaycastAll(origin, aim, def.range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(rayHits, (a, b) => a.distance.CompareTo(b.distance));
            int pierceLeft = def.pierce;
            foreach (var h in rayHits)
            {
                var target = h.collider.GetComponentInParent<IDamageable>();
                if (target is PlayerHealth) continue;
                if (target != null)
                {
                    if (!target.IsAlive || touched.Exists(x => x.target == target)) continue;
                    touched.Add((target, h.point));
                    end = h.point;
                    if (pierceLeft-- <= 0) break;
                    continue;
                }
                end = h.point;
                endNormal = h.normal;
                hitWall = true;
                break;
            }
            if (!hitWall && touched.Count <= def.pierce) end = origin + aim * def.range;

            beamTick -= dt;
            if (beamTick <= 0f)
            {
                beamTick += def.tickInterval;
                energy[def.kind] = Mathf.Max(0, Ammo - def.energyPerTick);
                bool killed = false;
                foreach (var (target, point) in touched)
                {
                    target.TakeDamage(new DamageInfo { amount = def.damage, point = point, direction = aim, knockback = def.knockback, quiet = true });
                    beamAccum.TryGetValue(target, out var acc);
                    beamAccum[target] = (acc.amount + def.damage, point);
                    if (!target.IsAlive) killed = true;
                }
                if (touched.Count > 0 && Hud.I) Hud.I.Hitmarker(killed, false);
                GameFeel.Shake(def.shake);
                GameFeel.Kick(-aim, def.cameraKick);
                owner.Recoil(-aim, def.playerRecoil);
                kickRollVel += Random.Range(-60f, 60f);
                kickZVel -= 1.5f;
                if (Ammo <= 0) { StopBeam(); autoReloadAt = Time.time + 0.25f; return; }
            }

            beamNumbersT -= dt;
            if (beamNumbersT <= 0f) { beamNumbersT = 0.3f; FlushBeamNumbers(); }

            DrawBeam(muzzle, end, dt);
            if (hitWall) Vfx.BeamBurn(end, endNormal, def.color);
            foreach (var (_, point) in touched) Vfx.BeamBurn(point, -aim, def.color);
            Vfx.Glow(muzzle, Mats.HDR(def.color, 2f), Random.Range(0.6f, 0.9f), 0.05f);
        }

        void FlushBeamNumbers()
        {
            if (Hud.I)
                foreach (var kv in beamAccum)
                    if (kv.Value.amount > 0f) Hud.I.DamageNumber(kv.Value.point + Vector3.up * 0.4f, kv.Value.amount, false);
            beamAccum.Clear();
        }

        void DrawBeam(Vector3 a, Vector3 b, float dt)
        {
            float flicker = Random.Range(0.85f, 1.15f);
            beamCore.SetPosition(0, a); beamCore.SetPosition(1, b);
            beamGlow.SetPosition(0, a); beamGlow.SetPosition(1, b);
            beamCore.widthMultiplier = 0.16f * flicker;
            beamGlow.widthMultiplier = 0.7f * Random.Range(0.9f, 1.1f);

            Vector3 dir = b - a;
            float len = dir.magnitude;
            Vector3 fwd = len > 0.001f ? dir / len : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3 up = Vector3.Cross(fwd, side);
            float tm = Time.time * 14f;
            for (int k = 0; k < 2; k++)
            {
                float off = k * Mathf.PI;
                for (int i = 0; i < HelixPoints; i++)
                {
                    float s = i / (HelixPoints - 1f);
                    float ph = s * len * 2.2f - tm + off;
                    float r = 0.13f * Mathf.Min(1f, s * len * 2f); // strands start tight at the muzzle
                    helixBuf[i] = a + dir * s + (side * Mathf.Cos(ph) + up * Mathf.Sin(ph)) * r;
                }
                (k == 0 ? helixA : helixB).SetPositions(helixBuf);
            }
            beamLight.transform.position = b - fwd * 0.4f;
            beamLight.intensity = 4f * flicker;
        }

        // ------------------------------------------------------------------ recharge

        void StartReload()
        {
            if (reloading || def == null || Ammo >= def.cellSize) return;
            StopBeam();
            reloading = true;
            reloadStart = Time.time;
            cellDropped = cellInserted = false;
            autoReloadAt = -1f;
            ProcAudio.Play("cell_out", 0.6f);
        }

        void UpdateReload()
        {
            float t = ReloadProgress;
            var cell = parts.cell;
            if (!cellDropped && t > 0.2f)
            {
                cellDropped = true;
                if (cell)
                {
                    var mf = cell.GetComponent<MeshFilter>();
                    var mr = cell.GetComponent<MeshRenderer>();
                    if (mf && mr)
                        Vfx.Debris(mf.sharedMesh, mr.sharedMaterial, cell.position, cell.rotation, cell.lossyScale,
                            Vector3.down * 1.5f + owner.Velocity * 0.5f + owner.transform.right * 0.8f, Random.insideUnitSphere * 6f, 2.5f);
                    cell.gameObject.SetActive(false);
                    if (parts.vent) Vfx.Vent(parts.vent.position, Vector3.up, def.color);
                }
                ProcAudio.Play("recharge", 0.45f, 0f, 1.6f / Mathf.Max(0.5f, def.rechargeTime));
            }
            if (!cellInserted && t > 0.7f)
            {
                cellInserted = true;
                if (cell) cell.gameObject.SetActive(true);
                ProcAudio.Play("reload_in", 0.6f, 0.05f, 1.2f);
                kickZ -= 0.05f;
            }
            if (t >= 1f)
            {
                reloading = false;
                energy[def.kind] = def.cellSize;
                heat = 0f;
                ProcAudio.Play("ready", 0.4f, 0f, 1.3f);
                Vfx.Glow(parts.muzzle.position, Mats.HDR(def.color, 2.5f), 1.2f, 0.15f);
                kickPitchVel -= 250f;
            }
        }

        // ------------------------------------------------------------------ animation

        float spinBoost;

        void AnimateModel(float dt)
        {
            if (!model) return;
            Spring.Step(ref kickZ, ref kickZVel, 0f, 420f, 26f, dt);
            Spring.Step(ref kickPitch, ref kickPitchVel, 0f, 380f, 22f, dt);
            Spring.Step(ref kickRoll, ref kickRollVel, 0f, 300f, 20f, dt);

            float r = reloading ? Mathf.Sin(ReloadProgress * Mathf.PI) : 0f;
            Vector3 reloadPos = new Vector3(-0.04f, -0.1f, -0.08f) * r;
            Quaternion reloadRot = Quaternion.Euler(28f * r, 0f, -42f * r);

            float s = swapT / SwapTime;
            Vector3 swapPos = new Vector3(0f, -0.35f, -0.15f) * s * s;
            Quaternion swapRot = Quaternion.Euler(55f * s * s, 0f, 0f);

            model.localPosition = modelBasePos + new Vector3(0f, 0f, kickZ) + reloadPos + swapPos;
            model.localRotation = modelBaseRot * swapRot * reloadRot * Quaternion.Euler(kickPitch, 0f, kickRoll);

            spinBoost = Mathf.MoveTowards(spinBoost, 0f, dt * 1.5f);
            spinAngle += dt * (60f + spinBoost * 900f + (reloading ? 500f : 0f));
            if (parts.spinners != null)
                for (int i = 0; i < parts.spinners.Length; i++)
                    if (parts.spinners[i]) parts.spinners[i].localRotation = Quaternion.Euler(0f, 0f, spinAngle * (i % 2 == 0 ? 1f : -1.3f));
        }

        /// <summary>Energy cores: dim right after a shot and charge back up; flare with heat; pulse while recharging.</summary>
        void UpdateGlows(float dt)
        {
            if (parts.glows == null || parts.glows.Length == 0) return;
            float ready = def.beam ? 1f : Mathf.Clamp01((Time.time - lastShotTime) * def.fireRate);
            float intensity = Mathf.Lerp(1.2f, 3f, ready) + heat * 3f;
            if (reloading) intensity = 0.4f + ReloadProgress * 3f + Mathf.Sin(Time.time * 30f) * 0.3f;
            if (beaming) intensity = 4.5f + Mathf.Sin(Time.time * 40f) * 0.8f;
            if (Ammo == 0 && !reloading) intensity = 0.2f;
            mpb.SetColor(EmissionId, Mats.HDR(def.color, intensity));
            foreach (var g in parts.glows) if (g) g.SetPropertyBlock(mpb);
        }

        void OnDisable() => StopBeam();
    }
}
