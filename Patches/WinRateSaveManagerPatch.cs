using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Saves;
using PathPlanner.Stats;

namespace PathPlanner.Patches;

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRunHistory))]
public static class WinRateSaveManagerPatch
{
    private static void Postfix()
    {
        try
        {
            RunHistoryWinRateAggregator.InvalidateCache();
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] WinRateSaveManagerPatch: {ex}");
        }
    }
}
