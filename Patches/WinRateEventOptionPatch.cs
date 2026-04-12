using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.addons.mega_text;
using PathPlanner.Stats;

namespace PathPlanner.Patches;

[HarmonyPatch(typeof(NEventOptionButton), "_Ready")]
public static class WinRateEventOptionPatch
{
    private static void Postfix(NEventOptionButton __instance)
    {
        try
        {
            if (__instance.HasMeta("pathplanner_winrate"))
                return;
            __instance.SetMeta("pathplanner_winrate", true);

            var option = __instance.Option;
            var label = __instance.GetNodeOrNull<MegaRichTextLabel>("%Text");
            if (label == null)
                return;

            RunHistoryWinRateAggregator.EnsureFresh();
            var line = WinRateUiHelper.EventLine(option.HistoryName);
            label.Text += $"\n{line}";
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] WinRateEventOptionPatch: {ex}");
        }
    }
}
