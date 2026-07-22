using Red5Pro.Streaming.Net.iOS;
using UIKit;

namespace Red5Pro.Streaming.Net;

/// <summary>
/// iOS half, built on the @objc facade's R5Client and R5ClientDelegate.
/// </summary>
public sealed partial class Red5Client
{
    private readonly R5Client _client = new();
    private readonly DelegateBridge _delegate;

    private UIView? _view;
    private bool _built;

    /// <summary>Creates a client.</summary>
    /// <param name="options">Connection settings. See <see cref="Red5Options" />.</param>
    public Red5Client(Red5Options options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        // Kept in a field, not a local: R5Client.Delegate is a weak reference, so a bridge that
        // only the assignment referenced would be collected and the callbacks would stop arriving
        // with no error anywhere.
        _delegate = new DelegateBridge(this);
        _client.Delegate = _delegate;
    }

    /// <summary>
    /// Renders video into <paramref name="view" />. The facade adds its own renderer as a subview.
    /// </summary>
    public void SetView(UIView? view)
    {
        _view = view;

        if (view is not null)
        {
            _client.SetVideoView(view);
        }
    }

    private void PublishCore(string streamName)
    {
        Configure(streamName);
        _client.Publish(streamName);
    }

    private void SubscribeCore(string streamName)
    {
        Configure(streamName);
        _client.Subscribe(streamName);
    }

    /// <inheritdoc />
    public void StartPreview()
    {
        // Preview normally runs before a stream name is known, so the session is configured with
        // the placeholder the SDK will overwrite when publishing actually starts.
        Configure(_client.IsBuilt() ? StreamName ?? string.Empty : string.Empty);
        _client.StartPreview();
    }

    /// <inheritdoc />
    public void StopPreview() => _client.StopPreview();

    /// <summary>
    /// Applies the options and builds once. Unlike Android, the facade keeps one client for its
    /// lifetime, so rebuilding on every operation would discard the licence validation that
    /// Build() triggers.
    /// </summary>
    private void Configure(string streamName)
    {
        if (_built)
        {
            _client.SetStreamName(streamName);
            return;
        }

        // The one place the two deployment shapes actually differ.
        if (_options.Deployment == Red5Deployment.Cloud)
        {
            _client.SetStreamManagerHost(_options.Host);
            _client.SetNodeGroup(_options.NodeGroup);
        }
        else
        {
            _client.SetServerIp(_options.Host);
        }

        _client.SetPort(_options.ResolvedPort);
        _client.SetAppName(_options.AppName);
        _client.SetStreamName(streamName);
        _client.SetLicenseKey(_options.LicenseKey);
        _client.SetVideoEnabled(_options.VideoEnabled);
        _client.SetAudioEnabled(_options.AudioEnabled);
        _client.SetVideoSize(_options.VideoWidth, _options.VideoHeight);
        _client.SetVideoFps(_options.VideoFps);
        _client.SetVideoBitrate(_options.VideoBitrateKbps);
        _client.SetVideoSource(_options.UseFrontCamera
            ? R5VideoSource.FrontCamera
            : R5VideoSource.RearCamera);

        if (!string.IsNullOrEmpty(_options.Token))
        {
            _client.SetToken(_options.Token);
        }

        if (_view is not null)
        {
            _client.SetVideoView(_view);
        }

        _client.Build();
        _built = true;
    }

    private void StopCore()
    {
        if (_built)
        {
            _client.Stop();
        }
    }

    private void DisposeCore()
    {
        _client.Delegate = null;
        _client.Dispose();
        _view = null;
    }

    /// <inheritdoc />
    public void SwitchCamera() => _client.SwitchCamera();

    /// <inheritdoc />
    public void SetAudioEnabled(bool enabled) => _client.ToggleSendAudio(enabled);

    /// <inheritdoc />
    public void SetVideoEnabled(bool enabled) => _client.ToggleSendVideo(enabled);

    /// <inheritdoc />
    public void SetVideoBitrate(int kbps) => _client.SetStreamVideoBitrate(kbps);

    /// <summary>
    /// Translates the facade's delegate callbacks into this client's events. Every member of
    /// R5ClientDelegate is optional, so only the ones that matter here are overridden.
    /// </summary>
    private sealed class DelegateBridge(Red5Client owner) : R5ClientDelegate
    {
        public override void OnLicenseValidated(bool validated, string message) =>
            owner.RaiseLicenseValidated(validated, message);

        public override void OnPublishStarted() => owner.RaisePublishStarted();

        public override void OnPublishStopped() => owner.RaisePublishStopped();

        public override void OnPublishFailed(string error) => owner.RaiseError(error);

        public override void OnSubscribeStarted() => owner.RaiseSubscribeStarted();

        public override void OnSubscribeStopped() => owner.RaiseSubscribeStopped();

        public override void OnSubscribeFailed(string error) => owner.RaiseError(error);

        public override void OnPreviewStarted() => owner.RaisePreviewStarted();

        public override void OnPeerStateChanged(R5PeerState state) =>
            owner.RaiseConnectionStateChanged(state switch
            {
                R5PeerState.New => Red5ConnectionState.New,
                R5PeerState.Connecting => Red5ConnectionState.Connecting,
                R5PeerState.Connected => Red5ConnectionState.Connected,
                R5PeerState.Disconnected => Red5ConnectionState.Disconnected,
                R5PeerState.Failed => Red5ConnectionState.Failed,
                R5PeerState.Closed => Red5ConnectionState.Closed,
                _ => Red5ConnectionState.Unknown,
            });

        public override void OnError(string error) => owner.RaiseError(error);
    }
}
