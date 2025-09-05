// Назначение: декоратор поверх IWeatherService, реализующий cache-aside с Redis.
// Логика:
//  - Пытаюсь прочитать WeatherReport из кэша по ключу "current:{slug}".
//  - Если попали (cache hit): возвращаю объект с Source="cache".
//  - Если промах: дергаю "inner" (реальный провайдер через WeatherService -> WeatherApiClient),
//                кладу результат в кэш с TTL и возвращаю с Source="origin".
//
// Важно: здесь мы не тащим детали DI — только зависим от IDistributedCache.

using System.Text.Json;                         // сериализация в строку для кэша
using Microsoft.Extensions.Caching.Distributed; // IDistributedCache
using Microsoft.Extensions.Logging; // логирование (полезно при ошибках кэша)
using Weather.Application;  // IWeatherService
using Weather.Domain;   // WeatherReport
using Microsoft.Extensions.Options;
using Weather.Infrastructure.Options;

namespace Weather.Infrastructure.Caching

{
    public sealed class CachedWeatherServiceDecorator : IWeatherService
    {
        private readonly IWeatherService _inner;    // исходная реализация (делегат)
        private readonly IDistributedCache _cache;  // Redis (через StackExchange.Redis)
        private readonly ILogger<CachedWeatherServiceDecorator> _logger; // логирование ошибок кэша
        private readonly TimeSpan _ttlCurrent;  // TTL для текущей погоды

        // TTL для текущей погоды: 15 минут (можно вынести в конфиг позже)
        private static readonly DistributedCacheEntryOptions TtlCurrent =
            new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) };

        public CachedWeatherServiceDecorator(
            IWeatherService inner,
            IDistributedCache cache,
            ILogger<CachedWeatherServiceDecorator> logger,
            IOptions<CachingOptions> caching)
        {
            _inner = inner;
            _cache = cache;
            _logger = logger;
            _ttlCurrent = TimeSpan.FromMinutes(
                Math.Max(1, caching.Value.CurrentTtlMinutes));
        }

        public async Task<WeatherReport> GetCurrentAsync(string city, CancellationToken ct = default)
        {
            // 1) Генерирую ключ
            var key = CacheKeyBuilder.CurrentKey(city);

            try
            {
                // 2) Пытаюсь прочитать JSON из кэша
                var cachedJson = await _cache.GetStringAsync(key, ct);
                if (!string.IsNullOrEmpty(cachedJson))
                {
                    var cached = JsonSerializer.Deserialize<WeatherReport>(cachedJson);
                    if (cached is not null)
                    {
                        // Явно отмечаем источник как "cache"
                        return cached with { Source = "cache" };
                    }
                }
            }
            catch (Exception ex)
            {
                // Ошибку кэша не считаем фатальной — просто логируем и идём к провайдеру.
                _logger.LogWarning(ex, "Redis read failed for key {Key}", key);
            }

            // 3) Промах — дергаем реальную реализацию
            var fresh = await _inner.GetCurrentAsync(city, ct);

            try
            {
                // 4) Кладём в кэш (сохраняем то, что пришло, без смены Source)
                var json = JsonSerializer.Serialize(fresh);
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _ttlCurrent // <-- TTL из конфигурации
                };
                await _cache.SetStringAsync(key, json, TtlCurrent, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis write failed for key {Key}", key);
            }

            return fresh; // здесь Source обычно "origin"
        }
    }
}