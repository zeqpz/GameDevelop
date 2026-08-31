// Interactable — put this on anything the player can E. The Unity twin of a
// Roblox proximity prompt: a verb, a reach, and an event. Systems wire
// behavior onto Interacted (chests, seats, doors, shop NPCs, ATMs…);
// InteractionService owns picking, LOS, the prompt UI, and firing.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Interaction
{
    public class Interactable : MonoBehaviour
    {
        [Tooltip("Verb shown in the prompt: \"[E] <prompt>\"")]
        public string prompt = "Interact";
        [Tooltip("Max distance from the PLAYER (≈10 studs default)")]
        public float maxDistance = 2.8f;
        [Tooltip("Skip the line-of-sight check (InteractIgnoreLOS twin)")]
        public bool ignoreLOS;

        public event Action<GameObject> Interacted;
        public void Fire(GameObject user) => Interacted?.Invoke(user);

        internal static readonly List<Interactable> Registry = new List<Interactable>();
        void OnEnable() => Registry.Add(this);
        void OnDisable() => Registry.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Registry.Clear();
    }
}
