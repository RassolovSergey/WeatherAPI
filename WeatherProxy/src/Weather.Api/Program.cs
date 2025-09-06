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

using Weather.Application;                               // IWeatherService, IWeatherProvider
using Weather.Infrastructure;                            // WeatherService, WeatherApiClient
using Weather.Infrastructure.Caching;                    // CachedWeatherServiceDecorator
using Weather.Infrastructure.Options;                    // WeatherApiOptions, CachingOptions
using Weather.Infrastructure.Http;                       // LoggingHttpMessageHandler
using Weather.Api.Health;                                // RedisCacheHealthCheck

// Создаем билдер приложения
var builder = WebApplication.CreateBuilder(args);

// --------------------------- DI / Конфигурация ---------------------------

// Валидация опций на старте (fail-fast)
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

// HealthChecks: пингуем Redis через IDistributedCache
builder.Services
    .AddHealthChecks()
    .AddCheck<RedisCacheHealthCheck>("redis", failureStatus: HealthStatus.Unhealthy);

// Redis как IDistributedCache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
    options.InstanceName = "weather:"; // префикс ключей
});

// Swagger
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

// Rate limiting (60 rpm per IP)
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

// Обработчик логирования исходящих HTTP-запросов
builder.Services.AddTransient<LoggingHttpMessageHandler>();

// Typed HttpClient для внешнего провайдера + логирование + Polly
builder.Services.AddHttpClient<IWeatherProvider, WeatherApiClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<WeatherApiOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
        http.BaseAddress = new Uri(opts.BaseUrl);
})
.AddHttpMessageHandler<LoggingHttpMessageHandler>()            // логируем каждый реальный запрос
.AddStandardResilienceHandler(o =>
{
    // Попытка (per-try) и общий таймаут — Total > Attempt
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(12);

    o.Retry.MaxRetryAttempts = 3;               // повторы с джиттером
    o.CircuitBreaker.FailureRatio = 0.2;
    o.CircuitBreaker.MinimumThroughput = 10;
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
});

// Декоратор с кэшем для IWeatherService
builder.Services.AddScoped<WeatherService>(); // «внутренний» сервис
builder.Services.AddScoped<IWeatherService>(sp =>
{
    var inner = sp.GetRequiredService<WeatherService>();
    var cache = sp.GetRequiredService<IDistributedCache>();
    var logger = sp.GetRequiredService<ILogger<CachedWeatherServiceDecorator>>();
    var caching = sp.GetRequiredService<IOptions<CachingOptions>>();
    return new CachedWeatherServiceDecorator(inner, cache, logger, caching);
});

// RFC 7807 — единый формат ошибок
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
});

// --------------------------- HTTP конвейер ---------------------------

var app = builder.Build();

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

app.UseRateLimiter();

// Liveness endpoint
app.MapHealthChecks("/healthz");

// Маршруты API
var weather = app.MapGroup("/api/v1/weather")
    .WithTags("Weather")
    .RequireRateLimiting("per-ip-60rpm");

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

app.Run();