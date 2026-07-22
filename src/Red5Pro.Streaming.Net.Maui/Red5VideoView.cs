using Microsoft.Maui.Handlers;

namespace Red5Pro.Streaming.Net.Maui;

/// <summary>
/// A MAUI view the SDK renders video into — a <c>Red5Renderer</c> on Android (which is an
/// <c>org.webrtc.SurfaceViewRenderer</c>), a plain <c>UIView</c> on iOS.
///
/// A custom view rather than a wrapped <c>ContentView</c> because both SDKs want the native view
/// itself: Android's builder takes the renderer, and the iOS facade puts its own
/// <c>RTCMTLVideoView</c> inside the UIView you hand it.
/// </summary>
public class Red5VideoView : View
{
}

/// <summary>
/// Shared half of the handler. Each platform's half declares the base class, which is what binds
/// <see cref="Red5VideoView" /> to that platform's native view type.
/// </summary>
public partial class Red5VideoViewHandler
{
    /// <summary>
    /// The view has no properties of its own; the mapper exists because a handler needs one.
    /// </summary>
    public static readonly IPropertyMapper<Red5VideoView, Red5VideoViewHandler> VideoMapper =
        new PropertyMapper<Red5VideoView, Red5VideoViewHandler>(ViewHandler.ViewMapper);

    /// <summary>Creates the handler. Registered by <see cref="AppBuilderExtensions.UseRed5Pro" />.</summary>
    public Red5VideoViewHandler() : base(VideoMapper)
    {
    }
}
