using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;

namespace PathPlanner.Stats;

/// <summary>
/// Aggregates win rates from on-disk run history (<c>*.run</c>) for picked cards, relics, and event options.
/// Per-run-once: a choice counts at most once toward totals for that run; abandoned runs are skipped.
/// When a run is in progress, card/relic/event stats only include history from runs played as the
/// <b>current</b> character (same <see cref="ModelId"/> as <c>LocalContext.GetMe</c>'s character), so colorless
/// reward rates are compared within the same roster.
/// </summary>
public static class RunHistoryWinRateAggregator
{
    private static readonly object Gate = new();

    private static IReadOnlyList<string>? _cachedFileNames;
    private static string? _cachedCharacterFilterKey;
    private static Dictionary<ModelId, (int Wins, int Total)> _cardStats = new();
    private static Dictionary<ModelId, (int Wins, int Total)> _relicStats = new();
    private static Dictionary<string, (int Wins, int Total)> _eventStats = new(StringComparer.Ordinal);

    /// <summary>Force the next <see cref="EnsureFresh"/> to rescan all history files.</summary>
    public static void InvalidateCache()
    {
        lock (Gate)
        {
            _cachedFileNames = null;
            _cachedCharacterFilterKey = null;
        }
    }

    public static void EnsureFresh()
    {
        List<string>? namesSorted;
        try
        {
            var raw = SaveManager.Instance.GetAllRunHistoryNames();
            namesSorted = raw.OrderBy(n => n, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex)
        {
            Log.Warn($"[PathPlanner] WinRate: SaveManager not ready: {ex.Message}");
            return;
        }

        var characterKey = GetLocalCharacterFilterKey();

        lock (Gate)
        {
            if (_cachedFileNames != null
                && NamesEqual(_cachedFileNames, namesSorted)
                && string.Equals(_cachedCharacterFilterKey, characterKey, StringComparison.Ordinal))
                return;

            RebuildLocked(namesSorted, characterKey);
            _cachedFileNames = namesSorted;
            _cachedCharacterFilterKey = characterKey;
        }
    }

    public static bool TryGetCardRate(ModelId id, out int wins, out int total)
    {
        EnsureFresh();
        lock (Gate)
        {
            if (_cardStats.TryGetValue(id, out var t) && t.Total > 0)
            {
                wins = t.Wins;
                total = t.Total;
                return true;
            }

            var sumW = 0;
            var sumT = 0;
            foreach (var kv in _cardStats)
            {
                if (!ModelIdMatchesLoose(kv.Key, id) || kv.Value.Total <= 0)
                    continue;
                sumW += kv.Value.Wins;
                sumT += kv.Value.Total;
            }

            if (sumT > 0)
            {
                wins = sumW;
                total = sumT;
                return true;
            }

            wins = 0;
            total = 0;
            return false;
        }
    }

    private static bool ModelIdMatchesLoose(ModelId a, ModelId b)
    {
        return string.Equals(a.Category, b.Category, StringComparison.OrdinalIgnoreCase)
               && string.Equals(a.Entry, b.Entry, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetRelicRate(ModelId id, out int wins, out int total)
    {
        EnsureFresh();
        lock (Gate)
        {
            if (_relicStats.TryGetValue(id, out var t) && t.Total > 0)
            {
                wins = t.Wins;
                total = t.Total;
                return true;
            }

            wins = 0;
            total = 0;
            return false;
        }
    }

    public static bool TryGetEventRate(LocString title, out int wins, out int total)
    {
        var key = EventLocKey(title);
        if (string.IsNullOrEmpty(key))
        {
            wins = 0;
            total = 0;
            return false;
        }

        EnsureFresh();
        lock (Gate)
        {
            if (_eventStats.TryGetValue(key, out var t) && t.Total > 0)
            {
                wins = t.Wins;
                total = t.Total;
                return true;
            }

            wins = 0;
            total = 0;
            return false;
        }
    }

    public static string FormatRate(int wins, int total)
    {
        if (total <= 0)
            return "Win: —";
        int pct = (int)Math.Round(100.0 * wins / total);
        return $"Win: {pct}% ({wins}/{total})";
    }

    public static string EventLocKey(LocString loc)
    {
        if (string.IsNullOrEmpty(loc.LocTable) || string.IsNullOrEmpty(loc.LocEntryKey))
            return string.Empty;
        return loc.LocTable + "|" + loc.LocEntryKey;
    }

    private static bool NamesEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static string? GetLocalCharacterFilterKey()
    {
        var id = TryGetLocalCharacterId();
        if (id is not { } v || v == ModelId.none)
            return null;
        return v.Category + "|" + v.Entry;
    }

    private static ModelId? TryGetLocalCharacterId()
    {
        try
        {
            if (!RunManager.Instance.IsInProgress)
                return null;
            var state = RunManager.Instance.DebugOnlyGetState();
            if (state == null)
                return null;
            var me = LocalContext.GetMe(state);
            return me?.Character?.Id;
        }
        catch
        {
            return null;
        }
    }

    private static void RebuildLocked(List<string> fileNames, string? characterFilterKey)
    {
        _cardStats = new Dictionary<ModelId, (int, int)>();
        _relicStats = new Dictionary<ModelId, (int, int)>();
        _eventStats = new Dictionary<string, (int, int)>(StringComparer.Ordinal);

        var filterCharacter = characterFilterKey is null
            ? (ModelId?)null
            : ParseCharacterKey(characterFilterKey);

        foreach (var file in fileNames)
        {
            if (!file.EndsWith(".run", StringComparison.OrdinalIgnoreCase))
                continue;

            ReadSaveResult<RunHistory> read;
            try
            {
                read = SaveManager.Instance.LoadRunHistory(file);
            }
            catch (Exception ex)
            {
                Log.Warn($"[PathPlanner] WinRate: load {file}: {ex.Message}");
                continue;
            }

            if (!read.Success || read.SaveData == null)
                continue;

            var run = read.SaveData;
            if (run.WasAbandoned)
                continue;

            if (!TryGetPlayerFilter(run, out var filterPlayerId))
                continue;

            if (filterCharacter is { } wantChar
                && wantChar != ModelId.none
                && filterPlayerId.HasValue
                && TryGetRunCharacter(run, filterPlayerId.Value, out var runChar)
                && runChar != ModelId.none
                && !ModelIdMatchesLoose(runChar, wantChar))
            {
                continue;
            }

            var cardPicks = new HashSet<ModelId>();
            var relicPicks = new HashSet<ModelId>();
            var eventPicks = new HashSet<string>(StringComparer.Ordinal);

            foreach (var act in run.MapPointHistory ?? Enumerable.Empty<List<MapPointHistoryEntry>>())
            {
                foreach (var pt in act)
                {
                    foreach (var ps in pt.PlayerStats)
                    {
                        if (filterPlayerId.HasValue && ps.PlayerId != filterPlayerId.Value)
                            continue;

                        foreach (var cc in ps.CardChoices)
                        {
                            if (!cc.wasPicked)
                                continue;
                            var id = cc.Card.Id;
                            if (id is not { } idv || idv == ModelId.none)
                                continue;
                            cardPicks.Add(idv);
                        }

                        foreach (var rc in ps.RelicChoices)
                        {
                            if (!rc.wasPicked || rc.choice == ModelId.none)
                                continue;
                            relicPicks.Add(rc.choice);
                        }

                        foreach (var ev in ps.EventChoices)
                        {
                            var ek = EventLocKey(ev.Title);
                            if (!string.IsNullOrEmpty(ek))
                                eventPicks.Add(ek);
                        }
                    }
                }
            }

            void Bump<TKey>(Dictionary<TKey, (int W, int T)> dict, IEnumerable<TKey> keys, bool won) where TKey : notnull
            {
                foreach (var k in keys)
                {
                    dict.TryGetValue(k, out var cur);
                    dict[k] = (cur.W + (won ? 1 : 0), cur.T + 1);
                }
            }

            Bump(_cardStats, cardPicks, run.Win);
            Bump(_relicStats, relicPicks, run.Win);
            Bump(_eventStats, eventPicks, run.Win);
        }
    }

    private static ModelId ParseCharacterKey(string key)
    {
        var i = key.IndexOf('|', StringComparison.Ordinal);
        if (i <= 0 || i >= key.Length - 1)
            return ModelId.none;
        return new ModelId(key[..i], key[(i + 1)..]);
    }

    private static bool TryGetRunCharacter(RunHistory run, ulong playerNetId, out ModelId characterId)
    {
        characterId = ModelId.none;
        foreach (var p in run.Players ?? Enumerable.Empty<RunHistoryPlayer>())
        {
            if (p.Id != playerNetId)
                continue;
            characterId = p.Character;
            return characterId != ModelId.none;
        }

        return false;
    }

    /// <summary>
    /// Map history is authoritative. Returns <c>(null)</c> to include every player's row when multiple
    /// NetIds exist and we cannot match <see cref="LocalContext.NetId"/> (e.g. history read outside a run).
    /// </summary>
    private static bool TryGetPlayerFilter(RunHistory run, out ulong? filterPlayerId)
    {
        var mapIds = CollectUniquePlayerIdsFromMap(run);
        if (mapIds.Count == 0)
        {
            filterPlayerId = null;
            return false;
        }

        if (LocalContext.NetId.HasValue && mapIds.Contains(LocalContext.NetId.Value))
        {
            filterPlayerId = LocalContext.NetId.Value;
            return true;
        }

        if (mapIds.Count == 1)
        {
            filterPlayerId = mapIds.First();
            return true;
        }

        filterPlayerId = null;
        return true;
    }

    private static HashSet<ulong> CollectUniquePlayerIdsFromMap(RunHistory run)
    {
        var set = new HashSet<ulong>();
        foreach (var act in run.MapPointHistory ?? Enumerable.Empty<List<MapPointHistoryEntry>>())
        {
            foreach (var pt in act)
            {
                foreach (var ps in pt.PlayerStats)
                    set.Add(ps.PlayerId);
            }
        }

        return set;
    }
}
