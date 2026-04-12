using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using PathPlanner.Stats;

namespace PathPlanner.Patches;

/// <summary>
/// Win-rate pills on card reward / choose-a-card overlays and merchant shop card grids.
/// </summary>
/// <remarks>
/// <see cref="NOverlayStack.Push"/> runs before the overlay's <c>_Ready</c> in the same frame, so hooks on
/// <c>AfterOverlayOpened</c> or <c>RefreshOptions</c> alone can be too early. We defer work from <c>Push</c> with
/// <see cref="Callable.From(Action).CallDeferred"/> so <c>_cardRow</c> and holders exist. <c>RefreshOptions</c> is
/// still patched for in-place rerolls.
/// </remarks>
internal static class CardWinRateSelectionApply
{
    internal static void TryApplyDeferred(Node screen)
    {
        try
        {
            RunHistoryWinRateAggregator.EnsureFresh();
            TryApplyCore(screen);
            // Relic/potion shop tiles are often not NMercantSlot; scan GetAllSlots() after card grid (see MerchantShopRelicWinRate).
            if (screen is NMerchantInventory or NMerchantRoom)
            {
                MerchantShopRelicWinRate.ScheduleScanMerchantShopRootWithFollowups(screen);
                if (MerchantShopRelicWinRate.MerchantShopSprayProbeMode)
                    MerchantShopRelicWinRate.ScheduleSprayProbeDeferred(screen);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] CardWinRateSelectionApply: {ex}");
        }
    }

