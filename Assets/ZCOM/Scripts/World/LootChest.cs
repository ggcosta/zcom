using UnityEngine;

namespace Zcom
{
    public enum CrateLoot { Basic, Gold, Xenite }

    /// <summary>
    /// Supply crate = lootable container (Duckov style). E opens the lid and shows its contents next to your
    /// backpack: drag items over, right-click to quick-move, or F to take all. Crates can be searched again later.
    /// </summary>

    public class LootChest : MonoBehaviour, IInteractable
    {
        public bool containsWeapon;
        public WeaponKind weapon = WeaponKind.NovaScatter;
        public CrateLoot loot = CrateLoot.Basic;
        public int slots = 9;
        public Transform lid;
        public MeshRenderer[] glowStrips;
        public Light glowLight;

        public ItemContainer Contents { get; private set; }

        bool opened;
        float openT = -1f;
        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public Color Tint => containsWeapon ? WeaponDefs.Get(weapon).rarityColor
            : loot == CrateLoot.Gold ? new Color(1f, 0.75f, 0.25f) : loot == CrateLoot.Xenite ? new Color(0.35f, 1f, 0.55f) : new Color(0.45f, 0.8f, 1f);
        bool Empty => Contents != null && Contents.IsEmpty;

        public Vector3 PromptPosition => transform.position + Vector3.up * 1.4f;
        public string Prompt => !opened ? "[E]  OPEN SUPPLY CRATE" : "[E]  SEARCH CRATE";
        public Color PromptColor => Tint;
        public bool CanInteract => !opened || !Empty;

        void OnEnable() { Interactables.All.Add(this); }
        void OnDisable() { Interactables.All.Remove(this); }
        void Awake() { mpb = new MaterialPropertyBlock(); }

        public void Interact(PlayerController player)
        {
            if (!opened)
            {
                opened = true;
                openT = 0f;
                Contents = new ItemContainer("SUPPLY CRATE", slots);
                foreach (var s in LootTable.CrateContents(this)) Contents.Add(s);
                Vector3 top = transform.position + Vector3.up * 1f;
                ProcAudio.Play("chest_open", 0.7f, 0.05f);
                Vfx.Burst(top, Tint, 24);
                Vfx.Ring(transform.position + Vector3.up * 0.05f, 0.4f, 2.2f, Mats.HDR(Tint, 2f), 0.4f);
                GameFeel.Shake(0.15f);
            }
            if (player.inventory) player.inventory.OpenPanel(Contents, "SUPPLY CRATE", transform);
        }

        void Update()
        {
            Color c;
            if (!opened)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.5f + transform.position.x);
                c = Mats.HDR(Tint, 1.2f + pulse * 1.5f);
                if (glowLight) glowLight.intensity = 1.2f + pulse * 1.2f;
            }
            else
            {
                openT += Time.deltaTime;
                float k = Ease.OutBack(openT / 0.45f);
                if (lid) lid.localRotation = Quaternion.Euler(-110f * k, 0f, 0f);
                float fade = Mathf.Clamp01(1f - openT / 1.5f);
                float rest = Empty ? 0.12f : 0.8f;
                c = Mats.HDR(Tint, rest + fade * 4f);
                if (glowLight) glowLight.intensity = (Empty ? 0.2f : 0.8f) + fade * 4f;
            }
            mpb.SetColor(EmissionId, c);
            if (glowStrips != null) foreach (var r in glowStrips) if (r) r.SetPropertyBlock(mpb);
        }
    }
}
