using Microsoft.Maui.Handlers;
using Red5Pro.Streaming.Core;

namespace Red5Pro.Streaming.Net.Maui;

/// <summary>
/// Android half: the native view is Red5Renderer, which extends org.webrtc's SurfaceViewRenderer
/// and is what Red5WebrtcClientBuilder.SetVideoRenderer takes.
/// </summary>
public partial class Red5VideoViewHandler : ViewHandler<Red5VideoView, Red5Renderer>
{
    /// <inheritdoc />
    protected override Red5Renderer CreatePlatformView() => new(Context);

    /// <inheritdoc />
    protected override void DisconnectHandler(Red5Renderer platformView)
    {
        // The renderer holds an EGL context and a native surface. Leaving it initialised leaks
        // both, and after a few navigations exhausts the surface pool.
        platformView.Release();
        base.DisconnectHandler(platformView);
    }
}
