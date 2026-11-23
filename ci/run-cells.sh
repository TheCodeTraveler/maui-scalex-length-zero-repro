#!/usr/bin/env bash
# Runs every cell on the booted emulator, one process per cell. A crash in one cell must never stop the others, so there is no `set -e`.
set -u -o pipefail

runtime="${1:?usage: run-cells.sh <runtime>}"
package=com.example.c18repro
artifacts="artifacts/$runtime/cells"
mkdir -p "$artifacts"

adb logcat -G 16M

apk=$(ls src/C18Repro/bin/Release/*/android-x64/*-Signed.apk | head -1)
timeout 300 adb install -r "$apk" > "$artifacts/install.txt" 2>&1 || { sleep 10; timeout 300 adb install -r "$apk" > "$artifacts/install.txt" 2>&1; } || { echo "Install failed"; cat "$artifacts/install.txt"; exit 1; }

for scenario in scalex0 scaley0 scale0 scalex250 scalex0-workaround; do
    folder="$artifacts/$scenario"
    mkdir -p "$folder"
    adb logcat -c
    adb logcat -b crash -c
    timeout 60 adb shell am start -W -n "$package/com.example.c18repro.MainActivity" --es scenario "$scenario" > "$folder/am-start.txt" 2>&1

    # Done when the app logs DONE, or when its process is gone (a crash)
    waited=0
    while [ "$waited" -lt 120 ]; do
        adb logcat -d -s C18:I | grep -q 'C18 DONE' && break
        if [ "$waited" -gt 10 ] && [ -z "$(adb shell pidof "$package" | tr -d '\r')" ]; then
            echo "$scenario: process is gone without DONE"
            break
        fi
        sleep 2
        waited=$((waited + 2))
    done

    adb logcat -d > "$folder/logcat.txt"
    adb logcat -d -b crash > "$folder/crash.txt"
    adb shell am force-stop "$package"
done
