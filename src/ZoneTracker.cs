using System;
using System.Collections.Generic;
using System.Reflection;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace WhereToDie
{
    // A place where the Page of Reincarnation currently works, reduced to what the visuals need.
    internal struct ZoneMarker : IEquatable<ZoneMarker>
    {
        internal Vector3 Position;
        internal float Radius;
        // The ritual kills everyone in its circle (KillAndLoad). Otherwise it only triggers an event.
        internal bool Dies;
        // The "first 30 seconds of a normal stage" zone, which covers the whole map.
        internal bool StageWide;
        // A vertical tube (any height) instead of a sphere.
        internal bool Tube;

        internal bool Contains(Vector3 position)
        {
            if (StageWide)
            {
                return true;
            }
            var offset = position - Position;
            if (Tube)
            {
                offset.y = 0f;
            }
            return offset.sqrMagnitude <= Radius * Radius;
        }

        public bool Equals(ZoneMarker other)
        {
            return (Position - other.Position).sqrMagnitude < 0.01f && Mathf.Abs(Radius - other.Radius) < 0.1f
                && Dies == other.Dies && StageWide == other.StageWide && Tube == other.Tube;
        }

        public override bool Equals(object obj) => obj is ZoneMarker other && Equals(other);

        public override int GetHashCode() => Position.GetHashCode() ^ Radius.GetHashCode();
    }

    // Host side: works out which sacrifice zones are usable right now without calling
    // CursedBookSacrificeZone.GetNextScene, which rolls Run.nextStageRng for reroll zones.
    // The resulting markers drive the aurora (host + modded clients) and the host's own icon highlight.
    internal static class ZoneTracker
    {
        // Radius at which a sphere zone counts as "the whole stage" (the reroll zone uses 65536).
        private const float StageWideRadius = 1000f;
        // Normal stages only allow the reroll for this long after entering.
        internal const float StageWideDuration = 30f;
        private const float PollInterval = 0.5f;

        private static FieldInfo useRerollField;
        private static FieldInfo useNextSceneField;
        private static FieldInfo useExplicitSceneField;
        private static FieldInfo explicitSceneField;
        private static FieldInfo pageRequirementField;

        // Zones spawned by DLC4SacrificeZoneSpawner (the 30 second reroll zone), in case its radius ever changes.
        private static readonly HashSet<CursedBookSacrificeZone> stageWideZones = new HashSet<CursedBookSacrificeZone>();

        private static float pollTimer;

        // What the visuals show. On the host it's computed here; on clients it comes from ZoneNetwork.
        internal static readonly List<ZoneMarker> Markers = new List<ZoneMarker>();
        internal static event Action onMarkersChanged;

        internal static void Init()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var type = typeof(CursedBookSacrificeZone);
            useRerollField = type.GetField("useRerollSceneInstead", flags);
            useNextSceneField = type.GetField("useNextSceneInstead", flags);
            useExplicitSceneField = type.GetField("useExplicitSceneInstead", flags);
            explicitSceneField = type.GetField("explicitScene", flags);
            pageRequirementField = type.GetField("pageRequirement", flags);
            if (useRerollField == null || useNextSceneField == null || useExplicitSceneField == null
                || explicitSceneField == null || pageRequirementField == null)
            {
                WhereToDiePlugin.Log.LogError("CursedBookSacrificeZone fields not found; the aurora may show zones that don't work.");
            }

            On.RoR2.DLC4SacrificeZoneSpawner.OnPrePopulateSceneServer += DLC4SacrificeZoneSpawner_OnPrePopulateSceneServer;
            RoR2Application.onFixedUpdate += OnFixedUpdate;
            Stage.onStageStartGlobal += _ => stageWideZones.RemoveWhere(zone => !zone);
            Run.onRunDestroyGlobal += _ => SetMarkers(new List<ZoneMarker>());
        }

        private static void DLC4SacrificeZoneSpawner_OnPrePopulateSceneServer(On.RoR2.DLC4SacrificeZoneSpawner.orig_OnPrePopulateSceneServer orig, DLC4SacrificeZoneSpawner self, SceneDirector director)
        {
            var before = new HashSet<CursedBookSacrificeZone>(CursedBookSacrificeZone.instanceList);
            orig(self, director);
            foreach (var zone in CursedBookSacrificeZone.instanceList)
            {
                if (zone && !before.Contains(zone))
                {
                    stageWideZones.Add(zone);
                }
            }
        }

        private static void OnFixedUpdate()
        {
            if (!NetworkServer.active || !Run.instance)
            {
                return;
            }
            pollTimer -= Time.fixedDeltaTime;
            if (pollTimer > 0f)
            {
                return;
            }
            pollTimer = PollInterval;
            SetMarkers(ComputeMarkers());
        }

        // Called with the host's markers, or with what a modded client received from the host.
        internal static void SetMarkers(List<ZoneMarker> markers)
        {
            if (markers.Count == Markers.Count)
            {
                bool same = true;
                for (int i = 0; i < markers.Count && same; i++)
                {
                    same = markers[i].Equals(Markers[i]);
                }
                if (same)
                {
                    return;
                }
            }
            Markers.Clear();
            Markers.AddRange(markers);
            if (NetworkServer.active)
            {
                ZoneNetwork.BroadcastMarkers();
            }
            onMarkersChanged?.Invoke();
        }

        private static List<ZoneMarker> ComputeMarkers()
        {
            var markers = new List<ZoneMarker>();
            foreach (var zone in CursedBookSacrificeZone.instanceList)
            {
                if (IsActiveServer(zone) && TryMakeMarker(zone, out var marker))
                {
                    AddOrMerge(markers, marker);
                }
            }
            return markers;
        }

        // Overlapping zones are one place to the player: show a single aurora, red if any of them kills.
        private static void AddOrMerge(List<ZoneMarker> markers, ZoneMarker marker)
        {
            if (!marker.StageWide)
            {
                for (int i = 0; i < markers.Count; i++)
                {
                    var other = markers[i];
                    if (other.StageWide || !(other.Contains(marker.Position) || marker.Contains(other.Position)))
                    {
                        continue;
                    }
                    // Keep the larger zone's shape, so the aurora covers both.
                    var merged = other.Radius >= marker.Radius ? other : marker;
                    merged.Dies = other.Dies || marker.Dies;
                    markers[i] = merged;
                    return;
                }
            }
            markers.Add(marker);
        }

        // For wheretodie_status: every sacrifice zone the host knows, and where it leads.
        internal static IEnumerable<string> DescribeZones()
        {
            foreach (var zone in CursedBookSacrificeZone.instanceList)
            {
                if (!zone)
                {
                    continue;
                }
                string destination = "?";
                if (useRerollField != null && (bool)useRerollField.GetValue(zone))
                {
                    destination = "reroll";
                }
                else if (useNextSceneField != null && (bool)useNextSceneField.GetValue(zone))
                {
                    destination = "next stage";
                }
                else if (explicitSceneField?.GetValue(zone) is SceneDef scene && scene)
                {
                    destination = scene.cachedName;
                }
                yield return $"{zone.name} at {zone.transform.position} behaviour={zone.behaviour} active={IsActiveServer(zone)} "
                    + $"pages={RequiredPageCount(zone)} -> {destination}";
            }
        }

        private static bool TryMakeMarker(CursedBookSacrificeZone zone, out ZoneMarker marker)
        {
            marker = new ZoneMarker
            {
                Position = zone.transform.position,
                Dies = zone.behaviour == CursedBookSacrificeZone.Behaviour.KillAndLoad,
            };

            if (zone.TryGetComponent<SphereZone>(out var sphere))
            {
                marker.Radius = sphere.radius;
                marker.StageWide = sphere.isInverted || sphere.radius >= StageWideRadius;
            }
            else if (zone.TryGetComponent<VerticalTubeZone>(out var tube))
            {
                marker.Radius = tube.radius;
                marker.Tube = true;
            }
            else if (zone.TryGetComponent<CustomZone>(out var custom))
            {
                marker.Position = custom.Center;
                marker.Radius = custom.radius;
                marker.Tube = !float.IsNegativeInfinity(custom.floorY);
            }
            else if (zone.TryGetComponent<BoxZone>(out _))
            {
                var halfSize = zone.transform.lossyScale * 0.5f;
                marker.Radius = new Vector2(halfSize.x, halfSize.z).magnitude;
            }
            else
            {
                return false;
            }

            if (stageWideZones.Contains(zone))
            {
                marker.StageWide = true;
            }
            if (marker.StageWide && Stage.instance && Stage.instance.entryTime.timeSince > StageWideDuration)
            {
                return false;
            }
            return true;
        }

        // CursedBookSacrificeZone.IsValidForPlayer without the position check and without rolling the RNG.
        internal static bool IsActiveServer(CursedBookSacrificeZone zone)
        {
            if (!zone || !zone.gameObject.activeInHierarchy || zone.isDisabled || !zone.runFlagConditionResult)
            {
                return false;
            }
            if (zone.GetComponent<IZone>() == null)
            {
                return false;
            }
            if (zone.behaviour == CursedBookSacrificeZone.Behaviour.KillAndLoad && !HasDestination(zone))
            {
                return false;
            }
            return Util.GetTotalPageCountFromTeam(TeamIndex.Player, playerControlledOnly: true, includeConsumedPages: true)
                >= RequiredPageCount(zone);
        }

        private static bool HasDestination(CursedBookSacrificeZone zone)
        {
            if (useRerollField == null)
            {
                return true;
            }
            if ((bool)useRerollField.GetValue(zone))
            {
                // Another scene of the same stage tier exists on every normal stage; vanilla only spawns the zone there.
                return true;
            }
            if ((bool)useNextSceneField.GetValue(zone))
            {
                return Run.instance.nextStageScene;
            }
            if (!(bool)useExplicitSceneField.GetValue(zone))
            {
                // Weighted scene collection: assume it has something to offer.
                return true;
            }
            var scene = explicitSceneField.GetValue(zone) as SceneDef;
            return scene && (!scene.requiredExpansion || Run.instance.IsExpansionEnabled(scene.requiredExpansion));
        }

        private static int RequiredPageCount(CursedBookSacrificeZone zone)
        {
            if (pageRequirementField == null)
            {
                return 0;
            }
            switch ((CursedBookSacrificeZone.PageRequirements)pageRequirementField.GetValue(zone))
            {
                case CursedBookSacrificeZone.PageRequirements.ChampRushMinimum:
                    return Util.ScalePageRequirement(3);
                case CursedBookSacrificeZone.PageRequirements.ProvidenceMinimum:
                    return Util.ScalePageRequirement(6);
                default:
                    return 0;
            }
        }

        // Whether the ritual would kill at this position, or null when no zone there works.
        // Host: the real zones. Clients: the zones the game syncs, then the host's markers (for zones only the host has).
        internal static bool? DiesAt(Vector3 position)
        {
            foreach (var zone in CursedBookSacrificeZone.instanceList)
            {
                if (!zone || !zone.gameObject.activeInHierarchy || zone.isDisabled || !zone.runFlagConditionResult)
                {
                    continue;
                }
                if (NetworkServer.active && !IsActiveServer(zone))
                {
                    continue;
                }
                var bounds = zone.GetComponent<IZone>();
                if (bounds != null && bounds.IsInBounds(position))
                {
                    return zone.behaviour == CursedBookSacrificeZone.Behaviour.KillAndLoad;
                }
            }
            foreach (var marker in Markers)
            {
                if (marker.Contains(position))
                {
                    return marker.Dies;
                }
            }
            return null;
        }
    }
}
