# Automatics Valheim Smoke Test

This directory contains optional developer tooling for in-game Automatics smoke
checks. It is intentionally not part of the root `Automatics.sln`, and it does
not add root-level MSBuild files or production project settings.

The smoke plugin compiles only its own `SmokePlugin.cs`. Every Automatics type it
exercises (including the `StoragePlanner` routing types) is bound from the
already-loaded `Automatics.dll` via
`[assembly: InternalsVisibleTo("Automatics.SmokeTest")]`. It does **not** ship a
second copy of `Automatics.dll` or `ModUtils.dll`, and it declares
`[BepInDependency("net.eidee.valheim.automatics")]` so the main plugin loads
first and the CLR binds the smoke assembly's `Automatics`-typed references to the
single loaded copy.

It replaces the deleted headless harnesses
(`scripts/ObjectValidationHarness.cs` + `scripts/test-object-validation.sh` and
`scripts/StorageRoutingHarness.cs` + `scripts/test-storage-routing.sh`) by
running the same behaviour against real game state plus additional checks for the
mod's main features.

The fast static-invariant checks that `test-object-validation.sh` ran alongside
the harness (all-files object-data JSON schema validation, the Runestone matcher,
the no-duplicate-exact-matchers guarantee, and the object/icon source-pattern
guards) were not in-game behaviour and were preserved as
`scripts/test-object-data-static-invariants.sh`, not deleted.

Because the in-game logger has no capture buffer, the harnesses' warning-text
assertions (invalid object data skipped, duplicate exact matcher, null/invalid
custom entries) are re-expressed as observable-state checks — the valid sibling
or entry still resolves while the invalid ones are skipped — rather than
asserting the exact warning strings.

## Build

The main plugin must be built in the same configuration first, because the smoke
project references `Automatics/bin/<Configuration>/Automatics.dll` at compile time:

```bash
VALHEIM_DIR=/mnt/g/steam/steamapps/common/Valheim scripts/build.sh Debug
VALHEIM_DIR=/mnt/g/steam/steamapps/common/Valheim scripts/build.sh smoke Debug
```

`scripts/build.sh smoke Debug [clean] [deploy|no-deploy]` builds (and, on Debug,
deploys) the smoke plugin. You can also build the smoke-only solution directly:

```bash
VALHEIM_DIR=/mnt/g/steam/steamapps/common/Valheim dotnet msbuild smoke/Automatics.SmokeTest.sln /p:Configuration=Debug "/p:Platform=Any CPU"
```

Pass `no-deploy` (or `/p:DeploySmoke=false`) to build without installing:

```bash
VALHEIM_DIR=/mnt/g/steam/steamapps/common/Valheim scripts/build.sh smoke Debug no-deploy
```

## Deploy

The deploy target writes only the smoke `dll`/`pdb` to:

```text
<VALHEIM_DIR>/BepInEx/plugins/AutomaticsSmoke/
```

It never copies `Automatics.dll`, `ModUtils.dll`, `LitJSON.dll`, or
`NDesk.Options.dll` — those are already loaded next to the running Automatics
plugin and the CLR resolves them there.

Debug builds deploy automatically when the directory is available. Pass
`DeploySmoke=true` to force deployment for another configuration:

```bash
VALHEIM_DIR=/mnt/g/steam/steamapps/common/Valheim dotnet msbuild smoke/Automatics.SmokeTest.sln /p:Configuration=Release "/p:Platform=Any CPU" /p:DeploySmoke=true
```

If `DeploySmoke=true` is set and the Valheim or BepInEx directory cannot be
resolved, the build fails before copying files.

## Run And Logs

Launch Valheim after deploying. The plugin waits for `Localization.instance`
(with a timeout), then logs individual smoke results and one summary line to the
BepInEx log:

```text
[Automatics Smoke] Summary: total=17 passed=17 failed=0 skipped=0 failedChecks=[] skippedChecks=[]
```

The `vanilla_label_localizes_via_game` check (and, when the translation cache is
unseeded, `automatics_translation_keys_loaded`) SKIP rather than FAIL when run at
the main menu before a world loads. The smoke checks create temporary object JSON
files only under `BepInEx/config/AutomaticsSmoke/` and clean them up on a
best-effort basis.

## Remove

To uninstall the smoke plugin, remove the dedicated deploy directory:

```bash
rm -r /mnt/g/steam/steamapps/common/Valheim/BepInEx/plugins/AutomaticsSmoke
```

The smoke project is optional developer tooling. The fast static-invariant grep
scripts under `scripts/` are unaffected by this plugin.
