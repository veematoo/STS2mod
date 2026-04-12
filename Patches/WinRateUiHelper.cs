// Card UI (MCP: get_entity_source("NCard")) — character/rarity/type swap textures on the same nodes, not different scenes.
// - %Frame (TextureRect): full card frame art including blue border; best parent for "bottom of visible card".
// - NCard root: fallback if Frame missing; layout rect can diverge from drawn frame in some contexts.
// - %CardContainer (Body): main chrome / glow children.
// - %OverlayContainer: portrait-sized; ReloadOverlay only — not full-card coordinates.
// See docs/card-ui-notes.md.

using System;
using System.Collections;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using PathPlanner.Stats;

namespace PathPlanner.Patches;

internal static class WinRateUiHelper
{
    internal const string WinRateLabelName = "PathPlannerWinRateLabel";
    internal const string LayerProbePrefix = "PathPlannerLayerProbe_";
    private const string MerchantProbeViewportLayerPrefix = "PathPlannerMerchantProbeVp_";
    internal const string SprayProbePrefix = "PathPlannerSpray_";
    private const string SprayViewportLayerPrefix = "PathPlannerSprayVp_";
    internal const string CardRowLabelPrefix = "PathPlannerWinRateCard_";
    private const string WinRateFollowerName = "PathPlannerWinRateFollower";
    private const string WinRateCanvasLayerLegacyName = "PathPlannerWinRateLayer";

    /// <summary>Full card frame texture rect (blue border included) — preferred host for bottom chrome.</summary>
    private const string CardFrameUniqueName = "%Frame";

    /// <summary>Legacy cleanup; older builds used portrait overlay or NCard-only parenting.</summary>
    private const string CardOverlayContainerUniqueName = "%OverlayContainer";

    private const int CardWinRateFontSize = 15; // ~10% over 14 for readability

    /// <summary>Thickness of the bottom blue bezel on the default card frame art (layout px at 1:1; counted ~16–18px in screenshots).</summary>
    private const float CardFrameBottomBlueBorderThicknessPx = 17f;

    /// <summary>Extra inset from frame bottom — shifts the whole pill up (layout px).</summary>
    private const float CardWinRatePillNudgeUpPx = 2f;

    /// <summary>Extra shift left in <strong>global</strong> px after resolving the coin column (wrapper rects often sit right of the drawn coin).</summary>
    private const float MerchantRelicPriceRowLeftNudgeGlobalPx = -2f;

    private const int MerchantRelicCoinSubtreeMaxDepth = 6;

    /// <summary>Draw above most card chrome; keep below typical focus/highlight layers (relative z).</summary>
    private const int CardWinRateRelativeZ = 28;

    /// <summary>On overlay <see cref="Node"/>: incremented so deferred win-rate layout is skipped after close.</summary>
    internal const string WinRateDeferralSeqMeta = "pathplanner_wr_defseq";

    internal static void InvalidateCardWinRateDeferrals(Node deferralOwner)
    {
        if (!GodotObject.IsInstanceValid(deferralOwner))
            return;
        var v = deferralOwner.HasMeta(WinRateDeferralSeqMeta) ? (int)deferralOwner.GetMeta(WinRateDeferralSeqMeta) : 0;
        deferralOwner.SetMeta(WinRateDeferralSeqMeta, v + 1);
    }

    /// <summary>False for any card in a character's official <c>StartingDeck</c> (see <see cref="WinRateStarterCardRegistry"/>).</summary>
    internal static bool ShouldShowCardWinRatePill(CardModel? model)
    {
        if (model == null)
            return false;
        return !WinRateStarterCardRegistry.IsStarterDeckCard(model);
    }

    /// <summary>False for starter entries when only <see cref="ModelId"/> is available (e.g. merchant UI before <see cref="CardModel"/> bind).</summary>
    internal static bool ShouldShowCardWinRatePill(ModelId id)
    {
        if (id == ModelId.none)
            return false;
        return !WinRateStarterCardRegistry.IsStarterDeckEntry(id.Entry);
    }

    internal static string CardLineFromModelId(ModelId id)
    {
        RunHistoryWinRateAggregator.EnsureFresh();
        RunHistoryWinRateAggregator.TryGetCardRate(id, out var w, out var t);
        return RunHistoryWinRateAggregator.FormatRate(w, t);
    }

    internal static void RemoveExisting(Control parent)
    {
        var n = parent.GetNodeOrNull<Label>(WinRateLabelName);
        if (n != null && GodotObject.IsInstanceValid(n))
            n.QueueFree();
    }

    /// <summary>Removes numbered layer-probe labels under <paramref name="root"/> (merchant UI debugging).</summary>
    internal static void RemoveLayerProbesUnder(Node root)
    {
        if (!GodotObject.IsInstanceValid(root))
            return;
        for (var i = root.GetChildCount() - 1; i >= 0; i--)
        {
            var ch = root.GetChild(i);
            RemoveLayerProbesUnder(ch);
            if (ch.Name.ToString().StartsWith(LayerProbePrefix, System.StringComparison.Ordinal))
                ch.QueueFree();
        }
    }

