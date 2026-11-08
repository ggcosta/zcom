using UnityEngine;

namespace Zcom
{
    /// <summary>Finds the closest interactable in range, shows its prompt and uses it on E.</summary>
    public class PlayerInteractor : MonoBehaviour
    {
        public float range = 2.4f;

        PlayerController owner;
        public IInteractable Focus { get; private set; }

        void Awake() { owner = GetComponent<PlayerController>(); }

        void Update()
        {
            Focus = null;
            if (!owner || !owner.CanAct) return;
            var inv = owner.inventory;
            if (inv && inv.PanelOpen)
            {
                if (PlayerInputSource.Current.interact && inv.OpenContainer != null) inv.ClosePanel();
                return;
            }
            Vector3 p = transform.position;
            float best = range * range;
            foreach (var it in Interactables.All)
            {
                if (Interactables.IsGone(it) || !it.CanInteract) continue;
                Vector3 d = it.PromptPosition - p;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < best) { best = sq; Focus = it; }
            }
            if (Focus != null && PlayerInputSource.Current.interact)
            {
                Focus.Interact(owner);
                Focus = null; // it may have destroyed itself
            }
        }
    }
}
