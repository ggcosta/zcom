using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// The dropship's stash: an armoured locker in the crew bay. Before the raid (aboard), E opens the persistent
    /// <see cref="Profile.Stash"/> next to your backpack (drag items across, RMB stores / takes); a drawer slides out
    /// while it is open and everything is saved to the profile. Once the raid started it stays sealed until you extract.
    /// </summary>
    public class StashLocker : MonoBehaviour, IInteractable
    {
        public Transform drawer;
        public float drawerTravel = 0.45f;
        public Light lockerLight;
        public Renderer[] statusLights = new Renderer[0];
        [Tooltip("Where the prompt sits / the player has to stand (local space).")]
        public Vector3 promptOffset = new Vector3(0f, 1.4f, 1.2f);

        static readonly Color Amber = new Color(1f, 0.62f, 0.2f), Cyan = new Color(0.35f, 0.88f, 1f);
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        Vector3 drawerClosed;
        float open, openVel;
        MaterialPropertyBlock mpb;

        public bool IsOpen
        {
            get
            {
                var p = PlayerController.I;
                return p && p.inventory && p.inventory.PanelOpen && p.inventory.OpenContainer == Profile.Stash;
            }
        }

        public Vector3 PromptPosition => transform.TransformPoint(promptOffset);
        public string Prompt => Locked ? "[E]  STASH  (SEALED DURING THE RAID)" : "[E]  OPEN STASH";
        public Color PromptColor => Locked ? new Color(1f, 0.35f, 0.25f) : Amber;
        static bool Locked => GameManager.I && !GameManager.InHub;
        public bool CanInteract => true;

        void OnEnable() { Interactables.All.Add(this); }
        void OnDisable() { Interactables.All.Remove(this); }

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (drawer) drawerClosed = drawer.localPosition;
        }

        public void Interact(PlayerController player)
        {
            if (!player.inventory) return;
            if (Locked)
            {
                ProcAudio.Play("ui_deny", 0.6f, 0f);
                if (Hud.I) Hud.I.Banner("STASH SEALED", "Extract first: hold the ship's door to end the raid with your loot.", "WARNING");
                return;
            }
            ProcAudio.Play("chest_open", 0.55f, 0.03f, 0.8f);
            ProcAudio.Play("ui_scan", 0.3f, 0f, 1.2f);
            player.inventory.OpenPanel(Profile.Stash, "STASH", transform);
        }

        void Update()
        {
            float target = IsOpen ? 1f : 0f;
            Spring.Step(ref open, ref openVel, target, 120f, 18f, Time.unscaledDeltaTime);
            if (drawer) drawer.localPosition = drawerClosed + Vector3.forward * (drawerTravel * open);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f);
            if (lockerLight) lockerLight.intensity = Mathf.Lerp(1.6f + pulse * 0.4f, 4.5f, Mathf.Clamp01(open));
            if (statusLights.Length > 0)
            {
                mpb.SetColor(EmissionId, Mats.HDR(Color.Lerp(Amber, Cyan, Mathf.Clamp01(open)), 2.5f + pulse * 1.5f));
                foreach (var r in statusLights) if (r) r.SetPropertyBlock(mpb);
            }
        }
    }
}
