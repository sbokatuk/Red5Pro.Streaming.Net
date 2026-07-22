# Building Red5Pro.Streaming.Net

Everything here is driven by the pins in [`Directory.Build.props`](../Directory.Build.props) and run
by the scripts in `build/` and `native/`. CI runs exactly these scripts, so a green local run means
the same thing a green pipeline does.

## Layout

```
Directory.Build.props            native SDK pins, target frameworks, package metadata
Directory.Build.targets          repository-wide .aar packing filter
Directory.Build.local.props      licence keys and endpoints (git-ignored; .example is committed)
global.json                      pins the .NET 9 SDK (the "net9 band")
NuGet.config                     nuget.org + ./artifacts, so tests consume the packed packages
build/
  pins.sh                        the only parser of Directory.Build.props for shell callers
  local-config.sh                resolves RED5_* credentials: environment, then the local file
  BuildNugets.sh                 two-pass pack + merge -> ./artifacts
  merge-packages.py              combines the two passes into one package per id
native/
  android/fetch-android.sh       downloads the Red5 .aar and libwebrtc, checksum-pinned
  ios/fetch-ios.sh               downloads the Red5 xcframework and libwebrtc, builds our facade
  ios/Facade/                    the @objc facade compiled into Red5ProFacade.xcframework
  build/                         downloads and intermediates (git-ignored)
src/                             the binding projects, the cross-platform client, the MAUI package
tests/                           package validation + on-device tests
samples/                         the MAUI sample app
```

The repository spans .NET 8/9/10 and both platforms, so no single SDK can build all of it at once.
Build individual projects, or use `build/BuildNugets.sh`, which handles the bands.

## Prerequisites

- macOS with Xcode (iOS only; Android builds anywhere)
- .NET 9 and .NET 10 SDKs, with the `android`, `ios` and `maui` workloads for **each** band
- JDK 17 and an Android SDK (`ANDROID_HOME` or `ANDROID_SDK_ROOT`)

## Native SDKs

Both are **downloaded binaries**. Red5 publishes no source for either, so unlike a source-built
binding these scripts fetch the exact pinned artifact and prove it is the one expected — the CDN
serves a mutable path, and a silently re-uploaded file would otherwise change what gets bound
without anything in git changing.

```sh
./native/android/fetch-android.sh     # Red5 .aar (sha256-pinned) + libwebrtc from Maven Central
./native/ios/fetch-ios.sh             # Red5 xcframework (sha256-pinned) + libwebrtc, then the facade
```

Neither download needs an account: Red5 gates the SDK at **runtime** with a licence key, not at the
download. That is what lets CI fetch them. To build against your own copy instead — the 2.0.0-labelled
download from a Red5 Pro account, say — set `RED5_ANDROID_AAR` or `RED5_IOS_XCFRAMEWORK`.

> The host matters: `red5-cloud-sdk.cachefly.net`, never `red5.net`. Every `red5.net` URL answers
> HTTP 403 to a scripted client, so nothing pointed at the documentation host can be automated.

### Why iOS needs a facade

`Red5WebRTCKit.framework` ships an **empty `Headers/` directory** and exports exactly one
Objective-C class — `CameraManager`, under its mangled name, and not part of the streaming API.
`Red5WebrtcClientBuilder`, `Red5WebrtcClient`, `Red5ProWebrtcEventDelegate` and every enum are
Swift-only, so Objective Sharpie sees nothing worth binding.

[`native/ios/Facade/Red5ProFacade.swift`](../native/ios/Facade/Red5ProFacade.swift) re-exposes that
API as `@objc` types. Unlike a facade compiled *into* the vendor's framework, it is built as its own
framework that **links against** Red5WebRTCKit rather than containing it — which is both what the
EULA requires and what lets our package ship while the SDK does not.

Two properties are load-bearing, and `fetch-ios.sh` asserts both with `nm`:

- **`_OBJC_CLASS_$_R5Client` is exported under that exact name.** A Swift `@objc` class with no
  explicit name is exported mangled (`_TtC13Red5ProFacade8R5Client`) while the .NET binding links
  the unmangled form, and every consuming app then fails at link time.
