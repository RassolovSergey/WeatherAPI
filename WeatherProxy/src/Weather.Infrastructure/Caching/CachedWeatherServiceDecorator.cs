using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Weather.Application;
using Weather.Domain;
using Weather.Infrastructure.Options;

namespace Weather.Infrastructure.Caching;

/// <summary>
/// Декоратор прикладного сервиса, добавляющий кэш Redis (IDistributedCache).
/// Схема ключей внутри приложения:
///   - current : current:{slug}
///   - forecast: forecast:{slug}:{days}
/// Провайдер StackExchangeRedisCache сам добавит InstanceName ("weather:") перед ключом,
/// так что в Redis будут ключи: weather:current:... и weather:forecast:...
/// </summary>
public sealed class CachedWeatherServiceDecorator : IWeatherService
{
    private readonly IWeatherService _inner;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedWeatherServiceDecorator> _logger;
    private readonly IOptions<CachingOptions> _caching;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public CachedWeatherServiceDecorator(
        IWeatherService inner,
        IDistributedCache cache,
        ILogger<CachedWeatherServiceDecorator> logger,
        IOptions<CachingOptions> caching)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
        _caching = caching;
    }

    private static string Slug(string city) => city.Trim().ToLowerInvariant();

    // -------------------------- CURRENT --------------------------

    public async Task<WeatherReport> GetCurrentAsync(string city, CancellationToken ct = default)
    {
        var slug = Slug(city);
        // ВНИМАНИЕ: без "weather:" — его добавит InstanceName.
        var key = $"current:{slug}";

        // 1) Попытка чтения из кэша
        var cachedJson = await _cache.GetStringAsync(key, ct);
        if (!string.IsNullOrEmpty(cachedJson))
        {
            try
            {
                var fromCache = System.Text.Json.JsonSerializer.Deserialize<WeatherReport>(cachedJson, Json);
                if (fromCache is not null)
                {
                    var result = fromCache with { Source = "cache" };
                    _logger.LogDebug("Cache HIT current → {Key}", key);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize cached current weather for {Key}", key);
            }
        }

        _logger.LogDebug("Cache MISS current → {Key}", key);

        // 2) Промах — идём во внешний провайдер
        var fresh = await _inner.GetCurrentAsync(city, ct);

        // 3) Кладём в кэш
        var ttlMinutes = Math.Max(1, _caching.Value.CurrentTtlMinutes);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(ttlMinutes)
        };

        var payload = System.Text.Json.JsonSerializer.Serialize(fresh, Json);
        await _cache.SetStringAsync(key, payload, options, ct);

        return fresh;
    }

    // -------------------------- FORECAST --------------------------

    public async Task<ForecastReport> GetForecastAsync(string city, int days, CancellationToken ct = default)
    {
        var slug = Slug(city);
        // ВНИМАНИЕ: без "weather:" — его добавит InstanceName.
        var key = $"forecast:{slug}:{days}";

        // 1) Попытка чтения из кэша
        var cachedJson = await _cache.GetStringAsync(key, ct);
        if (!string.IsNullOrEmpty(cachedJson))
        {
            try
            {
                var fromCache = System.Text.Json.JsonSerializer.Deserialize<ForecastReport>(cachedJson, Json);
                if (fromCache is not null)
                {
                    var cached = new ForecastReport
                    {
                        City = fromCache.City,
                        Country = fromCache.Country,
                        Days = fromCache.Days,
                        Items = fromCache.Items,
                        FetchedAtUtc = fromCache.FetchedAtUtc,
                        Source = "cache"
                    };

                    _logger.LogDebug("Cache HIT forecast → {Key}", key);
                    return cached;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize cached forecast for {Key}", key);
            }
        }

        _logger.LogDebug("Cache MISS forecast → {Key}", key);

        // 2) Промах — идём к провайдеру
        var fresh = await _inner.GetForecastAsync(city, days, ct);

        // 3) Кладём в кэш
        // TODO: при наличии ForecastTtlMinutes использовать его здесь.
        var ttlMinutes = Math.Max(1, _caching.Value.ForecastTtlMinutes);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(ttlMinutes)
        };

        var payload = System.Text.Json.JsonSerializer.Serialize(fresh, Json);
        await _cache.SetStringAsync(key, payload, options, ct);

        return fresh;
    }
}