using Android.App;

namespace Red5Pro.Streaming.Net.Android.DeviceTests;

/// <summary>
/// Validates a licence against a real Red5 endpoint, and — when the licence is accepted — publishes
/// a stream to it.
///
/// Everything else in this app is offline: it proves the native libraries load and the bound API is
/// callable, which is all CI can do without credentials. These are the checks that prove a stream
/// actually reaches somewhere: licence validation, websocket signalling, SDP exchange, ICE, and the
/// server accepting the broadcast.
///
/// Both run through <see cref="IRed5Client" /> rather than the binding directly, so they cover the
/// stack a consumer really uses, including PublishAsync's callback-to-Task bridge.
/// </summary>
public static class LiveStreamTest
{
    /// <summary>
    /// Validates the licence and then publishes, on a <b>single</b> client.
    ///
    /// Deliberately not two clients. An earlier revision validated on one and published on another,
    /// and the publish then failed with
    ///
    ///     Failed to set remote answer sdp: The order of m-lines in answer doesn't match order in
    ///     offer. Rejecting answer.
    ///
    /// Two Red5 clients in one process do not get independent WebRTC state, so the second one
    /// negotiates against the first one's. A real app has one client per session anyway, so this
    /// shape is both correct and representative.
    /// </summary>
    public static async Task<(string License, string Publish)> ValidateAndPublishAsync(
        Activity activity, Red5Options options)
    {
        var verdict = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // A fresh name per run: republishing a live stream name is rejected by the server, which
        // would make a re-run fail for a reason that has nothing to do with the code.
        var streamName = $"e2e{DateTime.UtcNow:HHmmss}";

        using var client = new Red5Client(options, activity);

        client.LicenseValidated += (_, e) =>
        {
            if (e.IsValid)
            {
                verdict.TrySetResult($"licence accepted: {e.Message}");
            }
            else
            {
                verdict.TrySetException(new InvalidOperationException($"licence rejected: {e.Message}"));
            }
        };

        // PublishAsync builds the client, which triggers validation, and defers the publish itself
        // until the licence is accepted - so this one call covers both, and a rejected licence
        // surfaces as an exception rather than as a timeout.
        var publishing = client.PublishAsync(streamName);

        var licensed = await Task.WhenAny(verdict.Task, Task.Delay(TimeSpan.FromSeconds(45)));

        if (licensed != verdict.Task)
        {
            throw new TimeoutException(
                $"no licence verdict from {options.Host} within 45s. The SDK reports nothing at " +
                "all when it cannot reach the server, so this is as likely to be connectivity as " +
                "a bad key.");
        }

        var license = await verdict.Task;

        await publishing;

        if (!client.IsStreaming)
        {
            throw new InvalidOperationException(
                "PublishAsync returned but the client does not consider itself streaming.");
        }

        client.Stop();
        return (license, $"published '{streamName}' to {options.Host}");
    }

    /// <summary>
    /// Builds the options for whichever deployment the runner asked for. The two are configured
    /// through different SDK calls, and this is where that choice is made in the tests.
    /// </summary>
    public static Red5Options OptionsFor(string host, string licenseKey, bool cloud) => new()
    {
        Deployment = cloud ? Red5Deployment.Cloud : Red5Deployment.Standalone,
        Host = host,
        LicenseKey = licenseKey,
        // Small and slow on purpose: the emulator's software encoder is not fast, and the check is
        // that the server accepted a broadcast rather than that it looked good.
        VideoWidth = 320,
        VideoHeight = 240,
        VideoFps = 15,
        VideoBitrateKbps = 300,
        Timeout = TimeSpan.FromSeconds(45),
    };
}
