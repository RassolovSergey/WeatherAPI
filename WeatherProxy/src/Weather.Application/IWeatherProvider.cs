using Weather.Domain;

namespace Weather.Application;

/// <summary>
/// Порт (интерфейс) к внешнему поставщику погоды.
/// Инфраструктурная реализация будет ходить во внешний HTTP-API (weatherapi.com),
/// а доменные/внешние DTO будут маппиться в <see cref="WeatherReport"/>.
/// 
/// ВАЖНО: Application знает ТОЛЬКО контракт, а не детали HTTP/JSON/Redis.
/// Это позволяет подменять реализацию в тестах и не «тащить» инфраструктуру внутрь.
/// </summary>
public interface IWeatherProvider
{
    /// <summary>
    /// Получить текущую погоду по городу из внешнего провайдера.
    /// Реализация должна:
    /// 1) Нормализовать вход (или ожидать нормализованный),
    /// 2) Обратиться к внешнему API,
    /// 3) Спроецировать ответ в <see cref="WeatherReport"/>.
    /// Исключения — только доменно-смысловые (например, "город не найден").
    /// Транспортные/HTTP-ошибки будут обрабатываться на уровне реализации/Polly.
    /// </summary>
    Task<WeatherReport> GetCurrentAsync(string city, CancellationToken ct = default);
}
