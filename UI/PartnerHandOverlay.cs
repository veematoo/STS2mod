using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using PathPlanner.Patches;

namespace PathPlanner.UI;

/// <summary>
/// Side panel listing each co-op partner's current hand.
/// </summary>
internal sealed class PartnerHandOverlay : CanvasLayer
{
    private const float PreviewScale = 0.55f;

    private readonly Dictionary<ulong, List<CardModel>> _hands = new();
    private readonly Dictionary<ulong, string> _names = new();
    private readonly Dictionary<ulong, Player> _partners = new();

    private readonly VBoxContainer _cardList = new();
    private Label? _titleLabel;

    private CanvasLayer _previewLayer = null!;
    private Control _previewHost = null!;
    private NCard? _previewCard;
    private Godot.Timer _hidePreviewTimer = null!;

    private string _lastSignature = "";
    private bool _subscribed;
    private int _tickCount;

    private PartnerHandOverlay() { Layer = 8; }

    /// <summary>Removes all partner-hand overlays from the current combat room (e.g. feature disabled).</summary>
    internal static void DetachAll()
    {
        var room = NCombatRoom.Instance;
        if (room == null || !GodotObject.IsInstanceValid(room))
            return;
        foreach (var child in room.GetChildren())
        {
            if (child is PartnerHandOverlay o)
                o.QueueFree();
        }
    }

    internal static void Attach(Node combatRoom)
    {
        if (RunManager.Instance.IsSinglePlayerOrFakeMultiplayer)
            return;

        var overlay = new PartnerHandOverlay();
        combatRoom.AddChild(overlay);
        overlay.BuildUi();

        CombatManager.Instance.CombatSetUp += overlay.OnCombatSetUp;

        var timer = new Godot.Timer();
        timer.WaitTime  = 0.12;
        timer.Autostart = true;
        overlay.AddChild(timer);
        timer.Timeout += overlay.Refresh;
    }

    private void OnCombatSetUp(CombatState state)
    {
        CombatManager.Instance.CombatSetUp -= OnCombatSetUp;
        Log.Info($"[PathPlanner] PartnerHandOverlay.OnCombatSetUp: {state.Players.Count} player(s)");

        foreach (var player in state.Players)
        {
            if (player == null) continue;
            if (LocalContext.IsMe(player)) continue;

            ulong partnerNet = player.NetId;
            string name = SafePlayerName(player);
            Log.Info($"[PathPlanner] partner netId={partnerNet} name={name} " +
                     $"pcs={(player.PlayerCombatState != null ? "OK" : "NULL")}");

            _hands[partnerNet] = new List<CardModel>();
            _names[partnerNet] = name;
            _partners[partnerNet] = player;

            if (player.PlayerCombatState == null)
            {
                Log.Error("[PathPlanner] PlayerCombatState is null — cannot subscribe.");
                continue;
            }

            var hand = player.PlayerCombatState.Hand;

            foreach (var card in hand.Cards)
                if (!_hands[partnerNet].Contains(card))
                    _hands[partnerNet].Add(card);

            ulong capturedNet = partnerNet;
            hand.CardAdded += card =>
            {
                if (_hands.TryGetValue(capturedNet, out var list) && !list.Contains(card))
                    list.Add(card);
            };
            hand.CardRemoved += card =>
            {
                if (_hands.TryGetValue(capturedNet, out var list))
                    list.Remove(card);
            };

            _subscribed = true;
        }
    }

    private void BuildUi()
    {
        var anchor = new Control();
        anchor.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        anchor.GrowHorizontal = Control.GrowDirection.Begin;
        anchor.GrowVertical   = Control.GrowDirection.End;
        anchor.OffsetRight    = -8;
        anchor.OffsetTop      = 72;
        anchor.MouseFilter    = Control.MouseFilterEnum.Ignore;
        AddChild(anchor);

        var panel = new PanelContainer();
        panel.MouseFilter         = Control.MouseFilterEnum.Stop;
        panel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        panel.SizeFlagsVertical   = Control.SizeFlags.ShrinkBegin;
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        panel.GrowHorizontal = Control.GrowDirection.Begin;
        panel.GrowVertical   = Control.GrowDirection.End;

        var flat = new StyleBoxFlat();
        flat.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.90f);
        flat.SetCornerRadiusAll(8);
        flat.ContentMarginLeft   = 10;
        flat.ContentMarginRight  = 10;
        flat.ContentMarginTop    = 8;
        flat.ContentMarginBottom = 10;
        panel.AddThemeStyleboxOverride("panel", flat);
        anchor.AddChild(panel);

