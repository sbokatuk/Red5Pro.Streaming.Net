namespace Red5Pro.Streaming.Net;

/// <summary>
/// Which kind of Red5 deployment a client connects to. The two are configured differently enough
/// that guessing from the host name would be fragile, so it is stated.
/// </summary>
public enum Red5Deployment
{
    /// <summary>
    /// Red5 Cloud. Connects to a stream manager host, which resolves an origin or edge node for
    /// the stream. Requires <see cref="Red5Options.Host" /> to be your
    /// <c>&lt;id&gt;.cloud.red5.net</c> endpoint.
    /// </summary>
    Cloud,

    /// <summary>
    /// A standalone Red5 Pro server you run yourself. Connects to it directly.
    /// </summary>
    Standalone,
}

/// <summary>
/// Settings applied to a client before it connects. <see cref="Host" /> and
/// <see cref="LicenseKey" /> are required; everything else has a usable default.
/// </summary>
public sealed class Red5Options
{
    /// <summary>
    /// Which kind of deployment <see cref="Host" /> refers to. This selects the SDK call used to
    /// configure it — a stream manager host and a standalone server address are not
    /// interchangeable, and getting it wrong fails as a connection timeout rather than as
    /// anything that points at the cause.
    /// </summary>
    public Red5Deployment Deployment { get; set; } = Red5Deployment.Cloud;

    /// <summary>
    /// The stream manager host for <see cref="Red5Deployment.Cloud" /> (for example
    /// <c>your-id.cloud.red5.net</c>), or the server's host name or IP for
    /// <see cref="Red5Deployment.Standalone" />. Required.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Defaults to 443 for <see cref="Red5Deployment.Cloud" /> and 5080 for
    /// <see cref="Red5Deployment.Standalone" />, which is what each deployment normally serves.
    /// Set explicitly to override.
    /// </summary>
    public int? Port { get; set; }

    /// <summary>The server application. <c>live</c> unless your deployment says otherwise.</summary>
    public string AppName { get; set; } = "live";

    /// <summary>
    /// Red5 Cloud node group. Ignored for <see cref="Red5Deployment.Standalone" />.
    /// </summary>
    public string NodeGroup { get; set; } = "default";

    /// <summary>
    /// The <b>SDK</b> licence key. Required.
    ///
    /// Red5 issues two keys and they are not interchangeable: the *server* licence key activates a
    /// Red5 Pro server install, while this one activates the client SDK. Using the wrong one fails
    /// at <see cref="Red5Events.LicenseValidated" /> with a message from the server.
    ///
    /// Red5 Cloud and Red5 Pro accounts each issue their own SDK key; use the one matching
    /// <see cref="Deployment" />.
    /// </summary>
    public string LicenseKey { get; set; } = string.Empty;

    /// <summary>Authentication token, when the deployment requires one.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Capture and send video. Disable for audio-only publishing.</summary>
    public bool VideoEnabled { get; set; } = true;

    /// <summary>Capture and send audio.</summary>
    public bool AudioEnabled { get; set; } = true;

    /// <summary>Preferred capture width. The camera may pick the closest size it supports.</summary>
    public int VideoWidth { get; set; } = 1280;

    /// <summary>Preferred capture height.</summary>
    public int VideoHeight { get; set; } = 720;

    /// <summary>Preferred capture frame rate.</summary>
    public int VideoFps { get; set; } = 30;

    /// <summary>Target video bitrate in kbps.</summary>
    public int VideoBitrateKbps { get; set; } = 1500;

    /// <summary>Start on the front (selfie) camera rather than the rear one.</summary>
    public bool UseFrontCamera { get; set; } = true;

    /// <summary>
    /// How long <see cref="IRed5Client.PublishAsync" /> and <see cref="IRed5Client.SubscribeAsync" />
    /// wait for the server to confirm before giving up.
    ///
    /// Not belt-and-braces: the SDKs report failures through callbacks, and some conditions — an
    /// unreachable host, a stream name that does not exist — produce no callback at all, so
    /// without a timeout those calls would never complete.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The port actually used, applying the per-deployment default.</summary>
    internal int ResolvedPort => Port ?? Deployment switch
    {
        Red5Deployment.Cloud => 443,
        _ => 5080,
    };

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new InvalidOperationException(
                $"{nameof(Red5Options)}.{nameof(Host)} is required — the stream manager host for " +
                "Red5 Cloud (e.g. your-id.cloud.red5.net), or the server address for a " +
                "standalone Red5 Pro server.");
        }

        if (string.IsNullOrWhiteSpace(LicenseKey))
        {
            throw new InvalidOperationException(
                $"{nameof(Red5Options)}.{nameof(LicenseKey)} is required. Use the *SDK* licence " +
                "key from your Red5 account, not the server licence key.");
        }
    }
}
