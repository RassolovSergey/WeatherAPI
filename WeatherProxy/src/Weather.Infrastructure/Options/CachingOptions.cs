namespace Weather.Infrastructure.Options
{
    /// <summary>
    /// Параметры кэширования.
    /// Биндим секцию "Caching" из конфигурации / переменных окружения.
    /// </summary>
    public sealed class CachingOptions
    {
        public const string SectionName = "Caching";

        /// <summary>TTL (в минутах) для текущей погоды.</summary>
        public int CurrentTtlMinutes { get; init; } = 15;

        /// <summary>TTL (в минутах) для прогноза.</summary>
        public int ForecastTtlMinutes { get; init; } = 180; // по умолчанию 3 часа
    }
}