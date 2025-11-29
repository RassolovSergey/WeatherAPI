using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Weather.Api.Middleware;

/// <summary>
/// Гарантирует наличие X-Correlation-ID:
/// - читает из запроса, либо генерирует новый GUID без тире;
/// - пишет в HttpContext.Items, лог-скоуп и в заголовок ответа.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var id = GetOrCreateId(context);

        // Лог-скоуп: все логи дальше будут со свойством CorrelationId
        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            context.Response.OnStarting(() =>
            {
                // безопаснее ставить через индексатор — не бросит исключение при дубликате
                context.Response.Headers[HeaderName] = id;
                return Task.CompletedTask;
            });

            await _next(context);
        }
    }

    private static string GetOrCreateId(HttpContext ctx)
    {
        if (ctx.Items.TryGetValue(ItemKey, out var existing) && existing is string s && !string.IsNullOrWhiteSpace(s))
            return s;

        string id = ctx.Request.Headers.TryGetValue(HeaderName, out var values) && values.Count > 0
            ? values[0]!
            : Guid.NewGuid().ToString("N"); // 32 символа, без тире

        ctx.Items[ItemKey] = id;
        return id;
    }
}