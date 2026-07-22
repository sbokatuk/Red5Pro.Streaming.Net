namespace Red5Pro.Streaming.Net.Maui;

/// <summary>
/// Creates clients and attaches views without the caller writing platform code.
///
/// This is the whole reason the MAUI package exists. On Android the SDK needs an
/// <c>Activity</c>, and the native video view types differ per platform, so a MAUI app would
/// otherwise need a <c>#if ANDROID</c> block for construction and another for rendering.
/// </summary>
public static class Red5ClientExtensions
{
    /// <summary>
    /// Creates a client for the current platform.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Android only, and only when called before the activity exists — from a page constructor,
    /// for example. Create the client once the page has appeared.
    /// </exception>
    public static IRed5Client CreateClient(this Red5Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

#if ANDROID
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException(
                "no current activity. The Red5 SDK needs one for the camera and the video " +
                "renderer, so create the client after the page has appeared rather than in its " +
                "constructor.");

        return new Red5Client(options, activity);
#elif IOS
        return new Red5Client(options);
#else
        throw new PlatformNotSupportedException(
            "Red5Pro.Streaming.Net supports Android and iOS. Mac Catalyst is not supported " +
            "because Red5 publishes no Catalyst slice for Red5WebRTCKit.");
#endif
    }

    /// <summary>
    /// Renders video into <paramref name="view" />. Call before publishing or subscribing.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The view has no handler yet, which means it is not on screen. Attach it after the page has
    /// appeared, not from its constructor.
    /// </exception>
    public static void SetVideoView(this IRed5Client client, Red5VideoView view)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(view);

#if ANDROID
        var native = view.Handler?.PlatformView as Red5Pro.Streaming.Core.Red5Renderer
            ?? throw NotRealised();
        ((Red5Client)client).SetRenderer(native);
#elif IOS
        var native = view.Handler?.PlatformView as UIKit.UIView ?? throw NotRealised();
        ((Red5Client)client).SetView(native);
#else
        throw NotRealised();
#endif
    }

    private static InvalidOperationException NotRealised() =>
        new("the Red5VideoView has no handler yet, so its native view does not exist. " +
            "Attach it once the page has appeared rather than from its constructor.");
}
