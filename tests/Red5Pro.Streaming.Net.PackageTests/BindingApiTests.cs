namespace Red5Pro.Streaming.Net.PackageTests;

/// <summary>
/// Asserts that the binding assembly inside each package actually exposes the SDK's API.
/// A binding that fails to generate still compiles and packs cleanly — it just produces an
/// almost-empty assembly — so the package layout alone is not enough to prove the build worked.
/// </summary>
public class BindingApiTests
{
    /// <summary>Types the Android binding must expose for the SDK to be usable at all.</summary>
    private static readonly string[] AndroidCoreTypes =
    [
        "Red5Pro.Streaming.Api.IRed5WebrtcClient",
        "Red5Pro.Streaming.Api.Red5WebrtcClientBuilder",
        "Red5Pro.Streaming.Api.Red5WebrtcClientConfig",
        "Red5Pro.Streaming.Core.Red5WebrtcClient",
        // Red5Renderer is what a MAUI app actually puts on screen, and it derives from a vendored
        // org.webrtc type - so its absence would mean libwebrtc did not bind either.
        "Red5Pro.Streaming.Core.Red5Renderer",
        "Org.Webrtc.SurfaceViewRenderer",
        "Org.Webrtc.PeerConnection",
    ];

    /// <summary>Types the iOS binding must expose. All come from our @objc facade.</summary>
    private static readonly string[] IosCoreTypes =
    [
        "Red5Pro.Streaming.Net.iOS.R5Client",
        "Red5Pro.Streaming.Net.iOS.R5ClientDelegate",
        "Red5Pro.Streaming.Net.iOS.IR5ClientDelegate",
    ];

    private static AssemblyApi OpenBinding(string packageId, string assemblyName, string tfm)
    {
        using var package = Packages.OpenPackage(packageId);
        var assembly = Packages.ReadEntry(package, $"lib/{tfm}/{assemblyName}.dll");
        return new AssemblyApi(assembly);
    }

    [SkippableTheory]
    [MemberData(nameof(Packages.AndroidFrameworks), MemberType = typeof(Packages))]
    public void Android_binding_exposes_the_core_sdk_types(string tfm)
    {
        Skip.IfNot(Packages.Exists(Packages.Android), "the Android package was not built here");

        using var api = OpenBinding(Packages.Android, "Red5Pro.Streaming.Net.Android", tfm);

        var missing = AndroidCoreTypes.Except(api.PublicTypes).ToList();

        Assert.True(
            missing.Count == 0,
            $"{Packages.Android} ({tfm}) is missing bound types: {string.Join(", ", missing)}. " +
            $"The assembly exposes {api.PublicTypes.Count} public types in total.");
    }

    [SkippableTheory]
    [MemberData(nameof(Packages.AndroidFrameworks), MemberType = typeof(Packages))]
    public void Android_binding_is_not_an_empty_shell(string tfm)
    {
        Skip.IfNot(Packages.Exists(Packages.Android), "the Android package was not built here");

        using var api = OpenBinding(Packages.Android, "Red5Pro.Streaming.Net.Android", tfm);

        // Guards a real failure mode: an unpinned Android API level (bare net8.0-android resolving
        // to android21.0) makes the generator produce a valid but essentially empty assembly, which
        // still packs and installs fine. libwebrtc alone is ~350 types.
        Assert.True(
            api.PublicTypes.Count >= 200,
            $"{Packages.Android} ({tfm}) exposes only {api.PublicTypes.Count} public types; " +
            "the binding generator likely did not run over the whole .aar.");
    }

    [SkippableTheory]
    [MemberData(nameof(Packages.AndroidFrameworks), MemberType = typeof(Packages))]
    public void Android_binding_hides_the_proguarded_internals(string tfm)
    {
        Skip.IfNot(Packages.Exists(Packages.Android), "the Android package was not built here");

        using var api = OpenBinding(Packages.Android, "Red5Pro.Streaming.Net.Android", tfm);

        // The SDK ships obfuscated, and its single-letter classes are public in the bytecode.
        // Transforms/Metadata.xml removes them; if that stops matching they reappear as
        // meaningless public API - including one in the global namespace.
        var leaked = api.PublicTypes
            .Where(name => name.StartsWith("Red5Pro.Streaming.Core.", StringComparison.Ordinal))
            .Where(name => name.Split('.').Last().Length == 1)
            .ToList();

        Assert.True(
            leaked.Count == 0,
            $"obfuscated SDK internals leaked into the public API: {string.Join(", ", leaked)}");
    }

