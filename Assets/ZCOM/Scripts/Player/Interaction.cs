using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>Anything the player can use with E (chests, weapon pickups).</summary>
    public interface IInteractable
    {
        Vector3 PromptPosition { get; }
        string Prompt { get; }
        Color PromptColor { get; }
        bool CanInteract { get; }
        void Interact(PlayerController player);
    }

    public static class Interactables
    {
        public static readonly List<IInteractable> All = new List<IInteractable>();

        /// <summary>True for null and for destroyed Unity objects (interface refs bypass Unity's == null).</summary>
        public static bool IsGone(IInteractable it) => it == null || (it is Object o && o == null);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); }
    }
}
