#!/usr/bin/env bash

set -euo pipefail

failures=0

fail() {
  printf 'FAIL: %s\n' "$*" >&2
  failures=$((failures + 1))
}

run_harness() {
  local tmpdir
  local repo_root
  tmpdir="$(mktemp -d "${TMPDIR:-/tmp}/automatics-object-validation.XXXXXX")"
  repo_root="$(pwd)"

  cp scripts/ObjectValidationHarness.cs "$tmpdir/Program.cs"
  cat > "$tmpdir/ObjectValidationHarness.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$repo_root/Automatics/Valheim/ValheimObjects.cs" Link="ValheimObjects.cs" />
    <Compile Include="$repo_root/Automatics/AutomaticMapping/IconPack.cs" Link="IconPack.cs" />
  </ItemGroup>
</Project>
EOF
  cat > "$tmpdir/NuGet.config" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
  </packageSources>
</configuration>
EOF

  DOTNET_CLI_HOME="$tmpdir/dotnet-home" \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_PACKAGES="$tmpdir/packages" \
    dotnet run --project "$tmpdir/ObjectValidationHarness.csproj" --no-launch-profile
  local result=$?

  rm -rf "$tmpdir"
  return "$result"
}

require_source_pattern() {
  local file="$1"
  local pattern="$2"
  local message="$3"

  if ! rg -q "$pattern" "$file"; then
    fail "$message"
  fi
}

if ! run_harness; then
  fail "object validation harness failed"
fi

object_files=()
while IFS= read -r file; do
  object_files+=("$file")
done < <(rg --files Automatics/Data/Objects -g '*.json')

for file in "${object_files[@]}"; do
  if ! jq -e '
    type == "object"
    and (.type | type == "string" and length > 0)
    and (.values | type == "array")
    and all(.values[];
      type == "object"
      and (.identifier | type == "string" and length > 0)
      and (.label | type == "string" and length > 0)
      and (.matches | type == "array" and length > 0)
      and all(.matches[];
        type == "object"
        and (.value | type == "string" and length > 0)
        and (((.regex // false) == false) or
          (try (.value as $pattern | ("probe" | test($pattern) | type == "boolean")) catch false))
      )
    )
  ' "$file" > /dev/null; then
    fail "object data must reject null or empty child values: $file"
  fi
done

runestone_match="$(
  jq -r '.values[] | select(.identifier == "Runestone") | .matches[0].value // empty' \
    Automatics/Data/Objects/AutomaticMapping/other.json
)"
if [[ "$runestone_match" != '$piece_lorestone' ]]; then
  fail "Runestone matcher should be \$piece_lorestone but was ${runestone_match:-<empty>}"
fi

duplicates="$(
  for file in "${object_files[@]}"; do
    jq -r '
    select(.type != null) as $doc
    | .values[]?
    | select(type == "object")
    | .matches[]?
    | select(type == "object")
    | select((.regex // false) == false)
    | select(.value | type == "string" and length > 0)
    | [$doc.type, .value]
    | @tsv
    ' "$file"
  done | sort | uniq -d
)"
if [[ -n "$duplicates" ]]; then
  fail "duplicate exact object matchers found: ${duplicates//$'\n'/, }"
fi

valheim_objects=Automatics/Valheim/ValheimObjects.cs
icon_pack=Automatics/AutomaticMapping/IconPack.cs

require_source_pattern \
  "$valheim_objects" \
  "TryValidate" \
  "custom and JSON object definitions must be validated before registration"
require_source_pattern \
  "$valheim_objects" \
  "catch \\(ArgumentException" \
  "invalid object regex patterns must be caught and treated as validation failures"
require_source_pattern \
  "$icon_pack" \
  "ValidateRegexTarget" \
  "custom icon regex patterns must be validated while loading icon data"
require_source_pattern \
  "$icon_pack" \
  "catch \\(ArgumentException" \
  "custom icon regex runtime failures must be caught and treated as non-matches"

if rg -q "m_type <= _vanillaPinTypeLength" "$icon_pack"; then
  fail "first custom icon must not be classified as a vanilla pin type"
fi

if ((failures > 0)); then
  exit 1
fi

printf 'PASS object validation checks\n'
