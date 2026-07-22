using System.Reflection;
using System.Text;
using Red5Pro.Streaming.Net.Maui;

namespace Red5Pro.Streaming.Net.Sample;

/// <summary>
/// Publishes and subscribes through Red5. The whole app is this one file — there is no
/// per-platform code at all, because <c>Red5Pro.Streaming.Net</c> presents the same client on
/// Android and iOS and <c>Red5Pro.Streaming.Net.Maui</c> supplies the video view and the Android
/// <c>Activity</c>.
///
/// That absence is the point of the sample: written against the two bindings directly, this would
/// need a <c>#if ANDROID</c> block for construction, another for the renderer, and two separate
/// callback adapters.
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly StringBuilder _status = new();
    private IRed5Client? _client;

    public MainPage()
    {
        InitializeComponent();
        ApplyDeploymentDefaults();
    }

    private bool IsCloud => CloudRadio.IsChecked;

    /// <summary>
    /// Prefills from whatever the build was given, so the app can be launched on a phone without
    /// typing a licence key on a touch keyboard. See the AssemblyMetadata block in the .csproj —
    /// a real app would not embed these.
    /// </summary>
    private void ApplyDeploymentDefaults()
    {
        HostEntry.Text = BuildConstant(IsCloud ? "Red5CloudEndpoint" : "Red5ProEndpoint");
        LicenseEntry.Text = BuildConstant(IsCloud ? "Red5CloudLicenseKey" : "Red5ProLicenseKey");
        HostEntry.Placeholder = IsCloud ? "your-id.cloud.red5.net" : "192.0.2.10";
    }

    private static string BuildConstant(string key) =>
        typeof(MainPage).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value ?? string.Empty;

    private void OnDeploymentChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (!e.Value)
        {
            return;
        }

        // Switching deployment invalidates any client already built for the other one.
        Reset();
        ApplyDeploymentDefaults();
    }

    /// <summary>
    /// Built lazily rather than in the constructor: on Android the SDK needs the current activity,
    /// and the video view needs its handler, neither of which exists until the page is on screen.
    /// </summary>
    private IRed5Client Client()
    {
        if (_client is not null)
        {
            return _client;
        }

        var client = new Red5Options
        {
            Deployment = IsCloud ? Red5Deployment.Cloud : Red5Deployment.Standalone,
            Host = HostEntry.Text?.Trim() ?? string.Empty,
            LicenseKey = LicenseEntry.Text?.Trim() ?? string.Empty,
            VideoWidth = 640,
            VideoHeight = 480,
            VideoBitrateKbps = 750,
        }.CreateClient();

        client.SetVideoView(VideoView);

        // The SDKs raise callbacks on their own threads; Append hops back to the UI thread.
        client.LicenseValidated += (_, e) =>
            Append(e.IsValid ? "licence accepted" : $"LICENCE REJECTED: {e.Message}");
        client.PreviewStarted += (_, _) => Append("preview started");
        client.PublishStarted += (_, e) => Append($"publishing {e.StreamName}");
        client.PublishStopped += (_, e) => Append($"publish stopped: {e.StreamName}");
        client.SubscribeStarted += (_, e) => Append($"subscribing {e.StreamName}");
        client.SubscribeStopped += (_, e) => Append($"subscribe stopped: {e.StreamName}");
        client.ConnectionStateChanged += (_, e) => Append($"connection: {e.State}");
        client.Error += (_, e) => Append($"error: {e.Message}");

        return _client = client;
    }

    private async void OnPreviewClicked(object sender, EventArgs e)
    {
        if (!await EnsureReadyAsync(needsCapture: true))
        {
            return;
        }

        try
        {
            Client().StartPreview();
            SwitchButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            Append($"preview failed: {exception.Message}");
        }
    }

    private async void OnPublishClicked(object sender, EventArgs e) => await StartAsync(publish: true);

    private async void OnSubscribeClicked(object sender, EventArgs e) => await StartAsync(publish: false);

    private async Task StartAsync(bool publish)
    {
        var streamName = StreamEntry.Text?.Trim();

        if (string.IsNullOrEmpty(streamName))
        {
            Append("enter a stream name first");
            return;
        }

        // Publishing needs camera and microphone; subscribing needs neither.
        if (!await EnsureReadyAsync(needsCapture: publish))
        {
            return;
        }

        SetBusy(true);

        try
        {
            var client = Client();

            // Completes when the server confirms, so there is no callback to wire up for the
            // common case, and a failure to start surfaces as an exception right here — including
            // a rejected licence, which the SDK otherwise reports by simply going quiet.
            if (publish)
            {
                await client.PublishAsync(streamName);
            }
            else
            {
                await client.SubscribeAsync(streamName);
            }

            SetStreaming(true);
        }
        catch (Red5Exception exception)
        {
            Append($"failed to start: {exception.Message}");
            SetStreaming(false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnSwitchCameraClicked(object sender, EventArgs e) => _client?.SwitchCamera();

    private void OnStopClicked(object sender, EventArgs e)
    {
        _client?.Stop();
        Append("stopped");
        SetStreaming(false);
    }

    private async Task<bool> EnsureReadyAsync(bool needsCapture)
    {
        if (string.IsNullOrEmpty(HostEntry.Text?.Trim()))
        {
            Append("enter a host first");
            return false;
        }

        if (string.IsNullOrEmpty(LicenseEntry.Text?.Trim()))
        {
            // Worth saying explicitly: Red5 accounts issue two keys and only one of them works
            // here, and the SDK's own rejection message does not mention it.
            Append("enter the SDK licence key (not the server licence key)");
            return false;
        }

        if (!needsCapture)
        {
            return true;
        }

        var camera = await Permissions.RequestAsync<Permissions.Camera>();
        var microphone = await Permissions.RequestAsync<Permissions.Microphone>();

        if (camera == PermissionStatus.Granted && microphone == PermissionStatus.Granted)
        {
            return true;
        }

        Append("camera and microphone permission denied");
        return false;
    }

    private void Reset()
    {
        _client?.Dispose();
        _client = null;
        SetStreaming(false);
        SwitchButton.IsEnabled = false;
    }

    private void SetBusy(bool busy)
    {
        PublishButton.IsEnabled = !busy;
        SubscribeButton.IsEnabled = !busy;
        PreviewButton.IsEnabled = !busy;
    }

    private void SetStreaming(bool streaming)
    {
        PublishButton.IsEnabled = !streaming;
        SubscribeButton.IsEnabled = !streaming;
        PreviewButton.IsEnabled = !streaming;
        StopButton.IsEnabled = streaming;
        SwitchButton.IsEnabled = streaming;
    }

    private void Append(string message) => MainThread.BeginInvokeOnMainThread(() =>
    {
        _status.AppendLine($"{DateTime.Now:HH:mm:ss}  {message}");
        StatusLabel.Text = _status.ToString();
        StatusScroll.ScrollToAsync(0, StatusLabel.Height, animated: false);
    });

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        // Releases the camera and the native renderer when the page goes away; both SDKs hold a
        // GL context that managed collection alone does not reclaim.
        if (Handler is null)
        {
            Reset();
        }
    }
}
