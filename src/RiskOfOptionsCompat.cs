using System.IO;
using System.Runtime.CompilerServices;
using RiskOfOptions;
using RiskOfOptions.Options;
using UnityEngine;

namespace WhereToDie
{
    // Only touched when RiskOfOptions is installed, so its types are never loaded otherwise.
    internal static class RiskOfOptionsCompat
    {
        internal const string PluginGUID = "com.rune580.riskofoptions";

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        internal static void Init(string pluginDirectory)
        {
            const string guid = WhereToDiePlugin.PluginGUID;
            const string name = "Where To Die";

            ModSettingsManager.SetModDescription(
                "Quality of life for the Page of Reincarnation (N'tormat). Shows where the page works and whether "
                + "the ritual kills you, gives the page with the N'tormat, recharges it in the Soulbound Threshold "
                + "and adds new places to use it. Options marked \"Host only\" only matter when you host.",
                guid, name);

            var icon = LoadIcon(Path.Combine(pluginDirectory, "icon.png"));
            if (icon)
            {
                ModSettingsManager.SetModIcon(icon, guid, name);
            }

            ModSettingsManager.AddOption(new CheckBoxOption(WhereToDiePlugin.PageIconHighlight), guid, name);
            ModSettingsManager.AddOption(new CheckBoxOption(WhereToDiePlugin.HallowedAurora), guid, name);

            ModSettingsManager.AddOption(new CheckBoxOption(WhereToDiePlugin.GivePageWithBook), guid, name);
            ModSettingsManager.AddOption(new CheckBoxOption(WhereToDiePlugin.RefreshPageInThreshold), guid, name);

            ModSettingsManager.AddOption(new CheckBoxOption(WhereToDiePlugin.RocketshipRoof), guid, name);
            ModSettingsManager.AddOption(new CheckBoxOption(WhereToDiePlugin.PlanetariumFinale), guid, name);
            ModSettingsManager.AddOption(new CheckBoxOption(WhereToDiePlugin.ThresholdReentry), guid, name);
            ModSettingsManager.AddOption(new CheckBoxOption(WhereToDiePlugin.FinaleToObliteration), guid, name);
        }

        private static Sprite LoadIcon(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                return null;
            }
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }
    }
}
