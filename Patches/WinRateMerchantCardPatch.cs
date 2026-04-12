using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using PathPlanner.Stats;

namespace PathPlanner.Patches;

/// <summary>
/// Shop cards are driven from <see cref="NMercantSlot"/> + <c>MerchantCardEntry</c>; <see cref="NMercantCard"/> often
/// does not expose <see cref="CardModel"/> until the slot binds. We patch slots and retry with several deferred frames.
/// </summary>
internal static class MerchantShopWinRate
{
    private const int DeferredChainDepth = 6;

    /// <summary>
    /// When true, draws <c>1</c>…<c>5</c> on: root → <see cref="NMercantCard"/> → <see cref="NCard"/> → <c>%Frame</c> → <c>%CardContainer</c>.
    /// Probe run: <c>%Frame</c> (digit 4) matches bottom-of-artwork placement — production attaches the line there (see <c>FindCardFrameHost</c>).
    /// </summary>
    internal static bool LayerProbeMode = false;

    internal static void ScheduleAttachMerchantCard(NMerchantCard card)
    {
        if (!GodotObject.IsInstanceValid(card))
            return;
        ChainDeferred(card, DeferredChainDepth);
    }

    internal static void ScheduleAttachMerchantSlot(Node slot)
    {
        if (!GodotObject.IsInstanceValid(slot))
            return;
        if (IsMerchantCardOfferSlot(slot))
        {
            ChainDeferred(slot, DeferredChainDepth);
            return;
        }

        // Relic/removal/potion rows also use NMercantSlot; Entry may be null on _Ready — always defer (retries inside TryAttachFromSlotCore).
        MerchantShopRelicWinRate.ScheduleAttachFromSlotUntyped(slot);
    }

    private static void ChainDeferred(object anchor, int depth)
    {
        if (!GodotObject.IsInstanceValid(anchor as GodotObject))
            return;
        if (depth <= 0)
        {
            TryAttachCore(anchor);
            return;
        }

        Callable.From(() => ChainDeferred(anchor, depth - 1)).CallDeferred();
    }

