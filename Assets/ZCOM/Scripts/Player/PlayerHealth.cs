using UnityEngine;

namespace Zcom
{
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        public float maxHealth = 100f;
        public float invulnerableAfterHit = 0.35f;
        [Header("Shield (absorbs damage first, regenerates)")]
        public float maxShield = 50f;
        public float shieldRegenDelay = 3.5f;
        public float shieldRegenRate = 18f;
        public float Shield { get; private set; }
        public float Shield01 => maxShield > 0f ? Shield / maxShield : 0f;
        float lastHitTime = -10f;

        public float Health { get; private set; }
        public bool IsAlive => Health > 0f;
        public float Health01 => Health / maxHealth;

        PlayerController pc;
        Renderer[] rends;
        MaterialPropertyBlock mpb;
        float invulnT;
        float flashT;
        bool flashDirty;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            Health = maxHealth;
            Shield = maxShield;
            pc = GetComponent<PlayerController>();
            rends = System.Array.FindAll(GetComponentsInChildren<Renderer>(true), r => r is MeshRenderer || r is SkinnedMeshRenderer);
            mpb = new MaterialPropertyBlock();
        }

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return;
            if (pc && pc.IsDashing && !info.explosive)
            {
                if (Hud.I) Hud.I.WorldText(transform.position + Vector3.up * 2.2f, "DODGE", new Color(0.5f, 0.9f, 1f), 30);
                return;
            }
            if (invulnT > 0f && !info.explosive) return;

            lastHitTime = Time.time;
            float dmg = info.amount;
            if (Shield > 0f)
            {
                float absorbed = Mathf.Min(Shield, dmg);
                Shield -= absorbed;
                dmg -= absorbed;
                Vfx.Sparks(transform.position + Vector3.up * 1.2f, -info.direction, new Color(0.35f, 0.9f, 1f), 8);
                if (Shield <= 0f)
                {
                    if (Hud.I) Hud.I.ShieldBroken();
                    ProcAudio.Play("beam_stop", 0.6f, 0f, 0.7f);
                    Vfx.Ring(transform.position + Vector3.up * 0.1f, 0.4f, 2.4f, Mats.HDR(new Color(0.35f, 0.9f, 1f), 3f), 0.35f);
                }
            }
            Health = Mathf.Max(0f, Health - dmg);
            invulnT = invulnerableAfterHit;
            flashT = 0.15f;

            GameFeel.Shake(0.45f);
            GameFeel.HitStop(0.06f, 0.1f);
            ProcAudio.Play("hurt", 0.7f, 0.08f);
            Vfx.Sparks(transform.position + Vector3.up * 1.2f, -info.direction, new Color(1f, 0.3f, 0.2f), 10);
            if (pc)
            {
                pc.AddPush(info.direction.normalized * info.knockback);
                pc.HitReact(info.direction);
            }
            if (Hud.I) Hud.I.OnPlayerDamaged(info.amount, info.direction);

            if (Health <= 0f) Die();
        }

        public bool Heal(float amount)
        {
            if (!IsAlive || Health >= maxHealth) return false;
            Health = Mathf.Min(maxHealth, Health + amount);
            if (Hud.I) Hud.I.WorldText(transform.position + Vector3.up * 2.2f, $"+{Mathf.RoundToInt(amount)}", new Color(0.4f, 1f, 0.5f), 34);
            return true;
        }

        void Die()
        {
            ProcAudio.Play("player_death", 0.8f, 0f);
            GameFeel.HitStop(0.25f, 0.05f);
            GameFeel.Shake(0.8f);
            Vfx.LightFlash(transform.position + Vector3.up, new Color(0.4f, 0.8f, 1f), 8f, 8f, 0.4f);
            if (pc) pc.OnDeath();
            if (GameManager.I) GameManager.I.OnPlayerDied();
        }

        void Update()
        {
            invulnT -= Time.deltaTime;
            if (IsAlive && Shield < maxShield && Time.time - lastHitTime > shieldRegenDelay)
                Shield = Mathf.Min(maxShield, Shield + shieldRegenRate * Time.deltaTime);
            if (flashT > 0f)
            {
                flashT -= Time.deltaTime;
                float k = Mathf.Clamp01(flashT / 0.15f);
                mpb.SetColor(EmissionId, new Color(1f, 0.15f, 0.1f) * (4f * k));
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
