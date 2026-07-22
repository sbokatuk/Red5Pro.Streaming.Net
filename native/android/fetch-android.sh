#!/usr/bin/env bash
set -euo pipefail

# Downloads the Red5 WebRTC Android SDK .aar and drops it where the binding project expects it
# (src/Red5Pro.Streaming.Net.Android/Jars/red5-android-sdk.aar).
#
# Usage:
#   ./native/android/fetch-android.sh                 # the pinned url from Directory.Build.props
#   ./native/android/fetch-android.sh <url>           # some other build
#   RED5_ANDROID_AAR=/path/to/local.aar ./native/android/fetch-android.sh
#
# WHY THIS DOWNLOADS RATHER THAN BUILDS
# The Red5 SDK is closed source - there is no repository to build from, only a published binary.
# So unlike a source-built binding this script's job is to fetch the exact artifact that was
# pinned and prove it is the one we expect, which is what the sha256 check is for: the CDN serves
# a mutable path, and a silently re-uploaded .aar would otherwise change what gets bound without
# anything in git changing.
#
# The download needs no account. Red5 gates the SDK at *runtime* with a licence key, not at the
# download - which is what lets CI fetch it. Red5 Pro customers can equally take the same .aar
# from Account -> Downloads; point RED5_ANDROID_AAR at it to use that copy instead.
#
# Note the host: red5-cloud-sdk.cachefly.net, never red5.net. Every red5.net url answers 403 to a
# scripted client, so anything pointed at the documentation host cannot be automated.
#
# THIS FILE IS NOT REDISTRIBUTED. The Red5 EULA forbids bundling the SDK (sections 3.6 and 3.7),
# so the .aar is git-ignored, and the packed NuGet carries the binding assembly only - consumers
# run this script or supply their own copy. See README.md.
#
# Requires: curl, shasum (or sha256sum), unzip.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
. "${SCRIPT_DIR}/../../build/pins.sh"

URL="${1:-${RED5_ANDROID_AAR_URL}}"
EXPECTED_SHA="${RED5_ANDROID_AAR_SHA256}"
DESTINATION="${RED5_REPO_ROOT}/src/Red5Pro.Streaming.Net.Android/Jars/red5-android-sdk.aar"
WORK_DIR="${RED5_REPO_ROOT}/native/build/android"

mkdir -p "${WORK_DIR}" "$(dirname "${DESTINATION}")"

# shasum ships with macOS, sha256sum with most Linux images; CI runs both.
sha256_of() {
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | cut -d' ' -f1
    else
        sha256sum "$1" | cut -d' ' -f1
    fi
}

if [ -n "${RED5_ANDROID_AAR:-}" ]; then
    # A locally supplied .aar - typically from Account -> Downloads - is taken as authoritative and
    # its checksum is only reported, not enforced: it is legitimately a different build from the
    # pinned one, and failing on that would make the escape hatch useless.
    if [ ! -f "${RED5_ANDROID_AAR}" ]; then
        echo "::error::RED5_ANDROID_AAR is set but '${RED5_ANDROID_AAR}' does not exist" >&2
        exit 1
    fi

    echo "==> using local .aar ${RED5_ANDROID_AAR}"
    cp "${RED5_ANDROID_AAR}" "${DESTINATION}"

    actual="$(sha256_of "${DESTINATION}")"
    if [ "${actual}" != "${EXPECTED_SHA}" ]; then
        echo "::warning::local .aar sha256 ${actual} differs from the pin ${EXPECTED_SHA};" >&2
        echo "           the generated binding will describe *this* file, not the pinned build" >&2
    fi
else
    echo "==> Red5 WebRTC Android SDK ${RED5_VERSION} (${RED5_ANDROID_BUILD})"
    echo "==> ${URL}"

    DOWNLOAD="${WORK_DIR}/red5-android-sdk.aar.download"
    # --fail so an HTML error page is not silently written out as a .aar; -L because the CDN
    # redirects.
    curl --fail --location --silent --show-error --output "${DOWNLOAD}" "${URL}"

    actual="$(sha256_of "${DOWNLOAD}")"
    if [ "${actual}" != "${EXPECTED_SHA}" ]; then
        echo "::error::sha256 mismatch for ${URL}" >&2
        echo "  expected ${EXPECTED_SHA}" >&2
        echo "  actual   ${actual}" >&2
        echo "If Red5 republished this build, update Red5AndroidAarSha256 in Directory.Build.props" >&2
        echo "and re-check the API surface before trusting it." >&2
        rm -f "${DOWNLOAD}"
        exit 1
    fi

    mv "${DOWNLOAD}" "${DESTINATION}"
