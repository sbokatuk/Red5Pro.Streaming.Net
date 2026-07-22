using Foundation;
using Red5Pro.Streaming.Net.iOS;
using UIKit;

namespace Red5Pro.Streaming.Net.IOS.DeviceTests;

/// <summary>
/// Checks that the packaged binding actually works on a simulator.
///
/// The offline ones stop short of contacting a server: they prove that Red5ProFacade.framework and
/// WebRTC.framework load, that the @objc facade is registered with the Objective-C runtime, and
/// that calls through the binding dispatch to Swift rather than merely compiling. Those are the
/// failures a desktop package test cannot see.
///
/// The licence check does contact a server, and runs only when a key is supplied. It is worth its
/// own tier because Red5 validates the key before anything streams and then simply stops calling
/// back if it was rejected - so an invalid key looks exactly like a network problem unless
/// something asks the question directly.
/// </summary>
public static class SmokeTests
{
    public delegate void Report(string message);

    public static IReadOnlyList<(string Name, Action<Report> Run)> Offline =>
    [
        ("frameworks load and the facade is registered", FacadeIsRegistered),
        ("configuration selectors dispatch", ConfigurationSelectorsDispatch),
        ("both deployment shapes are configurable", BothDeploymentsConfigure),
        ("delegate can be attached", DelegateCanBeAttached),
        ("session state is queryable", SessionStateIsQueryable),
    ];

    /// <summary>
    /// The single most valuable check. Instantiating R5Client forces dyld to load
    /// Red5ProFacade.framework, and the Objective-C runtime to resolve the facade's class. If the
    /// frameworks did not travel in the package, or the facade was not compiled into the
    /// xcframework, this is where it shows.
    /// </summary>
    private static void FacadeIsRegistered(Report report)
    {
        using var client = new R5Client();

        report($"R5Client is {client.Handle}");
        report($"Red5WebRTCKit reports version {R5Client.SdkVersion()}");
    }

    /// <summary>
    /// Calls that cross into Swift and return nothing. They would throw "unrecognized selector
    /// sent to instance" if the binding's [Export] selectors did not match what the facade emits —
    /// the most likely way this binding breaks silently.
    ///
    /// SetVideoSize is deliberately included: Swift's argument labels make its selector
    /// setVideoSizeWithWidth:height:, and the plausible-looking setVideoSize:height: would compile
    /// and fail only here.
    /// </summary>
    private static void ConfigurationSelectorsDispatch(Report report)
    {
        using var client = new R5Client();

        client.SetAppName("live");
        client.SetPort(443);
        client.SetVideoEnabled(true);
        client.SetAudioEnabled(true);
        client.SetVideoSize(1280, 720);
        client.SetVideoFps(30);
        client.SetVideoBitrate(1500);
        client.SetVideoSource(R5VideoSource.FrontCamera);
        client.SetScalingType(R5ScalingType.AspectFill);

        report("nine configuration selectors dispatched");
    }

    /// <summary>
    /// Red5 Cloud and a standalone Red5 Pro server are reached through different setters, and this
    /// package's whole cross-platform story depends on both existing. Losing either would leave one
    /// deployment silently unreachable.
    /// </summary>
    private static void BothDeploymentsConfigure(Report report)
    {
        using var cloud = new R5Client();
        cloud.SetStreamManagerHost("example.invalid.cloud.red5.net");
        cloud.SetNodeGroup("default");

        using var standalone = new R5Client();
        standalone.SetServerIp("192.0.2.10");

        report("stream-manager and standalone paths both dispatched");
    }

    /// <summary>
    /// The delegate is a weak reference bridged back into Swift, which is the fiddliest part of the
    /// facade. Setting and reading it proves the bridge is wired up.
    /// </summary>
    private static void DelegateCanBeAttached(Report report)
    {
        using var client = new R5Client();
        var recorder = new RecordingDelegate();

        client.Delegate = recorder;

        if (client.Delegate is null)
        {
            throw new InvalidOperationException("the delegate did not survive assignment.");
        }

        report($"delegate attached as {client.Delegate.GetType().Name}");

        // Held weakly on the Swift side, so the local must outlive the check.
        GC.KeepAlive(recorder);
    }

    /// <summary>Round-trips values out of Swift, exercising the return path of the bridge.</summary>
    private static void SessionStateIsQueryable(Report report)
    {
        using var client = new R5Client();

        if (client.IsPublishing() || client.IsSubscribing())
        {
            throw new InvalidOperationException("a client that never started reports streaming.");
        }

        if (client.IsLicenseValidated())
        {
            throw new InvalidOperationException("a client that never built reports a valid licence.");
        }

        if (client.IsBuilt())
        {
            throw new InvalidOperationException("a client that never built reports built.");
        }

        report("IsPublishing/IsSubscribing/IsLicenseValidated/IsBuilt all false before build");
    }

    /// <summary>
    /// Validates a real licence key against a real endpoint. Skipped when none is supplied, and
    /// reported as skipped either way so a run without credentials cannot be mistaken for one that
    /// proved the licence works.
    /// </summary>
    public static Task<string> ValidateLicenseAsync(string licenseKey, string host, bool cloud)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Kept alive for the duration: the facade holds its delegate weakly.
        var listener = new LicenseDelegate(completion);
        var client = new R5Client { Delegate = listener };

        if (cloud)
        {
            client.SetStreamManagerHost(host);
            client.SetNodeGroup("default");
            client.SetPort(443);
        }
        else
        {
            client.SetServerIp(host);
            client.SetPort(5080);
        }

        client.SetAppName("live");
        client.SetStreamName("licence-check");
        client.SetLicenseKey(licenseKey);
        client.SetVideoEnabled(false);
        client.SetAudioEnabled(false);

        // Build is what triggers validation; no camera or peer connection is needed for it.
        client.Build();

        return completion.Task.ContinueWith(task =>
        {
            GC.KeepAlive(listener);
            client.Stop();
            client.Dispose();
            return task.GetAwaiter().GetResult();
        });
    }

    private sealed class LicenseDelegate(TaskCompletionSource<string> completion) : R5ClientDelegate
    {
        public override void OnLicenseValidated(bool validated, string message)
        {
            if (validated)
            {
                completion.TrySetResult($"licence accepted: {message}");
            }
            else
            {
                completion.TrySetException(
                    new InvalidOperationException($"licence rejected: {message}"));
            }
        }

        public override void OnError(string error) =>
            completion.TrySetException(new InvalidOperationException($"SDK error: {error}"));
    }

    /// <summary>Minimal delegate implementation; the offline checks never trigger a callback.</summary>
    private sealed class RecordingDelegate : R5ClientDelegate
    {
        public override void OnError(string error) =>
            Console.WriteLine($"    delegate received error: {error}");
    }
}
