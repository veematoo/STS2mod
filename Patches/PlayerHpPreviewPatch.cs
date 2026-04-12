using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.addons.mega_text;
using PathPlanner.Combat;

namespace PathPlanner.Patches;

/// <summary>
/// Augments the local player's <see cref="NHealthBar"/> text to <c>75([color]70[/color])/100</c> with projected HP.
/// </summary>
[HarmonyPatch]
public static class PlayerHpPreviewPatch
{
    private const string RtfName = "PathPlannerHpRtf";
    private static readonly Color ProjectedHpRed = new Color("F1373E");
    /// <summary>Shift preview text up so it sits in the optical center of the HP fill (see NHealthBar layout).</summary>
    private const int HpPreviewVerticalNudgePx = 5;
    /// <summary>Within <see cref="NHealthBar"/> only — above HP fill/middleground, not card UI.</summary>
    private const int HpPreviewOverlayZIndex = 48;
    /// <summary>High-contrast outline for text on the red HP fill; not tied to <see cref="MegaLabel"/> block tint.</summary>
    private static readonly Color HpPreviewOutlineColor = new Color(0.06f, 0.06f, 0.08f, 1f);
    private const int HpPreviewOutlineSizeOuter = 4;
    private const int HpPreviewOutlineSizeProjected = 6;

    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() =>
        AccessTools.DeclaredMethod(typeof(NHealthBar), "RefreshText")
        ?? throw new InvalidOperationException("NHealthBar.RefreshText not found");

    private static void Postfix(NHealthBar __instance) => ApplyPreview(__instance);

