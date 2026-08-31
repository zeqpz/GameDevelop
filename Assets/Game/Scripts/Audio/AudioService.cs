// AudioService — pooled one-shot playback with bus volumes, mixer-lite.
// Buses are code-side volume groups (World / Ui / Music) applied at play
// time and retro-applied on change; a real AudioMixer asset can slot behind
// this API later without touching callers. Voices are AudioSources pooled
// under [GAME]/Services so they die with the world.
using System.Collections.Generic;
using UnityEngine;

namespace Game.Audio
{
    public enum AudioBus { World, Ui, Music }

    public class AudioService
    {
        class Voice
        {
            public AudioSource Src;
            public AudioBus Bus;
            public float BaseVol;
        }

        const int MaxVoices = 24;
        readonly Transform _host;
        readonly List<Voice> _pool = new List<Voice>();
        readonly float[] _busVol = { 1f, 1f, 1f };

        public AudioService(Transform host) { _host = host; }

        public float GetBusVolume(AudioBus bus) => _busVol[(int)bus];

        public void SetBusVolume(AudioBus bus, float v)
        {
            _busVol[(int)bus] = Mathf.Clamp01(v);
            foreach (var voice in _pool)
                if (voice.Bus == bus && voice.Src != null && voice.Src.isPlaying)
                    voice.Src.volume = voice.BaseVol * _busVol[(int)bus];
        }

        // 3D one-shot at a world position (footsteps, impacts, world FX).
        public AudioSource PlayAt(AudioClip clip, Vector3 pos, float volume = 1f,
            float pitch = 1f, float maxDistance = 25f, AudioBus bus = AudioBus.World)
        {
            var v = Lease(clip);
            if (v == null) return null;
            v.Src.transform.position = pos;
            v.Src.spatialBlend = 1f;
            v.Src.rolloffMode = AudioRolloffMode.Linear;
            v.Src.minDistance = 1.5f;
            v.Src.maxDistance = maxDistance;
            v.Src.dopplerLevel = 0f;
            Fire(v, bus, volume, pitch);
            return v.Src;
        }

        // 2D one-shot (UI clicks, notifications).
        public AudioSource PlayUi(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            var v = Lease(clip);
            if (v == null) return null;
            v.Src.spatialBlend = 0f;
            Fire(v, AudioBus.Ui, volume, pitch);
            return v.Src;
        }

        void Fire(Voice v, AudioBus bus, float volume, float pitch)
        {
            v.Bus = bus;
            v.BaseVol = volume;
            v.Src.volume = volume * _busVol[(int)bus];
            v.Src.pitch = pitch;
            v.Src.Play();
        }

        Voice Lease(AudioClip clip)
        {
            if (clip == null || _host == null) return null;
            foreach (var v in _pool)
                if (v.Src != null && !v.Src.isPlaying) { v.Src.clip = clip; return v; }
            if (_pool.Count >= MaxVoices) return null;   // saturated: drop, don't steal
            var go = new GameObject($"Voice{_pool.Count}");
            go.transform.SetParent(_host, false);
            var voice = new Voice { Src = go.AddComponent<AudioSource>() };
            voice.Src.playOnAwake = false;
            voice.Src.clip = clip;
            _pool.Add(voice);
            return voice;
        }
    }
}
