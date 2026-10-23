using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace WhereToDie
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(RiskOfOptionsCompat.PluginGUID, BepInDependency.DependencyFlags.SoftDependency)]
    public class WhereToDiePlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.elevated.WhereToDie";
        public const string PluginName = "WhereToDie";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;

        // Die or Not
        internal static ConfigEntry<bool> PageIconHighlight;
        internal static ConfigEntry<bool> HallowedAurora;

        // Quality (host)
        internal static ConfigEntry<bool> GivePageWithBook;
        internal static ConfigEntry<bool> RefreshPageInThreshold;

        // Space and Time (host)
        internal static ConfigEntry<bool> RocketshipRoof;
        internal static ConfigEntry<bool> PlanetariumFinale;
        internal static ConfigEntry<bool> ThresholdReentry;
        internal static ConfigEntry<bool> FinaleToObliteration;

        public void Awake()
        {
            Log = Logger;

            PageIconHighlight = Config.Bind("Die or Not", "Page icon highlight", true,
                "Highlights the Reincarnation page above the N'tormat while you stand where it works: "
                + "green-yellow if you survive the ritual, green-red if it kills you.");
            HallowedAurora = Config.Bind("Die or Not", "Hallowed aurora", true,
                "Shows a hallowed aurora above every place where Reincarnation works while you hold an unused page: "
                + "yellow if you survive the ritual, red if it kills you. The first 30 seconds of a normal stage "
                + "light up the whole stage with a sparser aurora. Needs the host to have the mod.");

            GivePageWithBook = Config.Bind("Quality", "Give Reincarnation with the N’tormat", true,
                "Host only. Picking up the N'tormat also gives the Page of Reincarnation, if you don't have it yet.");
            RefreshPageInThreshold = Config.Bind("Quality", "Refresh Reincarnation in the Soulbound Threshold", true,
                "Host only. Arriving in the Soulbound Threshold recharges a used Page of Reincarnation, "
                + "so one page is enough to continue to Terminal Lucidity.");

            RocketshipRoof = Config.Bind("Space and Time", "Rocketship roof", true,
                "Host only. After Mithrix is defeated, dying by Reincarnation on top of the rocketship on the Moon "
                + "takes you to the Soulbound Threshold.");
            PlanetariumFinale = Config.Bind("Space and Time", "Planetarium final arena", true,
                "Host only. After Voidling is defeated, dying by Reincarnation in its final arena "
                + "takes you to the Soulbound Threshold.");
            ThresholdReentry = Config.Bind("Space and Time", "Re-enter the Soulbound Threshold", true,
                "Host only. If you defeated the Anima Harvester but never entered Terminal Lucidity, "
                + "the Shrine of Descent and the soul-binding trial come back so you can enter the Soulbound Threshold again.");

            FinaleToObliteration = Config.Bind("Space and Time", "No way back after the finale", true,
                "Host only. After leaving the Moon or the Planetarium through Reincarnation, the Soulbound Threshold and "
                + "Terminal Lucidity offer a portal to Obliteration instead of their way back to the run, "
                + "and Obliteration has no way back either.");

            ZoneNetwork.Init();
            ZoneTracker.Init();
            ExtraZones.Init();
            FinalePath.Init();
            PageFixes.Init();
            AuroraRenderer.Init();
            PageIconHighlighter.Init();

            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(RiskOfOptionsCompat.PluginGUID))
            {
                RiskOfOptionsCompat.Init(System.IO.Path.GetDirectoryName(Info.Location));
            }
        }
    }
}
