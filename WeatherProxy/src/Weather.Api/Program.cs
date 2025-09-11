using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Distributed;          // IDistributedCache
using Microsoft.Extensions.Options;                      // IOptions<T>
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Http.Resilience;              // Polly v8 handler
using Microsoft.AspNetCore.Http;                         // StatusCodes
using Microsoft.AspNetCore.Diagnostics;                  // IExceptionHandlerFeature
using Microsoft.AspNetCore.Mvc.Infrastructure;           // IProblemDetailsService, ProblemDetailsContext
using Microsoft.Extensions.Diagnostics.HealthChecks;     // HealthStatus
using Polly.Timeout;                                     // TimeoutRejectedException
using StackExchange.Redis;                               // IConnectionMultiplexer

using Weather.Application;                               // IWeatherService, IWeatherProvider
using Weather.Infrastructure;                            // WeatherService, WeatherApiClient
using Weather.Infrastructure.Caching;                    // CachedWeatherServiceDecorator
using Weather.Infrastructure.Options;                    // WeatherApiOptions, CachingOptions
using Weather.Infrastructure.Http;                       // LoggingHttpMessageHandler
using Weather.Api.Health;                                // RedisCacheHealthCheck
using Weather.Api.Middleware;
using Prometheus;                            // CorrelationIdMiddleware

var builder = WebApplication.CreateBuilder(args);

// =========================== Конфигурация/Options ===========================

builder.Services.AddOptions<WeatherApiOptions>()
    .Bind(builder.Configuration.GetSection(WeatherApiOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
        "WeatherApi:BaseUrl must be an absolute URL.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey),
        "WeatherApi:ApiKey is required.")
    .ValidateOnStart();

builder.Services.AddOptions<CachingOptions>()
    .Bind(builder.Configuration.GetSection(CachingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Единожды читаем строку подключения Redis и валидируем — это убирает CS8604
var redisConnStr = builder.Configuration["Redis:ConnectionString"];
if (string.IsNullOrWhiteSpace(redisConnStr))
    throw new InvalidOperationException(
        "Missing Redis:ConnectionString (env REDIS__CONNECTIONSTRING).");

// ============================== HealthChecks ================================

builder.Services
    .AddHealthChecks()
    .AddCheck<RedisCacheHealthCheck>("redis", failureStatus: HealthStatus.Unhealthy);

// ============================== Redis cache =================================

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnStr;  // уже проверено, не null
    options.InstanceName = "weather:";     // префикс ключей
});

// Прямой доступ к Redis (для dev-инструментов, health и т.п.)
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnStr));        // уже проверено, не null


// ============================== Swagger =====================================

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

// ============================ Rate Limiting =================================

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

// ====================== Outgoing HTTP (provider) ============================

builder.Services.AddTransient<LoggingHttpMessageHandler>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddHttpClient<IWeatherProvider, WeatherApiClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<WeatherApiOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
        http.BaseAddress = new Uri(opts.BaseUrl);
})
.AddHttpMessageHandler<LoggingHttpMessageHandler>()
.AddStandardResilienceHandler(o =>
{
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(12);
    o.Retry.MaxRetryAttempts = 3;
    o.CircuitBreaker.FailureRatio = 0.2;
    o.CircuitBreaker.MinimumThroughput = 10;
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
});

// =============================== Services ===================================

builder.Services.AddScoped<WeatherService>(); // «внутренний» сервис
builder.Services.AddScoped<IWeatherService>(sp =>
{
    var inner = sp.GetRequiredService<WeatherService>();
    var cache = sp.GetRequiredService<IDistributedCache>();
    var logger = sp.GetRequiredService<ILogger<CachedWeatherServiceDecorator>>();
    var caching = sp.GetRequiredService<IOptions<CachingOptions>>();
    return new CachedWeatherServiceDecorator(inner, cache, logger, caching);
});

// ============================ ProblemDetails =================================

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
});

// ================================ App =======================================

var app = builder.Build();

// Метрики HTTP и эндпоинт для Prometheus
app.UseHttpMetrics();         // собирает метрики по входящим HTTP
app.MapMetrics("/metrics");   // отдать метрики здесь


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Глобальная обработка исключений → ProblemDetails
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;

        var status = ex switch
        {
            ArgumentException => StatusCodes.Status400BadRequest,
            HttpRequestException => StatusCodes.Status502BadGateway,
            TimeoutRejectedException => StatusCodes.Status504GatewayTimeout,
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

// Корреляция запросов
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseRateLimiter();

app.MapHealthChecks("/healthz");

// ----------------------------- Weather API ----------------------------------

var weather = app.MapGroup("/api/v1/weather")
    .WithTags("Weather")
    .RequireRateLimiting("per-ip-60rpm");
// GET /api/v1/weather/forecast
weather.MapGet("/forecast", async (string city, int days, IWeatherService svc, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(city) || city.Length > 64)
        return Results.BadRequest(new { error = "Invalid 'city'." });
    if (days < 1 || days > 7)
        return Results.BadRequest(new { error = "Invalid 'days' (1..7)." });

    var report = await svc.GetForecastAsync(city, days, ct);
    return Results.Ok(report);
})
.WithSummary("Get multi-day forecast")
.WithDescription("Реальные данные прогноза с провайдера. Кэш добавим отдельно.")
.Produces<Weather.Domain.ForecastReport>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest);

// GET /api/v1/weather/current
weather.MapGet("/current", async (string city, IWeatherService svc, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(city) || city.Length > 64)
        throw new ArgumentException("Invalid 'city': non-empty, up to 64 chars.", nameof(city));

    var report = await svc.GetCurrentAsync(city, ct);
    return Results.Ok(report);
})
.WithSummary("Get current weather")
.WithDescription("Реальные данные с провайдера + Redis-кэш (cache-aside).")
.Produces<Weather.Domain.WeatherReport>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest);

// ------------------------------- Dev cache ----------------------------------

if (app.Environment.IsDevelopment())
{
    var dev = app.MapGroup("/api/v1/dev/cache").WithTags("Dev/Cache");

    static string Slug(string city) => city.Trim().ToLowerInvariant();

    dev.MapDelete("/current", async (string city, IConnectionMultiplexer mux, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(city) || city.Length > 64)
            return Results.BadRequest(new { error = "Invalid 'city'." });

        var key = $"weather:current:{Slug(city)}";
        var db = mux.GetDatabase();
        bool removed = await db.KeyDeleteAsync(key);
        return Results.Ok(new { removed, key });
    })
    .WithSummary("Invalidate current-weather cache")
    .WithDescription("Удаляет ключ weather:current:{city}");

    dev.MapDelete("/forecast", async (string city, int days, IConnectionMultiplexer mux, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(city) || city.Length > 64)
            return Results.BadRequest(new { error = "Invalid 'city'." });
        if (days < 1 || days > 7)
            return Results.BadRequest(new { error = "Invalid 'days' (1..7)." });

        var key = $"weather:forecast:{Slug(city)}:{days}";
        var db = mux.GetDatabase();
        bool removed = await db.KeyDeleteAsync(key);
        return Results.Ok(new { removed, key });
    })
    .WithSummary("Invalidate forecast cache")
    .WithDescription("Удаляет ключ weather:forecast:{city}:{days}");
}

app.Run();