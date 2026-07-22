namespace Red5Pro.Streaming.Net;

/// <summary>
/// Cross-platform <see cref="IRed5Client" />. The platform halves live in Platforms/Android and
/// Platforms/iOS; everything shared — events, state, and turning the SDKs' callbacks into
/// awaitable operations — lives here.
/// </summary>
public sealed partial class Red5Client : IRed5Client
{
    private readonly Red5Options _options;

    /// <summary>
    /// The in-flight <see cref="PublishAsync" /> or <see cref="SubscribeAsync" />. Only one at a
    /// time: neither SDK tells you which call a callback belongs to, so a second concurrent
    /// operation could not be matched to its result.
    /// </summary>
    private TaskCompletionSource<bool>? _pending;

    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<Red5LicenseEventArgs>? LicenseValidated;

    /// <inheritdoc />
    public event EventHandler<Red5StreamEventArgs>? PublishStarted;

    /// <inheritdoc />
    public event EventHandler<Red5StreamEventArgs>? PublishStopped;

    /// <inheritdoc />
    public event EventHandler<Red5StreamEventArgs>? SubscribeStarted;

    /// <inheritdoc />
    public event EventHandler<Red5StreamEventArgs>? SubscribeStopped;

    /// <inheritdoc />
    public event EventHandler? PreviewStarted;

    /// <inheritdoc />
    public event EventHandler<Red5ConnectionStateEventArgs>? ConnectionStateChanged;

    /// <inheritdoc />
    public event EventHandler<Red5ErrorEventArgs>? Error;

    /// <inheritdoc />
    public bool IsStreaming { get; private set; }

    /// <inheritdoc />
    public string? StreamName { get; private set; }

    /// <inheritdoc />
    public bool IsLicenseValidated { get; private set; }

    /// <inheritdoc />
    public Task PublishAsync(string streamName, CancellationToken cancellationToken = default) =>
        RunAsync(streamName, () => PublishCore(streamName), cancellationToken);

    /// <inheritdoc />
    public Task SubscribeAsync(string streamName, CancellationToken cancellationToken = default) =>
        RunAsync(streamName, () => SubscribeCore(streamName), cancellationToken);

    /// <summary>
    /// Starts an operation and waits for the platform to report that it succeeded.
    ///
    /// Both SDKs are callback-driven with no completion handle, so the awaitable is built here:
    /// the platform half calls <see cref="CompletePending" /> from its started callback and
    /// <see cref="FailPending" /> from its error callback. The timeout is load-bearing — an
    /// unreachable host or an unknown stream name can produce no callback at all, and without it
    /// the returned task would never finish.
    /// </summary>
    private async Task RunAsync(string streamName, Action start, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _options.Validate();

        if (_pending is not null)
        {
            throw new InvalidOperationException(
                "another publish or subscribe is already in progress; await or Stop() it first.");
        }

        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;
        StreamName = streamName;

        using var timeout = new CancellationTokenSource(_options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeout.Token);

        await using var registration = linked.Token.Register(() =>
        {
            // Distinguishes the two reasons the token can fire: the caller's cancellation should
            // surface as OperationCanceledException, an expired timeout as a failure to start.
            if (cancellationToken.IsCancellationRequested)
            {
                pending.TrySetCanceled(cancellationToken);
            }
            else
            {
                // The licence is the single most common reason for a silent timeout, and the SDK
                // does not say so, so the message does.
                var licence = IsLicenseValidated
                    ? string.Empty
                    : " The licence key has not been validated — check Red5Options.LicenseKey is " +
                      "the SDK key (not the server key) for this deployment.";

                pending.TrySetException(new Red5Exception(
                    $"the server did not confirm '{streamName}' within " +
                    $"{_options.Timeout.TotalSeconds:0}s.{licence}"));
            }
        }).ConfigureAwait(false);

        try
        {
            start();
            await pending.Task.ConfigureAwait(false);
            IsStreaming = true;
        }
        catch
        {
            // A failed start leaves the native client holding a camera and a peer connection.
            StreamName = null;
            StopCore();
            throw;
        }
        finally
        {
            _pending = null;
        }
    }

