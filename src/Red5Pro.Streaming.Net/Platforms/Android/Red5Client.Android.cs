using Android.App;
using Red5Pro.Streaming.Api;
using Red5Pro.Streaming.Core;

// Both namespaces declare a Red5WebrtcClient - Api holds an interface-adjacent type and Core the
// concrete one the builder returns - so the concrete one is aliased rather than disambiguated at
// each of its half-dozen uses.
using NativeClient = Red5Pro.Streaming.Core.Red5WebrtcClient;

namespace Red5Pro.Streaming.Net;

/// <summary>
/// Android half, built on Red5WebrtcClientBuilder and IRed5WebrtcClient.IRed5EventListener.
/// </summary>
public sealed partial class Red5Client
{
    private readonly Activity _activity;
    private readonly EventBridge _listener;

    private Red5Renderer? _renderer;
    private NativeClient? _client;

    /// <summary>Creates a client.</summary>
    /// <param name="options">Connection settings. See <see cref="Red5Options" />.</param>
    /// <param name="activity">
    /// The activity the session belongs to. The SDK needs one for the camera and the renderer, so
    /// it cannot be derived from the application context. In a MAUI app,
    /// <c>Red5Pro.Streaming.Net.Maui</c> supplies it for you.
    /// </param>
    public Red5Client(Red5Options options, Activity activity)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _listener = new EventBridge(this);
    }

    /// <summary>
    /// Renders video into <paramref name="renderer" />. Call before publishing or subscribing; the
    /// renderer is handed to the SDK when the session is built.
    /// </summary>
    public void SetRenderer(Red5Renderer? renderer) => _renderer = renderer;

    private void PublishCore(string streamName)
    {
        var client = Build(streamName);

        // Publish without an explicit StartPreview. Red5's documentation shows
        // build -> onLicenseValidated -> startPreview -> publish, but publish() initialises the
        // renderer itself, so calling startPreview first leaves it half-owned:
        //
        //     Surface renderer not initialized, initializing now...
        //     java.lang.IllegalStateException: Already initialized
        //     Cannot create video track - factory or track state invalid
        //
        // after which the offer carries no video m-line and the server's answer is rejected.
        // StartPreview stays available as its own API for showing the camera before a session.
        RunWhenLicensed(() => client.Publish(streamName));
    }

    private void SubscribeCore(string streamName)
    {
        var client = Build(streamName);
        RunWhenLicensed(() => client.Subscribe(streamName));
    }

    /// <inheritdoc />
    public void StartPreview()
    {
        // Preview before a session exists is the common case - it is how an app shows the camera
        // while the user types a stream name - so the client is built on demand. The stream name is
        // empty rather than a placeholder: the SDK overwrites it when publishing actually starts,
        // and passing something meaningless here showed up in server logs.
        var client = _client ?? Build(string.Empty);
        RunWhenLicensed(client.StartPreview);
    }

    /// <inheritdoc />
    public void StopPreview() => _client?.StopPreview();

    private NativeClient Build(string streamName)
    {
        StopCore();

        var builder = new Red5WebrtcClientBuilder()
            .SetActivity(_activity)
            .SetPort(_options.ResolvedPort)
            .SetAppName(_options.AppName)
            .SetStreamName(streamName)
            .SetLicenseKey(_options.LicenseKey)
            .SetVideoEnabled(_options.VideoEnabled)
            .SetAudioEnabled(_options.AudioEnabled)
            .SetVideoWidth(_options.VideoWidth)
            .SetVideoHeight(_options.VideoHeight)
            .SetVideoFps(_options.VideoFps)
            .SetVideoBitrate(_options.VideoBitrateKbps)
            .SetEventListener(_listener);

        // The one place the two deployment shapes actually differ. A stream manager host and a
        // standalone server address go to different setters, and passing one to the other's setter
        // fails as a connection timeout rather than as anything that names the cause.
        builder = _options.Deployment == Red5Deployment.Cloud
            ? builder.SetStreamManagerHost(_options.Host).SetNodeGroup(_options.NodeGroup)
            : builder.SetServerIp(_options.Host);

        if (!string.IsNullOrEmpty(_options.Token))
        {
            builder = builder.SetToken(_options.Token);
        }

        if (_renderer is not null)
        {
            builder = builder.SetVideoRenderer(_renderer);
        }

        _client = builder.Build();
        return _client;
    }

    private void StopCore()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            _client.Stop();
            _client.Release();
        }
        catch (Exception)
        {
            // Stopping a session that never fully started throws from the native layer. There is
            // nothing a caller could do about it, and it would mask the real failure.
        }

        _client = null;
    }

    private void DisposeCore()
    {
        // The renderer holds an EGL context that managed collection alone does not reclaim.
        _renderer?.Release();
        _renderer = null;
    }

    /// <inheritdoc />
    public void SwitchCamera() => _client?.SwitchCamera();

    /// <inheritdoc />
    public void SetAudioEnabled(bool enabled) => _client?.ToggleSendAudio(enabled);

    /// <inheritdoc />
    public void SetVideoEnabled(bool enabled) => _client?.ToggleSendVideo(enabled);

    /// <inheritdoc />
    public void SetVideoBitrate(int kbps) => _client?.SetVideoBitrate(kbps);

    /// <summary>
    /// Translates the SDK's listener into this client's events.
    ///
    /// IRed5EventListener is a plain interface with no default implementation on the Java side, so
    /// every member has to be present even though most are not interesting here - the chat and
    /// stats callbacks in particular belong to features this layer does not expose.
    /// </summary>
    private sealed class EventBridge(Red5Client owner)
        : Java.Lang.Object, IRed5WebrtcClient.IRed5EventListener
    {
        public void OnLicenseValidated(bool validated, string? message) =>
            owner.RaiseLicenseValidated(validated, message ?? string.Empty);

        public void OnPublishStarted() => owner.RaisePublishStarted();

        public void OnPublishStopped() => owner.RaisePublishStopped();

        public void OnPublishFailed(string? error) => owner.RaiseError(error ?? "publish failed");

        public void OnSubscribeStarted() => owner.RaiseSubscribeStarted();

        public void OnSubscribeStopped() => owner.RaiseSubscribeStopped();

        public void OnSubscribeFailed(string? error) => owner.RaiseError(error ?? "subscribe failed");

        public void OnPreviewStarted() => owner.RaisePreviewStarted();

        public void OnPreviewStopped()
        {
        }

        public void OnError(string? error) => owner.RaiseError(error ?? "unknown error");

        public void OnConnectionStateChanged(IRed5WebrtcClient.PeerConnectionState? state) =>
            owner.RaiseConnectionStateChanged(MapState(state?.ToString()));

        public void OnIceConnectionStateChanged(IRed5WebrtcClient.IceConnectionState? state)
        {
            // Deliberately not surfaced: ICE state and peer state report the same session through
            // two different vocabularies, and raising both would make ConnectionStateChanged fire
            // twice per transition with different values.
        }

        public void OnRtcStats(Core.Model.RTCStats? stats)
        {
        }

        // Chat is PubNub-backed and not part of the bound surface - see Transforms/Metadata.xml.
        public void OnChatConnected()
        {
        }

        public void OnChatDisconnected()
        {
        }

        public void OnChatError(string? error)
        {
        }

        public void OnChatMessageReceived(string? channel, GoogleGson.JsonElement? message)
        {
        }

        public void OnChatSendError(string? channel, string? errorMessage)
        {
        }

        public void OnChatSendSuccess(string? channel, Java.Lang.Long? timetoken)
        {
        }

        /// <summary>
        /// Maps the Java enum by name. Java enums bind as classes rather than C# enums, and
        /// toString() returns the constant's name, which is stable across SDK builds in a way that
        /// ordinal values are not.
        /// </summary>
        private static Red5ConnectionState MapState(string? name) => name?.ToUpperInvariant() switch
        {
            "NEW" => Red5ConnectionState.New,
            "CONNECTING" => Red5ConnectionState.Connecting,
            "CONNECTED" => Red5ConnectionState.Connected,
            "DISCONNECTED" => Red5ConnectionState.Disconnected,
            "FAILED" => Red5ConnectionState.Failed,
            "CLOSED" => Red5ConnectionState.Closed,
            _ => Red5ConnectionState.Unknown,
        };
    }
}
