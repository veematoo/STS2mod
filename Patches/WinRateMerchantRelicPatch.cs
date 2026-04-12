using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using PathPlanner.Stats;

namespace PathPlanner.Patches;

/// <summary>
/// Shop relic tiles: MCP documents <c>GetAllSlots()</c> on merchant inventory; slot nodes are not always the generic
/// <see cref="NMercantSlot"/> type — relic/potion rows often use dedicated classes, so Harmony on <see cref="NMercantSlot"/>
/// alone never runs. We also scan <see cref="NMerchantInventory"/> via reflection and attach from any <see cref="Node"/>.
/// </summary>
internal static class MerchantShopRelicWinRate
{
    private const int DeferredChainDepth = 6;
    private const int InventoryScanChainDepth = 12;

    /// <summary>Set to <c>true</c> for one shop visit to debug layer indices; normal play uses <c>false</c>.</summary>
    internal static bool RelicSlotLayerProbeMode = false;

    /// <summary>
    /// When <c>true</c>, paints <c>1</c>…<c>N</c> on <c>%CostLabel</c>, its ancestors, heuristic price row, siblings, etc.
    /// Takes priority over <see cref="RelicSlotLayerProbeMode"/>. Normal play: <c>false</c>.
    /// </summary>
    internal static bool MerchantPriceRowProbeMode = false;

    /// <summary>
    /// When <c>true</c>, paints numbered labels on sampled controls (debug). Normal play: <c>false</c> so relic win-rate strips are visible.
    /// </summary>
    internal static bool MerchantShopSprayProbeMode = false;

    private const int MaxRelicModelBindRetries = 18;
    private static readonly Dictionary<ulong, int> RelicAttachBindAttempts = new();

    internal static bool IsMerchantRelicOfferSlot(NMerchantSlot slot)
    {
        return IsMerchantRelicEntry(GetRawEntry(slot));
    }

