using SkiaSharp.Views.Maui.Controls.Hosting;

namespace AndroidApp1;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                // Cormorant Garamond (variable font; default instance is Light,
                // matching ember.html's font-weight:300). Aliased as "EmberSerif".
                fonts.AddFont("CormorantGaramond.ttf", "EmberSerif");
            });

        return builder.Build();
    }
}
