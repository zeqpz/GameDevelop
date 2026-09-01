// ProceduralAudio — the repo ships zero audio assets, so placeholder sounds
// are SYNTHESIZED in code (very much the house style): enveloped filtered
// noise with a surface-specific body — soft dark thuds on grass, a woody
// knock, a bright concrete tap, a metallic ring. Four seeded variants per
// surface so steps never machine-gun the same sample. Real recorded clips
// later just replace this behind the same lookup.
using System.Collections.Generic;
using UnityEngine;

namespace Game.Audio
{
    public static class ProceduralAudio
    {
        const int Rate = 44100;
        static readonly Dictionary<SurfaceType, AudioClip[]> _steps =
            new Dictionary<SurfaceType, AudioClip[]>();

        public static AudioClip RandomStep(SurfaceType s)
        {
            if (!_steps.TryGetValue(s, out var arr))
            {
                arr = new AudioClip[4];
                for (int i = 0; i < arr.Length; i++)
                    arr[i] = MakeStep(s, (int)s * 977 + i * 131);
                _steps[s] = arr;
            }
            return arr[Random.Range(0, arr.Length)];
        }

        static AudioClip MakeStep(SurfaceType s, int seed)
        {
            float dur, lpAlpha, toneHz, toneAmp, noiseAmp, decay, clickAmp;
            switch (s)
            {
                case SurfaceType.Grass:
                    dur = 0.13f; lpAlpha = 0.07f; toneHz = 0f; toneAmp = 0f;
                    noiseAmp = 1.0f; decay = 26f; clickAmp = 0f; break;
                case SurfaceType.Wood:
                    dur = 0.11f; lpAlpha = 0.16f; toneHz = 175f; toneAmp = 0.4f;
                    noiseAmp = 0.8f; decay = 42f; clickAmp = 0f; break;
                case SurfaceType.Metal:
                    dur = 0.17f; lpAlpha = 0.28f; toneHz = 860f; toneAmp = 0.35f;
                    noiseAmp = 0.5f; decay = 22f; clickAmp = 0f; break;
                default: // Concrete
                    dur = 0.085f; lpAlpha = 0.34f; toneHz = 0f; toneAmp = 0f;
                    noiseAmp = 0.9f; decay = 58f; clickAmp = 0.5f; break;
            }

            int n = (int)(dur * Rate);
            var data = new float[n];
            var rng = new System.Random(seed);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += lpAlpha * (noise - lp);
                float env = Mathf.Exp(-t * decay);
                float v = lp * noiseAmp * env;
                if (toneHz > 0f)
                    v += Mathf.Sin(2f * Mathf.PI * toneHz * t) * toneAmp
                        * Mathf.Exp(-t * decay * 1.3f);
                if (clickAmp > 0f && t < 0.004f)
                    v += noise * clickAmp * (1f - t / 0.004f);
                data[i] = Mathf.Clamp(v * 0.9f, -1f, 1f);
            }

            var clip = AudioClip.Create($"step_{s}_{seed}", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ── Gun sounds (GunController) ─────────────────────────────────────
        static AudioClip[] _shots;
        static AudioClip _dryClick, _reloadClack;

        public static AudioClip Gunshot()
        {
            if (_shots == null)
            {
                _shots = new AudioClip[3];
                for (int i = 0; i < _shots.Length; i++) _shots[i] = MakeShot(701 + i * 37);
            }
            return _shots[Random.Range(0, _shots.Length)];
        }

        // Crack (high-passed burst) + 58 Hz thump + short noise tail.
        static AudioClip MakeShot(int seed)
        {
            const float dur = 0.26f;
            int n = (int)(dur * Rate);
            var data = new float[n];
            var rng = new System.Random(seed);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.25f * (noise - lp);
                lp2 += 0.05f * (noise - lp2);
                float crack = (noise - lp) * Mathf.Exp(-t * 85f) * 0.95f;
                float thump = Mathf.Sin(2f * Mathf.PI * 58f * t) * Mathf.Exp(-t * 26f) * 0.85f;
                float tail = lp2 * Mathf.Exp(-t * 9f) * 0.5f;
                data[i] = Mathf.Clamp(crack + thump + tail, -0.98f, 0.98f);
            }
            var clip = AudioClip.Create($"shot_{seed}", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static AudioClip DryClick() =>
            _dryClick != null ? _dryClick : _dryClick = MakeTicks("dry_click", 0.07f, 0f, 0.028f);

        public static AudioClip ReloadClack() =>
            _reloadClack != null ? _reloadClack : _reloadClack = MakeTicks("reload_clack", 0.34f, 0f, 0.2f);

        static AudioClip MakeTicks(string name, float dur, float t0, float t1)
        {
            int n = (int)(dur * Rate);
            var data = new float[n];
            var rng = new System.Random(name.Length * 7919);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float v = 0f;
                if (t >= t0) v += noise * Mathf.Exp(-(t - t0) * 260f) * 0.8f;
                if (t >= t1) v += noise * Mathf.Exp(-(t - t1) * 220f) * 0.7f;
                data[i] = Mathf.Clamp(v, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ── Inventory UI sounds (SOUND_IDS placeholders: zip/pickup/thud;
        // the category variety comes from PITCH at the call site, exactly
        // like InventoryClient's per-category PlaybackSpeed offsets) ────────
        static AudioClip _invZip, _uiTick, _uiThud;

        // Bag zip: an accelerating tick-train under a noise envelope.
        public static AudioClip InvZip()
        {
            if (_invZip != null) return _invZip;
            const float dur = 0.16f;
            int n = (int)(dur * Rate);
            var data = new float[n];
            var rng = new System.Random(4242);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.3f * (noise - lp);
                float phase = t * (55f + t * 400f);
                float gate = Mathf.Repeat(phase, 1f) < 0.22f ? 1f : 0.12f;
                data[i] = Mathf.Clamp(lp * gate * Mathf.Exp(-t * 6f) * 0.9f, -1f, 1f);
            }
            _invZip = AudioClip.Create("inv_zip", n, 1, Rate, false);
            _invZip.SetData(data, 0);
            return _invZip;
        }

        // Bright pickup tick (sndPickup stand-in).
        public static AudioClip UiTick() =>
            _uiTick != null ? _uiTick : _uiTick = MakeTicks("ui_tick", 0.05f, 0f, 0.012f);

        // Dull item thud — callers pitch it per category for variety.
        public static AudioClip UiThud()
        {
            if (_uiThud != null) return _uiThud;
            const float dur = 0.09f;
            int n = (int)(dur * Rate);
            var data = new float[n];
            var rng = new System.Random(777);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.09f * (noise - lp);
                float thump = Mathf.Sin(2f * Mathf.PI * 130f * t) * Mathf.Exp(-t * 60f) * 0.5f;
                data[i] = Mathf.Clamp(lp * Mathf.Exp(-t * 45f) + thump, -1f, 1f);
            }
            _uiThud = AudioClip.Create("ui_thud", n, 1, Rate, false);
            _uiThud.SetData(data, 0);
            return _uiThud;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _steps.Clear();
            _shots = null;
            _dryClick = null;
            _reloadClack = null;
            _invZip = null;
            _uiTick = null;
            _uiThud = null;
        }
    }
}