- **Red5's symbols stay undefined.** A build that absorbed the SDK would ship it inside our package.

The script also strips the `macos` and `maccatalyst` slices from libwebrtc. This package is iOS-only
— Red5 publish no Catalyst slice for their own SDK, so a Catalyst build could never link — and the
unused slices were ~100 MB.

## Packing

```sh
./build/BuildNugets.sh                          # version from Directory.Build.props
./build/BuildNugets.sh 2.1.0.2-beta.4           # explicit version
./build/BuildNugets.sh 2.1.0.2-beta.4 android   # Android only (what the Linux CI runner does)
./build/BuildNugets.sh 2.1.0.2-beta.4 apple     # iOS, core and MAUI (the macOS runner)
```

Each project is packed **twice** and merged. No single .NET SDK can build net8, net9 and net10 for a
platform — each SDK's workload carries only the current band and the previous one:

| | SDK 9 band | SDK 10 band |
| --- | --- | --- |
| Android | `net8.0-android34.0`, `net9.0-android35.0` | `net10.0-android36.0` |
| iOS | `net8.0-ios18.0`, `net9.0-ios18.0` | `net10.0-ios26.0` |

`merge-packages.py` copies the missing `lib/<tfm>` trees from the second package into the first and
adds the matching nuspec dependency groups.

The platform version in each target framework is pinned deliberately. Bare `net8.0-android` resolves
to `android21.0`, which produces a binding assembly with no `.aar` payload — it compiles, packs and
installs, and fails only at runtime.

### Two packaging traps

Both were shipped-by-default behaviour, and both are now asserted by the package tests:

- **`Pack="false"` on an `AndroidLibrary` item is silently ignored.** The Red5 SDK went into every
  `lib/<tfm>/` — a licensing violation, not a bug. `Directory.Build.targets` filters `.aar` files out
  of `TfmSpecificPackageFileWithRecursiveDir` after `_IncludeAarInNuGetPackage`. Both that item group
  and that hook were found by dumping items at pack time; guessing produced a package that looked
  right and was not.
- **`build/` targets do not run for transitive references.** NuGet imports `build/` only for a direct
  `PackageReference`, and nobody references the bindings directly — an app references `…Maui`. Packed
  under `build/`, the targets silently never ran, so libwebrtc never reached the javac classpath and
  `Red5ProAndroidSdk` was ignored. They live under `buildTransitive/`, which covers both.

## Licence keys

Needed only by the things that talk to a real server: the device tests' licence and live tiers, and
the sample. Copy `Directory.Build.local.props.example` to `Directory.Build.local.props` and fill it
in — that file is git-ignored.

Environment first, local file second, so CI supplies the same values from repository secrets and a
developer machine exports nothing:

| Property | Environment variable | Where to get it |
| --- | --- | --- |
| `Red5ProLicenseKey` | `RED5_PRO_LICENSE_KEY` | account.red5.net → **SDK** License |
| `Red5ProEndpoint` | `RED5_PRO_ENDPOINT` | your standalone server's host or IP |
| `Red5CloudLicenseKey` | `RED5_CLOUD_LICENSE_KEY` | cloud.red5.net → Dev Resources → Client SDK License |
| `Red5CloudEndpoint` | `RED5_CLOUD_ENDPOINT` | your `<id>.cloud.red5.net` host, no scheme |

**A Red5 Pro account issues two keys and only one works here.** The *Server* licence activates a
server install; the *SDK* licence activates the client. Red5 Cloud issues its own, separately.

Empty means *skip that tier*, never fail — a forked pull request has no secrets and must still run
the offline checks.

## Testing

```sh
dotnet test tests/Red5Pro.Streaming.Net.PackageTests            # asserts the packed .nupkg shape
./.github/scripts/run-android-device-tests.sh <version>        # needs a booted emulator
./.github/scripts/run-ios-device-tests.sh <version>            # boots its own simulator
```