    private static void TryAttachCore(object anchor)
    {
        try
        {
            if (anchor is Node rootProbe && GodotObject.IsInstanceValid(rootProbe))
                WinRateUiHelper.RemoveLayerProbesUnder(rootProbe);

            if (LayerProbeMode)
            {
                var hosts = CollectProbeHosts(anchor);
                if (hosts.Count > 0)
                    WinRateUiHelper.AttachLayerProbeNumbers(hosts);
                return;
            }

            if (anchor is not Node node || !GodotObject.IsInstanceValid(node))
                return;

            var ncard = FindDescendantOfType<NCard>(node);
            CardModel? model = null;
            switch (anchor)
            {
                case NMerchantCard mc:
                    model = TryGetMerchantCardModel(mc) ?? TryGetCardModelFromNCard(ncard);
                    break;
                case NMerchantSlot:
                    model = GetCardModelFromSlot(node) ?? TryGetCardModelFromNCard(ncard);
                    break;
            }

            var resolvedId = model != null ? model.Id : ModelId.none;
            if (resolvedId == ModelId.none
                && TryGetModelIdFromAnchor(node, ncard) is { } fid
                && fid != ModelId.none)
                resolvedId = fid;

            if (resolvedId == ModelId.none)
            {
                Log.Info($"[PathPlanner] MerchantShopWinRate: no CardModel/ModelId for {node.GetType().Name}");
                return;
            }

            if (ncard == null || !GodotObject.IsInstanceValid(ncard))
            {
                Log.Info($"[PathPlanner] MerchantShopWinRate: no NCard under {node.GetType().Name}");
                return;
            }

            var show = model != null
                ? WinRateUiHelper.ShouldShowCardWinRatePill(model)
                : WinRateUiHelper.ShouldShowCardWinRatePill(resolvedId);
            if (!show)
                return;

            RunHistoryWinRateAggregator.EnsureFresh();
            var line = model != null
                ? WinRateUiHelper.CardLine(model)
                : WinRateUiHelper.CardLineFromModelId(resolvedId);
            WinRateUiHelper.AttachCardWinRateStripToMerchantCard(ncard, line);
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] MerchantShopWinRate: {ex}");
        }
    }

    private static ModelId? TryGetModelIdFromAnchor(Node node, NCard? ncard)
    {
        object? entry = null;
        if (node is NMerchantSlot)
            entry = MerchantShopRelicWinRate.GetRawEntryFromSlotObject(node);
        else
        {
            for (Node? p = node.GetParent(); p != null; p = p.GetParent())
            {
                if (p is NMerchantSlot)
                {
                    entry = MerchantShopRelicWinRate.GetRawEntryFromSlotObject(p);
                    break;
                }
            }
        }

        var fromEntry = TryGetModelIdFromObject(entry);
        if (fromEntry != null && fromEntry != ModelId.none)
            return fromEntry;
        return TryGetModelIdFromNCard(ncard);
    }

    private static ModelId? TryGetModelIdFromObject(object? obj)
    {
        if (obj == null)
            return null;
        if (ScanForCardModel(obj) is { } cm)
            return cm.Id;
        var t = obj.GetType();
        foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (prop.GetValue(obj) is ModelId mid && mid != ModelId.none)
                return mid;
        }

        foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.GetValue(obj) is ModelId mid && mid != ModelId.none)
                return mid;
        }

        foreach (var pn in new[] { "Card", "CardModel", "OfferedCard", "DisplayedCard", "_card", "_cardModel" })
        {
            var p = t.GetProperty(pn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var v = p?.GetValue(obj);
            if (v is CardModel cm2)
                return cm2.Id;
            var f = t.GetField(pn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            v = f?.GetValue(obj);
            if (v is CardModel cm3)
                return cm3.Id;
        }

        return null;
    }

    private static ModelId? TryGetModelIdFromNCard(NCard? ncard)
    {
        if (ncard == null || !GodotObject.IsInstanceValid(ncard))
            return null;
        return TryGetModelIdFromObject(ncard);
    }

    private static CardModel? TryGetCardModelFromNCard(NCard? ncard)
    {
        if (ncard == null || !GodotObject.IsInstanceValid(ncard))
            return null;
        var t = ncard.GetType();
        foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (prop.GetValue(ncard) is CardModel cm)
                return cm;
        }

        foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.GetValue(ncard) is CardModel cm)
                return cm;
        }

        foreach (var name in new[] { "Model", "DisplayedCard", "OfferedCard", "_model", "_cardModel", "Card" })
        {
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p?.GetValue(ncard) is CardModel cm)
                return cm;
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f?.GetValue(ncard) is CardModel cm2)
                return cm2;
        }

        return null;
    }

    /// <summary>Every distinct <see cref="Control"/> under the anchor (pre-order), then ancestor wrappers — no cap.</summary>
    private static List<Control> CollectProbeHosts(object anchor)
    {
        var list = new List<Control>();
        var seen = new HashSet<ulong>();

        void TryAdd(Control? c)
        {
            if (c == null || !GodotObject.IsInstanceValid(c))
                return;
            if (!seen.Add(c.GetInstanceId()))
                return;
            list.Add(c);
        }

        void Walk(Node n)
        {
            if (!GodotObject.IsInstanceValid(n))
                return;
            if (n is Control c)
                TryAdd(c);
            foreach (var ch in n.GetChildren())
                Walk(ch);
        }

        Node? root = anchor as Node;
        if (root == null || !GodotObject.IsInstanceValid(root))
            return list;

        Walk(root);

        var mc = FindDescendantOfType<NMerchantCard>(root);
        TryAdd(mc);

        var ncard = FindDescendantOfType<NCard>(root);
        TryAdd(ncard);
        if (ncard != null && GodotObject.IsInstanceValid(ncard))
        {
            TryAdd(ncard.GetNodeOrNull<Control>("%Frame"));
            TryAdd(ncard.GetNodeOrNull<Control>("%CardContainer"));
        }

        for (Node? p = root.GetParent(); p != null; p = p.GetParent())
        {
            if (p is Control pc)
                TryAdd(pc);
        }

        return list;
    }

    private static bool IsMerchantCardOfferSlot(Node slot)
    {
        var entry = MerchantShopRelicWinRate.GetRawEntryFromSlotObject(slot);
        if (entry == null)
            return false;
        var n = entry.GetType().Name;
        return n.Contains("MerchantCard", StringComparison.OrdinalIgnoreCase)
               && !n.Contains("Removal", StringComparison.OrdinalIgnoreCase);
    }

    private static CardModel? GetCardModelFromSlot(Node slot)
    {
        var entry = MerchantShopRelicWinRate.GetRawEntryFromSlotObject(slot);
        return TryGetCardModelFromMerchantEntry(entry) ?? TryGetMerchantCardModel(FindDescendantOfType<NMerchantCard>(slot));
    }

    private static Control ResolveHostFromSlot(Node slot)
    {
        var merchantCard = FindDescendantOfType<NMerchantCard>(slot);
        if (merchantCard != null && GodotObject.IsInstanceValid(merchantCard))
            return FindCardFrameHost(merchantCard);

        var ncard = FindDescendantOfType<NCard>(slot);
        if (ncard != null && GodotObject.IsInstanceValid(ncard))
        {
            var frame = ncard.GetNodeOrNull<Control>("%Frame");
            if (frame != null && GodotObject.IsInstanceValid(frame))
                return frame;
            return ncard;
        }

        return (slot as Control ?? FindDescendantOfType<Control>(slot))!;
    }

    private static Control FindCardFrameHost(NMerchantCard merchantCard)
    {
        var ncard = FindDescendantOfType<NCard>(merchantCard);
        if (ncard != null && GodotObject.IsInstanceValid(ncard))
        {
            var frame = ncard.GetNodeOrNull<Control>("%Frame");
            if (frame != null && GodotObject.IsInstanceValid(frame))
                return frame;
            return ncard;
        }

        return merchantCard;
    }

    private static T? FindDescendantOfType<T>(Node? root) where T : Node
    {
        if (root == null)
            return null;
        if (root is T match)
            return match;
        foreach (var c in root.GetChildren())
        {
            var d = FindDescendantOfType<T>(c);
            if (d != null)
                return d;
        }

        return null;
    }

    private static CardModel? TryGetMerchantCardModel(NMerchantCard? mc)
    {
        if (mc == null || !GodotObject.IsInstanceValid(mc))
            return null;

        var direct = ScanForCardModel(mc);
        if (direct != null)
            return direct;

        for (Node? p = mc.GetParent(); p != null; p = p.GetParent())
        {
            if (p is NMerchantSlot)
                return GetCardModelFromSlot(p);
        }

        return null;
    }

    private static CardModel? ScanForCardModel(object obj)
    {
        var t = obj.GetType();
        foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (prop.GetValue(obj) is CardModel cm)
                return cm;
        }

        foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.GetValue(obj) is CardModel cm)
                return cm;
        }

        return null;
    }

    private static CardModel? TryGetCardModelFromMerchantEntry(object? entry)
    {
        if (entry == null)
            return null;
        var cm = ScanForCardModel(entry);
        if (cm != null)
            return cm;

        var et = entry.GetType();
        foreach (var pn in new[] { "Card", "CardModel", "OfferedCard", "DisplayedCard", "_card", "_cardModel" })
        {
            var p = et.GetProperty(pn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p?.GetValue(entry) is CardModel m)
                return m;
            var f = et.GetField(pn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f?.GetValue(entry) is CardModel m2)
                return m2;
        }

        return null;
    }
}

