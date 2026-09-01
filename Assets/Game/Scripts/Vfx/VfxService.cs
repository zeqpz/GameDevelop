// VfxService — pooled, kinematic combat FX. THE COLLISION POLICY, learned on
// Roblox and enforced by construction here:
//   • FX carry NO colliders and NO rigidbodies — anchored transforms moved by
//     code, so effects can never shove a player (fx-ignore-characters rule).
//   • Body hitboxes are TRIGGER colliders; every non-combat query in the
//     project (camera, footsteps, interaction LOS, crouch headroom) passes
//     QueryTriggerInteraction.Ignore, so ONLY gun casts see them. No
//     invisible-part damage hacks — that lesson is paid for.
// Effects (all code-built, URP-unlit):
//   • Tracer — stretched streak, 2-frame life.  • Muzzle flash — quad+light.
//   • Impact — speck burst + capped decal ring buffer (parented to the hit
//     surface so decals ride moving objects).  • Blood mist — flesh hits.
//   • Shells — the Roblox shell system verbatim: client-local ANCHORED brass
//     on a precomputed parabola, per-step raycast bounce (restitution +
//     friction), settle, expire. Launch tune = ShellConfig.LAUNCH in studs
//     (up 20+9 · out 13+8 · scatter 8° · gravity 90) through GameUnits.
using System.Collections.Generic;
using UnityEngine;
using Game.Combat;
using Game.Core;

namespace Game.Vfx
{
    public class VfxService
    {
        const float ShellGravity = 90f * GameUnits.StudsToMeters;    // 25.2
        const float ShellUpMin = 20f * GameUnits.StudsToMeters;      // 5.6
        const float ShellUpVar = 9f * GameUnits.StudsToMeters;
        const float ShellOutMin = 13f * GameUnits.StudsToMeters;     // 3.64
        const float ShellOutVar = 8f * GameUnits.StudsToMeters;
        const float ShellScatterDeg = 8f;
        // Bounciness is rolled PER SHELL around the Roblox 0.35: dead casings
        // die on the first hop, lively ones skip three or four times.
        const float ShellRestMin = 0.28f;
        const float ShellRestMax = 0.5f;
        const float ShellSettleSpeed = 0.3f;   // below this a bounce becomes the rest pose
        const float ShellSkitter = 0.08f;      // sideways wander added per bounce (×impact)
        // Roblox shell rule: a fixed PRELOADED per-player pool, reused
        // oldest-first — settled brass stays on the ground (no timers) until
        // its slot is recycled by a later eject.
        const int ShellPoolSize = 24;
        const int MaxDecals = 64;
        // Blood splats (spawnBloodSplatter, verbatim): discs pop open over
        // 0.10 s, folder-capped oldest-evicted (the cap IS the lifetime here;
        // Roblox also ran a 150 s Debris on top).
        const int MaxSplats = 400;
        const float SplatPopTime = 0.10f;                                // BLOOD_SPLAT_DURATION
        const float SplatThickness = 0.04f * GameUnits.StudsToMeters;
        const float SplatStandoff = 0.04f * GameUnits.StudsToMeters;
        const float SplatSeedD = 0.04f * GameUnits.StudsToMeters;        // spawn tiny, tween out

        readonly Transform _host;
        Material _tracerMat, _flashMat, _brassMat, _decalMat, _bloodMat, _speckMat;
        Pool _tracers, _flashes, _decals, _specks, _bloodPuffs, _splats;
        Material _splatMat;
        int _nextShell;

        class Timed { public GameObject Go; public float T; public Pool From; public Light Light; }
        class Speck { public GameObject Go; public Vector3 Vel; public float T; public float Dur; public Color Col; public Pool From; }
        class Shell { public GameObject Go; public Vector3 Vel; public Vector3 Spin; public float Rest = 0.35f; public float Settled = -1f; public bool Bounced; }
        class GrowingSplat { public GameObject Go; public float T; public float TargetD; }

        readonly List<Timed> _timed = new List<Timed>();
        readonly List<Speck> _live = new List<Speck>();
        readonly List<Shell> _brass = new List<Shell>();
        readonly List<GrowingSplat> _growing = new List<GrowingSplat>();
        readonly Queue<GameObject> _decalRing = new Queue<GameObject>();
        readonly Queue<GameObject> _splatRing = new Queue<GameObject>();