    /// <summary>
    /// Puts <c>1</c> on <paramref name="hosts"/>[0], <c>2</c> on [1], … so you can see which control is actually visible.
    /// </summary>
    internal static void AttachLayerProbeNumbers(IReadOnlyList<Control> hosts)
    {
        const int cols = 10;
        const float cellW = 10f;
        const float cellH = 12f;
        for (var i = 0; i < hosts.Count; i++)
        {
            var parent = hosts[i];
            if (!GodotObject.IsInstanceValid(parent))
                continue;
            parent.GetNodeOrNull<Label>(LayerProbePrefix + i)?.QueueFree();
            var label = new Label
            {
                Name = LayerProbePrefix + i,
                Text = (i + 1).ToString(),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 120,
                ZAsRelative = false
            };
            label.AddThemeFontSizeOverride("font_size", 22);
            label.AddThemeColorOverride("font_color", new Color(1f, 1f, 0.2f));
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 4);
            label.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            var col = i % cols;
            var row = i / cols;
            var staggerX = col * cellW;
            var staggerY = row * cellH;
            label.OffsetLeft = 4f + staggerX;
            label.OffsetTop = 4f + staggerY;
            label.OffsetRight = 40f + staggerX;
            label.OffsetBottom = 32f + staggerY;
            parent.AddChild(label);
            parent.MoveChild(label, parent.GetChildCount() - 1);
        }
    }

    /// <summary>Removes viewport-level probe layer for this anchor (pair with <see cref="AttachLayerProbeNumbersMerchantViewport"/>).</summary>
    internal static void RemoveMerchantProbeViewportLayer(Node anchorNode)
    {
        if (!GodotObject.IsInstanceValid(anchorNode))
            return;
        var vp = anchorNode.GetViewport();
        if (vp == null)
            return;
        vp.GetNodeOrNull<CanvasLayer>(MerchantProbeViewportLayerPrefix + anchorNode.GetInstanceId())?.QueueFree();
    }

    /// <summary>
    /// Duplicate probe digits on a high <see cref="CanvasLayer"/> using <see cref="Control.GlobalPosition"/> so numbers
    /// stay visible even when parents clip children.
    /// </summary>
    internal static void AttachLayerProbeNumbersMerchantViewport(IReadOnlyList<Control> hosts, Node anchorNode)
    {
        if (!GodotObject.IsInstanceValid(anchorNode) || hosts.Count == 0)
            return;
        RemoveMerchantProbeViewportLayer(anchorNode);
        var vp = anchorNode.GetViewport();
        if (vp == null)
            return;
        const int cols = 10;
        const float cellW = 10f;
        const float cellH = 12f;
        var layer = new CanvasLayer
        {
            Name = MerchantProbeViewportLayerPrefix + anchorNode.GetInstanceId(),
            Layer = 128
        };
        vp.AddChild(layer);
        for (var i = 0; i < hosts.Count; i++)
        {
            var h = hosts[i];
            if (!GodotObject.IsInstanceValid(h))
                continue;
            var label = new Label
            {
                Name = LayerProbePrefix + "vp_" + i,
                Text = (i + 1).ToString(),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            label.AddThemeFontSizeOverride("font_size", 22);
            label.AddThemeColorOverride("font_color", new Color(1f, 1f, 0.2f));
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 4);
            label.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            var col = i % cols;
            var row = i / cols;
            var staggerX = col * cellW;
            var staggerY = row * cellH;
            var gp = h.GlobalPosition;
            label.Position = new Vector2(gp.X + 4f + staggerX, gp.Y + 4f + staggerY);
            label.Size = new Vector2(40f, 32f);
            layer.AddChild(label);
        }
    }

    /// <summary>
    /// Ordered candidates for the shop price row (coin + digits): label, heuristic row box, ancestor chain, slot, hitbox, siblings.
    /// Pair with <see cref="AttachLayerProbeNumbers"/> / <see cref="AttachLayerProbeNumbersMerchantViewport"/>.
    /// </summary>
    internal static List<Control> BuildMerchantPriceRowProbeHosts(Control entryPoint)
    {
        var list = new List<Control>();
        var seen = new HashSet<ulong>();

        void Add(Control? c)
        {
            if (c == null || !GodotObject.IsInstanceValid(c) || !seen.Add(c.GetInstanceId()))
                return;
            list.Add(c);
        }

        var costLabel = TryGetMerchantSlotCostLabel(entryPoint);
        if (costLabel == null || !GodotObject.IsInstanceValid(costLabel))
            return list;

        NMerchantSlot? merchantSlot = null;
        for (Node? p = entryPoint; p != null; p = p.GetParent())
        {
            if (p is NMerchantSlot s && GodotObject.IsInstanceValid(s))
            {
                merchantSlot = s;
                break;
            }
        }

        Add(costLabel);

        var rowBox = TryGetMerchantPriceRowBox(costLabel);
        if (rowBox != null && GodotObject.IsInstanceValid(rowBox))
            Add(rowBox);

        for (Node? n = costLabel.GetParent(); n != null && n is not NMerchantSlot; n = n.GetParent())
        {
            if (n is Control pc)
                Add(pc);
        }

        if (merchantSlot != null)
        {
            Add(merchantSlot);
            Add(merchantSlot.GetNodeOrNull<Control>("%Hitbox"));
        }

        var parent = costLabel.GetParent();
        if (parent != null)
        {
            foreach (var ch in parent.GetChildren())
            {
                if (ch is Control sib)
                    Add(sib);
            }
        }

        var grandparent = parent?.GetParent();
        if (grandparent != null)
        {
            foreach (var ch in grandparent.GetChildren())
            {
                if (ch is Control sib2 && !ReferenceEquals(ch, parent))
                    Add(sib2);
            }
        }

        return list;
    }

    /// <summary>Removes spray labels under <paramref name="root"/> and any spray <see cref="CanvasLayer"/> on the viewport.</summary>
    internal static void RemoveMerchantSprayProbe(Node root)
    {
        if (!GodotObject.IsInstanceValid(root))
            return;
        RemoveSprayLabelsRecursive(root);
        var vp = root.GetViewport();
        if (vp == null)
            return;
        for (var i = vp.GetChildCount() - 1; i >= 0; i--)
        {
            var ch = vp.GetChild(i);
            if (ch is CanvasLayer cl && cl.Name.ToString().StartsWith(SprayViewportLayerPrefix, StringComparison.Ordinal))
                cl.QueueFree();
        }
    }

    private static void RemoveSprayLabelsRecursive(Node node)
    {
        if (!GodotObject.IsInstanceValid(node))
            return;
        for (var i = node.GetChildCount() - 1; i >= 0; i--)
        {
            var ch = node.GetChild(i);
            RemoveSprayLabelsRecursive(ch);
            if (ch.Name.ToString().StartsWith(SprayProbePrefix, StringComparison.Ordinal))
                ch.QueueFree();
        }
    }

    /// <summary>
    /// Debug: many numbered labels on sampled <see cref="Control"/>s plus a viewport <see cref="CanvasLayer"/> copy and a banner.
    /// Pass the broadest shop root (merchant room or inventory) so relic rows under the room are included.
    /// </summary>
    internal static void RunMerchantSprayProbe(Node shopRoot, int maxControls = 420, int maxDepth = 22)
    {
        if (!GodotObject.IsInstanceValid(shopRoot))
            return;
        for (Node? p = shopRoot.GetParent(); p != null; p = p.GetParent())
        {
            if (!p.GetType().Name.Contains("MerchantRoom", StringComparison.OrdinalIgnoreCase))
                continue;
            shopRoot = p;
            break;
        }

        RemoveMerchantSprayProbe(shopRoot);
        var collected = new List<Control>();
        var seen = new HashSet<ulong>();
        var remaining = maxControls;

        CollectAllNMerchantSlotsFirst(shopRoot, collected, seen, ref remaining, maxDepth, 0);
        CollectControlsByTypeNameHints(shopRoot, collected, seen, ref remaining, maxDepth, 0,
            new[] { "Relic", "Potion", "Removal", "Mercant", "Merchant", "Offer", "ShopSlot", "Slot" });
        CollectFromSubViewportTrees(shopRoot, collected, seen, ref remaining, maxDepth, 0);
        var visit = 0;
        CollectControlsSampled(shopRoot, collected, seen, ref remaining, maxDepth, 0, 2, ref visit);
        var vp = shopRoot.GetViewport();
        if (vp == null)
            return;
        var layer = new CanvasLayer
        {
            Name = SprayViewportLayerPrefix + shopRoot.GetInstanceId(),
            Layer = 200
        };
        vp.AddChild(layer);
        var banner = new Label
        {
            Name = SprayProbePrefix + "banner",
            Text = "PathPlanner SPRAY ACTIVE",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        banner.AddThemeFontSizeOverride("font_size", 28);
        banner.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        banner.AddThemeColorOverride("font_outline_color", Colors.Black);
        banner.AddThemeConstantOverride("outline_size", 6);
        banner.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        banner.OffsetTop = 8f;
        banner.OffsetBottom = 48f;
        banner.HorizontalAlignment = HorizontalAlignment.Center;
        layer.AddChild(banner);

        for (var i = 0; i < collected.Count; i++)
        {
            var c = collected[i];
            if (!GodotObject.IsInstanceValid(c))
                continue;
            c.GetNodeOrNull<Label>(SprayProbePrefix + i)?.QueueFree();
            var onControl = new Label
            {
                Name = SprayProbePrefix + i,
                Text = (i + 1).ToString(),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 200,
                ZAsRelative = false
            };
            onControl.AddThemeFontSizeOverride("font_size", 16);
            onControl.AddThemeColorOverride("font_color", new Color(0.2f, 1f, 0.4f));
            onControl.AddThemeColorOverride("font_outline_color", Colors.Black);
            onControl.AddThemeConstantOverride("outline_size", 3);
            onControl.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            onControl.OffsetLeft = 2f;
            onControl.OffsetTop = 2f;
            onControl.OffsetRight = 36f;
            onControl.OffsetBottom = 26f;
            c.AddChild(onControl);
            c.MoveChild(onControl, c.GetChildCount() - 1);

            var vpLabel = new Label
            {
                Name = SprayProbePrefix + "vp_" + i,
                Text = (i + 1).ToString(),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            vpLabel.AddThemeFontSizeOverride("font_size", 16);
            vpLabel.AddThemeColorOverride("font_color", new Color(0.2f, 1f, 0.4f));
            vpLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
            vpLabel.AddThemeConstantOverride("outline_size", 3);
            vpLabel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            var gp = c.GlobalPosition;
            vpLabel.Position = new Vector2(gp.X + 2f, gp.Y + 2f);
            vpLabel.Size = new Vector2(36f, 24f);
            layer.AddChild(vpLabel);
        }
    }

    private static void CollectAllNMerchantSlotsFirst(Node n, List<Control> outList, HashSet<ulong> seen, ref int remaining, int maxDepth, int depth)
    {
        if (!GodotObject.IsInstanceValid(n) || remaining <= 0 || depth > maxDepth)
            return;
        if (n is NMerchantSlot slot && GodotObject.IsInstanceValid(slot) && seen.Add(slot.GetInstanceId()))
        {
            outList.Add(slot);
            remaining--;
        }

        foreach (var ch in n.GetChildren())
            CollectAllNMerchantSlotsFirst(ch, outList, seen, ref remaining, maxDepth, depth + 1);
    }

    private static void CollectControlsByTypeNameHints(Node n, List<Control> outList, HashSet<ulong> seen, ref int remaining, int maxDepth, int depth, string[] hints)
    {
        if (!GodotObject.IsInstanceValid(n) || remaining <= 0 || depth > maxDepth)
            return;
        if (n is Control c && GodotObject.IsInstanceValid(c))
        {
            var tn = n.GetType().Name;
            foreach (var h in hints)
            {
                if (!tn.Contains(h, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Add(c.GetInstanceId()))
                {
                    outList.Add(c);
                    remaining--;
                }

                break;
            }
        }

        foreach (var ch in n.GetChildren())
            CollectControlsByTypeNameHints(ch, outList, seen, ref remaining, maxDepth, depth + 1, hints);
    }

    private static void CollectFromSubViewportTrees(Node n, List<Control> outList, HashSet<ulong> seen, ref int remaining, int maxDepth, int depth)
    {
        if (!GodotObject.IsInstanceValid(n) || remaining <= 0 || depth > maxDepth)
            return;
        if (n is SubViewport sv)
        {
            foreach (var ch in sv.GetChildren())
                CollectControlsInsideViewportRoot(ch, outList, seen, ref remaining, 14, 0);
        }

        foreach (var ch in n.GetChildren())
            CollectFromSubViewportTrees(ch, outList, seen, ref remaining, maxDepth, depth + 1);
    }

    private static void CollectControlsInsideViewportRoot(Node n, List<Control> outList, HashSet<ulong> seen, ref int remaining, int maxDepth, int depth)
    {
        if (!GodotObject.IsInstanceValid(n) || remaining <= 0 || depth > maxDepth)
            return;
        if (n is Control c && GodotObject.IsInstanceValid(c) && seen.Add(c.GetInstanceId()))
        {
            outList.Add(c);
            remaining--;
        }

        foreach (var ch in n.GetChildren())
            CollectControlsInsideViewportRoot(ch, outList, seen, ref remaining, maxDepth, depth + 1);
    }

    private static void CollectControlsSampled(Node n, List<Control> outList, HashSet<ulong> seen, ref int remaining, int maxDepth, int depth, int stride, ref int visitIndex)
    {
        if (!GodotObject.IsInstanceValid(n) || remaining <= 0 || depth > maxDepth)
            return;
        if (n is Control c && GodotObject.IsInstanceValid(c))
        {
            var force = n is NMerchantSlot;
            var take = force || visitIndex % stride == 0;
            visitIndex++;
            if (take && seen.Add(c.GetInstanceId()))
            {
                outList.Add(c);
                remaining--;
            }
        }

        foreach (var ch in n.GetChildren())
            CollectControlsSampled(ch, outList, seen, ref remaining, maxDepth, depth + 1, stride, ref visitIndex);
    }

    /// <summary>Every <see cref="NGridCardHolder"/> under <paramref name="root"/> (nested rows, scroll content, etc.).</summary>
    internal static void CollectGridCardHoldersInSubtree(Node root, List<NGridCardHolder> holders)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is NGridCardHolder h && GodotObject.IsInstanceValid(h))
                holders.Add(h);
            CollectGridCardHoldersInSubtree(child, holders);
        }
    }

    /// <summary>
    /// Like <see cref="CollectGridCardHoldersInSubtree"/> but skips holders not in the visible tree (shop rows split across containers).
    /// </summary>
    internal static void CollectVisibleGridCardHoldersInSubtree(Node root, List<NGridCardHolder> holders)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is NGridCardHolder h && GodotObject.IsInstanceValid(h) && h.IsVisibleInTree())
                holders.Add(h);
            CollectVisibleGridCardHoldersInSubtree(child, holders);
        }
    }

    /// <summary>Removes strips from each card, legacy row/canvas nodes, and the rotation follower.</summary>
    internal static void RemoveCardWinRateLabelsFromRow(Control row)
    {
        row.GetNodeOrNull(WinRateCanvasLayerLegacyName)?.QueueFree();
        row.GetViewport()?.GetNodeOrNull(WinRateCanvasLayerLegacyName)?.QueueFree();
        row.GetNodeOrNull<Node>(WinRateFollowerName)?.QueueFree();
        foreach (var child in row.GetChildren())
        {
            if (child.Name.ToString().StartsWith(CardRowLabelPrefix, System.StringComparison.Ordinal))
                child.QueueFree();
        }

        var holders = new List<NGridCardHolder>();
        CollectGridCardHoldersInSubtree(row, holders);
        foreach (var holder in holders)
            RemoveWinRateStripsFromHolder(holder);
    }

    private static void RemoveWinRateStripsFromHolder(NGridCardHolder holder)
    {
        RemoveNamedStripsFromControl(holder);
        if (holder.CardNode is not NCard card || !GodotObject.IsInstanceValid(card))
            return;
        RemoveNamedStripsFromControl(card);
        var frame = card.GetNodeOrNull<Control>(CardFrameUniqueName);
        if (frame != null && GodotObject.IsInstanceValid(frame))
            RemoveNamedStripsFromControl(frame);
        var legacy = card.GetNodeOrNull<Control>(CardOverlayContainerUniqueName);
        if (legacy != null && GodotObject.IsInstanceValid(legacy))
            RemoveNamedStripsFromControl(legacy);
    }

    internal static void RemoveNamedStripsFromControl(Control node)
    {
        for (var i = node.GetChildCount() - 1; i >= 0; i--)
        {
            var n = node.GetChild(i);
            if (n.Name.ToString().StartsWith(CardRowLabelPrefix, System.StringComparison.Ordinal))
                n.QueueFree();
        }
    }

    private const string MerchantRotFollowerName = "PathPlannerWinRateMerchantRot";
    private const string MerchantRelicRotFollowerName = "PathPlannerWinRateMerchantRelicRot";

    /// <summary>Shop relic tile: same pill as cards, parented under <paramref name="host"/> (often <see cref="MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantRelic"/> or <see cref="NMerchantSlot"/>).</summary>
    internal static void AttachMerchantRelicWinRateStrip(Control host, string text)
    {
        if (!GodotObject.IsInstanceValid(host) || string.IsNullOrWhiteSpace(text))
            return;
        host.GetNodeOrNull<Node>(MerchantRelicRotFollowerName)?.QueueFree();
        RemoveNamedStripsFromControl(host);
        var stripIndex = 0;
        var stripRoot = CreateMerchantRelicWinRateStripRoot(text, ref stripIndex);
        host.AddChild(stripRoot);
        host.MoveChild(stripRoot, host.GetChildCount() - 1);
        ApplyWinRateStripVerticalPlacement(stripRoot);
        host.AddChild(new MerchantWinRateRotationFollower(stripRoot) { Name = MerchantRelicRotFollowerName });

        // Vanilla uses MegaLabel "%CostLabel" on NMerchantSlot (see sts2 NMerchantSlot.ConnectSignals); match that rect.
        void AlignToPriceRow()
        {
            if (!GodotObject.IsInstanceValid(host) || !GodotObject.IsInstanceValid(stripRoot))
                return;
            TryAlignMerchantRelicStripHorizontalToCostLabel(host, stripRoot);
        }

        Callable.From(AlignToPriceRow).CallDeferred();
        Callable.From(AlignToPriceRow).CallDeferred();
        Callable.From(AlignToPriceRow).CallDeferred();
    }

    internal static void RemoveMerchantWinRateStripsFromCard(NCard card)
    {
        if (!GodotObject.IsInstanceValid(card))
            return;
        card.GetNodeOrNull<Node>(MerchantRotFollowerName)?.QueueFree();
        var host = TryGetCardWinRateHost(card);
        if (host != null && GodotObject.IsInstanceValid(host))
            RemoveNamedStripsFromControl(host);
        RemoveNamedStripsFromControl(card);
    }

    /// <summary>Same pill strip as card rewards: parented under <c>%Frame</c>, rotation follower on <see cref="NCard"/>.</summary>
    internal static void AttachCardWinRateStripToMerchantCard(NCard card, string text)
    {
        if (!GodotObject.IsInstanceValid(card) || string.IsNullOrWhiteSpace(text))
            return;
        RemoveMerchantWinRateStripsFromCard(card);
        var host = TryGetCardWinRateHost(card);
        if (host == null || !GodotObject.IsInstanceValid(host))
            return;
        var stripIndex = 0;
        var stripRoot = CreateCardWinRateStripRoot(text, ref stripIndex);
        host.AddChild(stripRoot);
        host.MoveChild(stripRoot, host.GetChildCount() - 1);
        ApplyWinRateStripVerticalPlacement(stripRoot);
        card.AddChild(new MerchantWinRateRotationFollower(stripRoot) { Name = MerchantRotFollowerName });
    }

    private static Control CreateCardWinRateStripRoot(string text, ref int stripIndex)
    {
        var stripRoot = new Control
        {
            Name = $"{CardRowLabelPrefix}{stripIndex++}",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = true,
            ZIndex = CardWinRateRelativeZ,
            ZAsRelative = true
        };
        stripRoot.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        stripRoot.OffsetLeft = CardWinRateInnerPad;
        stripRoot.OffsetRight = -CardWinRateInnerPad;
        var stripHeight = CardWinRateBarHeight + CardWinRateStripGapPx;
        var bottomInset = (CardFrameBottomBlueBorderThicknessPx - stripHeight) * 0.5f;
        if (bottomInset < 0f)
            bottomInset = 0f;
        bottomInset += CardWinRatePillNudgeUpPx;
        stripRoot.OffsetBottom = -bottomInset;
        stripRoot.OffsetTop = -(bottomInset + stripHeight);

        var center = new CenterContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        stripRoot.AddChild(center);
        center.AddChild(BuildWinRatePillPanel(text));
        return stripRoot;
    }

    /// <summary>
    /// Relic slot host is wider than the icon+coin column; left-align the pill so its left edge matches the relic / coin (not centered under price digits).
    /// </summary>
    private static Control CreateMerchantRelicWinRateStripRoot(string text, ref int stripIndex)
    {
        var stripRoot = new Control
        {
            Name = $"{CardRowLabelPrefix}{stripIndex++}",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = true,
            ZIndex = CardWinRateRelativeZ,
            ZAsRelative = true
        };
        stripRoot.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        stripRoot.OffsetLeft = 0f;
        stripRoot.OffsetRight = 0f;
        var stripHeight = CardWinRateBarHeight + CardWinRateStripGapPx;
        var bottomInset = (CardFrameBottomBlueBorderThicknessPx - stripHeight) * 0.5f;
        if (bottomInset < 0f)
            bottomInset = 0f;
        bottomInset += CardWinRatePillNudgeUpPx;
        stripRoot.OffsetBottom = -bottomInset;
        stripRoot.OffsetTop = -(bottomInset + stripHeight);

        // Begin: pill left edge matches strip left (coin); Center would indent the pill within the price-row span.
        var row = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Begin
        };
        row.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        stripRoot.AddChild(row);
        row.AddChild(BuildWinRatePillPanel(text));
        return stripRoot;
    }

    private static PanelContainer BuildWinRatePillPanel(string text)
    {
        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        panel.CustomMinimumSize = new Vector2(0f, CardWinRateBarHeight);

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.12f, 0.92f),
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
            ContentMarginLeft = 7,
            ContentMarginRight = 7,
            ContentMarginTop = 0,
            ContentMarginBottom = 0
        };
        panel.AddThemeStyleboxOverride("panel", style);

        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", CardWinRateFontSize);
        label.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        label.AddThemeConstantOverride("outline_size", 4);

        panel.AddChild(label);
        return panel;
    }

    private static void ApplyHorizontalRotationCancel(Control overlay)
    {
        if (!GodotObject.IsInstanceValid(overlay))
            return;
        float ancestorSum = 0f;
        for (Node? n = overlay.GetParent(); n != null; n = n.GetParent())
        {
            if (n is Control c)
                ancestorSum += c.Rotation;
        }

        overlay.Rotation = -ancestorSum;
    }

    private sealed class MerchantWinRateRotationFollower : Node
    {
        private readonly Control _strip;

        public MerchantWinRateRotationFollower(Control strip)
        {
            _strip = strip;
            ProcessMode = ProcessModeEnum.Always;
        }

        public override void _Process(double delta)
        {
            ApplyHorizontalRotationCancel(_strip);
        }
    }

    /// <summary>
    /// Parents each strip under <c>%Frame</c> (full frame TextureRect — blue border) so bottom anchors match the drawn card.
    /// Falls back to <see cref="NCard"/> root. <see cref="WinRateOverlayFollower"/> keeps text horizontal in the fan.
    /// </summary>
    private static Control? TryGetCardWinRateHost(NCard card)
    {
        var frame = card.GetNodeOrNull<Control>(CardFrameUniqueName);
        if (frame != null && GodotObject.IsInstanceValid(frame))
            return frame;
        return card;
    }

    internal static void ScheduleCardWinRatesInRow(Control row, IReadOnlyList<(NGridCardHolder Holder, string Text)> items, Node deferralOwner)
    {
        RemoveCardWinRateLabelsFromRow(row);
        if (!GodotObject.IsInstanceValid(deferralOwner) || row.GetTree() == null || items.Count == 0)
            return;

        var nextSeq = (deferralOwner.HasMeta(WinRateDeferralSeqMeta) ? (int)deferralOwner.GetMeta(WinRateDeferralSeqMeta) : 0) + 1;
        deferralOwner.SetMeta(WinRateDeferralSeqMeta, nextSeq);
        var capturedSeq = nextSeq;

        var copy = new List<(NGridCardHolder Holder, string Text)>(items);
        // Defer twice so card/holder layout and NCard children (e.g. %Frame) exist; avoids SceneTreeTimer edge cases.
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(row) || !GodotObject.IsInstanceValid(deferralOwner))
                return;
            if (!deferralOwner.HasMeta(WinRateDeferralSeqMeta) || (int)deferralOwner.GetMeta(WinRateDeferralSeqMeta) != capturedSeq)
                return;
            Callable.From(() =>
            {
                if (!GodotObject.IsInstanceValid(row) || !GodotObject.IsInstanceValid(deferralOwner))
                    return;
                if (!deferralOwner.HasMeta(WinRateDeferralSeqMeta) || (int)deferralOwner.GetMeta(WinRateDeferralSeqMeta) != capturedSeq)
                    return;
                BuildAndAttachWinRateRowDeferred(row, copy);
            }).CallDeferred();
        }).CallDeferred();
    }

    private static void BuildAndAttachWinRateRowDeferred(Control row, List<(NGridCardHolder Holder, string Text)> copy)
    {
        if (!GodotObject.IsInstanceValid(row))
            return;
        var pairs = CreateCardWinRateOverlays(copy);
        foreach (var (stripRoot, _) in pairs)
            ApplyWinRateStripVerticalPlacement(stripRoot);
        ApplyHorizontalTextToStrips(pairs);
        if (pairs.Count == 0)
            return;
        row.AddChild(new WinRateOverlayFollower(pairs) { Name = WinRateFollowerName });
    }

    /// <summary>Panel min height (actual pill is measured after layout for centering).</summary>
    private const float CardWinRateBarHeight = 15f;
    private const float CardWinRateInnerPad = 5f;
    /// <summary>Extra layout height on the strip root (spacing); stripHeight = bar + gap should stay near <see cref="CardFrameBottomBlueBorderThicknessPx"/>.</summary>
    private const float CardWinRateStripGapPx = 2f;

    private static List<(Control Overlay, NGridCardHolder Holder)> CreateCardWinRateOverlays(
        IReadOnlyList<(NGridCardHolder Holder, string Text)> items)
    {
        var pairs = new List<(Control Overlay, NGridCardHolder Holder)>();
        var stripIndex = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var (holder, text) = items[i];
            if (!GodotObject.IsInstanceValid(holder) || string.IsNullOrWhiteSpace(text))
                continue;

            // Prefer %Frame / NCard; if the pooled card is not ready yet, parent to the holder so a pill still appears.
            Control? host = null;
            if (holder.CardNode is NCard card && GodotObject.IsInstanceValid(card))
                host = TryGetCardWinRateHost(card);
            host ??= holder;

            if (!GodotObject.IsInstanceValid(host))
                continue;

            var stripRoot = CreateCardWinRateStripRoot(text, ref stripIndex);
            host.AddChild(stripRoot);
            host.MoveChild(stripRoot, host.GetChildCount() - 1);

            pairs.Add((stripRoot, holder));
        }

        return pairs;
    }

    /// <summary>
    /// Places the strip so the pill’s vertical center matches the midline of the bottom blue bezel (thickness
    /// <see cref="CardFrameBottomBlueBorderThicknessPx"/> px): bottomInset = B/2 − stripHeight/2, clamped ≥ 0.
    /// Uses measured panel height so font/outline/margins are included.
    /// </summary>
    private static void ApplyWinRateStripVerticalPlacement(Control stripRoot)
    {
        if (!GodotObject.IsInstanceValid(stripRoot) || !TryGetWinRateStripPanel(stripRoot, out var panel))
            return;

        var pillH = panel.GetCombinedMinimumSize().Y;
        if (pillH <= 0f)
            pillH = CardWinRateBarHeight;
        var stripHeight = pillH + CardWinRateStripGapPx;
        var bottomInset = (CardFrameBottomBlueBorderThicknessPx - stripHeight) * 0.5f;
        if (bottomInset < 0f)
            bottomInset = 0f;
        bottomInset += CardWinRatePillNudgeUpPx;
        stripRoot.OffsetBottom = -bottomInset;
        stripRoot.OffsetTop = -(bottomInset + stripHeight);
    }

    private static bool TryGetWinRateStripPanel(Control stripRoot, out PanelContainer panel)
    {
        panel = null!;
        if (!GodotObject.IsInstanceValid(stripRoot) || stripRoot.GetChildCount() < 1)
            return false;
        switch (stripRoot.GetChild(0))
        {
            case CenterContainer cc when cc.GetChildCount() >= 1 && cc.GetChild(0) is PanelContainer p1:
                panel = p1;
                return true;
            case HBoxContainer hb when hb.GetChildCount() >= 1 && hb.GetChild(0) is PanelContainer p2:
                panel = p2;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Align strip to the shop <strong>price row</strong>: vanilla uses <c>%CostLabel</c> for the number, but the gold coin is
    /// almost always a <see cref="Control"/> <strong>sibling</strong> in the same <see cref="BoxContainer"/> (see sts2
    /// <c>NMerchantSlot.ConnectSignals</c> — scene layout, not C#). We span every visible child of that row so the pill lines up with the coin.
    /// </summary>
    private static void TryAlignMerchantRelicStripHorizontalToCostLabel(Control relicHost, Control stripRoot)
    {
        if (!GodotObject.IsInstanceValid(relicHost) || !GodotObject.IsInstanceValid(stripRoot))
            return;
        var costLabel = TryGetMerchantSlotCostLabel(relicHost);
        if (costLabel == null || !GodotObject.IsInstanceValid(costLabel))
            return;

        var inv = relicHost.GetGlobalTransformWithCanvas().AffineInverse();
        var hostW = relicHost.Size.X;
        if (hostW < 8f)
            return;

        if (!TryGetMerchantPriceRowGlobalXExtents(costLabel, out var minGx, out var maxGx))
            return;

        minGx = RefineMerchantCoinLeftGlobalX(costLabel, minGx);

        var yRef = costLabel.GlobalPosition.Y;
        if (GodotObject.IsInstanceValid(stripRoot) && stripRoot.IsInsideTree() && stripRoot.Size.Y > 2f)
            yRef = stripRoot.GlobalPosition.Y + stripRoot.Size.Y * 0.5f;

        var localLeft = inv * new Vector2(minGx, yRef);
        var localRight = inv * new Vector2(maxGx, yRef);
        var spanW = localRight.X - localLeft.X;
        if (spanW < 4f || float.IsNaN(localLeft.X))
            return;

        // Do not reject localLeft.X < 0: coin column can sit past the host's left edge in local space after
        // global nudge / inverse transform; the old guard (localLeft.X < -4f) skipped alignment entirely so
        // OffsetLeft stayed 0 and left nudges appeared to do nothing.
        if (localLeft.X > hostW * 1.05f)
            return;

        stripRoot.OffsetLeft = localLeft.X;
        stripRoot.OffsetRight = -(hostW - localLeft.X - spanW);
    }

    /// <summary>
    /// Price row min X can be a wrapper (e.g. <c>CenterContainer</c>) whose rect starts left of the drawn coin icon.
    /// Descend sibling subtrees that sit left of the label to find the leftmost visible <see cref="Control"/> global X, then nudge further left.
    /// </summary>
    private static float RefineMerchantCoinLeftGlobalX(Control costLabel, float minGxFromRow)
    {
        var labelLeft = costLabel.GlobalPosition.X;
        var best = minGxFromRow;

        for (Node? p = costLabel.GetParent(); p != null && p is not NMerchantSlot; p = p.GetParent())
        {
            foreach (var ch in p.GetChildren())
            {
                if (ch is not Control c || !GodotObject.IsInstanceValid(c) || !c.Visible)
                    continue;
                if (ReferenceEquals(ch, costLabel) || c.IsAncestorOf(costLabel))
                    continue;
                var left = GetLeftmostVisibleGlobalXInSubtree(c, MerchantRelicCoinSubtreeMaxDepth);
                if (left < float.MaxValue * 0.5f && left < labelLeft - 0.5f)
                    best = Math.Min(best, left);
            }
        }

        return best - MerchantRelicPriceRowLeftNudgeGlobalPx;
    }

    private static float GetLeftmostVisibleGlobalXInSubtree(Control root, int depthRemaining)
    {
        if (!GodotObject.IsInstanceValid(root) || !root.Visible)
            return float.MaxValue;

        var min = root.GlobalPosition.X;
        if (depthRemaining <= 0)
            return min;

        foreach (var ch in root.GetChildren())
        {
            if (ch is Control c && GodotObject.IsInstanceValid(c) && c.Visible)
                min = Math.Min(min, GetLeftmostVisibleGlobalXInSubtree(c, depthRemaining - 1));
        }

        return min;
    }

    /// <summary>
    /// Find the shop price row (coin + digits): closest ancestor of <c>%CostLabel</c> with ≥2 visible <see cref="Control"/>
    /// children where one subtree contains the label; min X is the coin (probe overlay #5), span stays tight vs whole-slot parents.
    /// </summary>
    private static bool TryGetMerchantPriceRowGlobalXExtents(Control costLabel, out float minGx, out float maxGx)
    {
        minGx = float.MaxValue;
        maxGx = float.MinValue;

        var labelLeft = costLabel.GlobalPosition.X;
        var labelW = Math.Max(costLabel.Size.X, 8f);
        var maxReasonableSpan = labelW + 120f;

        for (Node? anc = costLabel.GetParent(); anc != null && anc is not NMerchantSlot; anc = anc.GetParent())
        {
            if (anc is not Control)
                continue;

            var controls = new List<Control>();
            foreach (var ch in anc.GetChildren())
            {
                if (ch is Control c && GodotObject.IsInstanceValid(c) && c.Visible)
                    controls.Add(c);
            }

            if (controls.Count < 2)
                continue;

            var rowHasLabel = false;
            foreach (var c in controls)
            {
                if (ReferenceEquals(c, costLabel) || c.IsAncestorOf(costLabel))
                {
                    rowHasLabel = true;
                    break;
                }
            }

            if (!rowHasLabel)
                continue;

            float min = float.MaxValue;
            float max = float.MinValue;
            foreach (var c in controls)
            {
                min = Math.Min(min, c.GlobalPosition.X);
                max = Math.Max(max, c.GlobalPosition.X + c.Size.X);
            }

            if (min >= max || max - min > maxReasonableSpan * 2.5f)
                continue;
            if (min > labelLeft - 1f)
                continue;

            minGx = min;
            maxGx = max;
            return true;
        }

        var row = TryGetMerchantPriceRowBox(costLabel);
        if (row != null && GodotObject.IsInstanceValid(row) && row.GetChildCount() >= 2)
        {
            foreach (var ch in row.GetChildren())
            {
                if (ch is not Control c || !GodotObject.IsInstanceValid(c) || !c.Visible)
                    continue;
                var g = c.GlobalPosition;
                minGx = Math.Min(minGx, g.X);
                maxGx = Math.Max(maxGx, g.X + c.Size.X);
            }
        }

        if (minGx >= maxGx)
        {
            minGx = costLabel.GlobalPosition.X;
            maxGx = costLabel.GlobalPosition.X + costLabel.Size.X;
        }

        return minGx < maxGx;
    }

    private static BoxContainer? TryGetMerchantPriceRowBox(Control costLabel)
    {
        if (!GodotObject.IsInstanceValid(costLabel))
            return null;
        // Direct parent is usually the HBox (coin + CostLabel).
        if (costLabel.GetParent() is BoxContainer direct && direct.GetChildCount() >= 2)
            return direct;
        // Optional wrapper (e.g. CenterContainer) — one level up.
        if (costLabel.GetParent()?.GetParent() is BoxContainer grand && grand.GetChildCount() >= 2)
            return grand;
        return null;
    }

    /// <summary>Vanilla: <see cref="NMerchantSlot"/> wires <c>%CostLabel</c> (MegaLabel) in ConnectSignals.</summary>
    private static Control? TryGetMerchantSlotCostLabel(Control host)
    {
        if (!GodotObject.IsInstanceValid(host))
            return null;
        if (host is NMerchantSlot slot)
            return slot.GetNodeOrNull<Control>("%CostLabel");

        for (Node? p = host; p != null; p = p.GetParent())
        {
            if (p is NMerchantSlot s && GodotObject.IsInstanceValid(s))
                return s.GetNodeOrNull<Control>("%CostLabel");
        }

        return null;
    }

    /// <summary>
    /// Cancels summed ancestor <see cref="Control.Rotation"/> so the strip stays screen-horizontal (fan rotates holders).
    /// </summary>
    private static void ApplyHorizontalTextToStrips(IReadOnlyList<(Control Overlay, NGridCardHolder Holder)> pairs)
    {
        foreach (var (overlay, _) in pairs)
        {
            if (!GodotObject.IsInstanceValid(overlay))
                continue;
            float ancestorSum = 0f;
            for (Node? n = overlay.GetParent(); n != null; n = n.GetParent())
            {
                if (n is Control c)
                    ancestorSum += c.Rotation;
            }
            overlay.Rotation = -ancestorSum;
        }
    }

    private sealed class WinRateOverlayFollower : Node
    {
        private readonly List<(Control Overlay, NGridCardHolder Holder)> _pairs;

        public WinRateOverlayFollower(List<(Control Overlay, NGridCardHolder Holder)> pairs)
        {
            _pairs = pairs;
            ProcessMode = ProcessModeEnum.Always;
        }

        public override void _Process(double delta)
        {
            ApplyHorizontalTextToStrips(_pairs);
        }
    }

    /// <summary>Small line under a reward card / relic tile.</summary>
    internal static void AttachBottomLine(Control parent, string plainText)
    {
        RemoveExisting(parent);
        var label = new Label
        {
            Name = WinRateLabelName,
            Text = plainText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        label.OffsetTop = -20;
        label.OffsetBottom = 0;
        parent.AddChild(label);
        parent.MoveChild(label, parent.GetChildCount() - 1);
    }

    internal static string CardLine(CardModel? model)
    {
        if (model == null)
            return RunHistoryWinRateAggregator.FormatRate(0, 0);
        RunHistoryWinRateAggregator.TryGetCardRate(model.Id, out var w, out var t);
        return RunHistoryWinRateAggregator.FormatRate(w, t);
    }

    internal static string RelicLine(ModelId? id)
    {
        if (id is not { } idv || idv == ModelId.none)
            return RunHistoryWinRateAggregator.FormatRate(0, 0);
        RunHistoryWinRateAggregator.TryGetRelicRate(idv, out var w, out var t);
        return RunHistoryWinRateAggregator.FormatRate(w, t);
    }

    internal static string EventLine(MegaCrit.Sts2.Core.Localization.LocString title)
    {
        RunHistoryWinRateAggregator.TryGetEventRate(title, out var w, out var t);
        return RunHistoryWinRateAggregator.FormatRate(w, t);
    }
}
