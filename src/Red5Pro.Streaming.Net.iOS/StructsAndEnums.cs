using ObjCRuntime;

namespace Red5Pro.Streaming.Net.iOS;

// Mirrors the @objc enums in native/ios/Facade/Red5ProFacade.swift. [Native] because Swift emits
// them as NSInteger-backed, which is 64-bit on every platform this package targets.

/// <summary>The role a client takes for a session.</summary>
[Native]
public enum R5Mode : long
{
    /// <summary>Send this device's camera and microphone.</summary>
    Publish = 0,

    /// <summary>Receive a stream someone else is publishing.</summary>
    Subscribe = 1,
}

/// <summary>Where the published video comes from.</summary>
[Native]
public enum R5VideoSource : long
{
    FrontCamera = 0,
    RearCamera = 1,

    /// <summary>Screen capture. Requires a broadcast upload extension in the host app.</summary>
    Screen = 2,

    /// <summary>A capturer supplied by the app.</summary>
    Custom = 3,
}

/// <summary>ICE transport state.</summary>
[Native]
public enum R5IceState : long
{
    /// <summary>
    /// A state this binding does not know about, which means the Red5 SDK in use is newer than
    /// the one this package was generated from. Possible because the SDK is supplied by the
    /// consumer rather than shipped here.
    /// </summary>
    Unknown = -1,

    New = 0,
    Checking = 1,
    Connected = 2,
    Completed = 3,
    Failed = 4,
    Disconnected = 5,
    Closed = 6,
}

/// <summary>Peer connection state.</summary>
[Native]
public enum R5PeerState : long
{
    /// <inheritdoc cref="R5IceState.Unknown" />
    Unknown = -1,

    New = 0,
    Connecting = 1,
    Connected = 2,
    Disconnected = 3,
    Failed = 4,
    Closed = 5,
}

/// <summary>How video is fitted into its view.</summary>
[Native]
public enum R5ScalingType : long
{
    /// <summary>Whole frame visible, letterboxed.</summary>
    AspectFit = 0,

    /// <summary>Fills the view, cropping the frame.</summary>
    AspectFill = 1,

    /// <summary>Stretches to fill, ignoring aspect ratio.</summary>
    Fill = 2,
}
