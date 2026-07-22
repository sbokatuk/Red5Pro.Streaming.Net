using Microsoft.Extensions.Logging;
using Red5Pro.Streaming.Net.Maui;

namespace Red5Pro.Streaming.Net.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            })
            // Registers the handler for Red5VideoView. MAUI does not discover third-party handlers
            // on its own, so without this the view renders nothing.
            .UseRed5Pro();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
