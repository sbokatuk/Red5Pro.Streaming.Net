#!/usr/bin/env bash
set -euo pipefail

# Stages the iOS native layer for the binding project:
#
#   src/Red5Pro.Streaming.Net.iOS/lib/Red5ProFacade.xcframework   built here, ours, shipped
#   src/Red5Pro.Streaming.Net.iOS/lib/WebRTC.xcframework          stasel/WebRTC, BSD, shipped
#
# Red5WebRTCKit.xcframework is downloaded too, but only so the facade can be compiled against it.
# It is deliberately *not* staged into lib/ and never enters a NuGet package: the Red5 EULA
# forbids redistributing it (3.6, 3.7), so consumers supply their own licensed copy. See README.md.
#
# Usage:
#   ./native/ios/fetch-ios.sh                 # pins from Directory.Build.props
#   RED5_IOS_XCFRAMEWORK=/path/to/dir ./native/ios/fetch-ios.sh
#       # use an already-unpacked Red5WebRTCKit.xcframework, e.g. the 2.0.0 build from a
#       # Red5 Pro account download rather than the 2.1.0.2 one on the Cloud CDN
#
# WHY swiftc RATHER THAN xcodebuild
# ---------------------------------
# The facade is one Swift file that has to become a framework carrying an Objective-C header.
# Driving that through xcodebuild means committing an .xcodeproj and keeping its pbxproj in step
# with the file list; compiling directly keeps the whole thing in this script, and - more
# importantly - guarantees the output is a *static* library with the Red5 symbols left
# unresolved. That is the property the BYO-SDK packaging depends on: our framework references
# Red5WebRTCKit, it does not contain it.
#
# Requires: macOS with Xcode, curl, unzip, python3.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
. "${SCRIPT_DIR}/../../build/pins.sh"

if ! command -v xcodebuild >/dev/null 2>&1; then
    echo "::error::xcodebuild not found - the iOS native build requires macOS with Xcode" >&2
    exit 1
fi

MODULE="Red5ProFacade"
WORK_DIR="${RED5_REPO_ROOT}/native/build/ios"
DESTINATION="${RED5_REPO_ROOT}/src/Red5Pro.Streaming.Net.iOS/lib"
FACADE_SOURCE="${SCRIPT_DIR}/Facade/${MODULE}.swift"

# stasel publishes one asset per major: WebRTC-M140.xcframework.zip on tag 140.0.0.
WEBRTC_MAJOR="${RED5_WEBRTC_IOS_VERSION%%.*}"
WEBRTC_URL="${RED5_WEBRTC_IOS_REPOSITORY}/releases/download/${RED5_WEBRTC_IOS_VERSION}/WebRTC-M${WEBRTC_MAJOR}.xcframework.zip"

mkdir -p "${WORK_DIR}" "${DESTINATION}"

sha256_of() { shasum -a 256 "$1" | cut -d' ' -f1; }

# Downloads and unpacks an xcframework zip, skipping the work when it is already there. Verifies
# the checksum when one is supplied; stasel's assets are not pinned by hash because the release
# tag is already immutable and the file is 44 MB.
fetch_xcframework() {
    local url="$1" name="$2" expected_sha="${3:-}"
    local zip="${WORK_DIR}/${name}.zip"

    if [ -d "${WORK_DIR}/${name}.xcframework" ]; then
        echo "==> ${name}.xcframework already unpacked"
        return
    fi

    echo "==> downloading ${name}"
    echo "    ${url}"
    curl --fail --location --silent --show-error --output "${zip}" "${url}"

    if [ -n "${expected_sha}" ]; then
        local actual
        actual="$(sha256_of "${zip}")"
        if [ "${actual}" != "${expected_sha}" ]; then
            echo "::error::sha256 mismatch for ${url}" >&2
            echo "  expected ${expected_sha}" >&2
            echo "  actual   ${actual}" >&2
            rm -f "${zip}"
            exit 1
        fi
    fi

    # The zips vary in whether they carry a wrapping directory, so unpack to a scratch directory
    # and go looking for the .xcframework rather than assuming a layout.
    local scratch="${WORK_DIR}/unpack-${name}"
    rm -rf "${scratch}"
    mkdir -p "${scratch}"
    unzip -q "${zip}" -d "${scratch}"

    local found
    found="$(find "${scratch}" -maxdepth 3 -name '*.xcframework' -type d | head -1)"
    if [ -z "${found}" ]; then
        echo "::error::no .xcframework found inside ${zip}" >&2
        exit 1
    fi

    rm -rf "${WORK_DIR}/${name}.xcframework"
    mv "${found}" "${WORK_DIR}/${name}.xcframework"
    rm -rf "${scratch}" "${zip}"
}

echo "==> Red5 WebRTC iOS SDK ${RED5_VERSION} (${RED5_IOS_BUILD})"

