using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace news.Services
{
    public interface IWeatherService
    {
        Task<WeatherDashboard> GetForecastAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
    }

    public sealed class OpenMeteoWeatherService : IWeatherService
    {
        private const string BaseAddress = "https://api.open-meteo.com";
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public OpenMeteoWeatherService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _httpClient.Timeout = TimeSpan.FromSeconds(15);

            if (!Uri.TryCreate(BaseAddress, UriKind.Absolute, out var baseUri) ||
                !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The weather API base URL must use HTTPS.");
            }
        }

        public async Task<WeatherDashboard> GetForecastAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
        {
            if (double.IsNaN(latitude) || double.IsNaN(longitude) ||
                double.IsInfinity(latitude) || double.IsInfinity(longitude) ||
                latitude < -90 || latitude > 90 ||
                longitude < -180 || longitude > 180)
            {
                throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude and longitude values must fall within valid global ranges.");
            }

            var uri = BuildForecastUri(latitude, longitude);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<OpenMeteoForecastResponse>(_jsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("The weather service returned an empty payload.");

            return BuildDashboard(payload);
        }

        private static Uri BuildForecastUri(double latitude, double longitude)
        {
            var builder = new UriBuilder(BaseAddress)
            {
                Path = "/v1/forecast",
                Query = string.Join("&",
                    $"latitude={latitude:F4}",
                    $"longitude={longitude:F4}",
                    "current=temperature_2m,relative_humidity_2m,precipitation_probability,wind_speed_10m,weather_code",
                    "hourly=temperature_2m,weather_code,precipitation_probability",
                    "forecast_days=1",
                    "timezone=auto")
            };

            if (!string.Equals(builder.Uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only HTTPS requests are allowed for the weather API.");
            }

            return builder.Uri;
        }

        private static WeatherDashboard BuildDashboard(OpenMeteoForecastResponse payload)
        {
            var current = payload.Current ?? throw new InvalidOperationException("Current weather data was not returned.");
            var hourly = payload.Hourly ?? throw new InvalidOperationException("Hourly forecast data was not returned.");

            var forecasts = new List<HourlyForecast>();
            var count = Math.Min(hourly.Time?.Length ?? 0, 6);
            for (var index = 0; index < count; index++)
            {
                var time = hourly.Time?[index] ?? string.Empty;
                var temperature = hourly.Temperature2M?[index] ?? 0;
                var code = hourly.WeatherCode?[index] ?? 0;
                var precipitation = hourly.PrecipitationProbability?[index] ?? 0;

                forecasts.Add(new HourlyForecast(
                    Time: FormatHour(time),
                    Icon: GetConditionIcon(code),
                    Temp: $"{temperature:F0}°",
                    Precipitation: $"{precipitation}% rain"));
            }

            return new WeatherDashboard(
                City: "Berlin",
                Condition: GetConditionText(current.WeatherCode ?? 0),
                TemperatureC: current.Temperature2M ?? 0,
                FeelsLikeC: current.Temperature2M ?? 0,
                WindKph: current.WindSpeed10M ?? 0,
                HumidityPercent: current.RelativeHumidity2M ?? 0,
                PrecipitationChance: current.PrecipitationProbability ?? 0,
                Summary: GetSummary(current.WeatherCode ?? 0, current.Temperature2M ?? 0),
                Forecasts: forecasts);
        }

        private static string FormatHour(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Now";
            }

            if (DateTimeOffset.TryParse(value, out var parsed))
            {
                return parsed.ToLocalTime().ToString("HH:mm");
            }

            return value;
        }

        private static string GetConditionText(int code)
        {
            return code switch
            {
                0 or 1 => "Clear skies",
                2 => "Partly cloudy",
                3 => "Overcast",
                >= 45 and <= 48 => "Foggy",
                >= 51 and <= 67 => "Drizzle",
                >= 71 and <= 77 => "Snow",
                >= 80 and <= 82 => "Showers",
                >= 85 and <= 86 => "Snow showers",
                _ => "Stormy"
            };
        }

        private static string GetConditionIcon(int code)
        {
            return code switch
            {
                0 or 1 => "☀️",
                2 => "⛅",
                3 => "☁️",
                >= 45 and <= 48 => "🌫️",
                >= 51 and <= 67 => "🌦️",
                >= 71 and <= 77 => "❄️",
                >= 80 and <= 82 => "🌧️",
                >= 85 and <= 86 => "🌨️",
                _ => "⛈️"
            };
        }

        private static string GetSummary(int code, double temperature)
        {
            var condition = GetConditionText(code);
            return $"{condition} with a comfortable {temperature:F0}°C feel.";
        }
    }

    public sealed record WeatherDashboard(
        string City,
        string Condition,
        double TemperatureC,
        double FeelsLikeC,
        double WindKph,
        double HumidityPercent,
        double PrecipitationChance,
        string Summary,
        IReadOnlyList<HourlyForecast> Forecasts);

    public sealed record HourlyForecast(string Time, string Icon, string Temp, string Precipitation);

    internal sealed class OpenMeteoForecastResponse
    {
        [JsonPropertyName("current")]
        public OpenMeteoCurrentResponse? Current { get; init; }

        [JsonPropertyName("hourly")]
        public OpenMeteoHourlyResponse? Hourly { get; init; }
    }

    internal sealed class OpenMeteoCurrentResponse
    {
        [JsonPropertyName("temperature_2m")]
        public double? Temperature2M { get; init; }

        [JsonPropertyName("relative_humidity_2m")]
        public double? RelativeHumidity2M { get; init; }

        [JsonPropertyName("precipitation_probability")]
        public double? PrecipitationProbability { get; init; }

        [JsonPropertyName("wind_speed_10m")]
        public double? WindSpeed10M { get; init; }

        [JsonPropertyName("weather_code")]
        public int? WeatherCode { get; init; }
    }

    internal sealed class OpenMeteoHourlyResponse
    {
        [JsonPropertyName("time")]
        public string[]? Time { get; init; }

        [JsonPropertyName("temperature_2m")]
        public double[]? Temperature2M { get; init; }

        [JsonPropertyName("weather_code")]
        public int[]? WeatherCode { get; init; }

        [JsonPropertyName("precipitation_probability")]
        public double[]? PrecipitationProbability { get; init; }
    }
}