    internal static bool IsMerchantRelicEntry(object? entry)
    {
        if (entry == null)
            return false;
        var typeName = entry.GetType().Name;
        if (typeName.Contains("Potion", StringComparison.OrdinalIgnoreCase))
            return false;
        if (TryGetRelicModelFromEntry(entry) != null)
            return true;
        if (TryGetRelicModelIdFromEntry(entry) is { } mid && mid != ModelId.none)
            return true;
        return typeName.Contains("MerchantRelic", StringComparison.OrdinalIgnoreCase)
               || (typeName.Contains("Relic", StringComparison.OrdinalIgnoreCase)
                   && typeName.Contains("Merchant", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMerchantCardOfferEntry(object? entry)
    {
        if (entry == null)
            return false;
        var n = entry.GetType().Name;
        return n.Contains("MerchantCard", StringComparison.OrdinalIgnoreCase)
               && !n.Contains("Removal", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Shop card row uses <see cref="NMercantSlot"/> + MerchantCard entry; relic row can use the same slot type.</summary>
    internal static bool IsNMerchantSlotCardOffer(NMerchantSlot? slot)
    {
        if (slot == null)
            return false;
        return IsMerchantCardOfferEntry(GetRawEntryFromSlotObject(slot));
    }

    /// <summary>
    /// Second path: MCP <c>GetAllSlots()</c> — catches relic/potion/removal tiles whose runtime type is not
    /// <see cref="NMercantSlot"/> (Harmony on that type never runs for them).
    /// </summary>
    internal static void ScheduleScanMerchantShopRoot(Node root)
    {
        if (!GodotObject.IsInstanceValid(root))
            return;
        ChainDeferredInventoryScan(root, InventoryScanChainDepth);
    }

    /// <summary>Initial scan plus two later passes so slots created after <c>_Ready</c> still get relic UI.</summary>
    internal static void ScheduleScanMerchantShopRootWithFollowups(Node root)
    {
        ScheduleScanMerchantShopRoot(root);
        if (!GodotObject.IsInstanceValid(root))
            return;
        ChainDeferredInventoryScan(root, InventoryScanChainDepth + 24);
        ChainDeferredInventoryScan(root, InventoryScanChainDepth + 48);
    }

    /// <summary>Deferred spray so layout exists; runs after same depth as first inventory scan.</summary>
    internal static void ScheduleSprayProbeDeferred(Node root)
    {
        if (!GodotObject.IsInstanceValid(root) || !MerchantShopSprayProbeMode)
            return;
        ChainDeferredSpray(root, InventoryScanChainDepth);
    }

    private static void ChainDeferredSpray(Node root, int depth)
    {
        if (!GodotObject.IsInstanceValid(root))
            return;
        if (depth <= 0)
        {
            // Relics/potions often sit under NMerchantRoom but outside NMerchantInventory; spray the full screen root.
            Node probeRoot = root;
            if (root is not NMerchantRoom && root is not NMerchantInventory)
                probeRoot = FindDescendantOfType<NMerchantInventory>(root) ?? root;
            if (GodotObject.IsInstanceValid(probeRoot))
                WinRateUiHelper.RunMerchantSprayProbe(probeRoot);
            return;
        }

        Callable.From(() => ChainDeferredSpray(root, depth - 1)).CallDeferred();
    }

    private static void ChainDeferredInventoryScan(Node root, int depth)
    {
        if (!GodotObject.IsInstanceValid(root))
            return;
        if (depth <= 0)
        {
            ScanInventorySlotsViaReflection(root);
            return;
        }

        Callable.From(() => ChainDeferredInventoryScan(root, depth - 1)).CallDeferred();
    }

    private static void ScanInventorySlotsViaReflection(Node root)
    {
        try
        {
            if (!TryScanGetAllSlotsOnNode(root) && root is not NMerchantInventory)
            {
                var inv = FindDescendantOfType<NMerchantInventory>(root);
                if (inv != null && GodotObject.IsInstanceValid(inv) && !ReferenceEquals(inv, root))
                    TryScanGetAllSlotsOnNode(inv);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] MerchantShopRelicWinRate ScanInventorySlots: {ex}");
        }
    }

    private static bool TryScanGetAllSlotsOnNode(Node root)
    {
        var processed = 0;
        var methods = new List<MethodInfo>();
        foreach (var m in EnumerateSlotEnumerableMethods(root))
            methods.Add(m);

        var hasGetAllSlots = root.GetType().GetMethod("GetAllSlots", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;

        foreach (var m in methods)
        {
            try
            {
                var result = m.Invoke(root, null);
                if (TryEnumerateNodes(result, out var nodes))
                {
                    foreach (var n in nodes)
                    {
                        if (!GodotObject.IsInstanceValid(n))
                            continue;
                        // Card grid already handles *card* NMercantSlots; relic/potion rows often use NMercantSlot too — do not skip them all.
                        if (n is NMerchantSlot mercSlot && IsNMerchantSlotCardOffer(mercSlot))
                            continue;

                        ProcessNonGenericMerchantSlot(n);
                        processed++;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Info($"[PathPlanner] MerchantShopRelicWinRate: slot invoke {m.Name} failed: {ex.Message}");
            }
        }

        if (processed == 0)
            TryFallbackScanDescendantsWithEntry(root);

        if (processed == 0)
            Log.Info(
                $"[PathPlanner] MerchantShopRelicWinRate: 0 slot tiles processed from {root.GetType().FullName}; " +
                $"GetAllSlots method present={hasGetAllSlots}; enumerable methods={methods.Count}; " +
                $"RelicProbe={RelicSlotLayerProbeMode}");

        return processed > 0;
    }

    private static IEnumerable<MethodInfo> EnumerateSlotEnumerableMethods(Node root)
    {
        var t = root.GetType();
        var seen = new HashSet<int>();
        var names = new[] { "GetAllSlots", "GetSlots", "AllSlots", "Slots", "GetMerchantSlots" };
        foreach (var name in names)
        {
            var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (m != null && seen.Add(m.MetadataToken))
                yield return m;
        }

        foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (m.GetParameters().Length != 0)
                continue;
            if (m.ReturnType == typeof(void))
                continue;
            if (!typeof(IEnumerable).IsAssignableFrom(m.ReturnType) && m.ReturnType.FullName?.Contains("Array") != true)
                continue;
            if (m.Name.Contains("Slot", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Offer", StringComparison.OrdinalIgnoreCase))
            {
                if (seen.Add(m.MetadataToken))
                    yield return m;
            }
        }
    }

    private static bool TryEnumerateNodes(object? result, out List<Node> nodes)
    {
        nodes = new List<Node>();
        if (result == null)
            return false;
        if (result is IEnumerable enumerable and not string)
        {
            foreach (var item in enumerable)
            {
                if (item is Node n && GodotObject.IsInstanceValid(n))
                    nodes.Add(n);
            }
        }

        if (nodes.Count == 0)
            TryAppendNodesFromIndexedCollection(result, nodes);

        return nodes.Count > 0;
    }

    private static void TryAppendNodesFromIndexedCollection(object result, List<Node> nodes)
    {
        try
        {
            if (result is IList list)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i] is Node n && GodotObject.IsInstanceValid(n))
                        nodes.Add(n);
                }

                return;
            }

            var t = result.GetType();
            var countProp = t.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance)
                            ?? t.GetProperty("Size", BindingFlags.Public | BindingFlags.Instance);
            if (countProp == null)
                return;
            var countObj = countProp.GetValue(result);
            if (countObj == null)
                return;
            var count = Convert.ToInt32(countObj);
            var getItem = t.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
            if (getItem == null)
                return;
            for (var i = 0; i < count; i++)
            {
                var item = getItem.Invoke(result, new object[] { i });
                if (item is Node n && GodotObject.IsInstanceValid(n))
                    nodes.Add(n);
            }
        }
        catch
        {
            // Godot array shape may differ; scan path has other fallbacks.
        }
    }

    private static void TryFallbackScanDescendantsWithEntry(Node root)
    {
        var seen = new HashSet<ulong>();
        Walk(root);

        void Walk(Node n)
        {
            if (!GodotObject.IsInstanceValid(n))
                return;
            if (GetRawEntryFromSlotObject(n) != null && seen.Add(n.GetInstanceId()))
            {
                if (!(n is NMerchantSlot merc && IsNMerchantSlotCardOffer(merc)))
                    ProcessNonGenericMerchantSlot(n);
            }
            foreach (var c in n.GetChildren())
                Walk(c);
        }
    }

    /// <summary>Relic/potion/removal tiles that are not <see cref="NMercantSlot"/> instances.</summary>
    internal static void ProcessNonGenericMerchantSlot(Node n)
    {
        if (!GodotObject.IsInstanceValid(n))
            return;
        var entry = GetRawEntryFromSlotObject(n);
        if (RelicSlotLayerProbeMode || MerchantPriceRowProbeMode)
        {
            if (IsMerchantCardOfferEntry(entry))
                return;
            ScheduleAttachFromSlotUntyped(n);
            return;
        }

        if (IsMerchantRelicEntry(entry))
            ScheduleAttachFromSlotUntyped(n);
    }

    internal static void ScheduleAttachFromSlot(NMerchantSlot slot)
    {
        if (!GodotObject.IsInstanceValid(slot))
            return;
        ChainDeferred(slot, DeferredChainDepth);
    }

    internal static void ScheduleAttachFromSlotUntyped(Node slotNode)
    {
        if (!GodotObject.IsInstanceValid(slotNode))
            return;
        ChainDeferred(slotNode, DeferredChainDepth);
    }

    private static void ChainDeferred(Node slotNode, int depth)
    {
        if (!GodotObject.IsInstanceValid(slotNode))
            return;
        if (depth <= 0)
        {
            TryAttachFromSlotCore(slotNode);
            return;
        }

        Callable.From(() => ChainDeferred(slotNode, depth - 1)).CallDeferred();
    }

    private static void TryAttachFromSlotCore(Node slotNode)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(slotNode))
                return;

            WinRateUiHelper.RemoveLayerProbesUnder(slotNode);
            WinRateUiHelper.RemoveMerchantProbeViewportLayer(slotNode);

            if (MerchantPriceRowProbeMode && slotNode is Control priceProbeHost)
            {
                var hosts = WinRateUiHelper.BuildMerchantPriceRowProbeHosts(priceProbeHost);
                if (hosts.Count > 0)
                {
                    WinRateUiHelper.AttachLayerProbeNumbers(hosts);
                    WinRateUiHelper.AttachLayerProbeNumbersMerchantViewport(hosts, slotNode);
                }
                else
                    Log.Info($"[PathPlanner] Price-row probe: no %CostLabel chain under {slotNode.GetType().Name}");
                return;
            }

            if (RelicSlotLayerProbeMode)
            {
                var hosts = CollectRelicProbeHosts(slotNode);
                if (hosts.Count > 0)
                {
                    WinRateUiHelper.AttachLayerProbeNumbers(hosts);
                    WinRateUiHelper.AttachLayerProbeNumbersMerchantViewport(hosts, slotNode);
                }
                else
                    Log.Info($"[PathPlanner] Relic probe: 0 Control nodes under {slotNode.GetType().Name}");
                return;
            }

            var entry = GetRawEntryFromSlotObject(slotNode);
            var model = TryGetRelicModelFromEntry(entry);
            var mid = model?.Id ?? TryGetRelicModelIdFromEntry(entry);

            if (mid == null || mid == ModelId.none)
            {
                if (!TryScheduleRelicBindRetry(slotNode, entry))
                    Log.Info($"[PathPlanner] MerchantShopRelicWinRate: no RelicModel/ModelId for {slotNode.GetType().Name}");
                return;
            }

            RunHistoryWinRateAggregator.EnsureFresh();
            var line = WinRateUiHelper.RelicLine(mid);
            var host = ResolveRelicStripHost(slotNode);
            if (host == null || !GodotObject.IsInstanceValid(host))
            {
                if (!TryScheduleRelicBindRetry(slotNode, entry))
                    Log.Info($"[PathPlanner] MerchantShopRelicWinRate: no Control host for strip");
                return;
            }

            if (slotNode is NMerchantSlot okSlot && GodotObject.IsInstanceValid(okSlot))
                RelicAttachBindAttempts.Remove(okSlot.GetInstanceId());

            WinRateUiHelper.AttachMerchantRelicWinRateStrip(host, line);
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] MerchantShopRelicWinRate: {ex}");
        }
    }

