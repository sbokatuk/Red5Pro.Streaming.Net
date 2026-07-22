namespace Red5Pro.Streaming.Net.Maui;

/// <summary>Wires the package into a MAUI app.</summary>
public static class AppBuilderExtensions
{
    /// <summary>
    /// Registers the handler for <see cref="Red5VideoView" />. Without this the view has no
    /// native counterpart and renders nothing — MAUI does not discover third-party handlers on
    /// its own.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.UseMauiApp&lt;App&gt;().UseRed5Pro();
    /// </code>
    /// </example>
    public static MauiAppBuilder UseRed5Pro(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<Red5VideoView, Red5VideoViewHandler>());

        return builder;
    }
}