The package tests read the packed `.nupkg` with `System.Reflection.Metadata` rather than loading the
assemblies, so they need no workloads and run anywhere — including the Linux runner CI validates on.
`LicenseComplianceTests` is the important one: no package may contain a Red5 binary.

The device tests are a plain .NET app per platform, not MAUI, so a failure points at the binding.

### Tiers

| Tier | Android | iOS | Needs |
| --- | --- | --- | --- |
| offline | ✅ | ✅ | nothing |
| offline, trimmed | ✅ | ✅ | nothing |
| licence validation | ✅ | ✅ | key + endpoint |
| live publish | ✅ | ✗ (simulator has no camera) | key + endpoint |

Trimming is the one configuration where a binding passes every desktop check and still fails at
runtime, because the linker strips types only Java reflection or the Objective-C runtime reaches.

### Two things about the Android runner

Both were found the hard way:

- **The offline and live tiers launch as separate processes.** The offline checks initialise
  `PeerConnectionFactory` and EGL state that the SDK then cannot set up cleanly for a real session,
  so the live check gets a process of its own via `-e skipOffline true`.
- **`am start -S`, not `am start`.** Without `-S` a second launch delivers an intent to the running
  activity, whose `onCreate` never runs again, and the script waits out its poll loop against the
  previous run's log — reported as a timeout with a stale, passing verdict in it.

## CI

| Workflow | Trigger | What it does |
| --- | --- | --- |
| [`build.yml`](../.github/workflows/build.yml) | called by the other two | Fetches natives, packs, validates, runs the device tiers |
| [`pr.yml`](../.github/workflows/pr.yml) | pull requests | Builds `<version>-beta.<pr>.<run>` and publishes it |
| [`release.yml`](../.github/workflows/release.yml) | `v*` tags | Publishes the tagged version and creates the GitHub release |

Forked pull requests get no secrets, so `pr.yml` passes `run-live-tiers: false` and the credentialed
matrix legs are skipped at job level — visibly skipped in the checks list, rather than passing having
done nothing.

Secrets never reach a command line: the Android runner passes the key as an intent extra, the iOS
runner through `SIMCTL_CHILD_*`, and `local-config.sh` prints only the last four characters.

### Repository configuration

**Settings → Secrets and variables → Actions → Secrets**

| Secret | Purpose |
| --- | --- |
| `RED5_CLOUD_LICENSE_KEY` | licence and live tiers |
| `RED5_CLOUD_ENDPOINT` | licence and live tiers |
| `RED5_PRO_LICENSE_KEY` | standalone tiers, when you have a server |
| `RED5_PRO_ENDPOINT` | standalone tiers |
| `NUGET_USER` | nuget.org account name, for trusted publishing |

**Environments**: create one named `nuget.org`. Then on nuget.org add **two** trusted-publishing
policies — one for `pr.yml`, one for `release.yml` — each recording environment `nuget.org`. Policies
are scoped to a single workflow file, so one will not cover the other, and a name mismatch fails the
OIDC exchange with HTTP 401 and `Environment mismatch for policy`.

## Known issues

- **Live publish fails on the Android emulator** with `The order of m-lines in answer doesn't match
  order in offer`. The SDP complaint is two steps downstream of the cause: the renderer fails to
  initialise (`IllegalStateException: Already initialized` from `EglRenderer.init`), so no local
  video track is created and the offer goes out audio-only. Eliminated so far: the emulator's
  `-noaudio` flag, two clients sharing WebRTC state, no renderer attached, offline-test pollution,
  and a missing `startPreview` before `publish`. The remaining suspect is the renderer being
  initialised twice — once when the builder is given it, once by `publish`.
- **iOS licence validation is rejected** where Android accepts the same key against the same
  endpoint, so it is platform-specific rather than an account problem.
- **The iOS package is large (~129 MB).** .NET for iOS emits both a `resources.zip` and an unzipped
  `resources/` tree per target framework, so the frameworks travel twice per TFM. Inefficient rather
  than broken, and under nuget.org's 250 MB limit.
- **Mac Catalyst is not supported.** Red5 ships no Catalyst slice for `Red5WebRTCKit`.
