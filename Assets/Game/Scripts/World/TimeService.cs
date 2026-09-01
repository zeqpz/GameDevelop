// TimeService — the Roblox TimeService port (ServerScriptService.Systems.
// TimeService, read from Studio 2026-08-31). The shipped numbers, verbatim:
//   • TIME_SCALE 36 → 40 real minutes = 1 game day.
//   • Clock starts 6:00 AM; the DATE rolls when 24 game-hours of elapsed
//     time pass — i.e. at 6 AM, not midnight (Roblox quirk, kept).
//   • Compressed calendar: 3-day months, 36-day years, starting Sep 1 2026.
//   • Seasons by month (Sep-Nov Fall / Dec-Feb Winter / Mar-May Spring /
//     Jun-Aug Summer) picking sunrise/sunset: 7-19 / 8-17 / 6-20 / 5-21.
//   • The hour-by-hour lighting curve (sunrise 5-7, day, sunset 17-19,
//     dusk 19-20, darkest at midnight, pre-dawn brightening) — brightness
//     + ambient color exactly as shipped, driving a code-owned sun light
//     and flat ambient here instead of Roblox Lighting.
// Deliberate differences:
//   • Elapsed game time PERSISTS (profile v4) — a Roblox server reboots to
//     day one; the single-player slice is a continuing world.
//   • The curve is evaluated on the fractional hour and eased on apply, so
//     hour marks fade over ~2 s instead of stepping (Roblox stepped hourly).
// Weather / wind / rain / lightning are a later port; season only feeds
// sunrise/sunset and the date string for now.
using UnityEngine;
using UnityEngine.Rendering;
using Game.Data;

namespace Game.World
{
    public enum Season { Winter, Spring, Summer, Fall }

    public class TimeService
    {
        public const float TimeScale = 36f;            // game-secs per real sec
        const int StartHour = 6;                       // GAME_START_HOUR
        const int StartDay = 1, StartMonth = 9, StartYear = 2026;
        const int DaysPerMonth = 3;                    // compressed calendar
        const float ApplyResponse = 1.5f;              // lighting ease (per sec)
        const float AmbientScale = 0.45f;              // curve RGB → flat ambient

        static readonly string[] MonthNames =
        {
            "Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
        };

        // Clock
        public int Hour { get; private set; }
        public int Minute { get; private set; }
        public float ClockTime { get; private set; }   // 0..24 fractional
        public string TimeString { get; private set; } = "6:00 AM";
        // Date
        public int Day { get; private set; } = StartDay;
        public int Month { get; private set; } = StartMonth;
        public int Year { get; private set; } = StartYear;
        public int GameDay { get; private set; }       // whole days since epoch
        public string DateString { get; private set; } = "Sep 1, 2026";
        // Context
        public Season CurrentSeason { get; private set; } = Season.Fall;
        public int SunriseHour { get; private set; } = 7;
        public int SunsetHour { get; private set; } = 19;
        public bool IsNight => Hour < SunriseHour || Hour >= SunsetHour;
        public string ClockString => $"{TimeString} | {DateString} | {CurrentSeason}";

        readonly Transform _host;
        double _elapsed;               // game-seconds since epoch (clock offset excluded)
        bool _loaded;
        Light _sun;
        float _curIntensity = 1f;
        Color _curSunColor = Color.white;
        Color _curAmbient = Color.white;
        bool _lightInit;

        public TimeService(Transform host)
        {
            _host = host;
            SaveService.OnBeforeSave += WriteProfile;
        }

        public void Shutdown() => SaveService.OnBeforeSave -= WriteProfile;

        public void Tick(float dt)
        {
            if (!_loaded && SaveService.Profile != null)
            {
                _loaded = true;
                if (SaveService.Profile.hasTime)
                    _elapsed = SaveService.Profile.gameTimeSecs;
                Debug.Log($"[TimeService] {ClockStringAfterRecompute()}");
            }

            _elapsed += dt * TimeScale;
            Recompute();
            UpdateLighting(dt);
        }

        string ClockStringAfterRecompute()
        {
            Recompute();
            return ClockString;
        }

        // Debug hook (the ForceTime twin): jump the clock FORWARD to an hour.
        public void ForceTime(float hour)
        {
            float ahead = (hour - ClockTime + 24f) % 24f;
            _elapsed += ahead * 3600.0;
            Recompute();
        }

        void WriteProfile(PlayerProfile p)
        {
            if (!_loaded) return;
            p.hasTime = true;
            p.gameTimeSecs = _elapsed;
        }

