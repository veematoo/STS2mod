using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using PathPlanner.Map;

namespace PathPlanner.Patches;

[HarmonyPatch(typeof(NMapScreen), "SetMap")]
public static class MapScreenSetMapPatch
{
    internal static CanvasLayer?     Layer;
    internal static Label?           StatsLabel;
    internal static Label?          CountFilterLabel;
    internal static HBoxContainer?  CountButtonRow;
    internal static MapPathSummary? Summary;
    internal static NMapScreen?      Screen;

    internal static PathFilterAxis SelectedAxis = PathFilterAxis.Any;
    internal static int              SelectedCount = -1;

    /// <summary>Removes the path overlay and clears highlight state (e.g. feature disabled from settings).</summary>
    internal static void DisposeMapOverlay()
    {
        if (Layer != null && GodotObject.IsInstanceValid(Layer))
        {
            Layer.QueueFree();
            Layer = null;
            StatsLabel = null;
            CountFilterLabel = null;
            CountButtonRow = null;
        }

        if (Screen != null && GodotObject.IsInstanceValid(Screen))
            MapHighlighter.Reset(Screen);
    }

    static void Postfix(NMapScreen __instance, ActMap map)
    {
        try
        {
            // Combat HP preview is parented under NHealthBar; clear it when the map loads so it never lingers.
            PlayerHpPreviewPatch.CleanupAllHpPreviewOverlays(__instance.GetTree()?.Root);

            if (Layer != null && GodotObject.IsInstanceValid(Layer))
            {
                Layer.QueueFree();
                Layer = null;
                StatsLabel = null;
                CountFilterLabel = null;
                CountButtonRow = null;
            }

            Screen  = __instance;
            Summary = PathAnalyzer.Analyze(map);
            SelectedAxis  = PathFilterAxis.Any;
            SelectedCount = -1;

            BuildOverlay();
            Layer!.Visible = false;

            Log.Info($"[PathPlanner] Map analyzed. Paths: {Summary.TotalPaths}  Elite range: {Summary.MinElites}–{Summary.MaxElites}");
        }
        catch (System.Exception ex)
        {
            Log.Error($"[PathPlanner] SetMap error: {ex}");
        }
    }

    private static void BuildOverlay()
    {
        if (Screen == null || Summary == null) return;

        Layer = new CanvasLayer();
        Layer.Layer = 10;
        Screen.AddChild(Layer);

        var anchor = new Control();
        anchor.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        anchor.GrowHorizontal = Control.GrowDirection.Begin;
        anchor.GrowVertical   = Control.GrowDirection.Begin;
        anchor.OffsetLeft = -8;
        anchor.OffsetBottom = -8;
        Layer.AddChild(anchor);

        var panel = new PanelContainer();
        panel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        panel.SizeFlagsVertical   = Control.SizeFlags.ShrinkEnd;
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        panel.GrowHorizontal = Control.GrowDirection.Begin;
        panel.GrowVertical   = Control.GrowDirection.Begin;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.05f, 0.05f, 0.05f, 0.82f);
        style.SetCornerRadiusAll(7);
        style.ContentMarginLeft   = 12;
        style.ContentMarginRight  = 12;
        style.ContentMarginTop    = 10;
        style.ContentMarginBottom = 10;
        panel.AddThemeStyleboxOverride("panel", style);
        Layer.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        panel.AddChild(vbox);

        var title = new Label();
        title.Text = $"── Path Planner ({Summary.TotalPaths} paths) ──";
        title.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        title.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(title);

        StatsLabel = new Label();
        StatsLabel.AddThemeColorOverride("font_color", Colors.White);
        StatsLabel.AddThemeFontSizeOverride("font_size", 13);
        StatsLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        vbox.AddChild(StatsLabel);
        RefreshStatsLabel();

        vbox.AddChild(new HSeparator());

        AddLegendRow(vbox, new Color(0.80f, 0.45f, 1.0f), "Shop + Rest");
        AddLegendRow(vbox, new Color(0.35f, 1.0f, 0.40f), "Shop only");
        AddLegendRow(vbox, new Color(0.40f, 0.70f, 1.0f), "Rest only");
        AddLegendRow(vbox, new Color(1.0f,  0.75f, 0.20f), "Neither");

        vbox.AddChild(new HSeparator());

        var filterAxisLabel = new Label();
        filterAxisLabel.Text = "Filter paths by:";
        filterAxisLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        filterAxisLabel.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(filterAxisLabel);

        var axisGroup = new ButtonGroup();
        var axisRow = new HBoxContainer();
        axisRow.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(axisRow);

