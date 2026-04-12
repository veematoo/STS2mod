using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using PathPlanner.UI;

namespace PathPlanner.Patches;

[HarmonyPatch(typeof(NCombatRoom), "_Ready")]
public static class PartnerHandPatch
{
    static void Postfix(NCombatRoom __instance)
    {
        try
        {
            if (__instance.Mode != CombatRoomMode.ActiveCombat)
                return;

            PartnerHandOverlay.Attach(__instance);
        }
        catch (System.Exception ex)
        {
            Log.Error($"[PathPlanner] PartnerHandPatch: {ex}");
        }
    }
}
