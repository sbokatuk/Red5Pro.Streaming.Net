# Red5Pro.Streaming.Net.Sample

A MAUI app that publishes and subscribes through **Red5 Cloud** or a **standalone Red5 Pro server**,
selected with a radio button.

## Running it

The sample consumes the packed packages from `../../artifacts`, so pack first:

```sh
./build/BuildNugets.sh 2.1.0.2-local.1                     # from the repository root

dotnet build samples/Red5Pro.Streaming.Net.Sample -f net9.0-android35.0 \
  -p:Red5PackageVersion=2.1.0.2-local.1 \
  -p:Red5ProAndroidSdk=/path/to/red5-android-sdk.aar

dotnet build samples/Red5Pro.Streaming.Net.Sample -f net9.0-ios18.0 \
  -p:Red5PackageVersion=2.1.0.2-local.1 \
  -p:Red5ProIosSdk=/path/to/Red5WebRTCKit.xcframework
```

Both `Red5Pro*Sdk` properties are required — the packages do not contain the Red5 SDK, and cannot.
See the repository README.

The host and licence key are prefilled from `Directory.Build.local.props` (or the `RED5_*`
environment variables), so the app can be started on a phone without typing a licence key on a
touch keyboard. Both fields remain editable. A real app would not embed them.

**Use the SDK licence key, not the server licence key.** A Red5 Pro account issues both on the same
page and only the SDK one activates the client.

## What is worth reading

[`MainPage.xaml.cs`](Red5Pro.Streaming.Net.Sample/MainPage.xaml.cs) is the whole app, and the
interesting thing about it is what is *absent*: there is no per-platform code at all — no
`#if ANDROID`, no platform folder, no callback adapter. `Red5Pro.Streaming.Net` presents the same
`IRed5Client` on both platforms and `Red5Pro.Streaming.Net.Maui` supplies the video view and the
Android `Activity`.

Written against the two bindings directly, the same app needs a `#if ANDROID` block to construct the
client, another to attach the renderer, and two separate callback adapters — an Android
`IRed5EventListener` with 19 members and an iOS `R5ClientDelegate`.

Three things the sample demonstrates that are easy to get wrong:

- **The deployment is chosen, not inferred.** A stream manager host and a standalone server address
  go to different SDK calls, and passing one to the other's setter fails as a connection timeout
  naming nothing.
- **A rejected licence surfaces as an exception**, not silence. Red5 validates the key before
  anything streams and then stops calling back if it was refused; `PublishAsync` fails with the
  server's message instead of hanging until the timeout.
- **The client is built lazily**, in `Client()` rather than the constructor. On Android the SDK
  needs the current activity and the video view needs its handler, and neither exists until the page
  is on screen.

## This sample earns its keep

It found two packaging bugs that every other check passed:

- The `.targets` files were packed under `build/`, which NuGet imports **only for direct
  references**. Reached transitively — as an app reaching the bindings through
  `Red5Pro.Streaming.Net.Maui` does — they silently never ran, so libwebrtc was missing from the
  javac classpath and `Red5ProAndroidSdk` was ignored. Now `buildTransitive/`.
- `PubnubClientListener` bound cleanly but generated an implementor that could not compile in a
  *consuming app*, because its `onStatusChanged(PNStatus)` refers to a PubNub type nothing
  references. The binding built, packed and passed its tests regardless.

Both failed only in an app that actually dexes the Java. That is the gap between a package test and
a real consumer.
