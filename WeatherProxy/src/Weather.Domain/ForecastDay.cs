using System;

namespace Weather.Domain;

/// <summary>
/// Прогноз на один календарный день.
/// Без лишних полей — только то, что точно покажем в API.
/// </summary>
public sealed class ForecastDay
{
    /// <summary>Дата прогноза (без времени, в UTC).</summary>
    public DateOnly Date { get; init; }

    /// <summary>Минимальная температура за день, °C.</summary>
    public double MinTempC { get; init; }

    /// <summary>Максимальная температура за день, °C.</summary>
    public double MaxTempC { get; init; }

    /// <summary>Краткое текстовое описание (например, "Sunny").</summary>
    public string Condition { get; init; } = string.Empty;
}
