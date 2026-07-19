#!/usr/bin/env bash

set -euo pipefail

failures=0
mapping_file="Automatics/AutomaticMapping/AutomaticMapping.cs"
# AutomaticMapping.cs was split into a facade plus the dynamic/static scan classes;
# the liveness invariants below moved with the code, so they are checked in those files.
static_mapping_file="Automatics/AutomaticMapping/StaticObjectMapping.cs"
dynamic_mapping_file="Automatics/AutomaticMapping/DynamicObjectMapping.cs"
flora_file="Automatics/AutomaticMapping/FloraNetwork.cs"
map_file="Automatics/AutomaticMapping/Map.cs"
navigation_file="Automatics/AutomaticMapping/Navigation.cs"
patches_file="Automatics/AutomaticMapping/Patches.cs"

fail() {
  printf 'FAIL: %s\n' "$*" >&2
  failures=$((failures + 1))
}

require_source_pattern() {
  local file="$1"
  local pattern="$2"
  local message="$3"

  if ! rg -q "$pattern" "$file"; then
    fail "$message"
  fi
}

reject_source_pattern() {
  local file="$1"
  local pattern="$2"
  local message="$3"

  if rg -q "$pattern" "$file"; then
    fail "$message"
  fi
}

require_mineral_validation_before_seen() {
  local validation_line
  local seen_line

  validation_line="$(
    awk '
      /private static bool MineralMapping/ { in_function = 1 }
      in_function && /TryGetMineralPosition/ { print NR; exit }
      in_function && /^        private static/ && !/MineralMapping/ { exit }
    ' "$static_mapping_file"
  )"
  seen_line="$(
    awk '
      /private static bool MineralMapping/ { in_function = 1 }
      in_function && /TryGetCachedPin\(identify/ { print NR; exit }
      in_function && /^        private static/ && !/MineralMapping/ { exit }
    ' "$static_mapping_file"
  )"

  if [[ -z "$validation_line" || -z "$seen_line" ]]; then
    fail "mineral mapping must contain both liveness validation and cached-pin handling"
    return
  fi

  if ((validation_line >= seen_line)); then
    fail "mineral cached pins must be marked seen only after liveness validation"
  fi
}

require_source_pattern \
  "$flora_file" \
  "Network == null && node\\.Network == null" \
  "flora network construction must explicitly handle two uninitialized nodes"
require_source_pattern \
  "$flora_file" \
  "node\\.Network = Network" \
  "flora network construction must assign the created network to the neighboring node"
require_source_pattern \
  "$flora_file" \
  "Network\\.AddNode\\(node\\)" \
  "flora network construction must add the neighboring node to the created network"
reject_source_pattern \
  "$flora_file" \
  "base\\.Awake\\(\\)" \
  "flora nodes must not schedule the generic ObjectNode network construction"

require_mineral_validation_before_seen
require_source_pattern \
  "$static_mapping_file" \
  "MineRock5Cache\\.TryGetLivePosition" \
  "MineRock5 mapping must use live hit-area liveness instead of full NonDestroyed snapshots"
require_source_pattern \
  "Automatics/AutomaticMapping/MineRock5Cache.cs" \
  "package\\.ReadSingle\\(\\) > 0f" \
  "MineRock5 mapping must treat any live saved hit area as live"
require_source_pattern \
  "Automatics/AutomaticMapping/MineRock5Cache.cs" \
  "Reflections\\.GetField<IList>\\(rock5, \"m_hitAreas\"\\)" \
  "MineRock5 snapshots must follow vanilla m_hitAreas health indexing"
require_source_pattern \
  "Automatics/AutomaticMapping/MineRock5Cache.cs" \
  "AccessTools\\.Field\\(hitArea\\.GetType\\(\\), \"m_collider\"\\)" \
  "MineRock5 snapshots must read the collider from each vanilla hit-area entry"
reject_source_pattern \
  "Automatics/AutomaticMapping/MineRock5Cache.cs" \
  "GetComponentsInChildren<Collider>\\(true\\)|NonDestroyed|TryGetOrBuildSnapshotAlive" \
  "MineRock5 mapping must not use includeInactive collider enumeration or full-health NonDestroyed liveness"
