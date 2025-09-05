namespace Weather.Domain
{
    /// <summary>
    /// Доменная модель «текущая погода». 
    /// Не зависит от инфраструктуры и внешних DTO.
    /// </summary>
    /// <param name="City">Город, нормализованный (например, "Perm")</param>
    /// <param name="Country">Страна (может быть пусто на заглушке)</param>
    /// <param name="TempC">Температура в °C</param>
    /// <param name="Condition">Краткое описание (Sunny / Cloudy / etc.)</param>
    /// <param name="FetchedAtUtc">Когда данные были получены/сформированы</param>
    /// <param name="Source">Откуда взяли данные: "stub" | "origin" | "cache"</param>
    /// <remarks>record — неизменяемый тип с поддержкой сравнения по значению</remarks>
    public sealed record WeatherReport(
        string City,
        string Country,
        double TempC,
        string Condition,
        DateTime FetchedAtUtc,
        string Source
    );
}