    internal static void TryApplyNow(Node screen)
    {
        try
        {
            RunHistoryWinRateAggregator.EnsureFresh();
            TryApplyCore(screen);
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] CardWinRateSelectionApply: {ex}");
        }
    }

    private static void TryApplyCore(Node screen)
    {
        if (!TryEnumerateGridHolders(screen, out var row, out var holders) || holders.Count == 0 || row == null)
            return;

        var list = new List<(NGridCardHolder Holder, string Text)>(holders.Count);
        foreach (var holder in holders)
        {
            if (!GodotObject.IsInstanceValid(holder))
                continue;
            if (!WinRateUiHelper.ShouldShowCardWinRatePill(holder.CardModel))
                continue;
            list.Add((holder, WinRateUiHelper.CardLine(holder.CardModel)));
        }

        if (list.Count == 0)
            return;

        WinRateUiHelper.ScheduleCardWinRatesInRow(row, list, screen);
    }

    /// <summary>
    /// Win-rate strips are parented under each card's frame; without cleanup they survive pool reuse and appear in combat.
    /// Also invalidates deferred applies so a fast close cannot attach pills after the overlay is gone.
    /// </summary>
    internal static void CleanupSelectionScreenWinRates(Node screen)
    {
        try
        {
            if (screen is NMerchantInventory or NMerchantRoom)
                WinRateUiHelper.RemoveMerchantSprayProbe(screen);
            if (!TryEnumerateGridHolders(screen, out var row, out _) || row == null)
                return;
            WinRateUiHelper.RemoveCardWinRateLabelsFromRow(row);
            WinRateUiHelper.InvalidateCardWinRateDeferrals(screen);
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] CardWinRateSelectionApply cleanup: {ex}");
        }
    }

    private static bool TryEnumerateGridHolders(Node screen, out Control? row, out List<NGridCardHolder> holders)
    {
        holders = new List<NGridCardHolder>();
        row = null;

        // Shop uses NMerchantRoom / NMerchantInventory (see MCP: GetAllSlots), not card-reward overlays — cards can sit
        // in multiple containers; _cardRow may only cover one strip (~3 holders). Sweep the whole shop UI root.
        if (IsMerchantShopScreenRoot(screen))
        {
            WinRateUiHelper.CollectVisibleGridCardHoldersInSubtree(screen, holders);
            if (holders.Count > 0)
            {
                row = screen as Control ?? FirstControlInSubtree(screen);
                if (row != null && GodotObject.IsInstanceValid(row))
                    return true;
                holders.Clear();
            }
        }

        Control? cardRow = null;
        if (screen is NCardRewardSelectionScreen reward)
            cardRow = Traverse.Create(reward).Field<Control>("_cardRow").Value
                      ?? reward.GetNodeOrNull<Control>("UI/CardRow");
        else if (screen is NChooseACardSelectionScreen choose)
            cardRow = Traverse.Create(choose).Field<Control>("_cardRow").Value
                      ?? choose.GetNodeOrNull<Control>("CardRow");
        else
            cardRow = Traverse.Create(screen).Field<Control>("_cardRow").Value;

        // Prefer _cardRow subtree first. Scanning NCardGrid on the whole screen first can hit a different grid
        // (preview / layout) and return early so we never reach the real shop or reward row — pills vanish.
        // Multi-row / scroll layouts nest holders under _cardRow; only direct children would miss rows (old bug).
        if (cardRow != null && GodotObject.IsInstanceValid(cardRow))
        {
            WinRateUiHelper.CollectGridCardHoldersInSubtree(cardRow, holders);
            if (holders.Count > 0)
            {
                row = cardRow;
                return true;
            }
        }

        // Fallback when holders are not under _cardRow: use NCardGrid (prefer under cardRow, then full screen).
        NCardGrid? grid = null;
        if (cardRow != null && GodotObject.IsInstanceValid(cardRow))
            grid = FindFirstDescendantOfType<NCardGrid>(cardRow);
        grid ??= FindFirstDescendantOfType<NCardGrid>(screen);
        if (grid != null && GodotObject.IsInstanceValid(grid))
        {
            foreach (var h in grid.CurrentlyDisplayedCardHolders)
            {
                if (GodotObject.IsInstanceValid(h))
                    holders.Add(h);
            }

            if (holders.Count == 0)
                WinRateUiHelper.CollectGridCardHoldersInSubtree(grid, holders);
            if (holders.Count > 0)
            {
                row = grid;
                return true;
            }
        }

        foreach (var fieldName in new[] { "_cardChoices", "_cardHolders", "_holders", "_gridHolders" })
        {
            if (!TryGetInstanceFieldValue(screen, fieldName, out var raw) || raw == null)
                continue;

            foreach (var h in FlattenToGridHolders(raw))
            {
                if (GodotObject.IsInstanceValid(h))
                    holders.Add(h);
            }

            if (holders.Count == 0)
                continue;

            var parent = holders[0].GetParent();
            row = (parent as Control)
                  ?? FirstControlInSubtree(holders[0])
                  ?? (screen as Control)
                  ?? FirstControlInSubtree(screen);
            if (row == null || !GodotObject.IsInstanceValid(row))
            {
                holders.Clear();
                return false;
            }

            return true;
        }

        holders.Clear();
        return false;
    }

    private static bool IsMerchantShopScreenRoot(Node screen)
    {
        return screen is NMerchantInventory or NMerchantRoom
               || screen.GetType().Name.Contains("Merchant", StringComparison.OrdinalIgnoreCase);
    }

    private static T? FindFirstDescendantOfType<T>(Node root) where T : Node
    {
        if (root is T match)
            return match;
        foreach (var child in root.GetChildren())
        {
            var d = FindFirstDescendantOfType<T>(child);
            if (d != null)
                return d;
        }

        return null;
    }

    private static Control? FirstControlInSubtree(Node n)
    {
        if (n is Control c && GodotObject.IsInstanceValid(c))
            return c;
        foreach (var child in n.GetChildren())
        {
            if (child is Node cn)
            {
                var d = FirstControlInSubtree(cn);
                if (d != null)
                    return d;
            }
        }

        return null;
    }

    private static bool TryGetInstanceFieldValue(object obj, string fieldName, out object? value)
    {
        value = null;
        for (var t = obj.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null)
                continue;
            value = f.GetValue(obj);
            return true;
        }

        return false;
    }

    private static IEnumerable<NGridCardHolder> FlattenToGridHolders(object val)
    {
        if (val is NGridCardHolder single)
        {
            yield return single;
            yield break;
        }

        if (val is Godot.Collections.Array garr)
        {
            foreach (var item in garr)
            {
                if (item.VariantType == Variant.Type.Object && item.AsGodotObject() is NGridCardHolder h)
                    yield return h;
            }

            yield break;
        }

        if (val is IEnumerable enumerable && val is not string)
        {
            foreach (var item in enumerable)
            {
                switch (item)
                {
                    case NGridCardHolder h:
                        yield return h;
                        break;
                    case Variant v when v.VariantType == Variant.Type.Object && v.AsGodotObject() is NGridCardHolder h2:
                        yield return h2;
                        break;
                }
            }
        }
    }
}

