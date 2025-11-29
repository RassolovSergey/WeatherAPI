using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Cors.Infrastructure;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly.Timeout;
using StackExchange.Redis;
using Weather.Application;
using Weather.Infrastructure;
using Weather.Infrastructure.Caching;
using Weather.Infrastructure.Options;
using Weather.Infrastructure.Http;
using Weather.Api.Health;
using Weather.Api.Middleware;
using Prometheus;
using Prometheus.DotNetRuntime;
using System.IO;

var builder = WebApplication.CreateBuilder(args);
_ = DotNetRuntimeStatsBuilder.Default().StartCollecting();

// --------------------------- Load .env (dev helper) --------------------------
// Если ключи заданы в .env (рядом с решением) и не проброшены в переменные окружения,
// подтягиваем их вручную, не затирая уже установленные значения.
var envFile = Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".env");
if (File.Exists(envFile))
{
    foreach (var line in File.ReadAllLines(envFile))
    {
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
            continue;
        var idx = line.IndexOf('=');
        if (idx <= 0) continue;
        var key = line[..idx].Trim();
        var value = line[(idx + 1)..].Trim();
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) continue;
        var current = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(current))
            Environment.SetEnvironmentVariable(key, value);
    }
}

// После подстановки из .env добавляем провайдер окружения, чтобы конфиг увидел новые значения
builder.Configuration.AddEnvironmentVariables();

var configuredMaxForecastDays =
    builder.Configuration.GetValue<int?>($"{WeatherApiOptions.SectionName}:MaxForecastDays")
    ?? WeatherApiOptions.DefaultMaxForecastDays;

// Проверка
// Читаем CSV со списком origin'ов из переменных окружения (.env)
var originsCsv = builder.Configuration["CORS:ALLOWEDORIGINS"] ?? string.Empty;
var origins = originsCsv
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
var allowLoopbackInDev = builder.Environment.IsDevelopment();

// Регистрируем CORS-политику "Default"
builder.Services.AddCors(o =>
{
    o.AddPolicy("Default", p =>
    {
        if (origins.Count > 0)
        {
            p.SetIsOriginAllowed(origin =>
            {
                // Dev-хелпер: разрешаем любой loopback (любой порт), даже если не перечислен в .env
                if (allowLoopbackInDev &&
                    Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                    uri.IsLoopback)
                {
                    return true;
                }

                return origins.Contains(origin);
            })
             .AllowAnyHeader()  // Разрешаем любые заголовки
             .AllowAnyMethod(); // Разрешаем любые методы
        }
        else
        {
            // Dev-фоллбек: если список пуст, открываем всем (не делай так в проде)
            p.AllowAnyOrigin()
             .AllowAnyHeader()
             .AllowAnyMethod();
        }
    });
});

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
    http.Timeout = opts.Timeout;
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

// Включаем CORS в пайплайне ДО объявления маршрутов
app.UseCors("Default"); // CORS-проверки + ответы на preflight (OPTIONS)


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

        // Спец-случай: провайдер отклонил API-ключ (401/403 от WeatherAPI)
        if (ex is InvalidOperationException inv &&
            inv.Message.StartsWith("WeatherAPI responded 401/403", StringComparison.OrdinalIgnoreCase))
        {
            await Results.Problem(
                title: "Weather provider rejected API key",
                detail: "Upstream returned 401/403. Check WEATHERAPI__APIKEY value.",
                statusCode: StatusCodes.Status502BadGateway
            ).ExecuteAsync(context);
            return;
        }

        // Базовые маппинги (оставляем как у тебя)
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
app.UseHttpMetrics();

app.MapHealthChecks("/healthz");
app.MapMetrics();

// ----------------------------- Weather API ----------------------------------

var weather = app.MapGroup("/api/v1/weather")
    .WithTags("Weather")
    .RequireRateLimiting("per-ip-60rpm");
// GET /api/v1/weather/forecast
weather.MapGet("/forecast", async (string city, int days, IWeatherService svc, IOptions<WeatherApiOptions> weatherApiOptions, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(city) || city.Length > 64)
        return Results.BadRequest(new { error = "Invalid 'city'." });

    var maxDays = weatherApiOptions.Value.MaxForecastDays;
    if (days < 1 || days > maxDays)
        return Results.BadRequest(new { error = $"Invalid 'days' (1..{maxDays} for current provider plan)." });

    var report = await svc.GetForecastAsync(city, days, ct);
    return Results.Ok(report);
})
.WithSummary("Get multi-day forecast")
.WithDescription($"Реальные данные прогноза с провайдера + Redis-кэш (cache-aside). Лимит текущего плана провайдера — до {configuredMaxForecastDays} дней.")
.Produces<Weather.Domain.ForecastReport>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.WithOpenApi(op =>
{
    op.Parameters[0].Description = "Город (UTF-8). Будет нормализован до slug для кэша.";
    op.Parameters[1].Description = $"Сколько дней прогноза (1..{configuredMaxForecastDays} для текущего плана провайдера).";
    return op;
});

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
.Produces(StatusCodes.Status400BadRequest)
.WithOpenApi(op =>
{
    op.Parameters[0].Description = "Город (UTF-8). Будет нормализован до slug для кэша.";
    return op;
});

// ------------------------------- Dev cache ----------------------------------

if (app.Environment.IsDevelopment())
{
    var dev = app.MapGroup("/api/v1/dev/cache").WithTags("Dev/Cache");

    dev.MapDelete("/current", async (string city, IConnectionMultiplexer mux, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(city) || city.Length > 64)
            return Results.BadRequest(new { error = "Invalid 'city'." });

        var key = $"weather:{CacheKeyBuilder.CurrentKey(city)}";
        var db = mux.GetDatabase();
        bool removed = await db.KeyDeleteAsync(key);
        return Results.Ok(new { removed, key });
    })
    .WithSummary("Invalidate current-weather cache")
    .WithDescription("Удаляет ключ weather:current:{city}")
    .WithOpenApi(op =>
    {
        op.Parameters[0].Description = "Город (UTF-8). Нормализуется до slug так же, как и при кэшировании.";
        return op;
    });

    dev.MapDelete("/forecast", async (string city, int days, IConnectionMultiplexer mux, IOptions<WeatherApiOptions> weatherApiOptions, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(city) || city.Length > 64)
            return Results.BadRequest(new { error = "Invalid 'city'." });

        var maxDays = weatherApiOptions.Value.MaxForecastDays;
        if (days < 1 || days > maxDays)
            return Results.BadRequest(new { error = $"Invalid 'days' (1..{maxDays} for current provider plan)." });

        var key = $"weather:{CacheKeyBuilder.ForecastKey(city, days)}";
        var db = mux.GetDatabase();
        bool removed = await db.KeyDeleteAsync(key);
        return Results.Ok(new { removed, key });
    })
    .WithSummary("Invalidate forecast cache")
    .WithDescription("Удаляет ключ weather:forecast:{city}:{days}")
    .WithOpenApi(op =>
    {
        op.Parameters[0].Description = "Город (UTF-8). Нормализуется до slug так же, как и при кэшировании.";
        op.Parameters[1].Description = $"Сколько дней прогноза (1..{configuredMaxForecastDays} для текущего плана провайдера) — часть ключа.";
        return op;
    });
}

app.Run();
