#!/usr/bin/env bash
set -euo pipefail

if [[ $# -gt 1 || ${1:-} == --help || ${1:-} == -h ]]; then
    printf 'Usage: bash scripts/pack.sh [version]\nOptional environment: REPOSITORY_URL\n'
    [[ $# -le 1 ]]
    exit
fi

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repository_root"

version=${1:-$(dotnet msbuild src/EMCL/EMCL.csproj -getProperty:Version -verbosity:quiet)}
if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$ ]]; then
    printf 'Expected a version such as 0.1.0-preview.1 or 0.1.0.\n' >&2
    exit 2
fi

properties=("-p:Version=$version")
if [[ -n ${REPOSITORY_URL:-} ]]; then
    properties+=("-p:RepositoryUrl=$REPOSITORY_URL")
fi

dotnet restore EMCL.slnx --disable-parallel --disable-build-servers -m:1 "${properties[@]}"
dotnet build EMCL.slnx -c Release --no-restore --disable-build-servers -m:1 "${properties[@]}"
dotnet test --project tests/EMCL.Tests/EMCL.Tests.csproj \
    -c Release --no-build --no-restore "${properties[@]}"
dotnet pack EMCL.slnx -c Release --no-build --no-restore \
    --disable-build-servers -m:1 "${properties[@]}" \
    "-p:PackageOutputPath=$repository_root/packages/$version"

printf '\nRelease packages: %s/packages/%s\n' "$repository_root" "$version"