require_source_pattern \
  "$static_mapping_file" \
  "TryGetMineRockPosition" \
  "MineRock mapping must use a live-hit-area position helper"
require_source_pattern \
  "$static_mapping_file" \
  "AddColliderBounds\\(collider, ref sum, ref maxHeight, ref count\\)" \
  "MineRock mapping must aggregate live hit-area bounds directly"
reject_source_pattern \
  "$static_mapping_file" \
  "var liveColliders = new List<Collider>\\(\\)|liveColliders\\.ToArray\\(\\)" \
  "MineRock mapping should not allocate a live collider list on every scan"
require_source_pattern \
  "$static_mapping_file" \
  "zdo\\.GetFloat\\(\"Health\" \\+ i, rock\\.m_health\\) <= 0f\\) continue" \
  "MineRock mapping must skip dead hit areas and keep live ones"
reject_source_pattern \
  "$static_mapping_file" \
  "GetFloat\\(\"Health\" \\+ i, rock\\.m_health\\) <= 0f\\)[[:space:]]*return empty" \
  "MineRock mapping must not treat one dead hit area as an entirely dead rock"
require_source_pattern \
  "$map_file" \
  "AccessTools\\.Method\\(typeof\\(Minimap\\), \"IsExplored\"" \
  "unexplored automatic-pin hiding must bind vanilla Minimap.IsExplored centrally"
require_source_pattern \
  "$map_file" \
  "MethodDelegate<Func<Minimap, Vector3, bool>>" \
  "Minimap.IsExplored binding must be cached instead of reflected per pin"
require_source_pattern \
  "$map_file" \
  "ShouldHideAutomaticPin\\(Minimap\\.PinData pinData\\)" \
  "automatic-pin visibility filtering must be centralized in Map"
require_source_pattern \
  "$map_file" \
  "IsPersistedAutomaticPin" \
  "saved automatic pins must be re-tracked after map data reloads"
require_source_pattern \
  "$map_file" \
  "Config\\.HideUnexploredAutomaticMappingPins" \
  "automatic-pin visibility filtering must be gated by the new config option"
require_source_pattern \
  "$patches_file" \
  "Minimap_UpdatePins_Postfix" \
  "hidden unexplored automatic pins must be reconciled after vanilla UpdatePins"
require_source_pattern \
  "$patches_file" \
  "ShouldHideAutomaticPin" \
  "hidden unexplored automatic pins must be filtered before vanilla creates markers"
require_source_pattern \
  "$patches_file" \
  "Minimap_Explore_Postfix" \
  "exploration updates must refresh hidden automatic pin markers"
require_source_pattern \
  "$dynamic_mapping_file" \
  "Map\\.ShouldSuppressTransientAutomaticPin\\(pos\\)" \
  "transient dynamic automatic pins must be suppressed before creation outside explored areas"
require_source_pattern \
  "$static_mapping_file" \
  "!save && Map\\.ShouldSuppressTransientAutomaticPin\\(pos\\)" \
  "unsaved static automatic pins must be suppressed before creation outside explored areas"
# Static duplicate checks are pin-existence checks, not UI-marker checks: an
# inactive marker (freshly added and not yet built, or deactivated off-screen by
# vanilla Minimap.UpdatePins) must still block a second pin so re-scans cannot
# create duplicates. Dedup therefore always includes inactive pins and must not
# be gated on the unexplored-pin-hiding config.
require_source_pattern \
  "$static_mapping_file" \
  "includeInactive: true" \
  "static mapping dedup must be pin-existence based (includeInactive: true) so inactive markers cannot spawn duplicate pins"
reject_source_pattern \
  "$static_mapping_file" \
  "includeInactive: Config\\.HideUnexploredAutomaticMappingPins" \
  "static mapping dedup must not be gated on unexplored-pin hiding (would recreate duplicate pins for inactive markers)"
require_source_pattern \
  "$navigation_file" \
  "Map\\.ShouldHideAutomaticPin\\(_targetPin\\)" \
  "navigation targets must clear when an automatic pin becomes hidden"

if git diff --name-only -- Automatics/Libraries/mod-utils | rg -q .; then
  fail "P8 must not edit files directly under Automatics/Libraries/mod-utils"
fi

if ((failures > 0)); then
  exit 1
fi

printf 'PASS mapping liveness static invariant checks\n'
