#!/usr/bin/env bash
set -euo pipefail

ROOT=/data/tenants/registrations
# ROOT=~/tmp/dotyou/tenants/registrations

shopt -s nullglob

for tenant_dir in "$ROOT"/*/; do
    tenant_id=$(basename "$tenant_dir")
    temp_drives="$tenant_dir/temp/drives"
    [[ -d "$temp_drives" ]] || continue

    for drive_dir in "$temp_drives"/*/; do
        drive_id=$(basename "$drive_dir")
        src="$drive_dir/inbox/"
        dst="$tenant_dir/inbox/drives/$drive_id/"

        [[ -d "$src" ]] || continue

        echo "syncing tenant=$tenant_id drive=$drive_id"
        mkdir -p "$dst"
        rsync -a "$src" "$dst"
        rm -rf "$src"
    done
done
