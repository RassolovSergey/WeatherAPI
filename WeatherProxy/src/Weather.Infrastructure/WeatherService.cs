using System.Threading;
using System.Threading.Tasks;
using Weather.Application;
using Weather.Domain;

namespace Weather.Infrastructure;

/// <summary>
/// Базовая реализация прикладного сервиса.
/// Делегирует в провайдера. Поверх него может навешиваться декоратор с кэшем.
/// </summary>
public sealed class WeatherService : IWeatherService
{
    private readonly IWeatherProvider _provider;

    public WeatherService(IWeatherProvider provider) => _provider = provider;

    /// <inheritdoc />
    public Task<WeatherReport> GetCurrentAsync(string city, CancellationToken ct = default)
        => _provider.GetCurrentAsync(city, ct);

    /// <inheritdoc />
    public Task<ForecastReport> GetForecastAsync(string city, int days, CancellationToken ct = default)
        => _provider.GetForecastAsync(city, days, ct);
}
