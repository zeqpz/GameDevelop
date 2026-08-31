// SaveService + PlayerProfile — the data layer, ProfileService-lite.
// Mirrors the Roblox DataManager contract in miniature: one versioned profile
// per player, loaded on boot, autosaved on a timer, force-flushed on quit.
// Local JSON for the single-player slice; the API surface is deliberately the
// thing the rest of the game codes against, so swapping the backend for
// UGS Cloud Save / PlayFab later touches ONLY this file.
using System;
using System.IO;
using UnityEngine;

namespace Game.Data
{
    [Serializable]
    public class PlayerProfile
    {
        public int version = 1;      // bump + migrate in SaveService.Migrate
        public float cash = 500f;    // Roblox starter economy parity
        public float bank = 2500f;
        public float posX, posY, posZ;
        public bool hasPosition;
    }

    public class SaveService : MonoBehaviour
    {
        public static SaveService Instance { get; private set; }
        public static PlayerProfile Profile => Instance != null ? Instance._profile : null;

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
        }
    }
}
