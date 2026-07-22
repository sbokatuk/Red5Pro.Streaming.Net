#!/usr/bin/env bash
# Resolves the Red5 licence keys and endpoints for shell callers, with the same precedence MSBuild
# uses: environment first, Directory.Build.local.props second. Sourced, not executed:
#
#   . "$(dirname "$0")/../build/local-config.sh"
#
# Exports RED5_PRO_LICENSE_KEY, RED5_PRO_ENDPOINT, RED5_CLOUD_LICENSE_KEY, RED5_CLOUD_ENDPOINT -
# each possibly empty, which callers are expected to treat as "skip this tier" rather than as an
# error. That is deliberate: a fork's pull request has no secrets, and the offline smoke tests must
# still run there.
#
# Nothing here is echoed. The device-test runners pass these to the app as intent extras or
# environment, never to stdout, so a CI log cannot leak a key.

# shellcheck disable=SC2034  # consumers use these; shellcheck cannot see across the source.

RED5_REPO_ROOT="${RED5_REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
RED5_LOCAL_PROPS="${RED5_REPO_ROOT}/Directory.Build.local.props"

# Same sed approach as pins.sh, for the same reason: grep -P is unavailable on macOS. Reads the
# first occurrence only, so a commented-out example above the real value does not win.
red5_local_prop() {
    local name="$1"
    [ -f "${RED5_LOCAL_PROPS}" ] || return 0
    sed -n "s|.*<${name}[^>]*>\\([^<]*\\)</${name}>.*|\\1|p" "${RED5_LOCAL_PROPS}" | head -1
}

# Environment wins; the local file only fills gaps.
: "${RED5_PRO_LICENSE_KEY:=$(red5_local_prop Red5ProLicenseKey)}"
: "${RED5_PRO_ENDPOINT:=$(red5_local_prop Red5ProEndpoint)}"
: "${RED5_CLOUD_LICENSE_KEY:=$(red5_local_prop Red5CloudLicenseKey)}"
: "${RED5_CLOUD_ENDPOINT:=$(red5_local_prop Red5CloudEndpoint)}"

export RED5_PRO_LICENSE_KEY RED5_PRO_ENDPOINT RED5_CLOUD_LICENSE_KEY RED5_CLOUD_ENDPOINT

# A one-line summary of what is available, with the keys reduced to their last four characters.
# Enough to tell "the key is missing" from "the key is wrong" in a CI log, without printing it.
red5_config_summary() {
    local pro cloud
    pro="${RED5_PRO_LICENSE_KEY:+…${RED5_PRO_LICENSE_KEY: -4}}"
    cloud="${RED5_CLOUD_LICENSE_KEY:+…${RED5_CLOUD_LICENSE_KEY: -4}}"
    printf 'red5 config: pro-key=%s pro-endpoint=%s cloud-key=%s cloud-endpoint=%s\n' \
        "${pro:-none}" "${RED5_PRO_ENDPOINT:-none}" \
        "${cloud:-none}" "${RED5_CLOUD_ENDPOINT:-none}"
}
