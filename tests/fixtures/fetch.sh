#!/usr/bin/env bash
# Downloads the third-party mods the step-aside tests run against into tests/fixtures/Mods.
# They are other authors' work, so they are fetched rather than kept in this repository.
set -euo pipefail

dir="$(cd "$(dirname "$0")" && pwd)/Mods"
mkdir -p "$dir"

fetch() {
    [ -f "$dir/$1" ] && { echo "have  $1"; return; }
    echo "fetch $1"
    curl -fsSL -o "$dir/$1.part" "$2"
    echo "$3  $dir/$1.part" | sha256sum -c --quiet
    mv "$dir/$1.part" "$dir/$1"
}

fetch smithingplus_1.9.0-rc.1.zip \
    "https://moddbcdn.vintagestory.at/smithingplus_1.9.0-r_387b188934e826e4ba9cd7b3661499ed.zip?dl=smithingplus_1.9.0-rc.1.zip" \
    eb7ff6abb499943d2abf1c007310eebbb445464f9c32999efd7acc6f6668ab2e
fetch AnvilGuard_1.0.0.zip \
    "https://moddbcdn.vintagestory.at/AnvilGuard_1.0.0_a70810d55d8695803b83303912fe8303.zip?dl=AnvilGuard_1.0.0.zip" \
    7df85a0e8eddfd5c16d3caf3de888b92b5992e800f0aceb0c8eb590f023f8688
