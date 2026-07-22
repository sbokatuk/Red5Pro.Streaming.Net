# Red5Pro.Streaming.Net

.NET bindings for the [Red5][red5] WebRTC SDKs, with one API across Android and iOS. Publish and
subscribe to low-latency WebRTC streams from C#, in .NET MAUI or plain .NET for Android / iOS,
against either **Red5 Cloud** or a **standalone Red5 Pro server**.

> **Status: complete but for one bug.** All four packages build, pack and pass their tests; both
> bindings have been run on a real emulator and simulator; the sample builds for both platforms; CI
> is in place. Publishing a live stream still fails — see [Status](#status). Nothing is on
> nuget.org yet.

```sh
dotnet add package Red5Pro.Streaming.Net.Maui    # MAUI apps: adds the video view
dotnet add package Red5Pro.Streaming.Net         # everything else
```

```csharp
var client = new Red5Options
{
    Deployment = Red5Deployment.Cloud,
    Host       = "your-id.cloud.red5.net",
    LicenseKey = "XXXX-XXXX-XXXX-XXXX",
}.CreateClient();

client.SetVideoView(LocalView);                  // a <red5:Red5VideoView /> from your XAML
client.PublishStarted += (_, e) => Console.WriteLine($"live: {e.StreamName}");

await client.PublishAsync("my-stream");          // completes when the server confirms
```

## You must supply the Red5 SDK yourself

**These packages do not contain the Red5 SDK.** They cannot: the Infrared5 EULA defines "Software"
to include SDKs (§1.11) and then forbids repackaging (§3.6) and bundling or distributing them
"in any manner whatsoever" (§3.7). §4.3.2 limits an SDK licence to one application and §4.3.3
requires a separate agreement for OEM use.

So the packages ship the *binding* — the generated C# and our Objective-C facade — and you point an
MSBuild property at your own licensed copy:

```xml
<PropertyGroup>
  <!-- Android: the .aar -->
  <Red5ProAndroidSdk>$(MSBuildProjectDirectory)/libs/red5-android-sdk.aar</Red5ProAndroidSdk>

  <!-- iOS: the unpacked .xcframework -->
  <Red5ProIosSdk>$(MSBuildProjectDirectory)/native/Red5WebRTCKit.xcframework</Red5ProIosSdk>
</PropertyGroup>
```

Forget it and the build fails with `RED5001` / `RED5101` and download instructions rather than an
undefined-symbol error.

Download from **Red5 Cloud** at <https://red5-cloud-sdk.cachefly.net/> (no account needed for the
download itself) or **Red5 Pro** at <https://account.red5.net/downloads>. The two builds differ only
in tagging — verified byte-different but with an identical 875-line public Java API — so either
works with either deployment.

> ⚠️ **iOS: take the CDN build, not the GitHub release.** Red5 published two incompatible packagings
> of 2.1.0.2. The GitHub release asset (`b11`) is a **static** library; the CDN and account
> downloads serve `b12`, a **dynamic** framework, which is what this binding expects and what
> `Package.swift` and the podspec describe. `Package.swift`'s own URL names `b12` under the releases
> host, where it 404s.

libwebrtc **is** bundled — it is BSD-licensed, and the Red5 `.aar` ships no `jniLibs` at all, so
without it every call dies with `UnsatisfiedLinkError`.

## Packages

| Package | What it is | Target frameworks |
| --- | --- | --- |
| `Red5Pro.Streaming.Net.Maui` | MAUI video view, handlers, and the Android `Activity` plumbing | net8.0, net9.0, net10.0 (android + ios) |
| `Red5Pro.Streaming.Net` | The cross-platform client: `IRed5Client`, options, events, async | net8.0, net9.0, net10.0 (android + ios) |
| `Red5Pro.Streaming.Net.Android` | The raw binding, plus libwebrtc | `net8.0-android34.0`, `net9.0-android35.0`, `net10.0-android36.0` |
| `Red5Pro.Streaming.Net.iOS` | The raw binding over our `@objc` facade, plus libwebrtc | `net8.0-ios18.0`, `net9.0-ios18.0`, `net10.0-ios26.0` |

Each pulls in the one below it, so a single reference is enough. Drop to a platform binding when you
need something the cross-platform API does not expose — the full SDK surface is there under
`Red5Pro.Streaming.*` (Android) and `Red5Pro.Streaming.Net.iOS.*` (iOS).

