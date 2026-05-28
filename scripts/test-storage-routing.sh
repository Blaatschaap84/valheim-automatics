#!/usr/bin/env bash

set -euo pipefail

tmpdir="$(mktemp -d "${TMPDIR:-/tmp}/automatics-storage-routing.XXXXXX")"
repo_root="$(pwd)"

cleanup() {
  rm -rf "$tmpdir"
}
trap cleanup EXIT

cp scripts/StorageRoutingHarness.cs "$tmpdir/Program.cs"
cat > "$tmpdir/StorageRoutingHarness.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$repo_root/Automatics/AutomaticStorage/StoragePlanner.cs" Link="StoragePlanner.cs" />
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
  dotnet run --project "$tmpdir/StorageRoutingHarness.csproj" --no-launch-profile

printf 'PASS storage routing checks\n'
