// SaveService + PlayerProfile — the data layer, ProfileService-lite.
// Mirrors the Roblox DataManager contract in miniature: one versioned profile
// per player, loaded on boot, autosaved on a timer, force-flushed on quit.
// Local JSON for the single-player slice; the API surface is deliberately the
// thing the rest of the game codes against, so swapping the backend for
// UGS Cloud Save / PlayFab later touches ONLY this file.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.Data
{
    // One saved item pile — flat on purpose (JsonUtility can't recurse):
    // container contents point at their holder via `parent` (list index),
    // and writers guarantee parents appear before children.
    [Serializable]
    public class SavedStack
    {
        public string defId;
        public int count = 1;
        public bool rotated;
        public int x = -1, y = -1;   // grid origin (in whichever grid holds it)
        public int slot = -1;        // equip slot (EquipSlot int), -1 = not equipped
        public int parent = -1;      // index of the container stack holding this, -1 = root
    }

    [Serializable]
    public class PlayerProfile
    {
        public int version = 2;      // bump + migrate in SaveService.Migrate
        public float cash = 500f;    // Roblox starter economy parity
        public float bank = 2500f;
        public float posX, posY, posZ;
        public bool hasPosition;
        public bool hasInventory;    // false = grant the starter kit
        public List<SavedStack> inventory = new List<SavedStack>();
    }

    public class SaveService : MonoBehaviour
    {
        public static SaveService Instance { get; private set; }
        public static PlayerProfile Profile => Instance != null ? Instance._profile : null;

        // Systems stamp their state into the profile here right before disk.
        public static event Action<PlayerProfile> OnBeforeSave;

        [Tooltip("Seconds between autosaves (Roblox ProfileService cadence)")]
        public float autosaveInterval = 30f;
        public Transform trackPlayer;   // optional: position persisted on save

        PlayerProfile _profile = new PlayerProfile();
        float _nextSave;

        static string PathOnDisk => Path.Combine(Application.persistentDataPath, "profile.json");

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Load();
        }

        void Update()
        {
            if (Time.unscaledTime >= _nextSave)
            {
                _nextSave = Time.unscaledTime + autosaveInterval;
                Save();
            }
        }

        void OnApplicationQuit() => Save();

        public void Load()
        {
            try
            {
                if (File.Exists(PathOnDisk))
                {
                    _profile = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(PathOnDisk))
                        ?? new PlayerProfile();
                    Migrate(_profile);
                    Debug.Log($"[SaveService] Loaded profile v{_profile.version} (${_profile.cash} cash / ${_profile.bank} bank)");
                }
                else
                {
                    Debug.Log("[SaveService] Fresh profile");
                }
            }
            catch (Exception e)
            {
                // A corrupt file must never nuke a session — keep defaults, keep playing.
                Debug.LogWarning($"[SaveService] Load failed, using fresh profile: {e.Message}");
                _profile = new PlayerProfile();
            }
        }

        public void Save()
        {
            try
            {
                OnBeforeSave?.Invoke(_profile);
                if (trackPlayer != null)
                {
                    Vector3 p = trackPlayer.position;
                    _profile.posX = p.x; _profile.posY = p.y; _profile.posZ = p.z;
                    _profile.hasPosition = true;
                }
                File.WriteAllText(PathOnDisk, JsonUtility.ToJson(_profile, prettyPrint: true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveService] Save failed: {e.Message}");
            }
        }

        // Version-gated migrations, same pattern as DataManager.MIGRATIONS.
        static void Migrate(PlayerProfile p)
        {
            if (p.version < 1) p.version = 1;
            if (p.version < 2)
            {
                // v2: inventory persistence (hasInventory=false grants starter kit)
                p.inventory ??= new List<SavedStack>();
                p.version = 2;
            }
        }
    }
}
