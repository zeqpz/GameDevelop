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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _steps.Clear();
    }
}
