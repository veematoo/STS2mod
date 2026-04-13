# Releasing PathPlanner (version + Git)

Mod **version strings** come from one place: **`ModVersion`** in [`ExampleMod.csproj`](../ExampleMod.csproj).  
Build targets regenerate `PathPlanner.json` and `pack/mod_manifest.json` from that value before each compile.

## 1. Bump the version

Edit **`ExampleMod.csproj`**:

```xml
<ModVersion>0.1.1</ModVersion>
```

Use [Semantic Versioning](https://semver.org/) (e.g. `0.2.0` for features, `0.1.1` for fixes).

## 2. Build

```powershell
dotnet build .\ExampleMod.csproj -c Release
```

Confirm the game is closed if Steam `mods` copy must succeed.

## 3. Update the changelog

In [`CHANGELOG.md`](../CHANGELOG.md):

- Under **`[Unreleased]`**, add bullets for user-visible changes.
- When you cut a release, add a **`[x.y.z] - YYYY-MM-DD`** section and move items out of `[Unreleased]`.
- Update the compare links at the bottom of the file (optional but nice).

## 4. Commit

```powershell
git add ExampleMod.csproj PathPlanner.json pack/mod_manifest.json CHANGELOG.md
git commit -m "Release v0.1.1"
```

Include any other changed files (`git status`). `PathPlanner.json` / `mod_manifest.json` may match generated output from the build.

## 5. Tag (annotated)

```powershell
git tag -a v0.1.1 -m "PathPlanner v0.1.1"
```

Tag name **`v` + version** matches the changelog links.

## 6. Push commits and tags

```powershell
git push origin main
git push origin v0.1.1
```

Or push all tags: `git push origin --tags`

## 7. GitHub Release (optional)

On GitHub: **Releases → Draft a new release** → choose the tag → paste changelog section → attach `PathPlanner.dll` / `.pck` from `dist\PathPlanner-friend` if you ship binaries there.

## Quick reference

| Concern              | Where |
|----------------------|--------|
| Mod semver           | `ExampleMod.csproj` → `ModVersion` |
| Human-readable log   | `CHANGELOG.md` |
| Immutable release    | Git tag `vX.Y.Z` |
| Published history    | `git push` + GitHub Releases (optional) |