fi

# The .aar is only ~578 KB and carries no jniLibs whatsoever - 127 KB of classes plus some
# drawables. Every org.webrtc type and every native .so comes from io.github.webrtc-sdk:android
# instead, which the binding project references separately. Asserting it here means a future
# release that starts vendoring its own libwebrtc is noticed at fetch time rather than as a
# duplicate-symbol or UnsatisfiedLinkError much later.
if unzip -l "${DESTINATION}" | grep -q 'jni/'; then
    echo "::warning::this .aar now carries jniLibs, which it did not at ${RED5_VERSION};" >&2
    echo "           check for a clash with io.github.webrtc-sdk:android ${RED5_WEBRTC_ANDROID_VERSION}" >&2
fi

# The SDK's own floor, read from the .aar rather than copied from Red5's testbed. The binding
# project pins SupportedOSPlatformVersion to Red5AndroidMinSdk, so a bump upstream must be
# reflected in Directory.Build.props or consumers get a manifest-merger failure instead.
MANIFEST_MIN_SDK="$(unzip -p "${DESTINATION}" AndroidManifest.xml 2>/dev/null \
    | sed -n 's/.*minSdkVersion="\([0-9]*\)".*/\1/p' | head -1)"

if [ -n "${MANIFEST_MIN_SDK}" ] && [ "${MANIFEST_MIN_SDK}" != "${RED5_ANDROID_MIN_SDK}" ]; then
    echo "::warning::the .aar declares minSdkVersion ${MANIFEST_MIN_SDK} but Directory.Build.props" >&2
    echo "           pins Red5AndroidMinSdk to ${RED5_ANDROID_MIN_SDK}" >&2
fi

echo "==> ${DESTINATION} ($(du -h "${DESTINATION}" | cut -f1), minSdkVersion ${MANIFEST_MIN_SDK:-unknown})"

# ---------------------------------------------------------------------------
# libwebrtc
# ---------------------------------------------------------------------------
# Every org.webrtc type and every native .so the Red5 SDK calls into comes from here - see the
# jniLibs note above. Unlike the Red5 SDK this is BSD-licensed and freely redistributable, so it
# *is* shipped inside our package; that asymmetry is why the two are fetched separately.
#
# 47 MB, and identical for all three target frameworks, so the binding project packs it once at
# the package root rather than under each lib/<tfm>/.
WEBRTC_DESTINATION="${RED5_REPO_ROOT}/src/Red5Pro.Streaming.Net.Android/Jars/webrtc-android.aar"
WEBRTC_URL="https://repo1.maven.org/maven2/io/github/webrtc-sdk/android/${RED5_WEBRTC_ANDROID_VERSION}/android-${RED5_WEBRTC_ANDROID_VERSION}.aar"

if [ -f "${WEBRTC_DESTINATION}" ]; then
    echo "==> libwebrtc ${RED5_WEBRTC_ANDROID_VERSION} already present"
else
    echo "==> libwebrtc ${RED5_WEBRTC_ANDROID_VERSION}"
    echo "==> ${WEBRTC_URL}"
    curl --fail --location --silent --show-error --output "${WEBRTC_DESTINATION}.download" "${WEBRTC_URL}"
    mv "${WEBRTC_DESTINATION}.download" "${WEBRTC_DESTINATION}"
fi

# The whole reason this is here: if the ABIs the Red5 SDK needs are missing, the binding generates,
# packs and installs, then dies on first use with UnsatisfiedLinkError. x86_64 in particular is
# what lets the smoke test run on a CI emulator.
ABIS="$(unzip -l "${WEBRTC_DESTINATION}" | sed -n 's|.*jni/\([^/]*\)/.*|\1|p' | sort -u | tr '\n' ' ')"
echo "==> libwebrtc ABIs: ${ABIS:-none}"

case "${ABIS}" in
    *x86_64*) ;;
    *)
        echo "::error::libwebrtc carries no x86_64 slice, so the CI emulator smoke test cannot run" >&2
        exit 1
        ;;
esac

cat > "${WORK_DIR}/pin.txt" <<EOF
version=${RED5_VERSION}
build=${RED5_ANDROID_BUILD}
url=${URL}
sha256=$(sha256_of "${DESTINATION}")
min_sdk=${MANIFEST_MIN_SDK:-unknown}
webrtc_version=${RED5_WEBRTC_ANDROID_VERSION}
webrtc_abis=${ABIS}
EOF

echo "==> ${WEBRTC_DESTINATION} ($(du -h "${WEBRTC_DESTINATION}" | cut -f1))"
