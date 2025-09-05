// Назначение: тип безопасной конфигурации для внешнего провайдера погоды.
// Этот класс будет заполняться из конфигурации (appsettings/ENV/.env).
// Важно: ключ API НЕ хранится в коде/репозитории — только через переменные окружения.
// В Docker Compose у нас уже есть переменные: WEATHERAPI__BASEURL и WEATHERAPI__APIKEY.
// В .NET двойное подчёркивание в имени переменной ENV маппится на секции конфигурации:
//   WEATHERAPI__BASEURL  -> Configuration["WeatherApi:BaseUrl"]
//   WEATHERAPI__APIKEY   -> Configuration["WeatherApi:ApiKey"]

namespace Weather.Infrastructure.Options
{
    public sealed class WeatherApiOptions
    {
        /// <summary>
        /// Имя секции конфигурации. 
        /// Позже будем связывать так: builder.Configuration.GetSection(SectionName).Bind(...)
        /// или services.Configure&lt;WeatherApiOptions&gt;(Configuration.GetSection(SectionName))
        /// </summary>
        public const string SectionName = "WeatherApi";

        /// <summary>
        /// Базовый URL внешнего API. Пример: "https://api.weatherapi.com/v1/"
        /// Берётся из: Configuration["WeatherApi:BaseUrl"] или ENV WEATHERAPI__BASEURL
        /// </summary>
        public string BaseUrl { get; init; } = string.Empty;

        /// <summary>
        /// Ключ доступа к провайдеру (секрет!). 
        /// Берётся из: Configuration["WeatherApi:ApiKey"] или ENV WEATHERAPI__APIKEY
        /// Никогда не логгируем и не кладём в репозиторий.
        /// </summary>
        public string ApiKey { get; init; } = string.Empty;

        /// <summary>
        /// Таймаут запросов к внешнему API. 
        /// Значение по умолчанию: 8 секунд (разумный старт, можно вынести в конфиг).
        /// </summary>
        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(8);
    }
}