using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace PathPlanner.Patches;

// InvokeDrawn fires on every client when any player draws a card.
// (STS2 multiplayer is a synchronized state machine — both clients step through
// the same game actions.) We skip local draws and show a speech bubble above
// the partner's character for the partner's draws.
// This lets the partner coordinate — e.g. "let me land the killing blow, I'll
// get a bonus effect."

[HarmonyPatch(typeof(CardModel), "InvokeDrawn")]
public static class CardDrawPatch
{
    // "Fatal" hover tip ID — cards that trigger only on non-minion kills
    // (Feed, HandOfGreed, TheHunt). Computed lazily on first draw.
    private static string? _fatalId;
    private static string FatalId =>
        _fatalId ??= HoverTipFactory.Static(StaticHoverTip.Fatal).Id;

    // Vulnerable hover tip ID — all Vulnerable-applying cards have this tip.
    // Checking hover tips (not DynamicVars key) catches cards like Putrefy that
    // use a generic "Power" key rather than "VulnerablePower".
    private static string? _vulnerableId;
    private static string VulnerableId =>
        _vulnerableId ??= HoverTipFactory.FromPower<VulnerablePower>().Id;

    // "If this kills" cards — trigger on ANY kill, including minions.
    // No shared game tag exists for this category; identified by class name.
    private static readonly HashSet<string> AnyKillCards = new()
    {
        "EchoingSlash",   // re-attacks for each kill
        "KnockoutBlow",   // gains Stars on kill
        "Sunder",         // gains Energy on kill
    };

    /// <summary>
    /// Cards where teammates usually want to act after this player (order-sensitive).
    /// Extend with more <see cref="CardModel"/> type names as needed.
    /// </summary>
    private static readonly HashSet<string> PlayFirstPriorityCards = new()
    {
        "Strangle",
        "Coordinate",
    };

    internal static bool IsPlayFirstPriorityCard(CardModel card)
    {
        try
        {
            return PlayFirstPriorityCards.Contains(card.GetType().Name);
        }
        catch
        {
            return false;
        }
    }

    static void Postfix(CardModel __instance)
    {
        try
        {
            var combat = NCombatRoom.Instance;
            if (combat == null) return;

            var owner = __instance.Owner;
            if (owner is null) return;
            if (LocalContext.IsMe(owner)) return;

            string cardName = __instance.Title;

            // ── Play order (teammates should usually let this player go first) ─
            if (IsPlayFirstPriorityCard(__instance))
            {
                ShowBubble(combat, owner.Creature,
                    $"{cardName}! (let them play first)",
                    VfxColor.Orange);
                Log.Info($"[PathPlanner] Partner drew play-order card: {cardName}");
                return;
            }

            // ── Vulnerable ────────────────────────────────────────────────────
            if (__instance.HoverTips.Any(t => t.Id == VulnerableId))
            {
                ShowBubble(combat, owner.Creature,
                    $"{cardName}! (applies Vulnerable)",
                    VfxColor.Red);
                Log.Info($"[PathPlanner] Partner drew Vulnerable card: {cardName}");
                return;
            }

            // ── Enemy strength reduction ──────────────────────────────────────
            if (__instance.DynamicVars.ContainsKey("StrengthLoss"))
            {
                ShowBubble(combat, owner.Creature,
                    $"{cardName}! (weakens enemies)",
                    VfxColor.Green);
                Log.Info($"[PathPlanner] Partner drew strength-reduction card: {cardName}");
                return;
            }

            // ── Teammate-affecting (Lift, Energy Surge, Rally, etc.) ──────────
            // AnyAlly / AllAllies are multiplayer-only targeting modes on cards.
            var targetType = __instance.TargetType;
            if (targetType == TargetType.AnyAlly || targetType == TargetType.AllAllies)
            {
                string hint = targetType == TargetType.AllAllies
                    ? "all teammates"
                    : "teammate target";
                ShowBubble(combat, owner.Creature,
                    $"{cardName}! ({hint})",
                    VfxColor.Cyan);
                Log.Info($"[PathPlanner] Partner drew teammate card: {cardName}");
                return;
            }

            // ── "If Fatal" (non-minion kill only) ─────────────────────────────
            if (__instance.HoverTips.Any(t => t.Id == FatalId))
            {
                ShowBubble(combat, owner.Creature,
                    $"{cardName}! (If Fatal)",
                    VfxColor.Purple);
                Log.Info($"[PathPlanner] Partner drew Fatal card: {cardName}");
                return;
            }

            // ── "If this kills" (any kill, including minions) ─────────────────
            if (AnyKillCards.Contains(__instance.GetType().Name))
            {
                ShowBubble(combat, owner.Creature,
                    $"{cardName}! (If this kills)",
                    VfxColor.Blue);
                Log.Info($"[PathPlanner] Partner drew any-kill card: {cardName}");
            }
        }
        catch (System.Exception ex)
        {
            Log.Error($"[PathPlanner] CardDrawPatch error: {ex}");
        }
    }

    private static void ShowBubble(NCombatRoom combat, MegaCrit.Sts2.Core.Entities.Creatures.Creature creature,
        string text, VfxColor color)
    {
        var bubble = NSpeechBubbleVfx.Create(text, creature, 3.0, color);
        if (bubble != null)
            combat.CombatVfxContainer.AddChild(bubble);
    }

    // ── Shared card classifier ─────────────────────────────────────────────
    // Returns a Godot Color that matches the speech-bubble category colours,
    // or null for an ordinary card (use the default text colour).

    internal static Godot.Color? GetHighlightColor(CardModel card)
    {
        try
        {
            if (IsPlayFirstPriorityCard(card))
                return new Godot.Color(1f, 0.62f, 0.18f); // orange — let them play first

            if (card.HoverTips.Any(t => t.Id == VulnerableId))
                return new Godot.Color(1f, 0.35f, 0.35f);        // red   – Vulnerable

            if (card.DynamicVars.ContainsKey("StrengthLoss"))
                return new Godot.Color(0.35f, 1f, 0.45f);         // green – weakens enemies

            var tt = card.TargetType;
            if (tt == TargetType.AnyAlly || tt == TargetType.AllAllies)
                return new Godot.Color(0.3f, 0.9f, 1f);           // cyan  – teammate card

            if (card.HoverTips.Any(t => t.Id == FatalId))
                return new Godot.Color(0.8f, 0.45f, 1f);          // purple – If Fatal

            if (AnyKillCards.Contains(card.GetType().Name))
                return new Godot.Color(0.45f, 0.65f, 1f);         // blue  – If this kills
        }
        catch { /* best-effort */ }

        return null; // ordinary card
    }
}
