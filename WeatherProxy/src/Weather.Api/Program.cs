using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Distributed;   // IDistributedCache (реализация регистрируется ниже)
using Microsoft.Extensions.Options;               // IOptions<T> для опций
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Http.Resilience;
using Weather.Application;                        // IWeatherService, IWeatherProvider
using Weather.Infrastructure;                     // WeatherService, WeatherApiClient
using Weather.Infrastructure.Caching;            // CachedWeatherServiceDecorator
using Weather.Infrastructure.Options;            // WeatherApiOptions, CachingOptions
using Microsoft.AspNetCore.Diagnostics;   // ExceptionHandler (ProblemDetails)
using Polly.Timeout;                      // TimeoutRejectedException (Polly v8)
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Http;     // ProblemDetailsService, ProblemDetailsContext
using Microsoft.Extensions.Diagnostics.HealthChecks; // типы для HealthChecks
using Weather.Api.Health;                            // RedisCacheHealthCheck


// Создаем билдер приложения
var builder = WebApplication.CreateBuilder(args);

// --------------------------- DI / Конфигурация ---------------------------

// 1) Биндим опции внешнего провайдера и кэша из конфигурации/ENV.
//    ENV с двойным подчеркиванием маппятся в секции:
//      WEATHERAPI__BASEURL  -> WeatherApi:BaseUrl
//      WEATHERAPI__APIKEY   -> WeatherApi:ApiKey
//      CACHING__CURRENTTTLMINUTES -> Caching:CurrentTtlMinutes
builder.Services.Configure<WeatherApiOptions>(
    builder.Configuration.GetSection(WeatherApiOptions.SectionName));
builder.Services.Configure<CachingOptions>(
    builder.Configuration.GetSection(CachingOptions.SectionName));

// HealthChecks: проверяем доступность Redis через IDistributedCache
builder.Services
    .AddHealthChecks()
    .AddCheck<RedisCacheHealthCheck>("redis", failureStatus: HealthStatus.Unhealthy);

// 2) Redis как IDistributedCache (используем StackExchange.Redis под капотом).
//    Строка берется из: REDIS__CONNECTIONSTRING -> Redis:ConnectionString
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
    options.InstanceName = "weather:"; // префикс для ключей (итоговый ключ: weather:current:{slug})
});

// 3) Swagger (включится в Dev-среде)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Weather Proxy API",
        Version = "v1",
        Description = "Учебный proxy-API: внешний WeatherAPI + Redis-кэш"
    });
});

// 4) Ограничение скорости (60 запросов/мин на IP)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("per-ip-60rpm", cfg =>
    {
        cfg.PermitLimit = 60;
        cfg.Window = TimeSpan.FromMinutes(1);
        cfg.QueueLimit = 0;
        cfg.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

// Провайдер погоды: Typed HttpClient + стандартный набор стратегий (retry/timeout/breaker).
builder.Services.AddHttpClient<IWeatherProvider, WeatherApiClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<WeatherApiOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
        http.BaseAddress = new Uri(opts.BaseUrl);
    // Timeout — таймауты теперь в политике ниже
})
// Подключаем стандартный обработчик устойчивости (Polly v8):
.AddStandardResilienceHandler(o =>
{
    // Попытка запроса (Attempt) — таймаут 5с
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);

    // Общая «крыша» на все повторы — 12с (должно быть > AttemptTimeout)
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(12);

    o.Retry.MaxRetryAttempts = 3;   // повторы с джиттером
    o.CircuitBreaker.FailureRatio = 0.2;    // 20% неудач в окне
    o.CircuitBreaker.MinimumThroughput = 10;    // минимум 10 запросов в окне
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);   // окно 30с
    o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);  // разрыв 30с
});

// 6) IWeatherService c декоратором кэша.
//    ВАЖНО: передаем 4-й аргумент caching (IOptions<CachingOptions>) — это и было причиной ошибки CS7036.
builder.Services.AddScoped<WeatherService>(); // «внутренний» сервис (делегирует в IWeatherProvider)
builder.Services.AddScoped<IWeatherService>(sp =>
{
    var inner = sp.GetRequiredService<WeatherService>();
    var cache = sp.GetRequiredService<IDistributedCache>();
    var logger = sp.GetRequiredService<ILogger<CachedWeatherServiceDecorator>>();
    var caching = sp.GetRequiredService<IOptions<CachingOptions>>();

    return new CachedWeatherServiceDecorator(inner, cache, logger, caching);
});

// --------------------------- HTTP конвейер ---------------------------

// RFC 7807 — единый формат ошибок
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Глобальная обработка исключений → ProblemDetails с корректным статусом
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;

        var status = ex switch
        {
            ArgumentException => StatusCodes.Status400BadRequest,       // неверные входные данные
            HttpRequestException => StatusCodes.Status502BadGateway,       // ошибка апстрима
            TimeoutRejectedException => StatusCodes.Status504GatewayTimeout,   // таймаут Polly
            _ => StatusCodes.Status500InternalServerError
        };

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var pds = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await pds.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = ex
        });
    });
});

app.UseRateLimiter();

// Liveness endpoint для Docker/оркестратора.
// 200 OK — всё ок; 503 — проблемы (например, Redis недоступен).
app.MapHealthChecks("/healthz");

// Группируем маршруты под /api/v1/weather
var weather = app.MapGroup("/api/v1/weather")
    .WithTags("Weather")
    .RequireRateLimiting("per-ip-60rpm");

// Эндпоинт: текущая погода.
// Теперь он вызывает IWeatherService, где стоит кэширующий декоратор:
// сначала читаем Redis, при промахе идем к внешнему провайдеру (WeatherAPI), записываем в кэш.
weather.MapGet("/current", async (string city, IWeatherService svc, CancellationToken ct) =>
{
    // Кидаем исключение → глобальный handler вернёт ProblemDetails (400)
    if (string.IsNullOrWhiteSpace(city) || city.Length > 64)
        throw new ArgumentException("Invalid 'city': non-empty, up to 64 chars.", nameof(city));

    var report = await svc.GetCurrentAsync(city, ct);
    return Results.Ok(report);
})
.WithSummary("Get current weather")
.WithDescription("Реальные данные с провайдера + Redis-кэш (cache-aside).")
.Produces<Weather.Domain.WeatherReport>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest);


app.Run();