if [ -n "${RED5_IOS_XCFRAMEWORK:-}" ]; then
    if [ ! -d "${RED5_IOS_XCFRAMEWORK}" ]; then
        echo "::error::RED5_IOS_XCFRAMEWORK is set but '${RED5_IOS_XCFRAMEWORK}' is not a directory" >&2
        exit 1
    fi
    echo "==> using local Red5WebRTCKit from ${RED5_IOS_XCFRAMEWORK}"
    rm -rf "${WORK_DIR}/Red5WebRTCKit.xcframework"
    cp -R "${RED5_IOS_XCFRAMEWORK}" "${WORK_DIR}/Red5WebRTCKit.xcframework"
else
    fetch_xcframework "${RED5_IOS_XCFRAMEWORK_URL}" "Red5WebRTCKit" "${RED5_IOS_XCFRAMEWORK_SHA256}"
fi

fetch_xcframework "${WEBRTC_URL}" "WebRTC"

# Locates the slice inside an xcframework whose Info.plist matches a platform and (optionally) a
# variant. Reading the plist rather than guessing directory names, because the naming differs
# between vendors - Red5 ships ios-arm64_x86_64-simulator, stasel ships ios-arm64-simulator on
# some releases.
slice_of() {
    local xcframework="$1" platform="$2" variant="${3:-}"
    python3 - "$xcframework" "$platform" "$variant" <<'PY'
import plistlib, sys, pathlib
root, platform, variant = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3]
plist = plistlib.loads((root / "Info.plist").read_bytes())
for lib in plist["AvailableLibraries"]:
    if lib["SupportedPlatform"] != platform:
        continue
    if (lib.get("SupportedPlatformVariant") or "") != variant:
        continue
    print(root / lib["LibraryIdentifier"])
    break
PY
}

# Builds one architecture of the facade and leaves the artefacts in a per-arch directory.
#
# -enable-library-evolution plus a module interface is what lets the framework be consumed by a
# different Swift compiler version than the one that built it - the same reason Red5 ship a
# .swiftinterface. Without it the framework is only usable from this exact toolchain.
compile_slice() {
    local sdk="$1" target="$2" arch="$3" out="$4" red5_slice="$5" webrtc_slice="$6"

    mkdir -p "${out}"

    xcrun --sdk "${sdk}" swiftc \
        -emit-library -static \
        -module-name "${MODULE}" \
        -target "${target}" \
        -swift-version 5 \
        -enable-library-evolution \
        -emit-module -emit-module-path "${out}/${MODULE}.swiftmodule" \
        -emit-module-interface-path "${out}/${MODULE}.swiftinterface" \
        -emit-objc-header -emit-objc-header-path "${out}/${MODULE}-Swift.h" \
        -F "${red5_slice}" -F "${webrtc_slice}" \
        -O \
        "${FACADE_SOURCE}" \
        -o "${out}/lib${MODULE}-${arch}.a"
}

# Assembles a real .framework around one or more compiled architectures.
#
# Done by hand because swiftc emits loose products; the layout below is what the linker, the
# Objective-C runtime and .NET's binding tooling all expect to find.
assemble_framework() {
    local framework="$1" min_version="$2"; shift 2
    local slices=("$@")

    rm -rf "${framework}"
    mkdir -p "${framework}/Headers" "${framework}/Modules/${MODULE}.swiftmodule"

    # One static binary covering every architecture in this slice.
    local archives=()
    for slice in "${slices[@]}"; do
        archives+=("${slice}"/lib${MODULE}-*.a)
    done
    lipo -create "${archives[@]}" -output "${framework}/${MODULE}"

    # The generated Objective-C header, plus an umbrella that includes it. The umbrella is what
    # the module map names, and what a consumer's #import <Red5ProFacade/Red5ProFacade.h> resolves.
    cp "${slices[0]}/${MODULE}-Swift.h" "${framework}/Headers/"
    cat > "${framework}/Headers/${MODULE}.h" <<EOF
// Umbrella header for ${MODULE}. Generated by native/ios/fetch-ios.sh - do not edit.
#import <${MODULE}/${MODULE}-Swift.h>
EOF

    cat > "${framework}/Modules/module.modulemap" <<EOF
framework module ${MODULE} {
    umbrella header "${MODULE}.h"
    export *
    module * { export * }
}
EOF

    # Swift module artefacts, one set per architecture, named as Swift expects to find them.
    for slice in "${slices[@]}"; do
        local arch
        arch="$(basename "${slice}")"
        cp "${slice}/${MODULE}.swiftmodule" \
            "${framework}/Modules/${MODULE}.swiftmodule/${arch}.swiftmodule"
        cp "${slice}/${MODULE}.swiftinterface" \
            "${framework}/Modules/${MODULE}.swiftmodule/${arch}.swiftinterface"
    done

    /usr/libexec/PlistBuddy -c "Clear dict" \
        -c "Add :CFBundleIdentifier string net.red5.streaming.facade" \
        -c "Add :CFBundleName string ${MODULE}" \
        -c "Add :CFBundleExecutable string ${MODULE}" \
        -c "Add :CFBundlePackageType string FMWK" \
        -c "Add :CFBundleShortVersionString string ${RED5_VERSION}" \
        -c "Add :MinimumOSVersion string ${min_version}" \
        "${framework}/Info.plist" >/dev/null
}

