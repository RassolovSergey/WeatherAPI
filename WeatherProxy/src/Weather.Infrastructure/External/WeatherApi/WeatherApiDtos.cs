// Внутренние DTO для парсинга ответа weatherapi.com /v1/current.json
// Хранятся отдельно, чтобы клиент был короче и читабельнее.
// Эти типы — чисто инфраструктурные: их не «поднимаем» в Application/Domain.

using System.Text.Json.Serialization;

namespace Weather.Infrastructure.External.WeatherApi;

/// <summary>
/// Корневой объект ответа current.json
/// </summary>
internal sealed class WeatherApiCurrentDto
{
    [JsonPropertyName("location")]
    public LocationDto? Location { get; set; }

    [JsonPropertyName("current")]
    public CurrentDto? Current { get; set; }
}

/// <summary>
/// Блок с локацией (город/страна)
/// </summary>
internal sealed class LocationDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }
}

/// <summary>
/// Блок с текущей погодой
/// </summary>
internal sealed class CurrentDto
{
    [JsonPropertyName("temp_c")]
    public double TempC { get; set; }

    [JsonPropertyName("condition")]
    public ConditionDto? Condition { get; set; }
}

/// <summary>
/// Вложенный объект с текстовым описанием погоды
/// </summary>
internal sealed class ConditionDto
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
