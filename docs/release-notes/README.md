# Release notes

`release.yml` looks for `docs/release-notes/<version>.md` when a `v*` tag is pushed and uses it
verbatim as the GitHub release body. The lookup drops a prerelease suffix, so `v2.1.0.2-beta.2`
reuses the notes written for `2.1.0.2`.

Without a matching file the workflow falls back to the commit subjects since the previous tag, which
is serviceable but reads like a changelog rather than release notes — worth writing one by hand for
anything a consumer would care about, particularly the Red5 SDK version being bound and any change
to the `Red5ProAndroidSdk` / `Red5ProIosSdk` contract.
