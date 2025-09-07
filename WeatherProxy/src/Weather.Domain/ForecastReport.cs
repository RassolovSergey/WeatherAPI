using System;
using System.Collections.Generic;

namespace Weather.Domain;

/// <summary>
/// Агрегированный прогноз на N дней для города.
/// Это то, что вернёт наш API для /forecast.
/// </summary>
public sealed class ForecastReport
{
    /// <summary>Город, нормализованный для вывода (например, "Helsinki").</summary>
    public string City { get; init; } = string.Empty;

    /// <summary>Страна (если есть в данных провайдера).</summary>
    public string Country { get; init; } = string.Empty;

    /// <summary>Сколько дней прогноза включено (1..7 для нашего API).</summary>
    public int Days { get; init; }

    /// <summary>Список суточных прогнозов.</summary>
    public IReadOnlyList<ForecastDay> Items { get; init; } = Array.Empty<ForecastDay>();

    /// <summary>Когда мы получили эти данные (UTC).</summary>
    public DateTime FetchedAtUtc { get; init; }

    /// <summary>
    /// Источник данных: "origin" — из внешнего API, "cache" — из Redis.
    /// Удобно для тестов и диагностики.
    /// </summary>
    public string Source { get; init; } = "origin";
}
