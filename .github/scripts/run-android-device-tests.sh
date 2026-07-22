#!/usr/bin/env bash
set -euo pipefail

# Installs the Android smoke-test app against a packed Red5Pro.Streaming.Net.Android package and
# runs it on the emulator the calling workflow step has already booted. The app reports results to
# logcat under the Red5E2E tag; this script turns them into an exit code.
#
# Usage: run-android-device-tests.sh <package-version> [target-framework]
#
# Tiers, selected by what is in the environment rather than by an argument:
#
#   offline           always. Native libraries load, bound types resolve, the transitive Java
#                     dependencies reached the dex.
#   licence + live    only when RED5_LICENSE_KEY and RED5_ENDPOINT are set. A fork's pull request
#                     has no secrets, so those are empty there and the app reports SKIP - which is
#                     printed either way, so a run without credentials cannot be mistaken for one
#                     that proved streaming works.
#
# The two are launched separately and deliberately. The offline checks initialise
# PeerConnectionFactory and EGL state that the SDK then cannot set up cleanly for a real session,
# so the live check gets a process of its own via -e skipOffline true.

VERSION="${1:?a package version is required}"
TARGET_FRAMEWORK="${2:-net10.0-android36.0}"

PACKAGE_NAME="net.red5.streaming.devicetests"
LOG_FILE="device-tests-logcat.txt"
# CI emulators are x86_64; override when running this against a local arm64 emulator or device.
DEVICE_RID="${RED5_DEVICE_RID:-android-x64}"
POLL_ATTEMPTS=60
POLL_INTERVAL=5

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="${REPO_ROOT}/tests/Red5Pro.Streaming.Net.Android.DeviceTests/Red5Pro.Streaming.Net.Android.DeviceTests.csproj"

. "${REPO_ROOT}/build/local-config.sh"

# The consumer-supplied SDK. The packages do not contain it - the Red5 EULA does not allow that -
# so even our own test app has to point at a copy, which the native fetch script has staged.
RED5_ANDROID_SDK="${RED5_ANDROID_SDK:-${REPO_ROOT}/src/Red5Pro.Streaming.Net.Android/Jars/red5-android-sdk.aar}"
if [ ! -f "${RED5_ANDROID_SDK}" ]; then
    echo "::error::${RED5_ANDROID_SDK} is missing. Run native/android/fetch-android.sh first." >&2
    exit 1
fi

# The .NET 9 band builds net8/net9 and the .NET 10 band builds net10, so pick the SDK that owns the
# requested target framework. The SDK is resolved from the working directory, and the repository's
# global.json pins .NET 9, hence the scratch directory.
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
trap 'rm -rf "${SDK_DIR}"' EXIT
printf '{ "sdk": { "version": "%s", "rollForward": "latestFeature" } }\n' "${sdk_version}" \
    > "${SDK_DIR}/global.json"

# NuGet caches by package id + version, so rebuilding a version that was already restored once
# silently reuses the stale copy. CI versions are unique, but locally you will re-pack the same
# version repeatedly and test yesterday's bits without this.
rm -rf "${HOME}/.nuget/packages/red5pro.streaming.net/${VERSION}" \
       "${HOME}/.nuget/packages/red5pro.streaming.net.android/${VERSION}"

TRIMMING="${RED5_TRIMMING:-none}"

echo "==> installing device tests (version=${VERSION}, tfm=${TARGET_FRAMEWORK}, sdk=${sdk_version}, trimming=${TRIMMING})"
red5_config_summary

BUILD_ARGS=(
    --configuration Release
    -p:Red5PackageVersion="${VERSION}"
    -p:Red5DeviceTargetFramework="${TARGET_FRAMEWORK}"
    -p:Red5ProAndroidSdk="${RED5_ANDROID_SDK}"
    -p:Red5Trimming="${TRIMMING}"
    -p:RuntimeIdentifier="${DEVICE_RID}"
)

