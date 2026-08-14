using Microsoft.Extensions.Logging;
using news.Services;
using System.Net.Http.Headers;

namespace news
{
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
                });

            builder.Services.AddSingleton<IFormFactor, FormFactor>();
            builder.Services.AddSingleton<IWeatherService>(_ =>
            {
                var httpClient = new HttpClient
                {
                    BaseAddress = new Uri("https://api.open-meteo.com"),
                    Timeout = TimeSpan.FromSeconds(15)
                };
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return new OpenMeteoWeatherService(httpClient);
            });

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}

