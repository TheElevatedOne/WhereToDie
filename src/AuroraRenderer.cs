using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.Navigation;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace WhereToDie
{
    // Draws the hallowed shrine's aurora (the beam of light and the rising yellow motes above
    // HallowingShrineBase) over every marker while the local player holds an unused Page of Reincarnation.
    // Red when the ritual kills, yellow (unchanged) when it doesn't.
    internal static class AuroraRenderer
    {
        // Primary key in the game's Addressables catalog (the asset path itself is not a key).
        private const string ShrineAddress = "RoR2/DLC4/HallowingShrineBase.prefab";
        private const string BeamName = "Beam";
        // Radius of the motes' emission cone in the prefab; markers scale the aurora relative to it.
        private const float PrefabRadius = 8.2f;

        // Stage-wide aurora: a few scattered auroras over the ground, sparser than over a single place. Cells are large
        // and each aurora is jittered and sized differently, so they don't line up in a visible grid.
        private const float StageCellSize = 220f;
        private const int MaxStageAuroras = 9;
        private const float StageAuroraScale = 2.5f;
        private const float StageEmissionMultiplier = 0.2f;

        private static readonly Color DeathColor = new Color(1f, 0.16f, 0.1f, 1f);

        private static GameObject beamPrefab;
        private static Vector3 beamOffset;
        private static Quaternion beamRotation;

        // Live auroras by what they show; ones that stay valid across an update are kept, the rest fade out.
        private static readonly Dictionary<string, AuroraFader> instances = new Dictionary<string, AuroraFader>();
        private static readonly HashSet<string> wanted = new HashSet<string>();
        private static bool dirty = true;
        private static bool shown;

        internal static void Init()
        {
            Addressables.LoadAssetAsync<GameObject>(ShrineAddress).Completed += handle =>
            {
                var shrine = handle.Result;
                // The beam sits deep in the shrine model's hierarchy; find it by name.
                var beam = shrine ? shrine.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == BeamName) : null;
                if (!beam)
                {
                    WhereToDiePlugin.Log.LogError($"'{BeamName}' not found in {ShrineAddress}; the hallowed aurora is disabled.");
                    return;
                }
                beamPrefab = beam.gameObject;
                beamOffset = shrine.transform.InverseTransformPoint(beam.position);
                beamRotation = Quaternion.Inverse(shrine.transform.rotation) * beam.rotation;
                dirty = true;
            };

            ZoneTracker.onMarkersChanged += () => dirty = true;
            WhereToDiePlugin.HallowedAurora.SettingChanged += (_, __) => dirty = true;
            RoR2Application.onUpdate += Update;
        }

        private static void Update()
        {
            bool show = WhereToDiePlugin.HallowedAurora.Value && beamPrefab && LocalPlayerHasUnusedPage();
            if (show != shown)
            {
                shown = show;
                dirty = true;
            }
            // Scene changes destroy the auroras with the scene.
            foreach (var key in instances.Where(pair => !pair.Value).Select(pair => pair.Key).ToList())
            {
                instances.Remove(key);
            }
            if (!dirty)
            {
                return;
            }
            dirty = false;

            wanted.Clear();
            if (show)
            {
                foreach (var marker in ZoneTracker.Markers)
                {
                    if (marker.StageWide)
                    {
                        SpawnStageWide(marker);
                    }
                    else
                    {
                        Spawn(marker.Position, Mathf.Clamp(marker.Radius / PrefabRadius, 0.6f, 8f), marker.Dies, 1f);
                    }
                }
            }
            foreach (var key in instances.Keys.Where(key => !wanted.Contains(key)).ToList())
            {
                instances[key].FadeOutAndDestroy();
                instances.Remove(key);
            }
        }

        internal static bool LocalPlayerHasUnusedPage()
        {
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            var page = DLC4Content.Items.PageDie;
            return body && body.inventory && page && body.inventory.GetItemCountEffective(page) > 0;
        }

        private static void SpawnStageWide(ZoneMarker marker)
        {
            var nodes = SceneInfo.instance ? SceneInfo.instance.groundNodes : null;
            var cells = new Dictionary<Vector2Int, Vector3>();
            if (nodes)
            {
                int count = nodes.GetNodeCount();
                for (int i = 0; i < count; i++)
                {
                    if (!nodes.GetNodePosition(new NodeGraph.NodeIndex(i), out var position))
                    {
                        continue;
                    }
                    var cell = new Vector2Int(Mathf.FloorToInt(position.x / StageCellSize), Mathf.FloorToInt(position.z / StageCellSize));
                    // The highest ground in the cell, so the aurora sits above it rather than inside a hill.
                    if (!cells.TryGetValue(cell, out var top) || position.y > top.y)
                    {
                        cells[cell] = position;
                    }
                }
            }
            if (cells.Count == 0)
            {
                Spawn(marker.Position, StageAuroraScale, marker.Dies, StageEmissionMultiplier);
                return;
            }

            // Thin out big maps evenly instead of cutting one side off. Seeded by the stage, so every player (and every
            // rebuild) gets the same layout.
            var random = new System.Random(SceneCatalog.mostRecentSceneDef ? SceneCatalog.mostRecentSceneDef.cachedName.GetHashCode() : 0);
            var picked = cells.Values.OrderBy(_ => random.Next()).Take(MaxStageAuroras);
            foreach (var position in picked)
            {
                var jitter = new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * (StageCellSize * 0.4f);
                float scale = StageAuroraScale * (0.7f + 0.6f * (float)random.NextDouble());
                Spawn(position + jitter, scale, marker.Dies, StageEmissionMultiplier);
            }
        }

        private static void Spawn(Vector3 position, float scale, bool dies, float emissionMultiplier)
        {
            string key = $"{position.x:0.0},{position.y:0.0},{position.z:0.0}|{scale:0.00}|{dies}|{emissionMultiplier:0.00}";
            wanted.Add(key);
            if (instances.ContainsKey(key))
            {
                return;
            }

            var aurora = Object.Instantiate(beamPrefab, position + beamOffset * scale, beamRotation);
            aurora.name = "WhereToDie_Aurora";
            aurora.transform.localScale = Vector3.one * scale;

            foreach (var system in aurora.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.loop = true;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                // The beam is a single burst that lives 30 s; repeat it so it never goes dark.
                if (main.startLifetime.constantMax > main.duration)
                {
                    main.duration = Mathf.Max(1f, main.startLifetime.constantMax * 0.5f);
                }
                if (dies)
                {
                    main.startColor = new ParticleSystem.MinMaxGradient(Tint(main.startColor.color));
                }
                var emission = system.emission;
                emission.rateOverTimeMultiplier *= emissionMultiplier;

                // Instanced materials, so tinting and fading only touch this aurora.
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                if (dies && renderer)
                {
                    foreach (var material in renderer.materials)
                    {
                        TintMaterial(material);
                    }
                }
            }
            var fader = aurora.AddComponent<AuroraFader>();
            foreach (var system in aurora.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Play(false);
            }
            instances[key] = fader;
        }

        // Keeps brightness and alpha, swaps the hue for red.
        private static Color Tint(Color color)
        {
            float brightness = Mathf.Max(color.r, color.g, color.b);
            return new Color(DeathColor.r * brightness, DeathColor.g * brightness, DeathColor.b * brightness, color.a);
        }

        private static readonly Dictionary<Texture, Texture2D> deathRamps = new Dictionary<Texture, Texture2D>();

        // RoR2's particle shaders (hgCloudRemap etc.) take their colour from a remap ramp, so a red copy of that ramp
        // is what actually turns the aurora red; the colour properties are tinted too for shaders without one.
        private static void TintMaterial(Material material)
        {
            if (!material)
            {
                return;
            }
            var shader = material.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
                {
                    int id = shader.GetPropertyNameId(i);
                    material.SetColor(id, Tint(material.GetColor(id)));
                }
            }
            if (material.HasProperty("_RemapTex") && material.GetTexture("_RemapTex") is Texture ramp && ramp)
            {
                var red = DeathRamp(ramp);
                if (red)
                {
                    material.SetTexture("_RemapTex", red);
                }
            }
        }

        // Same ramp, same brightness and alpha per pixel, red hue: whatever is transparent or black stays that way.
        private static Texture2D DeathRamp(Texture original)
        {
            if (deathRamps.TryGetValue(original, out var cached))
            {
                return cached;
            }
            Texture2D red = null;
            try
            {
                var renderTexture = RenderTexture.GetTemporary(original.width, original.height, 0, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                Graphics.Blit(original, renderTexture);
                RenderTexture.active = renderTexture;
                red = new Texture2D(original.width, original.height, TextureFormat.RGBA32, false)
                {
                    name = original.name + "_WhereToDieRed",
                    wrapMode = original.wrapMode,
                    filterMode = original.filterMode,
                };
                red.ReadPixels(new Rect(0, 0, original.width, original.height), 0, 0);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);

                var pixels = red.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = Tint(pixels[i]);
                }
                red.SetPixels(pixels);
                red.Apply();
                Object.DontDestroyOnLoad(red);
            }
            catch (System.Exception e)
            {
                WhereToDiePlugin.Log.LogWarning($"Could not recolour the aurora ramp {original.name}: {e.Message}");
                red = null;
            }
            deathRamps[original] = red;
            return red;
        }
    }

    // Fades an aurora in when it appears and out (then destroys it) when it goes away, by scaling every colour of its
    // particle materials. RoR2's particle shaders are additive or alpha blended, so black/transparent is invisible either way.
    internal class AuroraFader : MonoBehaviour
    {
        private const float FadeDuration = 1.5f;

        private readonly List<(Material material, int property, Color color)> colors = new List<(Material, int, Color)>();
        private float fade;
        private float target = 1f;

        private void Awake()
        {
            foreach (var renderer in GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                foreach (var material in renderer.materials)
                {
                    if (!material)
                    {
                        continue;
                    }
                    var shader = material.shader;
                    for (int i = 0; i < shader.GetPropertyCount(); i++)
                    {
                        if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
                        {
                            int id = shader.GetPropertyNameId(i);
                            colors.Add((material, id, material.GetColor(id)));
                        }
                    }
                }
            }
            Apply();
        }

        internal void FadeOutAndDestroy()
        {
            target = 0f;
            foreach (var system in GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void Update()
        {
            fade = Mathf.MoveTowards(fade, target, Time.deltaTime / FadeDuration);
            Apply();
            if (target <= 0f && fade <= 0f)
            {
                Destroy(gameObject);
            }
        }

        private void Apply()
        {
            float f = Mathf.SmoothStep(0f, 1f, fade);
            foreach (var (material, property, color) in colors)
            {
                if (material)
                {
                    material.SetColor(property, color * f);
                }
            }
        }
    }
}