        var outer = new VBoxContainer();
        outer.CustomMinimumSize = new Vector2(260, 0);
        outer.AddThemeConstantOverride("separation", 4);
        panel.AddChild(outer);

        _titleLabel = new Label();
        _titleLabel.Text = "── Partner hand ──";
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.85f, 1f));
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        outer.AddChild(_titleLabel);

        _cardList.CustomMinimumSize = new Vector2(260, 0);
        _cardList.AddThemeConstantOverride("separation", 3);
        outer.AddChild(_cardList);

        _previewLayer = new CanvasLayer { Layer = 9 };
        AddChild(_previewLayer);

        _previewHost = new Control();
        _previewHost.Visible = false;
        _previewHost.MouseFilter = Control.MouseFilterEnum.Stop;
        _previewHost.MouseEntered += () => _hidePreviewTimer.Stop();
        _previewHost.MouseExited += OnPreviewHostMouseExited;
        _previewLayer.AddChild(_previewHost);

        _hidePreviewTimer = new Godot.Timer { OneShot = true, WaitTime = 0.2 };
        AddChild(_hidePreviewTimer);
        _hidePreviewTimer.Timeout += OnHidePreviewDebounced;

        SetStatus("…initialising…");
    }

    private void OnPreviewHostMouseExited() => _hidePreviewTimer.Start();

    private void OnHidePreviewDebounced()
    {
        try
        {
            var mouse = GetViewport().GetMousePosition();
            if (_previewHost.Visible && _previewHost.GetGlobalRect().HasPoint(mouse))
                return;
            foreach (var c in _cardList.GetChildren())
            {
                if (c is Control ctrl && ctrl.Visible && ctrl.GetGlobalRect().HasPoint(mouse))
                    return;
            }
            HideCardPreview();
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] OnHidePreviewDebounced: {ex}");
        }
    }

    private void ShowCardPreview(CardModel card, Control row, Player partner)
    {
        try
        {
            _hidePreviewTimer.Stop();

            // Reassigning Model on a pooled/reused NCard does not reliably refresh all UI; always rebuild.
            DisposePreviewCard();
            _previewCard = NCard.Create(card, ModelVisibility.Visible);
            _previewHost.AddChild(_previewCard);

            var preview = _previewCard!;
            preview.Scale = new Vector2(PreviewScale, PreviewScale);
            // NCard resolves title/body using the local player unless we bind the owning teammate's creature.
            // card.Owner is not reliable for remote hands on the client; use the Player we subscribed with.
            ApplyPreviewPerspective(preview, partner);
            _previewHost.Visible = true;
            Callable.From(() => PositionPreview(row)).CallDeferred();
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] ShowCardPreview: {ex}");
        }
    }

    private void DisposePreviewCard()
    {
        if (_previewCard == null) return;
        var node = _previewCard;
        _previewCard = null;
        if (GodotObject.IsInstanceValid(node))
            node.QueueFree();
    }

    private void PositionPreview(Control row)
    {
        if (_previewCard == null || !_previewHost.Visible)
            return;

        var rowRect = row.GetGlobalRect();
        var cardSize = _previewCard.GetCurrentSize();
        var scaled = new Vector2(cardSize.X * PreviewScale, cardSize.Y * PreviewScale);

        var vp = GetViewport().GetVisibleRect();
        float leftX = rowRect.Position.X - scaled.X - 8f;
        leftX = Mathf.Max(vp.Position.X + 4f, leftX);
        float y = Mathf.Clamp(
            rowRect.Position.Y,
            vp.Position.Y + 4f,
            vp.End.Y - scaled.Y - 4f);

        _previewHost.GlobalPosition = new Vector2(leftX, y);
        _previewHost.Size = scaled;
    }

    private void HideCardPreview()
    {
        _previewHost.Visible = false;
        DisposePreviewCard();
    }

    /// <summary>
    /// Without this, <see cref="NCard"/> keeps using the local player's perspective for name/description
    /// while still showing the correct art for the hovered <see cref="CardModel"/>.
    /// </summary>
    private static void ApplyPreviewPerspective(NCard preview, Player partner)
    {
        preview.SetPreviewTarget(partner.Creature);
    }

    private void Refresh()
    {
        try
        {
            _tickCount++;

            if (!_subscribed)
            {
                var state = CombatManager.Instance?.DebugOnlyGetState();
                if (state == null)
                {
                    SetStatus($"waiting for combat (tick {_tickCount})");
                    return;
                }
                OnCombatSetUp(state);
            }

            if (!_subscribed)
            {
                SetStatus($"no partner found (tick {_tickCount})");
                return;
            }

            var entries = new List<(string text, CardModel? card, Player? partner)>();
            foreach (var (netId, cards) in _hands)
            {
                string pname = _names.GetValueOrDefault(netId, "Partner");
                _partners.TryGetValue(netId, out var partner);
                if (cards.Count == 0)
                    entries.Add(($"{pname}: (empty hand)", null, partner));
                else
                    foreach (var c in cards)
                    {
                        var line = $"{pname}: {c.Title}{(c.IsUpgraded ? "+" : "")}";
                        if (CardDrawPatch.IsPlayFirstPriorityCard(c))
                            line += " — let play first";
                        entries.Add((line, c, partner));
                    }
            }
            if (entries.Count == 0)
                entries.Add(("(no other players)", null, null));

            string sig = string.Join("\u001f", System.Linq.Enumerable.Select(entries, e => e.text));
            if (sig == _lastSignature) return;
            _lastSignature = sig;

            _hidePreviewTimer.Stop();
            HideCardPreview();

            foreach (var child in _cardList.GetChildren())
                child.QueueFree();
            foreach (var (text, card, partner) in entries)
                AddCardRow(text, card, partner, card != null ? CardDrawPatch.GetHighlightColor(card) : null);

            if (_titleLabel != null)
                _titleLabel.Text = "── Partner hand ──";
        }
        catch (Exception ex)
        {
            Log.Error($"[PathPlanner] PartnerHandOverlay.Refresh: {ex}");
            SetStatus($"ERROR — see log (tick {_tickCount})");
        }
    }

    private void SetStatus(string msg)
    {
        if (msg == _lastSignature) return;
        _lastSignature = msg;

        _hidePreviewTimer.Stop();
        HideCardPreview();

        foreach (var child in _cardList.GetChildren())
            child.QueueFree();

        AddCardRow(msg, null, null, new Color(0.6f, 0.6f, 0.65f));
    }

    private static readonly Color DefaultTextColor = new Color(0.95f, 0.95f, 0.97f);

    private void AddCardRow(string text, CardModel? card, Player? partner, Color? highlight = null)
    {
        var row = new Control();
        row.MouseFilter = Control.MouseFilterEnum.Stop;
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.CustomMinimumSize = new Vector2(0, 22);

        var lbl = new Label();
        lbl.Text = text;
        lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        lbl.AddThemeColorOverride("font_color", highlight ?? DefaultTextColor);
        lbl.AddThemeFontSizeOverride("font_size", 14);
        lbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        lbl.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        lbl.OffsetLeft = 0;
        lbl.OffsetTop = 0;
        lbl.OffsetRight = 0;
        lbl.OffsetBottom = 0;
        row.AddChild(lbl);

        if (card != null && partner != null)
        {
            var capturedCard = card;
            var capturedPartner = partner;
            row.MouseEntered += () =>
            {
                _hidePreviewTimer.Stop();
                ShowCardPreview(capturedCard, row, capturedPartner);
            };
            row.MouseExited += () => _hidePreviewTimer.Start();
        }

        _cardList.AddChild(row);
    }

    private static string SafePlayerName(Player p)
    {
        try { return p.Character.Title.GetFormattedText(); }
        catch { return "Partner"; }
    }
}