        AddAxisButton(axisRow, axisGroup, PathFilterAxis.Any, "Any");
        AddAxisButton(axisRow, axisGroup, PathFilterAxis.Elites, "Elites");
        AddAxisButton(axisRow, axisGroup, PathFilterAxis.Shops, "Shops");
        AddAxisButton(axisRow, axisGroup, PathFilterAxis.Rests, "Rests");

        CountFilterLabel = new Label();
        CountFilterLabel.Text = "Exact count:";
        CountFilterLabel.Visible = false;
        CountFilterLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        CountFilterLabel.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(CountFilterLabel);

        CountButtonRow = new HBoxContainer();
        CountButtonRow.AddThemeConstantOverride("separation", 4);
        CountButtonRow.Visible = false;
        vbox.AddChild(CountButtonRow);
    }

    private static void AddLegendRow(VBoxContainer vbox, Color color, string text)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        var swatch = new ColorRect();
        swatch.Color = color;
        swatch.CustomMinimumSize = new Vector2(12, 12);
        swatch.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(swatch);

        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        lbl.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(lbl);

        vbox.AddChild(row);
    }

    private static void AddAxisButton(HBoxContainer hbox, ButtonGroup group, PathFilterAxis axis, string text)
    {
        var btn = new Button();
        btn.Text = text;
        btn.ToggleMode = true;
        btn.ButtonGroup = group;
        btn.ButtonPressed = axis == SelectedAxis;
        btn.AddThemeFontSizeOverride("font_size", 13);
        btn.CustomMinimumSize = new Vector2(56, 0);
        var captured = axis;
        btn.Pressed += () => OnAxisButtonPressed(captured);
        hbox.AddChild(btn);
    }

    private static void OnAxisButtonPressed(PathFilterAxis axis)
    {
        try
        {
            SelectedAxis = axis;
            if (axis == PathFilterAxis.Any)
                SelectedCount = -1;
            else if (Summary != null)
                SelectedCount = DefaultCountForAxis(axis);

            RebuildCountButtons();
            RefreshStatsLabel();
            ApplyHighlight();
        }
        catch (System.Exception ex)
        {
            Log.Error($"[PathPlanner] Axis button error: {ex}");
        }
    }

    private static int DefaultCountForAxis(PathFilterAxis axis) => axis switch
    {
        PathFilterAxis.Elites => Summary!.MinElites,
        PathFilterAxis.Shops   => Summary!.MinShops,
        PathFilterAxis.Rests  => Summary!.MinRests,
        _                     => -1
    };

    private static void RebuildCountButtons()
    {
        if (CountButtonRow == null || Summary == null) return;

        foreach (var c in CountButtonRow.GetChildren())
            c.QueueFree();

        if (CountFilterLabel != null)
            CountFilterLabel.Visible = SelectedAxis != PathFilterAxis.Any;

        if (SelectedAxis == PathFilterAxis.Any)
        {
            CountButtonRow.Visible = false;
            return;
        }

        int min, max;
        switch (SelectedAxis)
        {
            case PathFilterAxis.Elites: min = Summary.MinElites; max = Summary.MaxElites; break;
            case PathFilterAxis.Shops:  min = Summary.MinShops;  max = Summary.MaxShops;  break;
            case PathFilterAxis.Rests:  min = Summary.MinRests; max = Summary.MaxRests; break;
            default:
                CountButtonRow.Visible = false;
                return;
        }

        CountButtonRow.Visible = true;

        if (SelectedCount < min || SelectedCount > max)
            SelectedCount = min;

        var countGroup = new ButtonGroup();
        for (int n = min; n <= max; n++)
        {
            int captured = n;
            var btn = new Button();
            btn.Text = n.ToString();
            btn.ToggleMode = true;
            btn.ButtonGroup = countGroup;
            btn.ButtonPressed = captured == SelectedCount;
            btn.AddThemeFontSizeOverride("font_size", 13);
            btn.CustomMinimumSize = new Vector2(38, 0);
            btn.Pressed += () => OnCountButtonPressed(captured);
            CountButtonRow.AddChild(btn);
        }
    }

    private static void OnCountButtonPressed(int count)
    {
        try
        {
            SelectedCount = count;
            RefreshStatsLabel();
            ApplyHighlight();
        }
        catch (System.Exception ex)
        {
            Log.Error($"[PathPlanner] Count button error: {ex}");
        }
    }

    internal static void ApplyHighlight()
    {
        MapHighlighter.Apply(Screen, Summary, SelectedAxis, SelectedCount);
    }

    internal static void RefreshStatsLabel()
    {
        if (StatsLabel == null || Summary == null) return;

        string Rng(int a, int b) => a == b ? $"{a}" : $"{a}–{b}";

        if (SelectedAxis == PathFilterAxis.Any)
        {
            var safe   = Summary.SafestPath;
            var greedy = Summary.GreediestPath;
            StatsLabel.Text =
                $"Elites:   {Rng(Summary.MinElites,   Summary.MaxElites)}\n" +
                $"Shops:    {Rng(Summary.MinShops,    Summary.MaxShops)}\n"  +
                $"Rests:    {Rng(Summary.MinRests,    Summary.MaxRests)}\n"  +
                $"Monsters: {Rng(Summary.MinMonsters, Summary.MaxMonsters)}\n" +
                $"Unknowns: {Rng(Summary.MinUnknowns, Summary.MaxUnknowns)}\n" +
                $"\n" +
                $"[Safe]   E:{safe.Elites} S:{safe.Shops} R:{safe.Rests}\n" +
                $"[Greedy] E:{greedy.Elites} S:{greedy.Shops} R:{greedy.Rests}";
            return;
        }

        if (!TryGetPathsForStats(Summary, SelectedAxis, SelectedCount, out var paths))
        {
            StatsLabel.Text = SelectedAxis switch
            {
                PathFilterAxis.Elites => $"No paths with {SelectedCount} elite{(SelectedCount == 1 ? "" : "s")}.",
                PathFilterAxis.Shops  => $"No paths with {SelectedCount} shop{(SelectedCount == 1 ? "" : "s")}.",
                PathFilterAxis.Rests  => $"No paths with {SelectedCount} rest{(SelectedCount == 1 ? "" : "s")}.",
                _                     => ""
            };
            return;
        }

        int count = paths.Count;
        int minE = paths.Min(p => p.Elites), maxE = paths.Max(p => p.Elites);
        int minS = paths.Min(p => p.Shops),  maxS = paths.Max(p => p.Shops);
        int minR = paths.Min(p => p.Rests),  maxR = paths.Max(p => p.Rests);
        int minM = paths.Min(p => p.Monsters), maxM = paths.Max(p => p.Monsters);
        int minU = paths.Min(p => p.Unknowns), maxU = paths.Max(p => p.Unknowns);

        string head = SelectedAxis switch
        {
            PathFilterAxis.Elites =>
                $"{count} path{(count == 1 ? "" : "s")} with {SelectedCount} elite{(SelectedCount == 1 ? "" : "s")}",
            PathFilterAxis.Shops =>
                $"{count} path{(count == 1 ? "" : "s")} with {SelectedCount} shop{(SelectedCount == 1 ? "" : "s")}",
            PathFilterAxis.Rests =>
                $"{count} path{(count == 1 ? "" : "s")} with {SelectedCount} rest{(SelectedCount == 1 ? "" : "s")}",
            _ => ""
        };

        string lineE = $"Elites:   {Rng(minE, maxE)}";
        string lineS = $"Shops:    {Rng(minS, maxS)}";
        string lineR = $"Rests:    {Rng(minR, maxR)}";
        string lineM = $"Monsters: {Rng(minM, maxM)}";
        string lineU = $"Unknowns: {Rng(minU, maxU)}";

        StatsLabel.Text = SelectedAxis switch
        {
            PathFilterAxis.Elites => $"{head}\n{lineS}\n{lineR}\n{lineM}\n{lineU}",
            PathFilterAxis.Shops  => $"{head}\n{lineE}\n{lineR}\n{lineM}\n{lineU}",
            PathFilterAxis.Rests  => $"{head}\n{lineE}\n{lineS}\n{lineM}\n{lineU}",
            _ => head
        };
    }

    private static bool TryGetPathsForStats(
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
}

[HarmonyPatch(typeof(NMapScreen), "Open")]
public static class MapScreenOpenPatch
{
    static void Postfix()
    {
        try
        {
            if (MapScreenSetMapPatch.Layer != null && GodotObject.IsInstanceValid(MapScreenSetMapPatch.Layer))
                MapScreenSetMapPatch.Layer.Visible = true;

            if (MapScreenSetMapPatch.SelectedAxis != PathFilterAxis.Any)
                MapScreenSetMapPatch.ApplyHighlight();
        }
        catch (System.Exception ex)
        {
            Log.Error($"[PathPlanner] Open error: {ex}");
        }
    }
}

[HarmonyPatch(typeof(NMapScreen), "Close")]
public static class MapScreenClosePatch
{
    static void Prefix()
    {
        try
        {
            if (MapScreenSetMapPatch.Layer != null && GodotObject.IsInstanceValid(MapScreenSetMapPatch.Layer))
                MapScreenSetMapPatch.Layer.Visible = false;

            MapHighlighter.Reset(MapScreenSetMapPatch.Screen);
        }
        catch (System.Exception ex)
        {
            Log.Error($"[PathPlanner] Close error: {ex}");
        }
    }
}
