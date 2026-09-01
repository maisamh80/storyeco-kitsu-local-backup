#!/usr/bin/env bash
set -Eeuo pipefail

SOURCE_PATH="${1:-./storyeco-backup-export}"
TARGET_PATH="/usr/local/sbin/storyeco-backup-export"

if [[ ! -f "$SOURCE_PATH" ]]; then
    echo "Helper not found: $SOURCE_PATH" >&2
    exit 1
fi

sudo install -o root -g root -m 0755 "$SOURCE_PATH" "$TARGET_PATH"
sudo "$TARGET_PATH" self-test

echo "Installed: $TARGET_PATH"
