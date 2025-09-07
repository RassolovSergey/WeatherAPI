using System.Threading;
using System.Threading.Tasks;
using Weather.Domain;

namespace Weather.Application;

/// <summary>
/// Прикладной сервис — то, что дергает наш API.
/// Внутри может быть кэш-декоратор и пр.
/// </summary>
public interface IWeatherService
{
    /// <summary>Текущая погода.</summary>
    Task<WeatherReport> GetCurrentAsync(string city, CancellationToken ct = default);

    /// <summary>Прогноз на N дней.</summary>
    Task<ForecastReport> GetForecastAsync(string city, int days, CancellationToken ct = default);
}