        // ── The Roblox GetTimeData math ────────────────────────────────────
        void Recompute()
        {
            double total = _elapsed + StartHour * 3600.0;
            Hour = (int)(total / 3600.0) % 24;
            Minute = (int)(total / 60.0) % 60;
            ClockTime = (float)((total / 3600.0) % 24.0);

            GameDay = (int)(_elapsed / 86400.0);
            int startOffset = (StartMonth - 1) * DaysPerMonth + (StartDay - 1);
            int absDay = startOffset + GameDay;
            int daysPerYear = 12 * DaysPerMonth;
            Year = StartYear + absDay / daysPerYear;
            int dayInYear = absDay % daysPerYear;
            Month = dayInYear / DaysPerMonth + 1;
            Day = dayInYear % DaysPerMonth + 1;

            CurrentSeason = Month >= 9 && Month <= 11 ? Season.Fall
                : Month == 12 || Month <= 2 ? Season.Winter
                : Month >= 3 && Month <= 5 ? Season.Spring
                : Season.Summer;
            (SunriseHour, SunsetHour) = CurrentSeason switch
            {
                Season.Winter => (8, 17),
                Season.Spring => (6, 20),
                Season.Summer => (5, 21),
                _ => (7, 19),
            };

            int h12 = Hour % 12;
            if (h12 == 0) h12 = 12;
            TimeString = $"{h12}:{Minute:00} {(Hour >= 12 ? "PM" : "AM")}";
            DateString = $"{MonthNames[Month - 1]} {Day}, {Year}";
        }

        // ── Day/night lighting ─────────────────────────────────────────────
        // The updateServerLighting curve on the fractional hour: brightness +
        // ambient are the shipped values; the sun tint is ours (Roblox got it
        // free from ClockTime sun geometry).
        static void Curve(float h, out float brightness, out Color ambient, out Color sun)
        {
            var moon = new Color(0.55f, 0.65f, 0.95f);
            if (h >= 5f && h < 7f)          // sunrise: brightening
            {
                brightness = 0.5f + (h - 5f) * 0.25f;
                ambient = Rgb(200, 180, 150);
                sun = new Color(1f, 0.85f, 0.7f);
            }
            else if (h >= 7f && h < 17f)    // full day
            {
                brightness = 1f;
                ambient = Rgb(255, 255, 255);
                sun = new Color(1f, 0.96f, 0.9f);
            }
            else if (h >= 17f && h < 19f)   // sunset: darkening
            {
                brightness = 1f - (h - 17f) * 0.25f;
                ambient = Rgb(255, 150, 100);
                sun = new Color(1f, 0.72f, 0.5f);
            }
            else if (h >= 19f && h < 20f)   // dusk: deep orange
            {
                brightness = 0.5f - (h - 19f) * 0.25f;
                ambient = Rgb(200, 100, 50);
                sun = new Color(0.9f, 0.55f, 0.35f);
            }
            else if (h >= 20f)              // night: darken to midnight
            {
                float np = (h - 20f) / 4f;
                brightness = 0.3f - np * 0.15f;
                ambient = Color.Lerp(Rgb(100, 100, 150), Rgb(30, 30, 50), np);
                sun = moon;
            }
            else                            // 12 AM - 5 AM: brighten to dawn
            {
                float np = h / 5f;
                brightness = 0.15f + np * 0.15f;
                ambient = Rgb(30f + np * 40f, 30f + np * 40f, 50f + np * 60f);
                sun = moon;
            }
        }

        static Color Rgb(float r, float g, float b) => new Color(r / 255f, g / 255f, b / 255f);

        void EnsureSun()
        {
            if (_sun != null) return;

            if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional)
                _sun = RenderSettings.sun;
            else
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional && l.enabled) { _sun = l; break; }

            if (_sun == null)
            {
                var go = new GameObject("Sun");
                go.transform.SetParent(_host, false);
                _sun = go.AddComponent<Light>();
                _sun.type = LightType.Directional;
                _sun.shadows = LightShadows.Soft;
            }
            RenderSettings.sun = _sun;
            RenderSettings.ambientMode = AmbientMode.Flat;   // we own ambient now
        }

        void UpdateLighting(float dt)
        {
            EnsureSun();
            Curve(ClockTime, out float brightness, out Color ambient, out Color sunColor);

            // Sun rides the 6-18 arc; at night the same light plays the moon
            // on the opposite arc, dim and cool (Roblox ships an automatic moon).
            bool sunUp = ClockTime >= 6f && ClockTime < 18f;
            float arc = sunUp ? ClockTime : (ClockTime + 12f) % 24f;
            float elevation = arc / 24f * 360f - 90f;   // 6→horizon, 12→overhead
            _sun.transform.rotation = Quaternion.Euler(elevation, -30f, 0f);
            float targetIntensity = sunUp ? brightness * 1.15f : brightness * 0.4f;

            // Eased application: hour-mark pops fade instead of stepping.
            float a = _lightInit ? 1f - Mathf.Exp(-dt * ApplyResponse) : 1f;
            _lightInit = true;
            _curIntensity = Mathf.Lerp(_curIntensity, targetIntensity, a);
            _curSunColor = Color.Lerp(_curSunColor, sunColor, a);
            _curAmbient = Color.Lerp(_curAmbient, ambient * AmbientScale, a);

            _sun.intensity = _curIntensity;
            _sun.color = _curSunColor;
            RenderSettings.ambientLight = _curAmbient;
        }
    }
}
