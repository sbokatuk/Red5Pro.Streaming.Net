namespace Red5Pro.Streaming.Net;

/// <summary>Identifies which stream an event refers to.</summary>
public class Red5StreamEventArgs(string streamName) : EventArgs
{
    /// <summary>The stream the event concerns.</summary>
    public string StreamName { get; } = streamName;
}

/// <summary>Something went wrong. The session may or may not still be usable.</summary>
public sealed class Red5ErrorEventArgs(string message) : EventArgs
{
    /// <summary>The SDK's description. Wording comes from the server and is not stable.</summary>
    public string Message { get; } = message;
}

/// <summary>
/// The outcome of the licence check, which Red5 performs before anything streams.
/// </summary>
public sealed class Red5LicenseEventArgs(bool valid, string message) : EventArgs
{
    /// <summary>False means nothing will stream, whatever else you do.</summary>
    public bool IsValid { get; } = valid;

    /// <summary>The server's explanation. Usually the only clue as to why a key was rejected.</summary>
    public string Message { get; } = message;
}

/// <summary>Connection state, unified across the two platforms' enums.</summary>
public enum Red5ConnectionState
{
    /// <summary>
    /// Reported by the SDK but not known to this binding, which means the Red5 SDK supplied at
    /// build time is newer than the one this package was generated against.
    /// </summary>
    Unknown = -1,

    New = 0,
    Connecting = 1,
    Connected = 2,
    Disconnected = 3,
    Failed = 4,
    Closed = 5,
}

/// <summary>The peer connection changed state.</summary>
public sealed class Red5ConnectionStateEventArgs(Red5ConnectionState state) : EventArgs
{
    /// <summary>The new state.</summary>
    public Red5ConnectionState State { get; } = state;
}

/// <summary>Raised when a session fails to start or the server reports an error.</summary>
public sealed class Red5Exception(string message) : Exception(message);
