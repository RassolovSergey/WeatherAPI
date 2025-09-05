using Weather.Application;   // IWeatherService, IWeatherProvider — порты приложения
using Weather.Domain;        // WeatherReport — доменная модель

namespace Weather.Infrastructure;

/// <summary>
/// Оркестратор сценария "получить текущую погоду".
/// Сейчас просто делегирует в провайдера (HTTP к weatherapi.com).
/// На следующем шаге сюда же добавим кэширование (Redis) как декоратор.
/// </summary>
public sealed class WeatherService : IWeatherService
{
    private readonly IWeatherProvider _provider;

    /// <summary>
    /// Внедряем провайдера через DI. Реализация провайдера — WeatherApiClient (Typed HttpClient).
    /// </summary>
    public WeatherService(IWeatherProvider provider)
        => _provider = provider;

    /// <summary>
    /// Получить текущую погоду по городу.
    /// Сервис отвечает за общую оркестрацию (нормализация/валидация),
    /// а провайдер — за внешний HTTP и маппинг в доменную модель.
    /// </summary>
    public async Task<WeatherReport> GetCurrentAsync(string city, CancellationToken ct = default)
    {
        // Базовая нормализация + защитная проверка (подробную валидацию добавим позже)
        var normalized = (city ?? string.Empty).Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("City must be provided.", nameof(city));

        // Делегируем вызов во внешний провайдер (weatherapi.com)
        var report = await _provider.GetCurrentAsync(normalized, ct);

        // На всякий случай гарантируем заполненность Source (если провайдер её не проставит)
        if (string.IsNullOrWhiteSpace(report.Source))
            report = report with { Source = "origin" };

        return report;
    }
}