    /// <summary>
    /// Work deferred until the licence has been validated. See <see cref="RunWhenLicensed" />.
    /// </summary>
    private Action? _whenLicensed;

    /// <summary>
    /// Runs <paramref name="action" /> once Red5 has accepted the licence key, or immediately if it
    /// already has.
    ///
    /// Publishing or subscribing before that point is silently ignored by both SDKs. Red5's
    /// documented sequence is build → onLicenseValidated → startPreview → publish, and calling
    /// publish() straight after building produces no error, no callback and no stream — it
    /// surfaces only as the operation timing out, which reads like a network problem.
    /// </summary>
    private void RunWhenLicensed(Action action)
    {
        if (IsLicenseValidated)
        {
            action();
            return;
        }

        _whenLicensed = action;
    }

    /// <summary>Called by the platform half when the server confirms the operation started.</summary>
    private void CompletePending() => _pending?.TrySetResult(true);

    /// <summary>Called by the platform half when the operation cannot proceed.</summary>
    private void FailPending(string message) =>
        _pending?.TrySetException(new Red5Exception(message));

    /// <inheritdoc />
    public void Stop()
    {
        StopCore();
        IsStreaming = false;
        StreamName = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        DisposeCore();
    }

    // Raised on whichever thread the SDK used. Marshalling to the UI thread is left to the caller,
    // because this package has no dependency on a UI framework; Red5Pro.Streaming.Net.Maui does it.

    private void RaiseLicenseValidated(bool valid, string message)
    {
        IsLicenseValidated = valid;

        if (valid)
        {
            // Anything that was waiting on the licence runs now. Taken and cleared before invoking
            // so a callback raised twice - which the SDKs do not promise not to do - cannot publish
            // twice.
            var deferred = _whenLicensed;
            _whenLicensed = null;
            deferred?.Invoke();
        }
        else
        {
            // A rejected licence is terminal, and the SDK will otherwise just never call back.
            // Failing the pending operation here turns "publish hangs until the timeout" into an
            // immediate, explanatory exception.
            _whenLicensed = null;
            FailPending($"the Red5 SDK licence key was rejected: {message}");
        }

        LicenseValidated?.Invoke(this, new Red5LicenseEventArgs(valid, message));
    }

    private void RaisePublishStarted()
    {
        CompletePending();
        PublishStarted?.Invoke(this, new Red5StreamEventArgs(StreamName ?? string.Empty));
    }

    private void RaisePublishStopped()
    {
        IsStreaming = false;
        PublishStopped?.Invoke(this, new Red5StreamEventArgs(StreamName ?? string.Empty));
    }

    private void RaiseSubscribeStarted()
    {
        CompletePending();
        SubscribeStarted?.Invoke(this, new Red5StreamEventArgs(StreamName ?? string.Empty));
    }

    private void RaiseSubscribeStopped()
    {
        IsStreaming = false;
        SubscribeStopped?.Invoke(this, new Red5StreamEventArgs(StreamName ?? string.Empty));
    }

    private void RaisePreviewStarted() => PreviewStarted?.Invoke(this, EventArgs.Empty);

    private void RaiseConnectionStateChanged(Red5ConnectionState state)
    {
        if (state is Red5ConnectionState.Failed or Red5ConnectionState.Closed)
        {
            IsStreaming = false;
        }

        ConnectionStateChanged?.Invoke(this, new Red5ConnectionStateEventArgs(state));
    }

    private void RaiseError(string message)
    {
        // An error while starting is the operation's result, not just a notification.
        FailPending(message);
        Error?.Invoke(this, new Red5ErrorEventArgs(message));
    }
}
