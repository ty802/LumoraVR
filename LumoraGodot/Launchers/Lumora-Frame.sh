#!/bin/sh
# Launch the shared Linux build directly on Steam Frame (x86_64 through FEX).
# Renderer selection must happen before Godot starts; C# device detection is too late.
set -eu

lumora_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$lumora_dir"
for lumora_name in Lumora.x86_64 LumoraVR.x86_64; do
    if [ -x "$lumora_dir/$lumora_name" ]; then
        exec "$lumora_dir/$lumora_name" --rendering-method mobile --xr-mode on "$@" -- --steam-frame --vr
    fi
done

printf '%s\n' 'Lumora executable not found. Keep Lumora-Frame.sh beside the Linux game executable.' >&2
exit 1