    /// <summary>Recomputes BBCode HP text from live <see cref="Creature"/> state (used by the combat-room timer).</summary>
    internal static void ApplyPreview(NHealthBar bar)
    {
        try
        {
            var cm = CombatManager.Instance;
            if (cm == null || !cm.IsInProgress)
            {
                CleanupPreviewOnBar(bar);
                return;
            }

            var creature = Traverse.Create(bar).Field<Creature>("_creature").Value;
            var hpLabel = Traverse.Create(bar).Field<MegaLabel>("_hpLabel").Value;
            if (creature == null || hpLabel == null)
            {
                CleanupPreviewOnBar(bar);
                return;
            }

            if (creature.Player == null || !LocalContext.IsMe(creature.Player))
            {
                HidePreview(bar, hpLabel);
                return;
            }

            if (creature.ShowsInfiniteHp || creature.CurrentHp <= 0)
            {
                HidePreview(bar, hpLabel);
                return;
            }

            int poisonDamage = creature.GetPower<PoisonPower>()?.CalculateTotalDamageNextTurn() ?? 0;
            int doomAmount = creature.GetPowerAmount<DoomPower>();
            if (IsPoisonLethal(creature, poisonDamage) || IsDoomLethal(creature, doomAmount, poisonDamage))
            {
                HidePreview(bar, hpLabel);
                return;
            }

            var state = CombatManager.Instance?.DebugOnlyGetState();
            int? projected = IncomingDamageEstimator.TryGetProjectedHpAfterUpcomingDamage(creature, state);
            if (projected == null)
            {
                HidePreview(bar, hpLabel);
                return;
            }

            var rtl = EnsureRichText(bar, hpLabel);
            if (rtl == null)
                return;

            int cur = creature.CurrentHp;
            int max = creature.MaxHp;
            int proj = projected.Value;

            string redHex = ToHexRgb(ProjectedHpRed);
            string olHex = ToHexRgb(HpPreviewOutlineColor);
            string blackHex = "#000000";
            // Outer outline for white body text; inner thicker black ring around [color] so red digits read on red fill.
            rtl.Text =
                $"[outline_size={HpPreviewOutlineSizeOuter}][outline_color={olHex}][center]{cur}("
                + $"[outline_size={HpPreviewOutlineSizeProjected}][outline_color={blackHex}][color={redHex}]{proj}[/color][/outline_color][/outline_size]"
                + $")/{max}[/center][/outline_color][/outline_size]";

            ApplyRichTextStyle(rtl, hpLabel);

            hpLabel.Visible = false;
            rtl.Visible = true;
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] PlayerHpPreviewPatch: {ex}");
        }
    }

    private static RichTextLabel? EnsureRichText(NHealthBar bar, MegaLabel hpLabel)
    {
        if (hpLabel.GetParent() == null)
            return null;

        var rtl = bar.GetNodeOrNull<RichTextLabel>(RtfName);
        if (rtl == null || !GodotObject.IsInstanceValid(rtl))
        {
            rtl = new RichTextLabel
            {
                Name = RtfName,
                BbcodeEnabled = true,
                FitContent = false,
                ScrollActive = false,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                AutowrapMode = TextServer.AutowrapMode.Off,
                Visible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            bar.AddChild(rtl);
        }
        else if (rtl.GetParent() != bar)
        {
            // Older builds parented under %HpLabel's sibling row — HP fill draws on top there.
            rtl.Reparent(bar);
        }

        ConfigureOverlay(bar, rtl, hpLabel);
        return rtl;
    }

    /// <summary>
    /// Parent overlay to <see cref="NHealthBar"/> (not under the fill row) and match the HP label rect so paint order wins.
    /// </summary>
    private static void ConfigureOverlay(NHealthBar bar, RichTextLabel rtl, MegaLabel hpLabel)
    {
        rtl.ZAsRelative = false;
        rtl.ZIndex = HpPreviewOverlayZIndex;
        bar.MoveChild(rtl, bar.GetChildCount() - 1);
        SyncOverlayToHpLabel(bar, rtl, hpLabel);
    }

    private static void SyncOverlayToHpLabel(NHealthBar bar, RichTextLabel rtl, MegaLabel hpLabel)
    {
        rtl.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        rtl.GrowHorizontal = Control.GrowDirection.Begin;
        rtl.GrowVertical = Control.GrowDirection.Begin;
        var barCtrl = (Control)(Node)bar;
        var inv = barCtrl.GetGlobalTransformWithCanvas().AffineInverse();
        rtl.Position = inv * hpLabel.GlobalPosition + new Vector2(0, -HpPreviewVerticalNudgePx);
        rtl.Size = hpLabel.Size;
    }

    private static void ApplyRichTextStyle(RichTextLabel rtl, MegaLabel hpLabel)
    {
        var font = hpLabel.GetThemeFont("font");
        if (font != null)
            rtl.AddThemeFontOverride("normal_font", font);

        int fs = hpLabel.GetThemeFontSize("font_size");
        if (fs > 0)
            rtl.AddThemeFontSizeOverride("normal_font_size", Mathf.Max(6, fs - 2));

        Color body = hpLabel.GetThemeColor("font_color");
        if (body.A > 0.01f)
            rtl.AddThemeColorOverride("default_color", body);

        rtl.AddThemeConstantOverride("outline_size", HpPreviewOutlineSizeOuter);
        rtl.AddThemeColorOverride("font_outline_color", HpPreviewOutlineColor);
    }

    private static string ToHexRgb(Color c) =>
        $"#{(byte)(c.R * 255):X2}{(byte)(c.G * 255):X2}{(byte)(c.B * 255):X2}";

    private static void HidePreview(NHealthBar bar, MegaLabel hpLabel)
    {
        var rtl = bar.GetNodeOrNull<RichTextLabel>(RtfName);
        if (rtl != null && GodotObject.IsInstanceValid(rtl))
            rtl.Visible = false;
        hpLabel.Visible = true;
    }

    /// <summary>Removes the overlay node and restores vanilla HP label (single bar).</summary>
    internal static void CleanupPreviewOnBar(NHealthBar bar)
    {
        try
        {
            var rtl = bar.GetNodeOrNull<RichTextLabel>(RtfName);
            if (rtl != null && GodotObject.IsInstanceValid(rtl))
                rtl.QueueFree();
            RestoreHpLabelOnBar(bar);
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] CleanupPreviewOnBar: {ex}");
        }
    }

    private static bool IsPoisonLethal(Creature creature, int poisonDamage)
    {
        if (poisonDamage <= 0 || !creature.HasPower<PoisonPower>())
            return false;
        return poisonDamage >= creature.CurrentHp;
    }

    private static bool IsDoomLethal(Creature creature, int doomAmount, int poisonDamage)
    {
        if (doomAmount <= 0 || !creature.HasPower<DoomPower>())
            return false;
        return doomAmount >= creature.CurrentHp - poisonDamage;
    }

    /// <summary>
    /// Removes preview overlays after combat (rewards / map) so HP text does not persist on the wrong screen.
    /// </summary>
    internal static void CleanupAllHpPreviewOverlays(Node? root)
    {
        if (root == null)
            return;

        try
        {
            var found = new List<RichTextLabel>();
            CollectHpPreviewRtfs(root, found);
            foreach (var rtl in found)
            {
                if (!GodotObject.IsInstanceValid(rtl))
                    continue;
                if (rtl.GetParent() is NHealthBar bar)
                    RestoreHpLabelOnBar(bar);
                rtl.QueueFree();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] CleanupAllHpPreviewOverlays: {ex}");
        }
    }

    private static void CollectHpPreviewRtfs(Node node, List<RichTextLabel> acc)
    {
        foreach (var child in node.GetChildren())
        {
            if (!GodotObject.IsInstanceValid(child))
                continue;

            if (child.Name == RtfName && child is RichTextLabel rtl)
                acc.Add(rtl);
            else
                CollectHpPreviewRtfs(child, acc);
        }
    }

    private static void RestoreHpLabelOnBar(NHealthBar bar)
    {
        try
        {
            var hpLabel = Traverse.Create(bar).Field<MegaLabel>("_hpLabel").Value;
            if (hpLabel != null && GodotObject.IsInstanceValid(hpLabel))
                hpLabel.Visible = true;
        }
        catch
        {
            /* best-effort */
        }
    }
}

