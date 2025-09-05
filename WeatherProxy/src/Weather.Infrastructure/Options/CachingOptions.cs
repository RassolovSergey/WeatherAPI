// Назначение: опции кэширования, которые будут биндиться из конфигурации/ENV.
// Маппинг ENV -> секции конфигурации:
//   CACHING__CURRENTTTLMINUTES  -> Configuration["Caching:CurrentTtlMinutes"]
//   CACHING__FORECASTTTLMINUTES -> Configuration["Caching:ForecastTtlMinutes"] (на будущее)

namespace Weather.Infrastructure.Options
{
    public sealed class CachingOptions
    {
        public const string SectionName = "Caching";

        /// <summary>
        /// TTL (в минутах) для ключа "current:{slug}".
        /// Значение по умолчанию — 15 минут.
        /// </summary>
        public int CurrentTtlMinutes { get; init; } = 15;

        /// <summary>
        /// TTL (в минутах) для прогноза (на будущее).
        /// </summary>
        public int ForecastTtlMinutes { get; init; } = 60;
    }
}