#!/usr/bin/env bash
set -euo pipefail

# Builds the iOS smoke-test app against a packed Red5Pro.Streaming.Net.iOS package, installs it on a
# simulator and runs it. The app prints its verdict to stdout and exits; this script turns that into
# an exit code.
#
# Usage: run-ios-device-tests.sh <package-version> [target-framework]
#
# Unlike the Android runner this boots the simulator itself - there is no equivalent of the
# emulator-runner action, and simctl gives a cleaner handle on the app's stdout than mlaunch does.
#
# Tiers: the offline checks always run; the licence check runs only when RED5_LICENSE_KEY and
# RED5_ENDPOINT are set, and reports SKIP otherwise. There is no live publish tier on iOS - the
# simulator has no camera, so it cannot produce a video track to publish.

VERSION="${1:?a package version is required}"
# One of the package's own target frameworks, so the smoke test proves what actually ships. It only
# builds against an Xcode carrying the matching iOS SDK; CI selects it with select-xcode.sh, and
# locally you may need DEVELOPER_DIR set to the same.
TARGET_FRAMEWORK="${2:-net10.0-ios26.0}"

BUNDLE_ID="net.red5.streaming.devicetests"
LOG_FILE="device-tests-simulator.log"

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="${REPO_ROOT}/tests/Red5Pro.Streaming.Net.iOS.DeviceTests/Red5Pro.Streaming.Net.iOS.DeviceTests.csproj"

. "${REPO_ROOT}/build/local-config.sh"

if [ "$(uname -s)" != "Darwin" ]; then
    echo "::error::the iOS smoke test requires macOS" >&2
    exit 1
fi

# The consumer-supplied SDK, staged by native/ios/fetch-ios.sh. Not in the package - the Red5 EULA
# does not allow that - so even our own test app has to link its own copy.
RED5_IOS_SDK="${RED5_IOS_SDK:-${REPO_ROOT}/native/build/ios/Red5WebRTCKit.xcframework}"
if [ ! -d "${RED5_IOS_SDK}" ]; then
    echo "::error::${RED5_IOS_SDK} is missing. Run native/ios/fetch-ios.sh first." >&2
    exit 1
fi

# GitHub's macOS runners are arm64, but keep this derived rather than hard-coded so the script also
# works on an Intel Mac.
case "$(uname -m)" in
    arm64) DEVICE_RID="${RED5_DEVICE_RID:-iossimulator-arm64}" ;;
    *)     DEVICE_RID="${RED5_DEVICE_RID:-iossimulator-x64}" ;;
esac

case "${TARGET_FRAMEWORK}" in
    net10.0-*) sdk_major=10 ;;
    *)         sdk_major=9 ;;
esac

sdk_version="$(dotnet --list-sdks | grep "^${sdk_major}\." | tail -1 | cut -d' ' -f1)"
if [ -z "${sdk_version}" ]; then
    echo "::error::no .NET ${sdk_major} SDK installed, cannot build ${TARGET_FRAMEWORK}"
    exit 1
fi

SDK_DIR="$(mktemp -d)"
printf '{ "sdk": { "version": "%s", "rollForward": "latestFeature" } }\n' "${sdk_version}" \
    > "${SDK_DIR}/global.json"

# See the Android runner: NuGet would otherwise serve a cached copy of a version we just re-packed.
rm -rf "${HOME}/.nuget/packages/red5pro.streaming.net.ios/${VERSION}"

# Debug, not Release. An iOS Release build trims and AOT-compiles, which for an app carrying the
# WebRTC framework takes the better part of an hour on a CI runner - and buys nothing here, since
# this app is never shipped and the binding's trimming behaviour is exercised by the explicit
# trimmed run instead. Debug still links the native frameworks, which is the part that matters.
CONFIGURATION="Debug"

echo "==> building device tests (version=${VERSION}, tfm=${TARGET_FRAMEWORK}, rid=${DEVICE_RID})"
red5_config_summary

