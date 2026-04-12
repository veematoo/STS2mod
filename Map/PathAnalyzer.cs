using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Map;

namespace PathPlanner.Map;

/// <summary>
/// Counts the node types along a single root-to-boss path.
/// </summary>
public record PathStats(
    int Elites,
    int Shops,
    int Rests,
    int Monsters,
    int Unknowns,
    int Treasures,
    List<MapPoint> Nodes
);

/// <summary>
/// Aggregated statistics across ALL legal paths through an act map.
/// </summary>
public record MapPathSummary(
    int MinElites, int MaxElites,
    int MinShops,  int MaxShops,
    int MinRests,  int MaxRests,
    int MinMonsters, int MaxMonsters,
    int MinUnknowns, int MaxUnknowns,
    int TotalPaths,
    PathStats SafestPath,
    PathStats GreediestPath,
    Dictionary<int, List<PathStats>> PathsByEliteCount,
    Dictionary<int, List<PathStats>> PathsByShopCount,
    Dictionary<int, List<PathStats>> PathsByRestCount
);

public static class PathAnalyzer
{
    /// <summary>
    /// Enumerate every root-to-boss path in the map and return aggregate stats.
    /// </summary>
    public static MapPathSummary Analyze(ActMap map)
    {
        var allPaths = new List<PathStats>();
        var bossPoint = map.BossMapPoint;

        foreach (var start in map.startMapPoints)
            DFS(start, bossPoint, new List<MapPoint>(), allPaths);

        if (allPaths.Count == 0)
            return EmptySummary();

        var byElite = allPaths
            .GroupBy(p => p.Elites)
            .ToDictionary(g => g.Key, g => g.ToList());
        var byShop = allPaths
            .GroupBy(p => p.Shops)
            .ToDictionary(g => g.Key, g => g.ToList());
        var byRest = allPaths
            .GroupBy(p => p.Rests)
            .ToDictionary(g => g.Key, g => g.ToList());

        return new MapPathSummary(
            MinElites:    allPaths.Min(p => p.Elites),
            MaxElites:    allPaths.Max(p => p.Elites),
            MinShops:     allPaths.Min(p => p.Shops),
            MaxShops:     allPaths.Max(p => p.Shops),
            MinRests:     allPaths.Min(p => p.Rests),
            MaxRests:     allPaths.Max(p => p.Rests),
            MinMonsters:  allPaths.Min(p => p.Monsters),
            MaxMonsters:  allPaths.Max(p => p.Monsters),
            MinUnknowns:  allPaths.Min(p => p.Unknowns),
            MaxUnknowns:  allPaths.Max(p => p.Unknowns),
            TotalPaths:   allPaths.Count,
            SafestPath:   allPaths.OrderBy(p => p.Elites).ThenByDescending(p => p.Rests + p.Shops).First(),
            GreediestPath: allPaths.OrderByDescending(p => p.Elites).First(),
            PathsByEliteCount: byElite,
            PathsByShopCount: byShop,
            PathsByRestCount: byRest
        );
    }

    private static void DFS(MapPoint current, MapPoint boss, List<MapPoint> path, List<PathStats> results)
    {
        path.Add(current);

        if (current.coord.Equals(boss.coord))
        {
            results.Add(CountPath(path));
            path.RemoveAt(path.Count - 1);
            return;
        }

        if (current.Children.Count == 0)
        {
            path.RemoveAt(path.Count - 1);
            return;
        }

        foreach (var child in current.Children)
            DFS(child, boss, path, results);

        path.RemoveAt(path.Count - 1);
    }

    private static PathStats CountPath(List<MapPoint> path)
    {
        int elites = 0, shops = 0, rests = 0, monsters = 0, unknowns = 0, treasures = 0;
        foreach (var node in path)
        {
            switch (node.PointType)
            {
                case MapPointType.Elite:    elites++;   break;
                case MapPointType.Shop:     shops++;    break;
                case MapPointType.RestSite: rests++;    break;
                case MapPointType.Monster:  monsters++; break;
                case MapPointType.Unknown:  unknowns++; break;
                case MapPointType.Treasure: treasures++; break;
            }
        }
        return new PathStats(elites, shops, rests, monsters, unknowns, treasures, new List<MapPoint>(path));
    }

    private static MapPathSummary EmptySummary() =>
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            new PathStats(0, 0, 0, 0, 0, 0, new List<MapPoint>()),
            new PathStats(0, 0, 0, 0, 0, 0, new List<MapPoint>()),
            new Dictionary<int, List<PathStats>>(),
            new Dictionary<int, List<PathStats>>(),
            new Dictionary<int, List<PathStats>>());
}