        public VfxService(Transform host)
        {
            _host = host;
            _tracerMat = Unlit(new Color(1f, 0.93f, 0.6f, 0.85f), true);
            _flashMat = Unlit(new Color(1f, 0.82f, 0.35f, 0.9f), true);
            _brassMat = Unlit(new Color(0.78f, 0.62f, 0.25f), false);
            _decalMat = Unlit(new Color(0.06f, 0.05f, 0.04f, 0.92f), true);
            _bloodMat = Unlit(new Color(0.55f, 0.06f, 0.05f, 0.8f), true);
            _speckMat = Unlit(new Color(0.75f, 0.72f, 0.66f), false);

            _tracers = new Pool(host, () => Bar("Tracer", _tracerMat));
            _flashes = new Pool(host, () => Flash());
            _decals = new Pool(host, () => DecalQuad());
            _specks = new Pool(host, () => Cube("Speck", 0.03f, _speckMat));
            _bloodPuffs = new Pool(host, () => Cube("Blood", 0.045f, _bloodMat));
            _splatMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _splatMat.SetColor("_BaseColor", new Color32(120, 0, 0, 255));
            _splats = new Pool(host, SplatDisc);

            for (int i = 0; i < ShellPoolSize; i++)          // preload the brass ring
                _brass.Add(new Shell { Go = BuildShell() });
        }

        // ── public API ─────────────────────────────────────────────────────
        public void Tracer(Vector3 from, Vector3 to)
        {
            var go = _tracers.Lease();
            var dir = to - from;
            go.transform.position = (from + to) * 0.5f;
            go.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 0.0001f ? dir : Vector3.forward);
            go.transform.localScale = new Vector3(0.02f, 0.02f, Mathf.Max(0.1f, dir.magnitude));
            _timed.Add(new Timed { Go = go, T = 0.05f, From = _tracers });
        }

        public void MuzzleFlash(Vector3 pos, Vector3 dir)
        {
            var go = _flashes.Lease();
            go.transform.position = pos + dir * 0.02f;
            go.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            go.transform.localScale = Vector3.one * Random.Range(0.16f, 0.24f);
            _timed.Add(new Timed { Go = go, T = 0.045f, From = _flashes, Light = go.GetComponentInChildren<Light>() });
        }

        public void Impact(Vector3 point, Vector3 normal, Transform surface)
        {
            for (int i = 0; i < 5; i++)
            {
                var s = _specks.Lease();
                s.transform.position = point + normal * 0.01f;
                s.transform.rotation = Random.rotation;
                var vel = (normal * Random.Range(1.2f, 2.6f)
                    + Random.insideUnitSphere * 1.1f);
                _live.Add(new Speck { Go = s, Vel = vel, T = 0.35f, Dur = 0.35f,
                    Col = new Color(0.75f, 0.72f, 0.66f, 1f), From = _specks });
            }

            var d = _decals.Lease();
            d.transform.position = point + normal * 0.004f;
            d.transform.rotation = Quaternion.LookRotation(-normal)
                * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            d.transform.localScale = Vector3.one * Random.Range(0.07f, 0.1f);
            d.transform.SetParent(surface != null ? surface : _host, true);
            _decalRing.Enqueue(d);
            while (_decalRing.Count > MaxDecals) _decals.Release(_decalRing.Dequeue());
        }

        // spawnBloodMist: damage-scaled (bloodMistScale — auto = dmg/28,
        // clamped 0.55–1.8, ±18% per-hit jitter), fast fade-in ~12% of life
        // then slow fade-out, and every fleck rolls its own 95–145 red.
        public void BloodMist(Vector3 point, Vector3 normal, float scale = 1f)
        {
            scale = Mathf.Clamp(scale, 0.55f, 1.8f) * Random.Range(0.82f, 1.18f);
            int count = Mathf.RoundToInt(6f * scale);
            for (int i = 0; i < count; i++)
            {
                var b = _bloodPuffs.Lease();
                b.transform.position = point;
                b.transform.rotation = Random.rotation;
                b.transform.localScale = Vector3.one * Random.Range(0.03f, 0.07f) * scale;
                var vel = (normal * Random.Range(0.6f, 1.6f)
                    + Random.insideUnitSphere * 0.9f + Vector3.up * 0.4f) * scale;
                float dur = 0.45f * Random.Range(0.85f, 1.25f);
                _live.Add(new Speck
                {
                    Go = b, Vel = vel, T = dur, Dur = dur, From = _bloodPuffs,
                    Col = new Color((95 + Random.Range(0, 51)) / 255f, 0f, 0f, 0.8f),
                });
            }
        }