[HarmonyPatch(typeof(NMerchantCard), "_Ready")]
public static class WinRateNMercantCardReadyPatch
{
    [HarmonyPostfix]
    public static void Postfix(NMerchantCard __instance)
    {
        try
        {
            MerchantShopWinRate.ScheduleAttachMerchantCard(__instance);
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] WinRateNMercantCard _Ready: {ex}");
        }
    }
}

[HarmonyPatch(typeof(NMerchantCard), "Reload")]
public static class WinRateNMercantCardReloadPatch
{
    [HarmonyPostfix]
    public static void Postfix(NMerchantCard __instance)
    {
        MerchantShopWinRate.ScheduleAttachMerchantCard(__instance);
    }
}

[HarmonyPatch(typeof(NMerchantSlot), "_Ready")]
public static class WinRateNMercantSlotReadyPatch
{
    [HarmonyPostfix]
    public static void Postfix(NMerchantSlot __instance)
    {
        try
        {
            MerchantShopWinRate.ScheduleAttachMerchantSlot(__instance);
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] WinRateNMercantSlot _Ready: {ex}");
        }
    }
}

[HarmonyPatch(typeof(NMerchantSlot), "Reload")]
public static class WinRateNMercantSlotReloadPatch
{
    [HarmonyPostfix]
    public static void Postfix(NMerchantSlot __instance)
    {
        MerchantShopWinRate.ScheduleAttachMerchantSlot(__instance);
    }
}
