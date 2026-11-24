# BepInEx core patch

fm-cli runs on BepInEx 6 IL2CPP be.755 (commit `3fab71a`) with one assembly patched: `BepInEx.Unity.IL2CPP.dll`.

- `IL2CPPChainloader.MainThreadTick`: raised from the `il2cpp_runtime_invoke` hook once plugins are loaded, at most
  every 15 ms, never re-entered. The plugin hides the window and keeps the game running from it.
- The hook stays on after plugins load (be.755 removes it); plugins load once, from the scene hook or, should the hook
  never fire, a timer thread after 60 s.
- arm64: the exported `il2cpp_runtime_invoke` can be a one-branch stub; the hook goes on its target. MonoMod's native
  proxy and `ConsoleSetOutFix` throw there and are skipped.
- `FM26_TICK_TEST=1` logs every 60th tick; `FM26_MEASURE_ONLY=1` counts the hook's calls instead of loading plugins.

Build: `tools/bepinex-patch/build.sh` (clones BepInEx into the cache, applies `il2cpp-main-thread-tick.patch`, builds).
The patch was written again on 24 Nov 2026 from the shipped build, whose source was lost; the rebuilt assembly has the
same members and ran the game on macOS (three starts, a save, the reads).

BepInEx is LGPL-2.1; this folder's patch is to its source.