[HarmonyPatch(typeof(NOverlayStack), nameof(NOverlayStack.Push))]
public static class WinRateCardRewardOverlayPushPatch
{
    [HarmonyPostfix]
    public static void Postfix(NOverlayStack __instance, IOverlayScreen screen)
    {
        _ = __instance;
        if (screen is not NCardRewardSelectionScreen and not NChooseACardSelectionScreen)
            return;
        var node = (Node)screen;
        Callable.From(() => CardWinRateSelectionApply.TryApplyDeferred(node)).CallDeferred();
    }
}

[HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.RefreshOptions))]
public static class WinRateNCardRewardRefreshPatch
{
    [HarmonyPostfix]
    public static void Postfix(NCardRewardSelectionScreen __instance)
    {
        CardWinRateSelectionApply.TryApplyNow(__instance);
    }
}

[HarmonyPatch(typeof(NCardRewardSelectionScreen), "_Ready")]
public static class WinRateNCardRewardReadyPatch
{
    [HarmonyPostfix]
    public static void Postfix(NCardRewardSelectionScreen __instance)
    {
        Callable.From(() => CardWinRateSelectionApply.TryApplyDeferred(__instance)).CallDeferred();
    }
}

[HarmonyPatch(typeof(NChooseACardSelectionScreen), "_Ready")]
public static class WinRateNChooseACardReadyPatch
{
    [HarmonyPostfix]
    public static void Postfix(NChooseACardSelectionScreen __instance)
    {
        Callable.From(() => CardWinRateSelectionApply.TryApplyDeferred(__instance)).CallDeferred();
    }
}

[HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.AfterOverlayShown))]
public static class WinRateNCardRewardShownPatch
{
    [HarmonyPostfix]
    public static void Postfix(NCardRewardSelectionScreen __instance)
    {
        Callable.From(() => CardWinRateSelectionApply.TryApplyDeferred(__instance)).CallDeferred();
    }
}

[HarmonyPatch(typeof(NChooseACardSelectionScreen), nameof(NChooseACardSelectionScreen.AfterOverlayShown))]
public static class WinRateNChooseACardShownPatch
{
    [HarmonyPostfix]
    public static void Postfix(NChooseACardSelectionScreen __instance)
    {
        Callable.From(() => CardWinRateSelectionApply.TryApplyDeferred(__instance)).CallDeferred();
    }
}

[HarmonyPatch(typeof(NCardRewardSelectionScreen), "_ExitTree")]
public static class WinRateNCardRewardExitTreePatch
{
    [HarmonyPostfix]
    public static void Postfix(NCardRewardSelectionScreen __instance)
    {
        CardWinRateSelectionApply.CleanupSelectionScreenWinRates(__instance);
    }
}

[HarmonyPatch(typeof(NChooseACardSelectionScreen), "_ExitTree")]
public static class WinRateNChooseACardExitTreePatch
{
    [HarmonyPostfix]
    public static void Postfix(NChooseACardSelectionScreen __instance)
    {
        CardWinRateSelectionApply.CleanupSelectionScreenWinRates(__instance);
    }
}

[HarmonyPatch(typeof(NMerchantInventory), "_Ready")]
public static class WinRateNMercantInventoryReadyPatch
{
    [HarmonyPostfix]
    public static void Postfix(NMerchantInventory __instance)
    {
        Callable.From(() => CardWinRateSelectionApply.TryApplyDeferred(__instance)).CallDeferred();
    }
}

[HarmonyPatch(typeof(NMerchantInventory), "_ExitTree")]
public static class WinRateNMercantInventoryExitTreePatch
{
    [HarmonyPostfix]
    public static void Postfix(NMerchantInventory __instance)
    {
        CardWinRateSelectionApply.CleanupSelectionScreenWinRates(__instance);
    }
}

[HarmonyPatch(typeof(NMerchantRoom), "_Ready")]
public static class WinRateNMercantRoomReadyPatch
{
    [HarmonyPostfix]
    public static void Postfix(NMerchantRoom __instance)
    {
        Callable.From(() => CardWinRateSelectionApply.TryApplyDeferred(__instance)).CallDeferred();
    }
}

[HarmonyPatch(typeof(NMerchantRoom), "_ExitTree")]
public static class WinRateNMercantRoomExitTreePatch
{
    [HarmonyPostfix]
    public static void Postfix(NMerchantRoom __instance)
    {
        CardWinRateSelectionApply.CleanupSelectionScreenWinRates(__instance);
    }
}