Minimum platform versions are **Android 26** and **iOS 16.0**, both read from the shipped binaries
rather than from Red5's documentation, which contradicts itself (`Package.swift` and the podspec say
iOS 15.0, the SDK README says 13.0, the docs site says 16.0; the framework is compiled
`-target arm64-apple-ios16.0`).

## Red5 Cloud and Red5 Pro

The two products are reached differently, and this is the only place your code has to care:

```csharp
// Red5 Cloud — a stream manager resolves an origin/edge node for you
new Red5Options
{
    Deployment = Red5Deployment.Cloud,
    Host       = "your-id.cloud.red5.net",   // port defaults to 443
    NodeGroup  = "default",
    LicenseKey = cloudSdkKey,
}

// A standalone Red5 Pro server you run
new Red5Options
{
    Deployment = Red5Deployment.Standalone,
    Host       = "192.0.2.10",               // port defaults to 5080
    LicenseKey = proSdkKey,
}
```

Getting this wrong fails as a connection timeout that names nothing, which is why it is an explicit
enum rather than inferred from the host name.

**Use the SDK licence key, not the server licence key.** A Red5 Pro account issues both on the same
page and they are not interchangeable. Red5 Cloud issues its own, separately, under
Dev Resources → Client SDK License.

## Why there is a cross-platform layer

The two SDKs look nothing like each other. Android has a fluent Java builder and a 19-method
listener interface; iOS is **effectively Swift-only**. `Red5WebRTCKit.framework` ships an empty
`Headers/` directory — no generated `-Swift.h` — and exports exactly one Objective-C class:

```
_OBJC_CLASS_$__TtC13Red5WebRTCKit13CameraManager
```

That is a camera-permissions helper, not the streaming API, and it is exported under its *mangled*
name, so nothing can link it as `CameraManager` anyway. `Red5WebrtcClientBuilder`,
`Red5WebrtcClient`, `Red5ProWebrtcEventDelegate` and every enum are invisible across the ABI —
Objective Sharpie sees nothing worth binding.

`Red5Pro.Streaming.Net` is the adapter over both, so you do not write it. It turns two
callback-driven SDKs into awaitable `PublishAsync`/`SubscribeAsync` and ordinary .NET events, and
handles the one thing that otherwise wastes an afternoon: Red5 validates your licence key before
anything streams and then **stops calling back entirely** if it was rejected, so a bad key looks
exactly like a network hang. `PublishAsync` fails immediately with the server's message instead.

On iOS, [`native/ios/Facade`](native/ios/Facade) re-exposes the Swift API as `@objc` types in a
framework this repository builds and owns. Unlike a facade compiled into the vendor's framework,
it *links against* the Red5 SDK rather than containing it — which is both what the EULA requires and
what lets the package ship while the SDK does not.

## Building

See [docs/BUILD.md](docs/BUILD.md) for the full picture — layout, the two-pass pack, the packaging
traps, the CI secrets table and the known issues. In short:

```sh
./native/android/fetch-android.sh     # Red5 .aar + libwebrtc, checksum-pinned
./native/ios/fetch-ios.sh             # Red5 xcframework + libwebrtc, then builds our facade
./build/BuildNugets.sh                # packs everything into ./artifacts
```

Each project is packed **twice** and the results merged. No single .NET SDK can build net8, net9 and
net10 for a platform — each SDK's workload carries only the current band and the previous one — so
`BuildNugets.sh` runs a pass per band and `merge-packages.py` splices the missing `lib/<tfm>` trees
and nuspec dependency groups together.

All native pins live in [`Directory.Build.props`](Directory.Build.props); `build/pins.sh` is the
only shell parser of it.

### Licence keys for the tests and sample

Copy `Directory.Build.local.props.example` to `Directory.Build.local.props` and fill it in. That
file is git-ignored. Environment variables take precedence, which is how CI supplies the same values
from repository secrets:

| Property | Environment variable |
| --- | --- |
| `Red5ProLicenseKey` | `RED5_PRO_LICENSE_KEY` |
| `Red5ProEndpoint` | `RED5_PRO_ENDPOINT` |
| `Red5CloudLicenseKey` | `RED5_CLOUD_LICENSE_KEY` |
| `Red5CloudEndpoint` | `RED5_CLOUD_ENDPOINT` |

Empty means *skip that tier*, never fail — a forked pull request has no secrets and must still run
the offline checks.

