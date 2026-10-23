using RoR2;
using UnityEngine.Networking;

namespace WhereToDie
{
    // Host-side fixes around the Page of Reincarnation ("PageDie") and the Soulbound Threshold.
    internal static class PageFixes
    {
        internal const string ThresholdScene = "champrush";
        internal const string LucidityScene = "ultima";

        // Our own run flag, set when anyone enters Terminal Lucidity.
        internal const string LucidityEnteredFlag = "WhereToDie_UltimaEntered";
        // Set by the game when Providence is beaten in Terminal Lucidity.
        private const string LucidityCompletedFlag = "ULTIMA_COMPLETED";
        // Set by the game when the Anima Harvester dies.
        private const string ReaperDefeatedFlag = "ReaperBossDefeated";

        internal static void Init()
        {
            On.RoR2.KeyItemDef.AttemptGrant += KeyItemDef_AttemptGrant;
            Stage.onServerStageBegin += OnServerStageBegin;
        }

        internal static bool HasEnteredLucidity()
        {
            var run = Run.instance;
            return run && (run.GetEventFlag(LucidityEnteredFlag) || run.GetEventFlag(LucidityCompletedFlag));
        }

        internal static string CurrentSceneName()
        {
            var scene = SceneCatalog.GetSceneDefForCurrentScene();
            return scene ? scene.cachedName : "";
        }

        #region N'tormat gives Reincarnation

        // The N'tormat pickup only grants the book itself. Give the Page of Reincarnation along with it.
        // Every key item pickup (including the N'tormat's "choose a page" pickup) is granted through KeyItemDef.AttemptGrant.
        private static void KeyItemDef_AttemptGrant(On.RoR2.KeyItemDef.orig_AttemptGrant orig, ref PickupDef.GrantContext context)
        {
            var inventory = context.body ? context.body.inventory : null;
            var before = inventory ? inventory.keyItemIndex : KeyItemIndex.None;
            orig(ref context);

            var book = DLC4Content.KeyItems.CursedBook;
            if (!NetworkServer.active || !WhereToDiePlugin.GivePageWithBook.Value || !inventory || !book
                || before == book.keyItemIndex || inventory.keyItemIndex != book.keyItemIndex)
            {
                return;
            }
            var master = context.body.master;
            if (!master || !master.playerCharacterMasterController)
            {
                return;
            }
            if (inventory.GetItemCountEffective(DLC4Content.Items.PageDie) > 0 || inventory.GetItemCountEffective(DLC4Content.Items.PageDieConsumed) > 0)
            {
                return;
            }
            inventory.GiveItemPermanent(DLC4Content.Items.PageDie);
            WhereToDiePlugin.Log.LogInfo($"Gave the Page of Reincarnation to {master.playerCharacterMasterController.GetDisplayName()} with the N'tormat.");
        }

        #endregion

        private static void OnServerStageBegin(Stage stage)
        {
            if (!NetworkServer.active || !Run.instance)
            {
                return;
            }
            string scene = CurrentSceneName();

            if (scene == LucidityScene)
            {
                Run.instance.SetEventFlag(LucidityEnteredFlag);
            }
            else if (scene == ThresholdScene)
            {
                if (WhereToDiePlugin.RefreshPageInThreshold.Value)
                {
                    RefreshReincarnation();
                }
            }
            else if (WhereToDiePlugin.ThresholdReentry.Value)
            {
                AllowThresholdReentry();
            }
        }

        #region Refresh in the Soulbound Threshold

        // A sacrifice that changes the scene gives PageDieCooldown, which makes the next stage skip the page's
        // recharge. The page used to reach the Soulbound Threshold would then still be spent there, and opening
        // the Veil of Memory to Terminal Lucidity needs a second page. Recharge it on arrival instead.
        private static void RefreshReincarnation()
        {
            var spell = DLC4Content.CursedBookSpells.Die;
            if (!spell)
            {
                return;
            }
            foreach (var player in PlayerCharacterMasterController.instances)
            {
                var inventory = player && player.master ? player.master.inventory : null;
                if (!inventory)
                {
                    continue;
                }
                if (spell.cooldownItemDef)
                {
                    int cooldowns = inventory.GetItemCountPermanent(spell.cooldownItemDef);
                    if (cooldowns > 0)
                    {
                        inventory.RemoveItemPermanent(spell.cooldownItemDef, cooldowns);
                    }
                }
                if (inventory.GetItemCountEffective(spell.consumedItemDef) <= 0)
                {
                    continue;
                }
                // Same transformation the game uses for the start-of-stage recharge.
                var transformation = new Inventory.ItemTransformation
                {
                    originalItemIndex = spell.consumedItemDef.itemIndex,
                    newItemIndex = spell.itemDef.itemIndex,
                    maxToTransform = int.MaxValue,
                    transformationType = 0,
                };
                if (transformation.TryTransform(inventory, out _))
                {
                    WhereToDiePlugin.Log.LogInfo($"Recharged the Page of Reincarnation of {player.GetDisplayName()} in the Soulbound Threshold.");
                }
            }
        }

        #endregion

        #region Soulbound Threshold re-entry

        // Defeating the Anima Harvester switches off every catastrophe (DLC4CatastropheProvider.ShouldProvideAnyCatastrophe),
        // and the soul-binding trial only ever happens once per run. Undo both while Terminal Lucidity hasn't been entered,
        // so the Shrine of Descent can bring the team back to the Soulbound Threshold.
        private static void AllowThresholdReentry()
        {
            var run = Run.instance;
            if (HasEnteredLucidity())
            {
                return;
            }
            var progression = DLC4ProgressionController.instance;
            bool reaperDefeated = run.GetEventFlag(ReaperDefeatedFlag) || (progression && progression.ReaperBossDefeated);
            if (!reaperDefeated)
            {
                return;
            }

            run.ResetEventFlag(ReaperDefeatedFlag);
            if (progression)
            {
                progression.NetworkreaperBossDefeated = false;
                var catastrophes = progression.catastropheProvider;
                if (catastrophes)
                {
                    catastrophes.NetworkhasSpawnedMajorCatastrophe = false;
                    catastrophes.NetworkshouldDoMajorCatastrophe = false;
                }
            }
            WhereToDiePlugin.Log.LogInfo("Terminal Lucidity not entered yet: the Soulbound Threshold can be entered again.");
        }

        #endregion

        internal static string StatusText()
        {
            if (!NetworkServer.active || !Run.instance)
            {
                return "";
            }
            var progression = DLC4ProgressionController.instance;
            return $"scene={CurrentSceneName()} lucidityEntered={HasEnteredLucidity()} "
                + $"reaperDefeated={(progression ? progression.ReaperBossDefeated : Run.instance.GetEventFlag(ReaperDefeatedFlag))}";
        }
    }
}
