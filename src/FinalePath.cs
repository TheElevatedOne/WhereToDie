using System.Reflection;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace WhereToDie
{
    // Host side: once the team leaves the Moon or the Planetarium through Reincarnation, Mithrix or Voidling is beaten and
    // the run must not go back to normal stages. The Soulbound Threshold and Terminal Lucidity then offer a portal to
    // Obliteration (A Moment, Fractured) instead of their way back, and nothing leaves Obliteration except the Obelisk.
    internal static class FinalePath
    {
        // Run flag, set when one of ExtraZones' rituals takes the team to the Soulbound Threshold.
        internal const string Flag = "WhereToDie_FinalePath";
        internal const string ObliterationScene = "mysteryspace";
        private const string ObliterationPortalAddress = "RoR2/Base/PortalMS/iscMSPortal.asset";
        private const string ObliterationPortalToken = "PORTAL_MS_OPEN";

        private static InteractableSpawnCard obliterationPortal;
        private static FieldInfo portalSpawnCardField;
        private static FieldInfo spawnMessageTokenField;

        internal static void Init()
        {
            Addressables.LoadAssetAsync<InteractableSpawnCard>(ObliterationPortalAddress).Completed += handle =>
            {
                obliterationPortal = handle.Result;
                if (!obliterationPortal)
                {
                    WhereToDiePlugin.Log.LogError($"{ObliterationPortalAddress} not found; the finale path can't offer Obliteration.");
                }
            };

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            portalSpawnCardField = typeof(PortalSpawner).GetField("portalSpawnCard", flags);
            spawnMessageTokenField = typeof(PortalSpawner).GetField("spawnMessageToken", flags);

            On.RoR2.PortalSpawner.AttemptSpawnPortalServer += PortalSpawner_AttemptSpawnPortalServer;
            On.EntityStates.Missions.ProvidenceBossEncounter.EncounterFinished.OnEnter += EncounterFinished_OnEnter;
            On.RoR2.Run.AdvanceStage += Run_AdvanceStage;
            On.EntityStates.Interactables.MSObelisk.EndingGame.DoFinalAction += EndingGame_DoFinalAction;
            Stage.onServerStageBegin += OnServerStageBegin;
        }

        internal static bool IsActive()
        {
            return WhereToDiePlugin.FinaleToObliteration.Value && NetworkServer.active && Run.instance && Run.instance.GetEventFlag(Flag);
        }

        internal static void Begin()
        {
            if (Run.instance && NetworkServer.active)
            {
                Run.instance.SetEventFlag(Flag);
            }
        }

        // Soulbound Threshold: the portal that appears after the Anima Harvester leads back to the run. Spawn the
        // Obliteration portal in its place.
        private static bool PortalSpawner_AttemptSpawnPortalServer(On.RoR2.PortalSpawner.orig_AttemptSpawnPortalServer orig, PortalSpawner self)
        {
            if (!IsActive() || !obliterationPortal || PageFixes.CurrentSceneName() != PageFixes.ThresholdScene
                || portalSpawnCardField == null || spawnMessageTokenField == null)
            {
                return orig(self);
            }
            var card = portalSpawnCardField.GetValue(self);
            var token = spawnMessageTokenField.GetValue(self);
            portalSpawnCardField.SetValue(self, obliterationPortal);
            spawnMessageTokenField.SetValue(self, ObliterationPortalToken);
            try
            {
                bool spawned = orig(self);
                WhereToDiePlugin.Log.LogInfo($"Finale path: Obliteration portal instead of the way back (spawned={spawned}).");
                return spawned;
            }
            finally
            {
                portalSpawnCardField.SetValue(self, card);
                spawnMessageTokenField.SetValue(self, token);
            }
        }

        // Terminal Lucidity: after Providence the game puts a teleporter back to the run at "FinalTeleporter".
        private static void EncounterFinished_OnEnter(On.EntityStates.Missions.ProvidenceBossEncounter.EncounterFinished.orig_OnEnter orig, EntityStates.Missions.ProvidenceBossEncounter.EncounterFinished self)
        {
            bool replace = IsActive() && obliterationPortal;
            if (replace)
            {
                self.teleporterCard = obliterationPortal;
                Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = ObliterationPortalToken });
                WhereToDiePlugin.Log.LogInfo("Finale path: Obliteration portal instead of the final teleporter.");
            }
            orig(self);
        }

        // Safety net for every other way out (the Threshold's exit on death, other mods' portals, console commands):
        // from the Threshold or Terminal Lucidity only Terminal Lucidity or Obliteration, and nothing out of Obliteration.
        private static void Run_AdvanceStage(On.RoR2.Run.orig_AdvanceStage orig, Run self, SceneDef nextScene)
        {
            if (IsActive())
            {
                string current = PageFixes.CurrentSceneName();
                string next = nextScene ? nextScene.cachedName : "";
                if (current == ObliterationScene)
                {
                    WhereToDiePlugin.Log.LogInfo($"Finale path: blocked leaving Obliteration for '{next}'.");
                    return;
                }
                if ((current == PageFixes.ThresholdScene || current == PageFixes.LucidityScene)
                    && next != PageFixes.LucidityScene && next != ObliterationScene)
                {
                    var obliteration = SceneCatalog.FindSceneDef(ObliterationScene);
                    if (obliteration)
                    {
                        WhereToDiePlugin.Log.LogInfo($"Finale path: '{next}' replaced with Obliteration.");
                        nextScene = obliteration;
                    }
                }
            }
            orig(self, nextScene);
        }

        // Obliteration: with Beads of Fealty the Obelisk sends the run to A Moment, Whole instead of ending it, and from
        // there Reincarnation leads back to the Moon. On the finale path obliterating always ends the run.
        private static void EndingGame_DoFinalAction(On.EntityStates.Interactables.MSObelisk.EndingGame.orig_DoFinalAction orig, EntityStates.Interactables.MSObelisk.EndingGame self)
        {
            if (!IsActive())
            {
                orig(self);
                return;
            }
            WhereToDiePlugin.Log.LogInfo("Finale path: obliterated; Beads of Fealty don't lead to A Moment, Whole.");
            Run.instance.BeginGameOver(RoR2Content.GameEndings.ObliterationEnding);
            self.outer.SetNextState(new EntityStates.Idle());
        }

        // Obliteration: no Reincarnation zone may lead anywhere.
        private static void OnServerStageBegin(Stage stage)
        {
            if (!IsActive() || PageFixes.CurrentSceneName() != ObliterationScene)
            {
                return;
            }
            foreach (var zone in CursedBookSacrificeZone.instanceList)
            {
                if (zone)
                {
                    zone.SetDisabled(true);
                }
            }
        }
    }
}
