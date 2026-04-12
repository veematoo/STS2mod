# BaseLib and conventional STS2 modding (PathPlanner)

This doc implements the research steps from the BaseLib plan: wiki skim, template comparison, dependency decision, and ModAnalyzers guidance.

## Official references

- [BaseLib-StS2 (source)](https://github.com/Alchyr/BaseLib-StS2)
- [BaseLib Wiki](https://alchyr.github.io/BaseLib-Wiki/)
- [Features list](https://alchyr.github.io/BaseLib-Wiki/docs/Features.html)
- [Custom models overview](https://alchyr.github.io/BaseLib-Wiki/docs/models/index.html)
- [Mod template + Setup](https://github.com/Alchyr/ModTemplate-StS2/wiki/Setup)
- [Modding Basics](https://github.com/Alchyr/ModTemplate-StS2/wiki/Modding-Basics)

## BaseLib capabilities (relevant to PathPlanner)

PathPlanner today: Harmony patches, direct `sts2` / `GodotSharp` references, UI overlays, no custom cards/relics.

| BaseLib area | Use for PathPlanner? | Notes |
|--------------|---------------------|--------|
| **HarmonyExtensions.PatchAsync** | Maybe | If we patch `async` game methods, BaseLib’s helper is safer than hand-rolled transpilers. |
| **SpireField / SavedSpireField** | Maybe | Attach per-card or per-run planner state without subclassing game types (CWT-style). |
| **SimpleModConfig + ModConfigRegistry** | High value later | In-game toggles (e.g. overlay density, colors) with automatic Mod Configuration UI + save. |
| **ICustomUiModel / GeneratedNodePool** | Maybe | If we hook card/relic presentation beyond raw Control overlays. |
| **Custom*Model / ICustomModel** | Low | For new cards/relics/powers; not core to map/path UI. |
| **Custom enums / keywords** | Low | Content-mod territory. |
| **SavedProperty integration** | Low unless we ship Models | Harmony-only tools rarely need this. |

## Conventional STS2 modding (short)

1. Match **.NET** and **Godot** to Megadot / game (see template wiki).
2. **`Alchyr.Sts2.Templates`**: solution + project same folder; optional BaseLib package reference.
3. Prefer **Commands** on **Models** for gameplay changes; decompile vanilla for patterns.
4. **Manifest**: `dependencies` (e.g. `BaseLib`), `affects_gameplay` accurate for multiplayer.
5. **PCK** when changing non-code assets; **Build** vs **Publish** split in Rider template workflow.

## Template vs PathPlanner (`ExampleMod.csproj`)

Installed templates: `dotnet new install Alchyr.Sts2.Templates` (package **2.3.6** as of this run). Short names: `alchyrsts2mod`, `alchyrsts2contentmod`, `alchyrsts2charmod`.

A scratch **Slay the Spire 2 Mod** was generated with `dotnet new alchyrsts2mod` into `_dev/TemplateCompare` (gitignored). Highlights versus PathPlanner:

| Aspect | Template (`alchyrsts2mod`) | PathPlanner |
|--------|---------------------------|-------------|
| **SDK** | `Godot.NET.Sdk/4.5.1` | Plain `Microsoft.NET.Sdk`; Godot invoked only for `--export-pack` |
| **Paths** | `Sts2PathDiscovery.props` (registry/Steam heuristics) + optional `Directory.Build.props` for `GodotPath` | `local.props`: `STS2GamePath`, `GodotExePath`; `GameDataDir` = `data_sts2_windows_x86_64` |
| **References** | `0Harmony`, `sts2` from `Sts2DataDir`; **no** explicit `GodotSharp` item (SDK pulls Godot refs) | `0Harmony`, `GodotSharp`, `sts2` from `GameDataDir` |
| **BaseLib** | `PackageReference` **Alchyr.Sts2.BaseLib** `Version="*"` | None |
| **Analyzers** | **Alchyr.Sts2.ModAnalyzers** + `AdditionalFiles` for `localization/**/*.json` | None |
| **Publicizer** | `Krafs.Publicizer` on `sts2` (disabled by default `Condition="False"`) | Not used |
| **Deploy** | `CopyToModsFolderOnBuild` → `$(ModsPath)$(MSBuildProjectName)/` (per-mod subfolder); `GodotPublish` after `Publish` | `PostBuild`: PCK to `$(TargetDir)`, then copy dll+json+pck to **flat** `mods` + `dist/PathPlanner-friend` |
| **Manifest** | `dependencies: ["BaseLib"]` in generated `.json` | `dependencies: []` |
| **Godot project** | Committed `project.godot`, `export_presets.cfg` | Generated `pack/project.godot` on each build |

PathPlanner’s approach is valid for a **non–Godot.NET.Sdk** tool mod; adopt template pieces **à la carte** (e.g. `Sts2PathDiscovery`) if we want fewer manual path overrides on new machines.

## Dependency decision (PathPlanner)

**Choice: remain Harmony + direct game references without a BaseLib compile-time dependency for now.**

Reasons:

- No custom `CardModel` / relic content; no need for ID prefixing or `ModelDb` registration via BaseLib.
- Avoids forcing all players to install BaseLib until we call a BaseLib API from our DLL.
- `affects_gameplay: true` stays correct; if we later add **only** cosmetic overlays, consider flipping to `false` per manifest rules.

**When to add BaseLib:** first use of `SimpleModConfig`, `SpireField`, `HarmonyExtensions.PatchAsync`, or shared types with other mods—then add `PackageReference` / DLL reference, set `"dependencies": ["BaseLib"]` in generated JSON, and ship BaseLib in install instructions.

## ModAnalyzers (`Alchyr.Sts2.ModAnalyzers`)

**Status: not referenced in PathPlanner.** The template adds `PackageReference Include="Alchyr.Sts2.ModAnalyzers"` plus `AdditionalFiles` for localization JSON so analyzers can validate/generate strings. That pipeline targets **content** mods (cards, relics, characters). PathPlanner’s localization surface is minimal and hand-maintained; **no package was added.** When we introduce BaseLib `Custom*Model` content or want analyzer-driven localization, add the same `PackageReference` and wire `AdditionalFiles` like the template’s `**/localization/**/*.json` include.
