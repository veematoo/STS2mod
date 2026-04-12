using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace PathPlanner.Stats;

/// <summary>
/// Starter cards match <see cref="MegaCrit.Sts2.Core.Models.CharacterModel.StartingDeck"/> for every character in
/// <see cref="ModelDb.AllCharacters"/> (Ironclad, Silent, Regent, Necrobinder, Defect per sts2 decompile).
/// </summary>
public static class WinRateStarterCardRegistry
{
    private static readonly object Gate = new();
    private static HashSet<string>? _entriesByCaseInsensitive;

    /// <summary>
    /// True if this card's <see cref="CardModel.Id"/>.Entry appears in any official starting deck.
    /// </summary>
    public static bool IsStarterDeckCard(CardModel model)
    {
        EnsureBuilt();
        return _entriesByCaseInsensitive != null
               && _entriesByCaseInsensitive.Contains(model.Id.Entry);
    }

    /// <summary>Same as <see cref="IsStarterDeckCard"/> but when only <see cref="CardModel.Id"/>.<c>Entry</c> is known.</summary>
    public static bool IsStarterDeckEntry(string entry)
    {
        if (string.IsNullOrEmpty(entry))
            return false;
        EnsureBuilt();
        return _entriesByCaseInsensitive != null
               && _entriesByCaseInsensitive.Contains(entry);
    }

    private static void EnsureBuilt()
    {
        lock (Gate)
        {
            if (_entriesByCaseInsensitive != null)
                return;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var card in ModelDb.AllCharacters.SelectMany(c => c.StartingDeck))
                    set.Add(card.Id.Entry);
            }
            catch (Exception ex)
            {
                Log.Warn($"[PathPlanner] WinRateStarterCardRegistry: could not read ModelDb.AllCharacters.StartingDeck: {ex.Message}");
            }

            _entriesByCaseInsensitive = set;
        }
    }
}
