# Path Planner on Linux (Harmony / Snap)

Path Planner uses **Harmony** to hook into the game. On Linux, Harmony relies on a small native helper (`mm-exhelper.so`) that is loaded from `/tmp`. Under some setups that helper fails to link correctly.

## Snap-packaged Steam: not supported for testing

If Steam was installed from the **Snap store** (`steam` snap), Path Planner (and other Harmony-based STS2 mods) often **fail during mod load** with errors similar to:

- `Unable to load shared library '/tmp/mm-exhelper.so.…'`
- `undefined symbol: _Unwind_RaiseException`
- Wrapped in `HarmonyException` while patching (e.g. `CardModel::InvokeDrawn`)

That combination comes from **Snap’s sandbox** plus **Steam Linux Runtime** (`pressure vessel`), where the dynamic linker does not resolve unwind / `libgcc_s` the way a normal desktop Steam install does. **This is not a bug in Path Planner’s C# logic**; the same mod can work on Linux with a non-Snap Steam install.

**Recommendation:** Install Steam from **Valve’s `.deb`**, the official tarball, or your distro’s **non-Snap** package, and run **native Linux** Slay the Spire 2 from that Steam. Use the mod files from `dist/PathPlanner-friend/linux/` (or the three files your host built for Linux).

## If you use non-Snap Steam and still see Harmony load errors

1. Update the game and Steam client.
2. Ask on the Path Planner / STS2 mod channel with a **full log** from a failed launch.

Advanced users sometimes work around linker issues with host `LD_LIBRARY_PATH` or `LD_PRELOAD` pointed at `libgcc_s.so.1`. That is **environment-specific** and can break after Steam or OS updates; only try if you know how to revert.

## Install reminder (Linux)

Copy **`PathPlanner.dll`**, **`PathPlanner.json`**, and **`PathPlanner.pck`** into the game directory’s **`mods`** folder (same layout as on Windows).
