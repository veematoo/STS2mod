# PathPlanner (STS2 mod)

Slay the Spire 2 mod: act-map path summary, optional merchant/card win-rate hints, and related UI. Built as **PathPlanner** (`PathPlanner.dll` / `PathPlanner.pck`).

---

## GitHub and version control

This folder is a **Git** repo. The default remote **`origin`** may still point at the template repo (`lamali292/sts2_example_mod`). Use one of these:

**Path A — You push to that repo**  
If you have collaborator access, commit locally and run `git push origin main`.

**Path B — Your own repository (typical)**  
1. On [GitHub](https://github.com/new), create an empty repository under your account (e.g. `PathPlanner` or `sts2-path-planner`).
2. Point `origin` at it (replace `YOUR_USERNAME` and `YOUR_REPO`):

```powershell
git remote set-url origin https://github.com/YOUR_USERNAME/YOUR_REPO.git
git push -u origin main
```

3. Optional: keep the template as **`upstream`** for pulling updates:

```powershell
git remote add upstream https://github.com/lamali292/sts2_example_mod.git
```

**Authentication**

- **HTTPS:** use a [Personal Access Token (classic)](https://github.com/settings/tokens) with the `repo` scope when Git prompts for a password (GitHub does not accept your account password for Git).
- **SSH:** [add an SSH key](https://docs.github.com/en/authentication/connecting-to-github-with-ssh) to GitHub and use `git@github.com:YOUR_USERNAME/YOUR_REPO.git` as the remote URL.

After commits are created locally, run **`git push -u origin main`** from this folder in a normal terminal (or Cursor’s Git UI) so **Git Credential Manager** or SSH can sign in—unattended pushes often hang waiting for that prompt.

### Releases and versioning

- **Single source of truth:** set **`ModVersion`** in [`ExampleMod.csproj`](ExampleMod.csproj); build regenerates `PathPlanner.json` and `pack/mod_manifest.json`.
- **Process:** see [`docs/releasing.md`](docs/releasing.md) and keep [`CHANGELOG.md`](CHANGELOG.md) up to date; tag releases as `v0.1.0`, etc.

---

## Development Setup

### Prerequisites

Before you begin, ensure you have:

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Godot 4.5.1 Mono](https://godotengine.org/download/archive/4.5.1-stable/) - **Download the "Windows 64-bit, .NET" version**
- Slay the Spire 2 installed via Steam

---

### Initial Configuration

#### 1. Clone the Repository
```bash
git clone https://github.com/YOUR_USERNAME/YOUR_REPO.git
cd YOUR_REPO
```
(Or clone the template: `https://github.com/lamali292/sts2_example_mod.git` and then `git remote set-url origin` to your fork as above.)

#### 2. Configure Your Paths

**Windows (PowerShell):**
```powershell
Copy-Item local.props.example local.props
```

**Linux/Mac:**
```bash
cp local.props.example local.props
```

#### 3. Edit `local.props`

Open `local.props` in any text editor and update with **your** paths:
```xml
<Project>
  <PropertyGroup>
    <!-- Example for default Steam installation: -->
    <STS2GamePath>C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2</STS2GamePath>
    
    <!-- Example Godot path: -->
    <GodotExePath>C:\Godot\Godot_v4.5.1-stable_mono_win64.exe</GodotExePath>
  </PropertyGroup>
</Project>
```
---

### Building the Mod

#### Visual Studio
Open ExampleMod.csproj as Visual Studio Project

Press **Ctrl+Shift+B** or click **Build → Build Solution**


The mod will **automatically** install to:

`Slay the Spire 2\mods\` (see `ExampleMod.csproj` PostBuild targets for `PathPlanner.dll`, `PathPlanner.json`, and `.pck`). 


---

## Path Planner settings

In-game: click the **PP** button (bottom-left) to open toggles for map overlay, win-rate lines, partner UI, and HP preview. **Esc** or **Close** / backdrop dismisses the panel. Settings are saved to:

`%AppData%\.sts2mods\PathPlanner\config.json`

You can edit that file directly when the game is not running; a cold restart applies manual edits reliably.

---

## Troubleshooting

### "Cannot find Godot executable"
- Make sure `GodotExePath` in `local.props` points to the `.exe` file
- Download the **Mono** version, not the standard version

### "Cannot find Slay the Spire 2"
- Right-click STS2 in Steam → Manage → Browse local files
- Copy the full path and paste into `STS2GamePath`

### Build succeeds but mod doesn't load
- Check that both `ExampleMod.dll` **AND** `ExampleMod.pck` exist in `mods/ExampleMod/`
- Check the game's log file for errors: `%AppData%\Roaming\SlayTheSpire2\Player.log`

### Changes don't appear in game
- Rebuild the mod (**Ctrl+Shift+B**) or with Rebuild Solution
- Restart Slay the Spire 2

---
