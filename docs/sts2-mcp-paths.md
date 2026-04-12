# STS2 Modding MCP paths (PathPlanner workspace)

Decompiled game C# from `sts2.dll` lives at the **workspace root** so tools and search stay stable even if the MCP bundle folder moves.

| Path | Purpose |
|------|---------|
| `STS2-decompiled/` (repo root) | ILSpy output (~3300 `.cs` files). Gitignored. Re-run MCP tool `decompile_game` after a game patch. |
| `STS2 Modding MCP v3.8.0/sts2-modding-mcp/sts2mcp_config.json` | MCP defaults: `game_dir`, `decompiled_dir` (`../../STS2-decompiled` relative to that folder). |
| `.cursor/mcp.json` | Cursor Agent: sets `STS2_GAME_DIR` and `STS2_DECOMPILED_DIR` (env overrides config). |

Align `game_dir` with `STS2GamePath` in `local.props` if you move the Steam library.

**Precedence** (from MCP `resolve_config`): `STS2_DECOMPILED_DIR` env → `sts2mcp_config.json` → default `sts2-modding-mcp/decompiled`.

If you open the repo on another PC, adjust `game_dir` in `sts2mcp_config.json` or set `STS2_GAME_DIR` / `STS2_DECOMPILED_DIR` in the environment.
