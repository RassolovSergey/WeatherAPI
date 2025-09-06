using System.ComponentModel.DataAnnotations;

namespace Weather.Infrastructure.Options;

public sealed class WeatherApiOptions
{
    /// <summary>
    /// Имя секции в конфигурации (appsettings.json / ENV / etc).
    /// </summary>
    public const string SectionName = "WeatherApi";

    /// <summary>
    /// Базовый URL внешнего API, например: "http://api.weatherapi.com/v1/"
    /// </summary>
    [Required, Url]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Ключ API для доступа к внешнему провайдеру (weatherapi.com).
    /// НЕ ХРАНИМ В КОДЕ! Берём из конфигурации/переменных окружения.
    /// </summary>
    [Required]
    public string ApiKey { get; init; } = string.Empty;

    // ВАЛИДНАЯ форма для TimeSpan: указываем тип и границы как строки "hh:mm:ss".
    // Разрешаем 1..60 секунд. По умолчанию 8 секунд.
    /// <summary>
    /// Таймаут для HTTP-клиента при вызове внешнего API.
    /// Значение по умолчанию — 8 секунд.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00",
        ErrorMessage = "Timeout must be between 00:00:01 and 00:01:00.")]
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(8);
}
