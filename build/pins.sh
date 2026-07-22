#!/usr/bin/env bash
# Reads the native SDK pins out of Directory.Build.props and exports them.
#
# Directory.Build.props is the single source of truth: MSBuild reads it when building, and the
# fetch scripts, BuildNugets.sh and the CI cache keys read it through here. Sourced, not executed:
#
#   . "$(dirname "$0")/../build/pins.sh"
#
# Exports RED5_VERSION, RED5_ANDROID_BUILD, RED5_ANDROID_AAR_URL, RED5_ANDROID_AAR_SHA256,
# RED5_IOS_BUILD, RED5_IOS_XCFRAMEWORK_URL, RED5_IOS_XCFRAMEWORK_SHA256,
# RED5_WEBRTC_ANDROID_VERSION, RED5_WEBRTC_IOS_REPOSITORY, RED5_WEBRTC_IOS_VERSION,
# RED5_ANDROID_MIN_SDK, RED5_IOS_MIN_VERSION.

# shellcheck disable=SC2034  # consumers use these; shellcheck cannot see across the source.

RED5_REPO_ROOT="${RED5_REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
RED5_PROPS="${RED5_REPO_ROOT}/Directory.Build.props"

# grep -P is unavailable on macOS, so this stays with sed. Reads the first occurrence only, which
# matters because Directory.Build.props mentions some of these names in comments.
red5_raw_prop() {
    local name="$1" value
    value="$(sed -n "s|.*<${name}>\\([^<]*\\)</${name}>.*|\\1|p" "${RED5_PROPS}" | head -1)"
    if [ -z "${value}" ]; then
        echo "error: <${name}> not found in ${RED5_PROPS}" >&2
        return 1
    fi
    printf '%s' "${value}"
}

# The URLs are composed from other properties - $(Red5Version) and $(Red5AndroidBuild) appear
# inside them - so a raw read returns the unexpanded MSBuild text. Expanding here keeps the
# property definitions readable in the props file instead of forcing every URL to be spelled out
# in full twice.
#
# Deliberately a fixed list rather than a general evaluator: these are the only properties any URL
# interpolates, and expanding arbitrary $(...) from a file into shell would be a poor idea.
red5_prop() {
    local value
    value="$(red5_raw_prop "$1")" || return 1
    value="${value//\$(Red5Version)/${RED5_VERSION}}"
    value="${value//\$(Red5AndroidBuild)/${RED5_ANDROID_BUILD}}"
    value="${value//\$(Red5IosBuild)/${RED5_IOS_BUILD}}"

    # An unexpanded reference means a new interpolation was added to the props file without being
    # taught to this function, which would otherwise surface as a 404 from curl.
    case "${value}" in
        *'$('*)
            echo "error: unexpanded MSBuild property in <$1>: ${value}" >&2
            return 1
            ;;
    esac

    printf '%s' "${value}"
}

# Order matters: the version and build suffixes are read first because the URLs interpolate them.
RED5_VERSION="$(red5_raw_prop Red5Version)" || return 1 2>/dev/null || exit 1
RED5_ANDROID_BUILD="$(red5_raw_prop Red5AndroidBuild)" || return 1 2>/dev/null || exit 1
RED5_IOS_BUILD="$(red5_raw_prop Red5IosBuild)" || return 1 2>/dev/null || exit 1

RED5_ANDROID_AAR_URL="$(red5_prop Red5AndroidAarUrl)" || return 1 2>/dev/null || exit 1
RED5_ANDROID_AAR_SHA256="$(red5_prop Red5AndroidAarSha256)" || return 1 2>/dev/null || exit 1
RED5_IOS_XCFRAMEWORK_URL="$(red5_prop Red5IosXcframeworkUrl)" || return 1 2>/dev/null || exit 1
RED5_IOS_XCFRAMEWORK_SHA256="$(red5_prop Red5IosXcframeworkSha256)" || return 1 2>/dev/null || exit 1

RED5_WEBRTC_ANDROID_VERSION="$(red5_prop WebRtcAndroidVersion)" || return 1 2>/dev/null || exit 1
RED5_WEBRTC_IOS_REPOSITORY="$(red5_prop WebRtcIosRepository)" || return 1 2>/dev/null || exit 1
RED5_WEBRTC_IOS_VERSION="$(red5_prop WebRtcIosVersion)" || return 1 2>/dev/null || exit 1

RED5_ANDROID_MIN_SDK="$(red5_prop Red5AndroidMinSdk)" || return 1 2>/dev/null || exit 1
RED5_IOS_MIN_VERSION="$(red5_prop Red5IosMinVersion)" || return 1 2>/dev/null || exit 1

export RED5_REPO_ROOT RED5_VERSION
export RED5_ANDROID_BUILD RED5_ANDROID_AAR_URL RED5_ANDROID_AAR_SHA256
export RED5_IOS_BUILD RED5_IOS_XCFRAMEWORK_URL RED5_IOS_XCFRAMEWORK_SHA256
export RED5_WEBRTC_ANDROID_VERSION RED5_WEBRTC_IOS_REPOSITORY RED5_WEBRTC_IOS_VERSION
export RED5_ANDROID_MIN_SDK RED5_IOS_MIN_VERSION