    /// <summary>Entry often binds after <c>_Ready</c>; retry a few times for <see cref="NMercantSlot"/> relic rows only.</summary>
    private static bool TryScheduleRelicBindRetry(Node slotNode, object? entry)
    {
        if (slotNode is not NMerchantSlot merc || !GodotObject.IsInstanceValid(merc))
            return false;
        if (IsMerchantCardOfferEntry(entry))
            return false;
        if (entry != null && !IsMerchantRelicEntry(entry))
            return false;

        var id = merc.GetInstanceId();
        RelicAttachBindAttempts.TryGetValue(id, out var n);
        if (n >= MaxRelicModelBindRetries)
            return false;
        RelicAttachBindAttempts[id] = n + 1;
        Callable.From(() => ChainDeferred(merc, 5)).CallDeferred();
        return true;
    }

    private static List<Control> CollectRelicProbeHosts(Node slotRoot)
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

        if (!GodotObject.IsInstanceValid(slotRoot))
            return list;

        Walk(slotRoot);

        for (Node? p = slotRoot.GetParent(); p != null; p = p.GetParent())
        {
            if (p is Control pc)
                TryAdd(pc);
        }

        return list;
    }

    private static Control? ResolveRelicStripHost(Node slotNode)
    {
        var nr = FindDescendantOfType<NMerchantRelic>(slotNode);
        if (nr != null && GodotObject.IsInstanceValid(nr))
            return nr;
        if (slotNode is Control c)
            return c;
        return FindDescendantOfType<Control>(slotNode);
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

    internal static object? GetRawEntry(NMerchantSlot slot)
    {
        return GetRawEntryFromSlotObject(slot);
    }

    internal static object? GetRawEntryFromSlotObject(object? slotObj)
    {
        if (slotObj == null)
            return null;
        var t = slotObj.GetType();
        foreach (var name in new[] { "Entry", "_entry", "MerchantEntry" })
        {
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                var v = p.GetValue(slotObj);
                if (v != null)
                    return v;
            }

            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var v = f.GetValue(slotObj);
                if (v != null)
                    return v;
            }
        }

        return null;
    }

    private static RelicModel? ScanForRelicModel(object obj)
    {
        var t = obj.GetType();
        foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (prop.GetValue(obj) is RelicModel rm)
                return rm;
        }

        foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.GetValue(obj) is RelicModel rm)
                return rm;
        }

        return null;
    }

    private static RelicModel? TryGetRelicModelFromEntry(object? entry)
    {
        if (entry == null)
            return null;
        var rm = ScanForRelicModel(entry);
        if (rm != null)
            return rm;

        var et = entry.GetType();
        foreach (var pn in new[] { "Relic", "RelicModel", "OfferedRelic", "DisplayedRelic", "_relic" })
        {
            var p = et.GetProperty(pn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p?.GetValue(entry) is RelicModel m)
                return m;
            var f = et.GetField(pn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f?.GetValue(entry) is RelicModel m2)
                return m2;
        }

        return null;
    }

    private static ModelId? TryGetRelicModelIdFromEntry(object? entry)
    {
        if (entry == null)
            return null;
        if (TryGetRelicModelFromEntry(entry) is { } rm)
            return rm.Id;
        var t = entry.GetType();
        foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (prop.GetValue(entry) is ModelId id && id != ModelId.none)
                return id;
        }

        foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.GetValue(entry) is ModelId id && id != ModelId.none)
                return id;
        }

        return null;
    }
}

/// <summary>Refresh scan when inventory reloads (slots may be recreated).</summary>
[HarmonyPatch(typeof(NMerchantInventory), "Reload")]
public static class WinRateNMerchantInventoryReloadForRelicPatch
{
    [HarmonyPostfix]
    public static void Postfix(NMerchantInventory __instance)
    {
        try
        {
            Callable.From(() => MerchantShopRelicWinRate.ScheduleScanMerchantShopRoot(__instance)).CallDeferred();
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] WinRateNMerchantInventory Reload relic scan: {ex}");
        }
    }
}