## Testing

```sh
dotnet test tests/Red5Pro.Streaming.Net.PackageTests     # asserts the packed .nupkg shape
```

These run against the packed `.nupkg` rather than the build output, using
`System.Reflection.Metadata` so they need no workloads and run anywhere. They exist because three
separate packaging bugs got through everything else — most seriously, `Pack="false"` on an
`AndroidLibrary` item is **silently ignored**, and the default behaviour put the licensed Red5 SDK
into every `lib/<tfm>/`. `LicenseComplianceTests` now asserts that cannot happen.

The device tests are a plain .NET app per platform, not MAUI, so a failure points at the binding:

```sh
# iOS — builds, installs on a simulator, reads the verdict from stdout
dotnet build tests/Red5Pro.Streaming.Net.iOS.DeviceTests -f net9.0-ios18.0 \
  -p:Red5PackageVersion=<version> -p:Red5ProIosSdk=<path> -p:RuntimeIdentifier=iossimulator-arm64
```

## Status

Verified on real hardware, not merely written:

| | |
| --- | --- |
| ✅ | Four packages build, pack and merge across all six target frameworks |
| ✅ | 53 package tests pass, including the licence-compliance assertions |
| ✅ | Android binding generates a real API with the proguarded internals hidden |
| ✅ | Android device tests run on an emulator — 5/5 offline, and libwebrtc, gson and okhttp all reach the dex |
| ✅ | iOS binding links and runs on a simulator — 5/5 offline, and the SDK reports its own version back through .NET → binding → facade → Swift |
| ✅ | MAUI sample builds for Android and iOS against the packed packages |
| ✅ | CI packs, validates and runs the device tiers; forks skip the credentialed legs |
| ✅ | **Red5 Cloud licence accepted on Android** |
| ⚠️ | iOS licence rejected with the same key — platform-specific |
| ⚠️ | Live publish fails on the emulator — see below |

### The open problem: live publish

Publishing fails on the Android emulator with

```
Failed to set remote answer sdp: The order of m-lines in answer doesn't match order in offer
```

That complaint is **two steps downstream of the cause**. Full logcat shows the renderer failing to
initialise (`IllegalStateException: Already initialized` from `EglRenderer.init`), so no local video
track is created, the offer goes out audio-only, and the server's audio+video answer cannot match it.

Five hypotheses have been tested and eliminated: the emulator's `-noaudio` flag, two clients sharing
process-wide WebRTC state, no renderer attached, the offline checks polluting EGL state, and a
missing `startPreview` before `publish`. The remaining suspect is the renderer being initialised
twice — once when the builder is given it, once by `publish`.

**Licence validation is separately odd.** The same Red5 Cloud SDK key that Android accepts
(`License validation successful`) is rejected on iOS. Since one platform accepts it, the account and
key are fine. Worth knowing while reading Red5's iOS documentation: its Quick Start and Full Working
Example both omit the mandatory `setLicenseKey`, and a client configured exactly as documented
reports `No license key provided`.

## Known issues

- **The iOS package is large (~129 MB).** .NET for iOS emits both a `resources.zip` and an unzipped
  `resources/` tree per target framework, so the frameworks travel twice per TFM. Inefficient, not
  broken, and under nuget.org's 250 MB limit.
- **Mac Catalyst is not supported.** Red5 ships no Catalyst slice for `Red5WebRTCKit`, so a Catalyst
  build could never link. `fetch-ios.sh` strips the Catalyst and macOS slices from libwebrtc for the
  same reason.
- **Red5's own materials disagree** about which SDK to use. The Cloud dashboard still links to
  `streaming-ios` / `streaming-android` — the deprecated `R5Streaming` line — while the docs site and
  the SDK repository document `Red5WebRTCKit` 2.x. This repository binds 2.x.
- **`red5.net` blocks scripted clients** (HTTP 403), so nothing in CI can fetch from it. The SDK
  downloads use `cachefly.net`, which does not.

## Licence

The binding, facade and client code in this repository are MIT. The Red5 SDKs are **not**
redistributed here and remain subject to the [Infrared5 EULA][eula]. libwebrtc, bundled from
[stasel/WebRTC][webrtc] and `io.github.webrtc-sdk`, is under its own BSD licence.

[red5]: https://www.red5.net/
[eula]: https://account.red5.net/assets/LICENSE.txt
[webrtc]: https://github.com/stasel/WebRTC
