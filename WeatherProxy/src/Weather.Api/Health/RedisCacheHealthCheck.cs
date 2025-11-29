// Назначение: простая проверка доступности Redis через IDistributedCache.
// Мы делаем легкий GET несуществующего ключа — этого достаточно, чтобы убедиться,
// что сеть и соединение с Redis живы. При ошибке возвращаем Unhealthy.

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Weather.Api.Health;

public sealed class RedisCacheHealthCheck : IHealthCheck
{
    private readonly IDistributedCache _cache;

    public RedisCacheHealthCheck(IDistributedCache cache) => _cache = cache;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Форсируем roundtrip в Redis.
            await _cache.GetAsync("__health__", cancellationToken);
            return HealthCheckResult.Healthy("Redis reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis unreachable.", ex);
        }
    }
}
