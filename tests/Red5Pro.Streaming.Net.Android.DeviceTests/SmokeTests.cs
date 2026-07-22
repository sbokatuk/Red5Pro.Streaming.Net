using Android.Content;
using Org.Webrtc;
using Red5Pro.Streaming.Api;
using Red5Pro.Streaming.Core;

namespace Red5Pro.Streaming.Net.Android.DeviceTests;

/// <summary>
/// Offline checks that the packaged binding actually works on a device.
///
/// They deliberately stop short of connecting to a server. What is proven here is everything that
/// can break between "the package restored" and "the SDK is usable" — that the native libraries
/// load, that the bound types resolve at runtime, and that calls into them dispatch across JNI.
/// Those are the failures a desktop package test cannot see.
/// </summary>
public static class SmokeTests
{
    public delegate void Report(string message);

    public static IReadOnlyList<(string Name, Action<Context, Report> Run)> All =>
    [
        ("native library loads", NativeLibraryLoads),
        ("peer connection factory is usable", PeerConnectionFactoryIsUsable),
        ("camera enumeration works", CameraEnumerationWorks),
        ("red5 types resolve", Red5TypesResolve),
        ("transitive java dependencies are present", TransitiveDependenciesArePresent),
    ];

    /// <summary>
    /// The single most valuable check. PeerConnectionFactory.Initialize is what dlopens
    /// libjingle_peerconnection.so, and that library comes from libwebrtc rather than from Red5 —
    /// the Red5 .aar carries no jniLibs at all. If libwebrtc was not packaged, or its .targets did
    /// not run in the consuming project, this throws UnsatisfiedLinkError and nothing else matters.
    /// </summary>
    private static void NativeLibraryLoads(Context context, Report report)
    {
        var options = PeerConnectionFactory.InitializationOptions
            .InvokeBuilder(context)
            .CreateInitializationOptions();

        PeerConnectionFactory.Initialize(options);

        report("libjingle_peerconnection loaded");
    }

    /// <summary>
    /// Creating a factory crosses JNI in both directions and allocates native objects, so it proves
    /// the binding dispatches rather than merely linking.
    /// </summary>
    private static void PeerConnectionFactoryIsUsable(Context context, Report report)
    {
        var factory = PeerConnectionFactory.InvokeBuilder().CreatePeerConnectionFactory();

        if (factory is null)
        {
            throw new InvalidOperationException("CreatePeerConnectionFactory returned null.");
        }

        try
        {
            report($"created {factory.Class.SimpleName}");
        }
        finally
        {
            factory.Dispose();
        }
    }

    /// <summary>
    /// Exercises a bound API that returns a Java array. An emulator image always has at least one
    /// camera, but the assertion is deliberately only that the call round-trips: the value matters
    /// less than the marshalling working.
    /// </summary>
    private static void CameraEnumerationWorks(Context context, Report report)
    {
        var enumerator = new Camera2Enumerator(context);
        var devices = enumerator.GetDeviceNames();

        report($"enumerated {devices?.Length ?? 0} camera(s)");
    }

    /// <summary>
    /// org.webrtc above comes from libwebrtc, which we ship, so it could work while the Red5
    /// classes were entirely absent — which is exactly what happens if a consumer forgets
    /// Red5ProAndroidSdk. Touching these types resolves them from the dex at runtime.
    /// </summary>
    private static void Red5TypesResolve(Context context, Report report)
    {
        // Class is resolved lazily by the runtime, so reading it is what forces the load.
        var builder = new Red5WebrtcClientBuilder();
        report($"{builder.Class.Name} resolved");

        // Red5Renderer is checked by name rather than instantiated, deliberately.
        //
        // Constructing one initialises shared EGL state for the process, and the SDK later fails
        // to set up its *own* renderer with
        //
        //     Surface renderer not initialized, initializing now...
        //     java.lang.IllegalStateException: Already initialized
        //         at org.webrtc.EglRenderer.init(EglRenderer.java:203)
        //
        // after which no local video track is created, the offer goes out audio-only, and the live
        // publish dies with an m-line mismatch that names none of this. An offline check must not
        // change the state the later checks run against.
        var renderer = Java.Lang.Class.ForName("net.red5.android.core.Red5Renderer");

        if (renderer is null)
        {
            throw new InvalidOperationException("net.red5.android.core.Red5Renderer is missing.");
        }

        report($"{renderer.Name} resolved");
    }

    /// <summary>
    /// gson and okhttp are declared by Red5's integration instructions as `implementation`
    /// dependencies, which means they are *not* inside the .aar. Without them a consumer restores,
    /// builds and installs perfectly happily, then dies on the first publish with
    ///
    ///     Java.Lang.NoClassDefFoundError: Failed resolution of: Lcom/google/gson/GsonBuilder;
    ///
    /// The binding brings them as PackageReferences; this asserts they actually reached the dex.
    /// Checked by name rather than by using the bound types, so the check stays honest even if the
    /// binding stops referencing them directly.
    /// </summary>
    private static void TransitiveDependenciesArePresent(Context context, Report report)
    {
        foreach (var className in new[]
                 {
                     "com.google.gson.GsonBuilder",
                     "okhttp3.OkHttpClient",
                 })
        {
            var found = Java.Lang.Class.ForName(className);

            if (found is null)
            {
                throw new InvalidOperationException($"{className} is missing from the dex.");
            }

            report($"{className} resolved");
        }
    }
}