        // ── spawnBloodSplatter, ported verbatim (studs → meters) ───────────
        // Rays fan out from the hit, biased along the bullet's continued
        // path; each ray that lands on a solid STATIC surface takes a disc.
        // The no-mid-air rules: a Rigidbody or a Health anywhere above the
        // hit collider rejects it (bodies/corpses/props — a splat left there
        // floats once they move), triggers are never queried, and a ray that
        // hits nothing places nothing — the sky stays clean. Rejects spend
        // the attempt budget (count × 3) and re-roll.
        public void BloodSplatter(Vector3 hitPos, Vector3 hitNormal, bool light = false)
        {
            Vector3 n = hitNormal.sqrMagnitude > 0.0001f ? hitNormal.normalized : Vector3.up;
            Vector3 through = -n;
            if (light)
            {
                // Bleed-drip pattern: no big centre, ground-only.
                PlaceLayer(Random.Range(1, 3), 0.55f, 0.30f, 0.55f, 6f, hitPos, through, true);
                PlaceLayer(Random.Range(2, 5), 0.90f, 0.10f, 0.22f, 5f, hitPos, through, true);
                return;
            }
            PlaceLayer(Random.Range(1, 3), 0.25f, 0.90f, 1.50f, 8f, hitPos, through, false);
            PlaceLayer(Random.Range(3, 6), 0.60f, 0.35f, 0.65f, 7f, hitPos, through, false);
            PlaceLayer(Random.Range(4, 9), 0.90f, 0.10f, 0.25f, 6f, hitPos, through, false);
        }

        void PlaceLayer(int count, float dirSpread, float sizeMinSt, float sizeMaxSt,
            float rangeSt, Vector3 hitPos, Vector3 through, bool groundOnly)
        {
            int placed = 0;
            int attempts = count * 3;
            float range = rangeSt * GameUnits.StudsToMeters;
            while (placed < count && attempts > 0)
            {
                attempts--;
                Vector3 rand = Random.insideUnitSphere;
                if (rand.sqrMagnitude < 0.0001f) rand = Vector3.right;
                Vector3 dir = through * (1f - dirSpread) + rand.normalized * dirSpread;
                if (dir.sqrMagnitude < 0.0001f) dir = through;
                dir.Normalize();

                if (!Physics.Raycast(hitPos, dir, out RaycastHit res, range, ~0,
                        QueryTriggerInteraction.Ignore)) continue;       // sky/nothing
                if (res.rigidbody != null) continue;                     // movable
                if (res.collider.GetComponentInParent<Health>() != null) continue; // body
                if (groundOnly && res.normal.y <= 0.5f) continue;        // floors only

                float d = (sizeMinSt + Random.value * (sizeMaxSt - sizeMinSt))
                    * GameUnits.StudsToMeters;
                d = ClampToFace(res, d);
                if (d < 0.1f * GameUnits.StudsToMeters) continue;        // sliver: re-roll

                placed++;
                SpawnSplatDisc(res.point + res.normal * SplatStandoff, res.normal, d);
            }
        }

        // Fit-to-part clamp: project the hit into the box's local space, pick
        // the face by dominant local-normal axis, and cap the diameter by the
        // room left to each edge — discs never overhang crate lips or trim.
        // Mesh colliders (the baseplate) are the Terrain case: no clamp.
        static float ClampToFace(RaycastHit res, float d)
        {
            var box = res.collider as BoxCollider;
            if (box == null) return d;
            Transform t = box.transform;
            Vector3 lp = t.InverseTransformPoint(res.point) - box.center;
            Vector3 ln = t.InverseTransformDirection(res.normal);
            Vector3 half = box.size * 0.5f;
            Vector3 s = t.lossyScale;
            float ax = Mathf.Abs(ln.x), ay = Mathf.Abs(ln.y), az = Mathf.Abs(ln.z);
            float eA, eB;
            if (ax >= ay && ax >= az)
            {
                eA = (half.y - Mathf.Abs(lp.y)) * Mathf.Abs(s.y);
                eB = (half.z - Mathf.Abs(lp.z)) * Mathf.Abs(s.z);
            }
            else if (ay >= ax && ay >= az)
            {
                eA = (half.x - Mathf.Abs(lp.x)) * Mathf.Abs(s.x);
                eB = (half.z - Mathf.Abs(lp.z)) * Mathf.Abs(s.z);
            }
            else
            {
                eA = (half.x - Mathf.Abs(lp.x)) * Mathf.Abs(s.x);
                eB = (half.y - Mathf.Abs(lp.y)) * Mathf.Abs(s.y);
            }
            return Mathf.Min(d, 2f * Mathf.Min(eA, eB));
        }

