using System.Threading;
using System.Threading.Tasks;
using Weather.Domain;

namespace Weather.Application;

/// <summary>
/// Порт (интерфейс) доступа к внешнему провайдеру погоды.
/// Реализация — в инфраструктуре (WeatherApiClient).
/// </summary>
public interface IWeatherProvider
{
    /// <summary>Текущая погода для города.</summary>
    Task<WeatherReport> GetCurrentAsync(string city, CancellationToken ct = default);

    /// <summary>Прогноз на N дней для города.</summary>
    Task<ForecastReport> GetForecastAsync(string city, int days, CancellationToken ct = default);
}
