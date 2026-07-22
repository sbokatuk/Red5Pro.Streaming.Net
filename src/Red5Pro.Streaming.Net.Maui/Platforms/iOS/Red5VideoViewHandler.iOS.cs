using Microsoft.Maui.Handlers;
using UIKit;

namespace Red5Pro.Streaming.Net.Maui;

/// <summary>
/// iOS half: the facade's SetVideoView takes a plain UIView and adds its own RTCMTLVideoView
/// inside it, pinned to the bounds, so there is nothing SDK-specific to create here.
/// </summary>
public partial class Red5VideoViewHandler : ViewHandler<Red5VideoView, UIView>
{
    /// <inheritdoc />
    protected override UIView CreatePlatformView() => new() { BackgroundColor = UIColor.Black };
}