RED5_DEVICE="$(slice_of "${WORK_DIR}/Red5WebRTCKit.xcframework" ios)"
RED5_SIM="$(slice_of "${WORK_DIR}/Red5WebRTCKit.xcframework" ios simulator)"
WEBRTC_DEVICE="$(slice_of "${WORK_DIR}/WebRTC.xcframework" ios)"
WEBRTC_SIM="$(slice_of "${WORK_DIR}/WebRTC.xcframework" ios simulator)"

for pair in "Red5WebRTCKit device:${RED5_DEVICE}" "Red5WebRTCKit simulator:${RED5_SIM}" \
            "WebRTC device:${WEBRTC_DEVICE}" "WebRTC simulator:${WEBRTC_SIM}"; do
    if [ -z "${pair#*:}" ]; then
        echo "::error::could not locate the ${pair%%:*} slice" >&2
        exit 1
    fi
done

BUILD="${WORK_DIR}/facade"
rm -rf "${BUILD}"

echo "==> compiling the facade (device, arm64)"
compile_slice iphoneos "arm64-apple-ios${RED5_IOS_MIN_VERSION}" arm64 \
    "${BUILD}/device/arm64" "${RED5_DEVICE}" "${WEBRTC_DEVICE}"

echo "==> compiling the facade (simulator, arm64 + x86_64)"
compile_slice iphonesimulator "arm64-apple-ios${RED5_IOS_MIN_VERSION}-simulator" arm64 \
    "${BUILD}/simulator/arm64" "${RED5_SIM}" "${WEBRTC_SIM}"
compile_slice iphonesimulator "x86_64-apple-ios${RED5_IOS_MIN_VERSION}-simulator" x86_64 \
    "${BUILD}/simulator/x86_64" "${RED5_SIM}" "${WEBRTC_SIM}"

echo "==> assembling frameworks"
assemble_framework "${BUILD}/device/${MODULE}.framework" "${RED5_IOS_MIN_VERSION}" \
    "${BUILD}/device/arm64"
assemble_framework "${BUILD}/simulator/${MODULE}.framework" "${RED5_IOS_MIN_VERSION}" \
    "${BUILD}/simulator/arm64" "${BUILD}/simulator/x86_64"

# A Swift @objc class with no explicit Objective-C name is exported under its mangled name
# (_OBJC_CLASS_$__TtC13Red5ProFacade8R5Client) even though the generated header calls it
# R5Client, while the .NET binding links against _OBJC_CLASS_$_R5Client. Every consuming app then
# fails at link time with an undefined symbol, and nothing before that notices. So the built
# binary is checked, not the header.
echo "==> verifying the Objective-C surface"
for class in R5Client; do
    if ! nm -gU "${BUILD}/device/${MODULE}.framework/${MODULE}" 2>/dev/null \
        | grep -q "_OBJC_CLASS_\\\$_${class}\$"; then
        echo "::error::${MODULE} does not export _OBJC_CLASS_\$_${class};" >&2
        echo "         the facade class needs an explicit @objc(${class}) name" >&2
        exit 1
    fi
    echo "    _OBJC_CLASS_\$_${class} exported"
done

# The facade must reference Red5WebRTCKit rather than contain it - that is what makes the
# BYO-SDK packaging lawful as well as correct. A build that accidentally absorbed the SDK would
# ship it inside our package.
if nm -gU "${BUILD}/device/${MODULE}.framework/${MODULE}" 2>/dev/null \
    | grep -q "Red5WebrtcClientBuilder"; then
    echo "::error::${MODULE} appears to define Red5 symbols rather than import them;" >&2
    echo "         the SDK must stay an undefined reference, resolved by the consuming app" >&2
    exit 1
fi
echo "    Red5WebRTCKit left as an undefined reference"

echo "==> creating the xcframework"
rm -rf "${WORK_DIR}/${MODULE}.xcframework"
xcodebuild -create-xcframework \
    -framework "${BUILD}/device/${MODULE}.framework" \
    -framework "${BUILD}/simulator/${MODULE}.framework" \
    -output "${WORK_DIR}/${MODULE}.xcframework" >/dev/null

rm -rf "${DESTINATION}/${MODULE}.xcframework" "${DESTINATION}/WebRTC.xcframework"
cp -R "${WORK_DIR}/${MODULE}.xcframework" "${DESTINATION}/"
cp -R "${WORK_DIR}/WebRTC.xcframework" "${DESTINATION}/"

cat > "${WORK_DIR}/pin.txt" <<EOF
red5_version=${RED5_VERSION}
red5_build=${RED5_IOS_BUILD}
red5_sha256=${RED5_IOS_XCFRAMEWORK_SHA256}
webrtc_version=${RED5_WEBRTC_IOS_VERSION}
facade_min_ios=${RED5_IOS_MIN_VERSION}
EOF

echo "==> staged in ${DESTINATION}:"
du -sh "${DESTINATION}"/*.xcframework
echo "==> Red5WebRTCKit.xcframework deliberately NOT staged - consumers supply their own"
