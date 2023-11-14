#!/bin/sh

PLATFORM=$(uname -m)

case "$PLATFORM" in
    x86_64)
        echo "x64"
        ;;
    arm64)
        echo "arm64"
        ;;
    # ... other platforms ...
    *)
        echo "Unsupported platform: $PLATFORM"
        exit 1
        ;;
esac
