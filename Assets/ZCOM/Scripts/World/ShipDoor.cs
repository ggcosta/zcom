using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// The inside of the dropship's rear door (interior instance): E steps out onto KEPLER-7, which starts a raid with
    /// everything you carry. The world outside is a separate instance (the raid map).
    /// </summary>
    public class ShipDoor : MonoBehaviour, IInteractable
    {
        public string destination = "KEPLER-7";
        static readonly Color Cyan = new Color(0.35f, 0.88f, 1f);
        bool used;

        public Vector3 PromptPosition => transform.position + Vector3.up * 1.3f;
        public string Prompt => $"[E]  EXIT TO {destination}";
        public Color PromptColor => Cyan;
        public bool CanInteract => !used;

        void OnEnable() { Interactables.All.Add(this); }
        void OnDisable() { Interactables.All.Remove(this); }

        public void Interact(PlayerController player)
        {
            if (used || !GameManager.I) return;
            used = true;
            ProcAudio.Play("beam_start", 0.6f, 0f, 0.9f);
            ProcAudio.Play("chest_open", 0.5f, 0f, 0.6f);
            GameFeel.Shake(0.12f);
            if (Hud.I) Hud.I.Banner("RAMP DOWN", $"Deploying to {destination}. Everything you carry is at stake.");
            GameManager.I.Deploy(0.6f);
        }
    }
}