if [ "${TRIMMING}" = "none" ]; then
    ( cd "${SDK_DIR}" && dotnet build "${PROJECT}" "${BUILD_ARGS[@]}" -t:Install )
else
    # PublishTrimmed is honoured on publish, not on build: `dotnet build -t:Install` produces an
    # untrimmed APK whatever the property says, so asking for trimming and then using the build
    # path would report a pass without the linker ever having run over the binding.
    ( cd "${SDK_DIR}" && dotnet publish "${PROJECT}" "${BUILD_ARGS[@]}" -f "${TARGET_FRAMEWORK}" )

    APK="$(find "$(dirname "${PROJECT}")/bin/Release/${TARGET_FRAMEWORK}/${DEVICE_RID}/publish" \
        -name '*-Signed.apk' | head -1)"
    if [ -z "${APK}" ]; then
        echo "::error::publish succeeded but no signed APK was produced" >&2
        exit 1
    fi

    echo "==> installing ${APK}"
    adb install -r "${APK}"
fi

# Granted rather than prompted for: there is nobody to tap a dialog on a CI emulator, and without
# them the SDK declines to publish, logs a permission complaint and then raises no callback at all -
# so the live check would fail as an unexplained timeout.
# Ignored failures: a device below Android 6 has them granted at install time and `pm grant` errors,
# which is not worth failing the run over.
echo "==> granting camera and microphone"
for permission in android.permission.CAMERA android.permission.RECORD_AUDIO android.permission.MODIFY_AUDIO_SETTINGS; do
    adb shell pm grant "${PACKAGE_NAME}" "${permission}" 2>/dev/null || true
done

# Runs one launch of the app and returns its verdict. `am start -S` forces a fresh instance:
# without it a second launch delivers an intent to the running activity, whose onCreate never runs
# again, and the script waits out its poll loop against the previous run's log.
run_tier() {
    local label="$1"; shift

    echo "==> ${label}"
    adb shell am force-stop "${PACKAGE_NAME}" >/dev/null 2>&1 || true
    adb logcat -c

    adb shell am start -S -n "${PACKAGE_NAME}/.MainActivity" "$@" >/dev/null

    for _ in $(seq 1 "${POLL_ATTEMPTS}"); do
        if adb logcat -d -s 'Red5E2E:*' | grep -q "RED5_E2E_DONE"; then
            break
        fi
        sleep "${POLL_INTERVAL}"
    done

    adb logcat -d -s 'Red5E2E:*' | tee -a "${LOG_FILE}"

    if ! adb logcat -d -s 'Red5E2E:*' | grep -q "RED5_E2E_DONE PASS"; then
        # No verdict usually means the app died before reporting, so keep the crash trace.
        echo "==> no passing verdict for '${label}'; capturing crash output"
        adb logcat -d -s AndroidRuntime:E DEBUG:F "${PACKAGE_NAME}:*" Red5WebrtcClient:* \
            | tee -a "${LOG_FILE}"
        return 1
    fi

    return 0
}

: > "${LOG_FILE}"
status=0

run_tier "offline checks" || status=1

if [ -n "${RED5_LICENSE_KEY:-}" ] && [ -n "${RED5_ENDPOINT:-}" ]; then
    # A separate launch, with the offline checks skipped - see the note at the top of this file.
    # The key is passed as an intent extra rather than echoed; `am start` output is discarded above.
    run_tier "licence + live publish (${RED5_DEPLOYMENT:-cloud})" \
        -e host "${RED5_ENDPOINT}" \
        -e licenseKey "${RED5_LICENSE_KEY}" \
        -e deployment "${RED5_DEPLOYMENT:-cloud}" \
        -e skipOffline true || status=1
else
    echo "==> no RED5_LICENSE_KEY/RED5_ENDPOINT: licence and live tiers skipped"
fi

if [ "${status}" -ne 0 ]; then
    echo "::error::Red5 Android device tests failed or timed out"
    exit 1
fi

echo "==> device tests passed"
