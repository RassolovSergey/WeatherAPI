using Weather.Domain;

namespace Weather.Application
{
    /// <summary>
    /// Порт приложения: сценарии работы с погодой,
    /// которые вызывает Web API. Реализации находятся в Infrastructure.
    /// </summary>
    public interface IWeatherService
    {
        /// <summary>
        /// Получить текущую погоду по городу.
        /// Реализация сама решает: кэш, внешний API и т.п.
        /// </summary>
        Task<WeatherReport> GetCurrentAsync(string city, CancellationToken ct = default);
    }
}