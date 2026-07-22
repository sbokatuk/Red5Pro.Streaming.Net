namespace Red5Pro.Streaming.Net;

/// <summary>
/// Publishes to and subscribes from Red5 Pro or Red5 Cloud, with the same surface on Android and
/// iOS.
///
/// The platform bindings underneath do not resemble each other — Android has a fluent Java builder
/// and a listener interface, iOS has an <c>@objc</c> facade and a delegate — and this is the layer
/// that hides the difference. Reach for <c>Red5Pro.Streaming.*</c> (Android) or
/// <c>Red5Pro.Streaming.Net.iOS.*</c> (iOS) directly when you need something not exposed here.
/// </summary>
public interface IRed5Client : IDisposable
{
    /// <summary>
    /// The outcome of the licence check. Red5 validates the SDK key before anything streams, so
    /// this is normally the first event raised — and when it reports false, nothing else will
    /// work no matter what you call.
    /// </summary>
    event EventHandler<Red5LicenseEventArgs>? LicenseValidated;

    /// <summary>The server accepted the publish and is receiving media.</summary>
    event EventHandler<Red5StreamEventArgs>? PublishStarted;

    /// <summary>Publishing ended, whether by <see cref="Stop" /> or by the server.</summary>
    event EventHandler<Red5StreamEventArgs>? PublishStopped;

    /// <summary>The server started sending the stream being subscribed to.</summary>
    event EventHandler<Red5StreamEventArgs>? SubscribeStarted;

    /// <summary>Subscription ended, whether by <see cref="Stop" /> or by the server.</summary>
    event EventHandler<Red5StreamEventArgs>? SubscribeStopped;

    /// <summary>The local camera is running, in response to <see cref="StartPreview" />.</summary>
    event EventHandler? PreviewStarted;

    /// <summary>The peer connection changed state.</summary>
    event EventHandler<Red5ConnectionStateEventArgs>? ConnectionStateChanged;

    /// <summary>
    /// The SDK reported a problem. When it happens while starting, the pending
    /// <see cref="PublishAsync" /> or <see cref="SubscribeAsync" /> fails with the same message.
    /// </summary>
    event EventHandler<Red5ErrorEventArgs>? Error;

    /// <summary>True between a successful publish or subscribe and <see cref="Stop" />.</summary>
    bool IsStreaming { get; }

    /// <summary>The stream of the running session, or null when idle.</summary>
    string? StreamName { get; }

    /// <summary>
    /// True once the server has confirmed <see cref="Red5Options.LicenseKey" />.
    /// </summary>
    bool IsLicenseValidated { get; }

    /// <summary>
    /// Starts the camera and shows it in the local view, without contacting the server. Optional —
    /// <see cref="PublishAsync" /> starts capture itself — but calling it first makes the preview
    /// appear immediately rather than after the round trip.
    /// </summary>
    void StartPreview();

    /// <summary>Stops the local camera preview.</summary>
    void StopPreview();

    /// <summary>
    /// Publishes this device's camera and microphone, completing when the server confirms.
    /// </summary>
    /// <param name="streamName">The name to publish under.</param>
    /// <param name="cancellationToken">
    /// Abandons the wait. The session is stopped, so a cancelled call leaves nothing running.
    /// </param>
    /// <exception cref="Red5Exception">
    /// The server reported an error, rejected the licence, or did not confirm within
    /// <see cref="Red5Options.Timeout" />.
    /// </exception>
    Task PublishAsync(string streamName, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to a stream, completing when the server starts sending it.</summary>
    /// <inheritdoc cref="PublishAsync" />
    Task SubscribeAsync(string streamName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the session and releases the camera, microphone and peer connection. Safe to call
    /// when nothing is running.
    /// </summary>
    void Stop();

    /// <summary>Switches between the front and rear cameras mid-session.</summary>
    void SwitchCamera();

    /// <summary>
    /// Mutes or unmutes the outgoing audio track. This does not release the microphone — the OS
    /// will still show the app as recording.
    /// </summary>
    void SetAudioEnabled(bool enabled);

    /// <summary>Pauses or resumes the outgoing video track.</summary>
    void SetVideoEnabled(bool enabled);

    /// <summary>Changes the target bitrate of a running session, in kbps.</summary>
    void SetVideoBitrate(int kbps);
}
