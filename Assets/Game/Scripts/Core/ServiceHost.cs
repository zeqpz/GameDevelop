// ServiceHost — the composition root. WorldBuilder puts this on
// [GAME]/Services FIRST; Awake constructs and registers every core service
// in dependency order, Update pumps the ones that tick (explicit order —
// no scattered MonoBehaviour update lottery), OnDestroy tears the world's
// services down with it. Add new services here and nowhere else.
using UnityEngine;
using Game.Audio;
using Game.Interaction;
using Game.Inventory;
using Game.UI;

namespace Game.Core
{
    public class ServiceHost : MonoBehaviour
    {
        InputService _input;
        AudioService _audio;
        InteractionService _interaction;
        InventoryService _inventory;

        void Awake()
        {
            _input = Services.Register(new InputService());
            _audio = Services.Register(new AudioService(transform));
            _interaction = Services.Register(new InteractionService(transform));
            _inventory = Services.Register(new InventoryService());
            EventBus.Subscribe<InteractionPerformed>(LogInteraction);

            var invScreen = new GameObject("InventoryScreen");
            invScreen.transform.SetParent(transform, false);
            invScreen.AddComponent<InventoryScreen>();   // Tab-toggled Robloxia GUI
        }

        void Update()
        {
            _interaction.Tick();   // input/audio don't tick — they poll/play live
            _inventory.Tick();
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<InteractionPerformed>(LogInteraction);
            _inventory?.Shutdown();
            _input?.Dispose();
            Services.Clear();
            EventBus.Clear();
        }

        // Dev visibility + the canonical subscriber example.
        static void LogInteraction(InteractionPerformed e) =>
            Debug.Log($"[EventBus] InteractionPerformed: {e.User.name} → {e.Target.name}");
    }
}
