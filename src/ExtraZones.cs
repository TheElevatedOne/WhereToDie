using System.Linq;
using System.Reflection;
using RoR2;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace WhereToDie
{
    // Bounds for the sacrifice zones this mod adds. CursedBookSacrificeZone finds its bounds through
    // GetComponent<IZone>(), so a plain MonoBehaviour works and keeps the zone off the network.
    internal class CustomZone : MonoBehaviour, IZone
    {
        internal float radius;
        // Positions below this height don't count (the rocketship roof). Negative infinity = sphere.
        internal float floorY = float.NegativeInfinity;

        internal Vector3 Center => transform.position;

        public bool IsInBounds(Vector3 position)
        {
            var offset = position - transform.position;
            if (!float.IsNegativeInfinity(floorY))
            {
                if (position.y < floorY)
                {
                    return false;
                }
                offset.y = 0f;
            }
            return offset.sqrMagnitude <= radius * radius;
        }
    }

    // Host side: the extra places where dying by Reincarnation leads to the Soulbound Threshold.
    // The zones exist only on the host, which is where the ritual decides where to go.
    internal static class ExtraZones
    {
        private const string MoonScene = "moon2";
        private const string PlanetariumScene = "voidraid";

        // Bold green, like N'kuhana's own messages.
        private const string MessageFormat = "<color=#7BE35A><b>{0}</b></color>";

        // Fallback radius of the Planetarium's last donut, if its geometry can't be measured.
        private const float PlanetariumRadius = 150f;
        // How far below the roof top the ritual circle may land and still count.
        private const float RoofTolerance = 3f;

        private static FieldInfo useExplicitSceneField;
        private static FieldInfo explicitSceneField;
        private static FieldInfo currentDonutField;

        internal static void Init()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            useExplicitSceneField = typeof(CursedBookSacrificeZone).GetField("useExplicitSceneInstead", flags);
            explicitSceneField = typeof(CursedBookSacrificeZone).GetField("explicitScene", flags);
            currentDonutField = typeof(VoidRaidGauntletController).GetField("currentDonut", flags);
            if (useExplicitSceneField == null || explicitSceneField == null)
            {
                WhereToDiePlugin.Log.LogError("CursedBookSacrificeZone fields not found; the new places to use Reincarnation are disabled.");
            }

            On.RoR2.EscapeSequenceController.BeginEscapeSequence += EscapeSequenceController_BeginEscapeSequence;
            On.RoR2.VoidRaidGauntletController.SpawnOutroPortal += VoidRaidGauntletController_SpawnOutroPortal;
        }

        private static bool CanCreate(string scene, bool enabled)
        {
            return enabled && NetworkServer.active && Run.instance && PageFixes.CurrentSceneName() == scene
                && !PageFixes.HasEnteredLucidity() && useExplicitSceneField != null && explicitSceneField != null;
        }

        #region Moon: rocketship roof

        // Mithrix is dead once the escape sequence starts.
        private static void EscapeSequenceController_BeginEscapeSequence(On.RoR2.EscapeSequenceController.orig_BeginEscapeSequence orig, EscapeSequenceController self)
        {
            orig(self);
            if (!CanCreate(MoonScene, WhereToDiePlugin.RocketshipRoof.Value))
            {
                return;
            }
            var ship = FindSceneObject("RescueshipMoon");
            if (!ship)
            {
                WhereToDiePlugin.Log.LogWarning("Rocketship not found on the Moon; no Reincarnation zone on its roof.");
                return;
            }

            var bounds = default(Bounds);
            bool hasBounds = false;
            foreach (var renderer in ship.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                {
                    continue;
                }
                if (hasBounds)
                {
                    bounds.Encapsulate(renderer.bounds);
                }
                else
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
            }
            if (!hasBounds)
            {
                WhereToDiePlugin.Log.LogWarning("Rocketship has no renderers; no Reincarnation zone on its roof.");
                return;
            }

            // The roof is where the ritual's own downward raycast lands on the ship; antennas etc. make bounds.max
            // too high, and other colliders (triggers, a sphere above the ship) aren't the ship.
            float roofY = bounds.max.y;
            var above = new Vector3(bounds.center.x, bounds.max.y + 20f, bounds.center.z);
            var hits = Physics.RaycastAll(above, Vector3.down, bounds.size.y + 40f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
            var roofHit = hits.Where(h => h.collider.transform.IsChildOf(ship.transform)).OrderBy(h => h.distance).FirstOrDefault();
            if (roofHit.collider)
            {
                roofY = roofHit.point.y;
                WhereToDiePlugin.Log.LogDebug($"Rocketship roof: {roofHit.collider.name} at {roofHit.point}.");
            }
            else
            {
                WhereToDiePlugin.Log.LogWarning("Rocketship has no world collider under its centre; using its bounds instead.");
            }

            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.6f;
            CreateZone("RocketshipRoof", new Vector3(bounds.center.x, roofY, bounds.center.z), radius, roofY - RoofTolerance,
                "Found a way to escape the Moon");
        }

        #endregion

        #region Planetarium: final arena

        // Voidling's last phase is over when the game spawns the exit portal.
        private static void VoidRaidGauntletController_SpawnOutroPortal(On.RoR2.VoidRaidGauntletController.orig_SpawnOutroPortal orig, VoidRaidGauntletController self)
        {
            orig(self);
            if (!CanCreate(PlanetariumScene, WhereToDiePlugin.PlanetariumFinale.Value))
            {
                return;
            }
            var donut = currentDonutField?.GetValue(self) as VoidRaidGauntletController.DonutInfo;
            if (donut == null || !donut.root)
            {
                WhereToDiePlugin.Log.LogWarning("Planetarium final arena not found; no Reincarnation zone there.");
                return;
            }

            // The arena is the donut's solid geometry; Voidling's position and the return point are both off-centre.
            var bounds = default(Bounds);
            bool hasBounds = false;
            foreach (var collider in donut.root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger || (LayerIndex.world.mask & (1 << collider.gameObject.layer)) == 0)
                {
                    continue;
                }
                if (hasBounds)
                {
                    bounds.Encapsulate(collider.bounds);
                }
                else
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
            }
            if (!hasBounds)
            {
                var anchor = donut.crabPosition ? donut.crabPosition : donut.returnPoint;
                bounds = new Bounds(anchor ? anchor.position : donut.root.transform.position, Vector3.one * PlanetariumRadius * 2f);
                WhereToDiePlugin.Log.LogWarning("Planetarium final arena has no world colliders; using a fixed radius.");
            }
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            CreateZone("PlanetariumFinale", bounds.center, radius, float.NegativeInfinity, "Found peace in the Void", tube: true);
        }

        #endregion

        private static void CreateZone(string name, Vector3 position, float radius, float floorY, string message, bool tube = false)
        {
            var threshold = SceneCatalog.FindSceneDef(PageFixes.ThresholdScene);
            if (!threshold || (threshold.requiredExpansion && !Run.instance.IsExpansionEnabled(threshold.requiredExpansion)))
            {
                WhereToDiePlugin.Log.LogWarning("The Soulbound Threshold isn't available in this run.");
                return;
            }

            // Inactive while setting up, so CursedBookSacrificeZone.Awake/OnEnable see the final values.
            var go = new GameObject("WhereToDie_" + name);
            go.SetActive(false);
            go.transform.position = position;

            var bounds = go.AddComponent<CustomZone>();
            bounds.radius = radius;
            bounds.floorY = tube ? -1e6f : floorY;

            var zone = go.AddComponent<CursedBookSacrificeZone>();
            zone.behaviour = CursedBookSacrificeZone.Behaviour.KillAndLoad;
            useExplicitSceneField.SetValue(zone, true);
            explicitSceneField.SetValue(zone, threshold);
            zone.OnTriggered = zone.OnTriggered ?? new UnityEvent();
            string text = string.Format(MessageFormat, message);
            zone.OnTriggered.AddListener(() =>
            {
                Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = text });
                // Mithrix or Voidling is beaten: from here on the run ends in Obliteration.
                FinalePath.Begin();
            });

            go.SetActive(true);
            // The ritual uses the first zone containing it; if one of the game's own zones ever overlaps ours, ours wins.
            CursedBookSacrificeZone.instanceList.Remove(zone);
            CursedBookSacrificeZone.instanceList.Insert(0, zone);
            WhereToDiePlugin.Log.LogInfo($"Added Reincarnation zone '{name}' at {position} (radius {radius:0.#}).");
        }

        private static GameObject FindSceneObject(string name)
        {
            var found = GameObject.Find(name);
            if (found)
            {
                return found;
            }
            foreach (var transform in Object.FindObjectsOfType<Transform>())
            {
                if (transform.name == name)
                {
                    return transform.gameObject;
                }
            }
            return null;
        }
    }
}
