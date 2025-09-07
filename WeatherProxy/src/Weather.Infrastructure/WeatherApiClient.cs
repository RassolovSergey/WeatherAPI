using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Weather.Application;
using Weather.Domain;
using Weather.Infrastructure.Options;

namespace Weather.Infrastructure;

/// <summary>
/// HTTP-клиент к внешнему провайдеру weatherapi.com.
/// Реализация порта <see cref="IWeatherProvider"/>.
/// </summary>
public sealed class WeatherApiClient : IWeatherProvider
{
    private readonly HttpClient _http;
    private readonly WeatherApiOptions _options;
    private readonly ILogger<WeatherApiClient> _logger;

    // Единые настройки десериализации JSON
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public WeatherApiClient(
        HttpClient http,
        IOptions<WeatherApiOptions> options,
        ILogger<WeatherApiClient> logger)
    {
        _http = http;
        _options = options.Value; // храним уже развернутые опции (без .Value в методах)
        _logger = logger;
    }

    // ------------------------- CURRENT -------------------------

    /// <inheritdoc />
    public async Task<WeatherReport> GetCurrentAsync(string city, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("city is required", nameof(city));

        var q = Uri.EscapeDataString(city);
        var url = $"v1/current.json?key={_options.ApiKey}&q={q}&aqi=no";

        using var resp = await _http.GetAsync(url, ct);

        if ((int)resp.StatusCode is 401 or 403)
            throw new InvalidOperationException("WeatherAPI responded 401/403. Проверьте API ключ (WEATHERAPI__APIKEY).");

        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var model = await JsonSerializer.DeserializeAsync<CurrentApiResponse>(stream, _json, ct)
                    ?? throw new InvalidOperationException("Cannot parse WeatherAPI response");

        // ВАЖНО: WeatherReport имеет позиционный конструктор → передаем все аргументы по порядку.
        return new WeatherReport(
            model.location.name ?? city,                    // City
            model.location.country ?? string.Empty,         // Country
            model.current.temp_c,                           // TempC
            model.current.condition?.text ?? string.Empty,  // Condition
            DateTime.UtcNow,                                // FetchedAtUtc
            "origin"                                        // Source
        );
    }

    // ------------------------- FORECAST -------------------------

    /// <inheritdoc />
    public async Task<ForecastReport> GetForecastAsync(string city, int days, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("city is required", nameof(city));
        if (days < 1 || days > 7)
            throw new ArgumentOutOfRangeException(nameof(days), "days must be 1..7");

        var q = Uri.EscapeDataString(city);
        var url = $"v1/forecast.json?key={_options.ApiKey}&q={q}&days={days}&aqi=no&alerts=no";

        using var resp = await _http.GetAsync(url, ct);

        if ((int)resp.StatusCode is 401 or 403)
            throw new InvalidOperationException("WeatherAPI responded 401/403. Проверьте API ключ (WEATHERAPI__APIKEY).");

        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var model = await JsonSerializer.DeserializeAsync<ForecastApiResponse>(stream, _json, ct)
                    ?? throw new InvalidOperationException("Cannot parse WeatherAPI forecast response");

        var items = new List<ForecastDay>(capacity: model.forecast.forecastday.Count);
        foreach (var d in model.forecast.forecastday)
        {
            if (!DateOnly.TryParse(d.date, out var date))
                continue;

            items.Add(new ForecastDay
            {
                Date = date,
                MinTempC = d.day.mintemp_c,
                MaxTempC = d.day.maxtemp_c,
                Condition = d.day.condition?.text ?? string.Empty
            });
        }

        return new ForecastReport
        {
            City = model.location.name ?? city,
            Country = model.location.country ?? string.Empty,
            Days = items.Count,
            Items = items,
            FetchedAtUtc = DateTime.UtcNow,
            Source = "origin"
        };
    }

    // ----------------------- JSON моделейки -----------------------

    private sealed class CurrentApiResponse
    {
        public Location location { get; set; } = new();
        public Current current { get; set; } = new();
    }
    private sealed class Current
    {
        public double temp_c { get; set; }
        public Condition? condition { get; set; }
    }

    private sealed class ForecastApiResponse
    {
        public Location location { get; set; } = new();
        public Forecast forecast { get; set; } = new();
    }
    private sealed class Location
    {
        public string? name { get; set; }
        public string? country { get; set; }
    }
    private sealed class Forecast
    {
        public List<ForecastDayNode> forecastday { get; set; } = new();
    }
    private sealed class ForecastDayNode
    {
        public string date { get; set; } = "";
        public Day day { get; set; } = new();
    }
    private sealed class Day
    {
        [JsonPropertyName("mintemp_c")] public double mintemp_c { get; set; }
        [JsonPropertyName("maxtemp_c")] public double maxtemp_c { get; set; }
        public Condition? condition { get; set; }
    }
    private sealed class Condition
    {
        public string? text { get; set; }
    }
}