    [SkippableTheory]
    [MemberData(nameof(Packages.AndroidFrameworks), MemberType = typeof(Packages))]
    public void Android_data_channel_overload_keeps_its_renamed_member(string tfm)
    {
        Skip.IfNot(Packages.Exists(Packages.Android), "the Android package was not built here");

        using var api = OpenBinding(Packages.Android, "Red5Pro.Streaming.Net.Android", tfm);

        var methods = api.MethodsOf("Red5Pro.Streaming.Api.IRed5WebrtcClient+IDataChannelListener");

        // Metadata.xml renames the byte[] overload of onDataChannelMessage, because both overloads
        // otherwise generate the same handler field and the binding does not compile. If the
        // transform is dropped the build breaks loudly — but if it is *changed*, consumers silently
        // lose the member they implement, so it is pinned here.
        Assert.Contains("OnDataChannelMessage", methods);
        Assert.Contains("OnDataChannelBinaryMessage", methods);
    }

    [SkippableTheory]
    [MemberData(nameof(Packages.IosFrameworks), MemberType = typeof(Packages))]
    public void Ios_binding_exposes_the_facade_types(string tfm)
    {
        Skip.IfNot(Packages.Exists(Packages.IOS), "the iOS package is only built on macOS");

        using var api = OpenBinding(Packages.IOS, "Red5Pro.Streaming.Net.iOS", tfm);

        var missing = IosCoreTypes.Except(api.PublicTypes).ToList();

        Assert.True(
            missing.Count == 0,
            $"{Packages.IOS} ({tfm}) is missing bound types: {string.Join(", ", missing)}. " +
            $"The assembly exposes {api.PublicTypes.Count} public types in total.");
    }

    [SkippableTheory]
    [MemberData(nameof(Packages.IosFrameworks), MemberType = typeof(Packages))]
    public void Ios_client_exposes_the_session_entry_points(string tfm)
    {
        Skip.IfNot(Packages.Exists(Packages.IOS), "the iOS package is only built on macOS");

        using var api = OpenBinding(Packages.IOS, "Red5Pro.Streaming.Net.iOS", tfm);

        var methods = api.MethodsOf("Red5Pro.Streaming.Net.iOS.R5Client");

        // These exist only because native/ios/Facade is compiled into Red5ProFacade.xcframework.
        // Bound against Red5WebRTCKit alone, R5Client would not exist at all and the binding would
        // be an empty shell — this is the assertion that proves the facade survived the build.
        //
        // SetStreamManagerHost and SetServerIp are both listed on purpose: they are the two halves
        // of the Red5 Cloud / Red5 Pro split, and losing either silently breaks one deployment.
        foreach (var member in new[]
                 {
                     "SetStreamManagerHost", "SetServerIp", "SetLicenseKey", "Build",
                     "Publish", "Subscribe", "Stop", "SetVideoView", "IsLicenseValidated",
                 })
        {
            Assert.Contains(member, methods);
        }
    }

    [SkippableTheory]
    [MemberData(nameof(Packages.IosFrameworks), MemberType = typeof(Packages))]
    public void Ios_delegate_exposes_the_lifecycle_callbacks(string tfm)
    {
        Skip.IfNot(Packages.Exists(Packages.IOS), "the iOS package is only built on macOS");

        using var api = OpenBinding(Packages.IOS, "Red5Pro.Streaming.Net.iOS", tfm);

        var methods = api.MethodsOf("Red5Pro.Streaming.Net.iOS.R5ClientDelegate");

        foreach (var member in new[]
                 {
                     "OnLicenseValidated", "OnPublishStarted", "OnSubscribeStarted", "OnError",
                 })
        {
            Assert.Contains(member, methods);
        }
    }
}
