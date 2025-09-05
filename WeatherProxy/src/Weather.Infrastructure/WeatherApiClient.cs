using System.Net;                               // HttpStatusCode — для разборов ответов
using System.Text.Json;                         // JsonSerializer — парсинг JSON
using System.Text.Json.Serialization;           // JsonPropertyName — соответствие имён полей
using Microsoft.Extensions.Options;             // IOptions<T> — доступ к конфигу
using Weather.Application;                      // IWeatherProvider — порт приложения
using Weather.Domain;                           // WeatherReport — доменная модель
using Weather.Infrastructure.External.WeatherApi;
using Weather.Infrastructure.Options;           // WeatherApiOptions — BaseUrl/ApiKey/Timeout

namespace Weather.Infrastructure;

/// <summary>
/// Клиент внешнего провайдера погоды (weatherapi.com).
/// Делает GET /v1/current.json?q={city}&key={API_KEY} и маппит ответ в <see cref="WeatherReport"/>.
/// ВАЖНО: ключ API берём только из переменных окружения/конфига (не храним в коде).
/// </summary>
public sealed class WeatherApiClient : IWeatherProvider
{
    private readonly HttpClient _http;              // Typed HttpClient
    private readonly WeatherApiOptions _opts;       // BaseUrl, ApiKey, Timeout (из конфигурации)

    public WeatherApiClient(HttpClient http, IOptions<WeatherApiOptions> opts)
    {
        _http = http;
        _opts = opts.Value;
    }

    public async Task<WeatherReport> GetCurrentAsync(string city, CancellationToken ct = default)
    {
        // --- 1) Валидация и нормализация входа ---
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("City must be provided.", nameof(city));

        var q = city.Trim();

        // --- 2) Проверяем наличие базового URL и ключа (подсказка разработчику) ---
        if (string.IsNullOrWhiteSpace(_opts.BaseUrl))
            throw new InvalidOperationException(
                "WeatherApiOptions.BaseUrl is empty. " +
                "Установите ENV WEATHERAPI__BASEURL (например, https://api.weatherapi.com/v1/).");

        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
            throw new InvalidOperationException(
                "WeatherApiOptions.ApiKey is empty. " +
                "Установите ENV WEATHERAPI__APIKEY (ключ от weatherapi.com).");

        // --- 3) Формируем запрос ---
        // В weatherapi.com ключ передаётся как query-параметр key=..., город — q=...
        // Uri.EscapeDataString — экранируем пользовательский ввод (безопасность).
        var url = $"current.json?key={Uri.EscapeDataString(_opts.ApiKey)}&q={Uri.EscapeDataString(q)}";

        // --- 4) Делаем вызов ---
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        // --- 5) Базовая обработка ошибок провайдера ---
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // Обычно это неверный/просроченный ключ API
            throw new InvalidOperationException("WeatherAPI responded 401/403. Проверьте API ключ (WEATHERAPI__APIKEY).");
        }

        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync(ct);
            // В учебных целях выбрасываем осмысленную ошибку с кодом и коротким телом
            throw new HttpRequestException($"WeatherAPI error {(int)resp.StatusCode}: {errorBody}");
        }

        // --- 6) Декодируем JSON в минимальный внутренний DTO ---
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);

        var dto = await JsonSerializer.DeserializeAsync<WeatherApiCurrentDto>(
            stream,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true // на случай неожиданных регистров
            },
            ct);

        if (dto is null || dto.Location is null || dto.Current is null || dto.Current.Condition is null)
            throw new InvalidOperationException("WeatherAPI: unexpected JSON (missing required fields).");

        // --- 7) Маппинг во внутреннюю доменную модель ---
        var report = new WeatherReport(
            City: dto.Location.Name ?? q,                 // если поставщик вернул иное имя — используем его
            Country: dto.Location.Country ?? string.Empty,
            TempC: dto.Current.TempC,
            Condition: dto.Current.Condition.Text ?? "n/a",
            FetchedAtUtc: DateTime.UtcNow,
            Source: "origin"                              // источник: внешний провайдер
        );

        return report;
    }
}
