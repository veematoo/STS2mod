using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Rewards;
using PathPlanner.Stats;

namespace PathPlanner.Patches;

[HarmonyPatch(typeof(NRewardButton), "Reload")]
public static class WinRateRelicRewardPatch
{
    private static void Postfix(NRewardButton __instance)
    {
        try
        {
            if (__instance.Reward is not RelicReward relicReward)
                return;

            var relic = Traverse.Create(relicReward).Field<RelicModel?>("_relic").Value;
            var id = relic?.Id;
            RunHistoryWinRateAggregator.EnsureFresh();
            var line = WinRateUiHelper.RelicLine(id);
            WinRateUiHelper.AttachBottomLine(__instance, line);
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] WinRateRelicRewardPatch: {ex}");
        }
    }
}
