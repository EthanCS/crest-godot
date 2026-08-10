#!/bin/sh
set -eu

if [ "$#" -lt 1 ] || [ "$#" -gt 2 ]; then
    echo "Usage: $0 <version, e.g. v0.1.0> [git-ref]" >&2
    exit 2
fi

version="$1"
ref="${2:-HEAD}"
case "$version" in
    v[0-9]*.[0-9]*.[0-9]*) ;;
    *)
        echo "Version must look like v0.1.0." >&2
        exit 2
        ;;
esac

repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
plugin_version=$(git -C "$repo_root" show "$ref:addons/crest/plugin.cfg" |
    sed -n 's/^version="\([^"]*\)"$/\1/p')
expected_version=${version#v}

if [ "$plugin_version" != "$expected_version" ]; then
    echo "plugin.cfg version '$plugin_version' does not match '$version'." >&2
    exit 1
fi

output_dir="$repo_root/dist"
archive_name="crest-godot-$version.zip"
temporary_dir=$(mktemp -d "${TMPDIR:-/tmp}/crest-release.XXXXXX")
trap 'rm -rf "$temporary_dir"' EXIT HUP INT TERM

mkdir -p "$output_dir"
git -C "$repo_root" archive "$ref" addons/crest |
    tar -x -C "$temporary_dir"

(
    cd "$temporary_dir"
    zip -X -q -r "$output_dir/$archive_name" addons
)

(
    cd "$output_dir"
    shasum -a 256 -b "$archive_name" > "$archive_name.sha256"
)

echo "$output_dir/$archive_name"
echo "$output_dir/$archive_name.sha256"
