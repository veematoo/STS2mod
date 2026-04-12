using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Logging;

namespace PathPlanner.Map;

public static class MapHighlighter
{
    private static readonly Color ColDim      = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color ColDefault  = new Color(1f, 1f, 1f, 1f);

    private static readonly Color ColShopOnly     = new Color(0.35f, 1.0f, 0.40f, 1f);
    private static readonly Color ColRestOnly       = new Color(0.40f, 0.70f, 1.0f, 1f);
    private static readonly Color ColShopAndRest    = new Color(0.80f, 0.45f, 1.0f, 1f);
    private static readonly Color ColNeither        = new Color(1.0f,  0.75f, 0.20f, 1f);

    private static FieldInfo? _pathsField;

    /// <summary>
    /// Among paths matching the chosen filter, color edges by shop/rest presence on that path.
    /// <see cref="PathFilterAxis.Any"/> leaves all connections dimmed (no subset).
    /// </summary>
    public static void Apply(NMapScreen? screen, MapPathSummary? summary, PathFilterAxis axis, int count)
    {
        if (screen == null || summary == null) return;

        var pathSprites = GetPathSprites(screen);
        if (pathSprites == null) return;

        foreach (var sprites in pathSprites.Values)
            SetColor(sprites, ColDim);

        if (axis == PathFilterAxis.Any)
            return;

        if (!TryGetMatchingPaths(summary, axis, count, out var matchingPaths) || matchingPaths.Count == 0)
            return;

        var edgeColor = new Dictionary<(MapCoord, MapCoord), Color>();

        foreach (var path in matchingPaths)
        {
            bool hasShop = path.Shops > 0;
            bool hasRest = path.Rests > 0;
            Color c = (hasShop, hasRest) switch
            {
                (true, true)   => ColShopAndRest,
                (true, false)  => ColShopOnly,
                (false, true)  => ColRestOnly,
                _              => ColNeither,
            };

            for (int i = 0; i < path.Nodes.Count - 1; i++)
            {
                var key = (path.Nodes[i].coord, path.Nodes[i + 1].coord);
                if (!edgeColor.TryGetValue(key, out Color existing) || ColorPriority(c) > ColorPriority(existing))
                    edgeColor[key] = c;
            }
        }

        foreach (var (edge, color) in edgeColor)
        {
            if (pathSprites.TryGetValue(edge, out var sprites))
                SetColor(sprites, color);
        }
    }

    private static bool TryGetMatchingPaths(
        MapPathSummary summary,
        PathFilterAxis axis,
        int count,
        out List<PathStats> paths)
    {
        if (axis == PathFilterAxis.Elites && summary.PathsByEliteCount.TryGetValue(count, out var pe))
        {
            paths = pe;
            return true;
        }
        if (axis == PathFilterAxis.Shops && summary.PathsByShopCount.TryGetValue(count, out var ps))
        {
            paths = ps;
            return true;
        }
        if (axis == PathFilterAxis.Rests && summary.PathsByRestCount.TryGetValue(count, out var pr))
        {
            paths = pr;
            return true;
        }
        paths = new List<PathStats>();
        return false;
    }

    public static void Reset(NMapScreen? screen)
    {
        if (screen == null) return;
        var pathSprites = GetPathSprites(screen);
        if (pathSprites == null) return;
        foreach (var sprites in pathSprites.Values)
            SetColor(sprites, ColDefault);
    }

    private static void SetColor(IReadOnlyList<TextureRect> sprites, Color color)
    {
        foreach (var sprite in sprites)
            if (GodotObject.IsInstanceValid(sprite))
                sprite.Modulate = color;
    }

    private static int ColorPriority(Color c)
    {
        if (c == ColShopAndRest) return 4;
        if (c == ColShopOnly)    return 3;
        if (c == ColRestOnly)    return 2;
        if (c == ColNeither)     return 1;
        return 0;
    }

    private static Dictionary<(MapCoord, MapCoord), IReadOnlyList<TextureRect>>? GetPathSprites(NMapScreen screen)
    {
        try
        {
            _pathsField ??= typeof(NMapScreen).GetField(
                "_paths",
                BindingFlags.NonPublic | BindingFlags.Instance);

            return _pathsField?.GetValue(screen)
                as Dictionary<(MapCoord, MapCoord), IReadOnlyList<TextureRect>>;
        }
        catch (System.Exception ex)
        {
            Log.Error($"[PathPlanner] GetPathSprites reflection error: {ex}");
            return null;
        }
    }
}