/// <summary>Clears HP preview as soon as the post-combat rewards UI appears.</summary>
[HarmonyPatch(typeof(NRewardsScreen), "_Ready")]
public static class PlayerHpPreviewRewardsScreenPatch
{
    private static void Postfix(NRewardsScreen __instance)
    {
        try
        {
            PlayerHpPreviewPatch.CleanupAllHpPreviewOverlays(__instance.GetTree()?.Root);
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] PlayerHpPreviewRewardsScreenPatch: {ex}");
        }
    }
}

/// <summary>
/// Keeps projected HP in sync when block/intents change without a full <see cref="NHealthBar.RefreshText"/> call.
/// </summary>
[HarmonyPatch(typeof(NCombatRoom), "_Ready")]
public static class PlayerHpPreviewRefreshTimerPatch
{
    private static void Postfix(NCombatRoom __instance)
    {
        try
        {
            if (__instance.Mode != CombatRoomMode.ActiveCombat)
                return;

            var timer = new Godot.Timer { WaitTime = 0.1, Autostart = true };
            __instance.AddChild(timer);
            timer.Timeout += () =>
            {
                try
                {
                    var cm = CombatManager.Instance;
                    var bar = FindLocalPlayerHealthBar(__instance);
                    if (bar == null)
                        return;

                    if (cm == null || !cm.IsInProgress)
                    {
                        PlayerHpPreviewPatch.CleanupPreviewOnBar(bar);
                        return;
                    }

                    PlayerHpPreviewPatch.ApplyPreview(bar);
                }
                catch (Exception ex)
                {
                    Log.Error($"[PathPlanner] PlayerHpPreviewRefreshTimerPatch: {ex}");
                }
            };
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] PlayerHpPreviewRefreshTimerPatch._Ready: {ex}");
        }
    }

    private static NHealthBar? FindLocalPlayerHealthBar(Node node)
    {
        if (node is NHealthBar bar)
        {
            var c = Traverse.Create(bar).Field<Creature>("_creature").Value;
            if (c?.Player != null && LocalContext.IsMe(c.Player))
                return bar;
        }

        foreach (var child in node.GetChildren())
        {
            var found = FindLocalPlayerHealthBar(child);
            if (found != null)
                return found;
        }

        return null;
    }
}

/// <summary>Subscribes once to <see cref="CombatManager.CombatEnded"/> so HP preview nodes are freed as soon as combat stops.</summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetUpCombat))]
public static class PlayerHpPreviewCombatEndedHookPatch
{
    private static bool _hookRegistered;

    private static void Postfix(CombatManager __instance)
    {
        if (_hookRegistered || __instance == null)
            return;
        __instance.CombatEnded += OnCombatEnded;
        _hookRegistered = true;
    }

    private static void OnCombatEnded(CombatRoom _)
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            PlayerHpPreviewPatch.CleanupAllHpPreviewOverlays(tree?.Root);
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] PlayerHpPreviewCombatEndedHookPatch: {ex}");
        }
    }
}