        void SpawnSplatDisc(Vector3 pos, Vector3 normal, float targetD)
        {
            var go = _splats.Lease();
            go.transform.position = pos;
            // Cylinder axis = local Y: flat face hugs the surface, random spin
            // around the normal (the flatToSurface twin).
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal)
                * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            go.transform.localScale = new Vector3(SplatSeedD, SplatThickness * 0.5f, SplatSeedD);
            // THE coloring effect: each disc rolls its own red (95–145, 0, 0).
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor",
                new Color32((byte)(95 + Random.Range(0, 51)), 0, 0, 255));
            go.GetComponent<Renderer>().SetPropertyBlock(mpb);
            _growing.Add(new GrowingSplat { Go = go, T = 0f, TargetD = targetD });
            _splatRing.Enqueue(go);
            while (_splatRing.Count > MaxSplats) _splats.Release(_splatRing.Dequeue());
        }

        public void EjectShell(Vector3 pos, Vector3 gunRight, Vector3 gunFwd)
        {
            var sh = _brass[_nextShell];                     // oldest slot recycles
            _nextShell = (_nextShell + 1) % _brass.Count;
            sh.Go.SetActive(true);
            sh.Go.transform.position = pos;
            sh.Go.transform.rotation = Quaternion.LookRotation(gunFwd);
            var scatter = Quaternion.Euler(Random.Range(-ShellScatterDeg, ShellScatterDeg),
                Random.Range(-ShellScatterDeg, ShellScatterDeg), 0f);
            sh.Vel = scatter * (Vector3.up * (ShellUpMin + Random.value * ShellUpVar)
                + gunRight * (ShellOutMin + Random.value * ShellOutVar));
            sh.Spin = Random.insideUnitSphere * 720f;
            sh.Rest = Random.Range(ShellRestMin, ShellRestMax);
            sh.Settled = -1f;
            sh.Bounced = false;
        }

        // ── tick ───────────────────────────────────────────────────────────
        public void Tick(float dt)
        {
            for (int i = _timed.Count - 1; i >= 0; i--)
            {
                _timed[i].T -= dt;
                if (_timed[i].T <= 0f) { _timed[i].From.Release(_timed[i].Go); _timed.RemoveAt(i); }
            }

            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var p = _live[i];
                p.T -= dt;
                if (p.T <= 0f) { p.From.Release(p.Go); _live.RemoveAt(i); continue; }
                p.Vel += Vector3.down * 9.8f * dt;
                p.Go.transform.position += p.Vel * dt;
                // Mist transparency curve: fast fade-in (~12% of life), slow out.
                float lifeFrac = 1f - p.T / Mathf.Max(0.01f, p.Dur);
                float env = lifeFrac < 0.12f
                    ? lifeFrac / 0.12f
                    : 1f - (lifeFrac - 0.12f) / 0.88f;
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor",
                    new Color(p.Col.r, p.Col.g, p.Col.b, p.Col.a * env));
                p.Go.GetComponent<Renderer>().SetPropertyBlock(mpb);
            }

            // Splat-pop: discs tween 0.04 st → full size (0.10 s Quad-Out).
            for (int i = _growing.Count - 1; i >= 0; i--)
            {
                var g = _growing[i];
                if (g.Go == null || !g.Go.activeSelf) { _growing.RemoveAt(i); continue; }
                g.T += dt;
                float raw = Mathf.Clamp01(g.T / SplatPopTime);
                float e = 1f - (1f - raw) * (1f - raw);
                float d = Mathf.Lerp(SplatSeedD, g.TargetD, e);
                g.Go.transform.localScale = new Vector3(d, SplatThickness * 0.5f, d);
                if (raw >= 1f) _growing.RemoveAt(i);
            }

            for (int i = _brass.Count - 1; i >= 0; i--)
            {
                var sh = _brass[i];
                if (sh.Settled >= 0f || !sh.Go.activeSelf) continue;   // grounded brass just stays

                sh.Vel += Vector3.down * ShellGravity * dt;
                Vector3 step = sh.Vel * dt;
                float len = step.magnitude;
                // Roblox rule: raycast the movement step, reflect off whatever
                // it meets (triggers excluded — hitboxes aren't walls).
                if (len > 0.0001f && Physics.Raycast(sh.Go.transform.position, step / len,
                        out RaycastHit hit, len + 0.005f, ~0, QueryTriggerInteraction.Ignore))
                {
                    float impact = sh.Vel.magnitude;
                    sh.Go.transform.position = hit.point + hit.normal * 0.012f;
                    sh.Vel = Vector3.Reflect(sh.Vel, hit.normal) * sh.Rest;
                    // Real casings skitter: every hop wanders a touch sideways.
                    sh.Vel += Vector3.ProjectOnPlane(Random.insideUnitSphere, hit.normal)
                        * (impact * ShellSkitter);
                    sh.Spin *= 0.5f;
                    if (!sh.Bounced)
                    {
                        sh.Bounced = true;
                        if (Services.TryGet(out Game.Audio.AudioService audio))
                            audio.PlayAt(Game.Audio.ProceduralAudio.RandomStep(Game.Audio.SurfaceType.Metal),
                                hit.point, 0.1f, 1.85f, 10f);
                    }
                    if (sh.Vel.magnitude < ShellSettleSpeed)
                    {
                        sh.Settled = 0f;
                        // FLAT rest pose: the long axis (local Z) hugs the
                        // surface — random yaw around the normal + a random
                        // roll about the case itself. (The old Euler X=90
                        // pitched local Z vertical and stood casings on end.)
                        sh.Go.transform.rotation =
                            Quaternion.FromToRotation(Vector3.up, hit.normal)
                            * Quaternion.Euler(0f, Random.Range(0f, 360f), Random.Range(0f, 360f));
                    }
                }
                else
                {
                    sh.Go.transform.position += step;
                    sh.Go.transform.Rotate(sh.Spin * dt, Space.Self);
                }
            }
        }

        // ── builders ───────────────────────────────────────────────────────
        static Material Unlit(Color c, bool transparent)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = 3000;
            }
            return m;
        }

        static GameObject Strip(GameObject go)
        {
            Object.Destroy(go.GetComponent<Collider>());
            return go;
        }

        GameObject Cube(string name, float size, Material mat)
        {
            var go = Strip(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = name;
            go.transform.localScale = Vector3.one * size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.SetActive(false);
            return go;
        }

        GameObject Bar(string name, Material mat)
        {
            var go = Cube(name, 1f, mat);
            return go;
        }

        GameObject Flash()
        {
            var go = Strip(GameObject.CreatePrimitive(PrimitiveType.Quad));
            go.name = "MuzzleFlash";
            go.GetComponent<Renderer>().sharedMaterial = _flashMat;
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(go.transform, false);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.8f, 0.45f);
            l.intensity = 2.6f;
            l.range = 7f;
            go.SetActive(false);
            return go;
        }

        GameObject SplatDisc()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "BloodSplatter";
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = _splatMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go;
        }

        GameObject DecalQuad()
        {
            var go = Strip(GameObject.CreatePrimitive(PrimitiveType.Quad));
            go.name = "BulletHole";
            go.GetComponent<Renderer>().sharedMaterial = _decalMat;
            go.SetActive(false);
            return go;
        }

        // The real Shell9mm casing (Studio export, Resources/Vfx/Shell9mm.obj),
        // normalized so its LONG axis lies on local Z at 0.048 m — matching
        // ShellConfig's "9mm length is local Z, settles flat with plain yaw".
        // Falls back to the brass box if the model is missing.
        GameObject BuildShell()
        {
            var model = Resources.Load<GameObject>("Vfx/Shell9mm");
            if (model == null)
            {
                var box = Cube("Shell", 1f, _brassMat);
                box.transform.SetParent(_host, true);
                box.transform.localScale = new Vector3(0.02f, 0.02f, 0.048f);
                return box;
            }

            var root = new GameObject("Shell");
            root.transform.SetParent(_host, false);
            var visual = Object.Instantiate(model, root.transform, false);
            foreach (var c in visual.GetComponentsInChildren<Collider>())
                Object.Destroy(c);
            var rends = visual.GetComponentsInChildren<Renderer>();
            foreach (var r in rends) r.sharedMaterial = _brassMat;

            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                var s = b.size;
                if (s.x >= s.y && s.x >= s.z)
                    visual.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                else if (s.y >= s.x && s.y >= s.z)
                    visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                float maxDim = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                if (maxDim > 0.0001f)
                    visual.transform.localScale = Vector3.one * (0.048f / maxDim);

                b = rends[0].bounds;                       // re-center on the root
                foreach (var r in rends) b.Encapsulate(r.bounds);
                visual.transform.localPosition -= b.center - root.transform.position;
            }
            root.SetActive(false);
            return root;
        }
    }
}