( cd "${SDK_DIR}" && dotnet build "${PROJECT}" \
    --configuration "${CONFIGURATION}" \
    -f "${TARGET_FRAMEWORK}" \
    -p:Red5PackageVersion="${VERSION}" \
    -p:Red5DeviceTargetFramework="${TARGET_FRAMEWORK}" \
    -p:Red5ProIosSdk="${RED5_IOS_SDK}" \
    -p:Red5Trimming="${RED5_TRIMMING:-none}" \
    -p:RuntimeIdentifier="${DEVICE_RID}" )

APP="$(find "$(dirname "${PROJECT}")/bin/${CONFIGURATION}/${TARGET_FRAMEWORK}/${DEVICE_RID}" \
    -maxdepth 1 -name '*.app' -type d | head -1)"
if [ -z "${APP}" ]; then
    echo "::error::build succeeded but no .app bundle was produced" >&2
    exit 1
fi
echo "==> built ${APP}"

# Newest available iPhone simulator. Pinning a specific device name would break every time the
# runner image drops that model.
UDID="$(xcrun simctl list devices available --json | python3 -c '
import json, sys
devices = json.load(sys.stdin)["devices"]
candidates = [
    device
    for runtime, entries in sorted(devices.items())
    if "iOS" in runtime
    for device in entries
    if device.get("isAvailable") and "iPhone" in device["name"]
]
print(candidates[-1]["udid"] if candidates else "")
')"

if [ -z "${UDID}" ]; then
    echo "::error::no available iPhone simulator on this runner" >&2
    xcrun simctl list devices available >&2
    exit 1
fi

echo "==> booting simulator ${UDID}"
# 'boot' fails if the device is already booted, which is fine and not worth failing the run over.
xcrun simctl boot "${UDID}" 2>/dev/null || true
xcrun simctl bootstatus "${UDID}" -b

cleanup() {
    xcrun simctl shutdown "${UDID}" >/dev/null 2>&1 || true
    rm -rf "${SDK_DIR}"
}
trap cleanup EXIT

echo "==> installing"
xcrun simctl install "${UDID}" "${APP}"

echo "==> launching"
# The app reads its credentials from the environment; simctl forwards SIMCTL_CHILD_* to the child.
# Passed this way rather than as launch arguments so a licence key never reaches a command line
# that `ps` can read.
#
# --console-pty streams the app's stdout and blocks until it exits, so the app's own
# Environment.Exit is what ends this step. macOS has no coreutils timeout, so the guard against a
# hang before that point is a watchdog that kills the launch.
SIMCTL_CHILD_RED5_LICENSE_KEY="${RED5_LICENSE_KEY:-}" \
SIMCTL_CHILD_RED5_ENDPOINT="${RED5_ENDPOINT:-}" \
SIMCTL_CHILD_RED5_DEPLOYMENT="${RED5_DEPLOYMENT:-cloud}" \
    xcrun simctl launch --console-pty "${UDID}" "${BUNDLE_ID}" > "${LOG_FILE}" 2>&1 &
launch_pid=$!

( sleep 300; kill -TERM "${launch_pid}" 2>/dev/null ) &
watchdog_pid=$!
# Detached so the shell does not print a "Terminated" job notice over the test output when the
# watchdog is killed on the happy path.
disown "${watchdog_pid}" 2>/dev/null || true

set +e
wait "${launch_pid}"
status=$?
set -e
kill "${watchdog_pid}" 2>/dev/null || true

cat "${LOG_FILE}"

if [ "${status}" -ne 0 ]; then
    echo "==> the app exited with status ${status} (killed by the watchdog if it ran for 300s)"
fi

if ! grep -q "RED5_E2E_DONE PASS" "${LOG_FILE}"; then
    echo "==> no passing verdict; capturing the simulator's crash log"
    xcrun simctl spawn "${UDID}" log show --last 2m --predicate "process CONTAINS 'DeviceTests'" \
        2>/dev/null | tail -100 | tee -a "${LOG_FILE}" || true
    echo "::error::Red5 iOS simulator device tests failed or timed out"
    exit 1
fi

echo "==> simulator device tests passed"
