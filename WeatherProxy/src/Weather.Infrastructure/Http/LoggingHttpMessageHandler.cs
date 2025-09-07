using System.Diagnostics;                    // Stopwatch — измеряем длительность
using System.Text.RegularExpressions;        // Regex — редактирование секретов
using Microsoft.Extensions.Logging;          // ILogger
using System.Net.Http;                       // DelegatingHandler
using Microsoft.AspNetCore.Http;             // IHttpContextAccessor

namespace Weather.Infrastructure.Http;

/// <summary>
/// Делегирующий обработчик для логирования исходящих HTTP-запросов к внешнему провайдеру.
/// - Логируем метод, URL (без секрета), статус и длительность.
/// - Прокидываем X-Correlation-ID из входящего запроса.
/// - НИКОГДА не пишем в логи тело и секреты.
/// </summary>
public sealed class LoggingHttpMessageHandler : DelegatingHandler
{
    private readonly ILogger<LoggingHttpMessageHandler> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    private const string CorrelationHeader = "X-Correlation-ID";
    private const string CorrelationItemKey = "CorrelationId";

    // Редактируем потенциальные секреты в query: ?key=...&access_token=...
    private static readonly Regex SecretParams = new(
        pattern: @"([?&])(key|apikey|token|access_token)=[^&]*",
        options: RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public LoggingHttpMessageHandler(
        ILogger<LoggingHttpMessageHandler> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Пробрасываем X-Correlation-ID из текущего контекста (если есть)
        var ctx = _httpContextAccessor.HttpContext;
        string? cid = null;

        if (ctx is not null)
        {
            if (ctx.Items.TryGetValue(CorrelationItemKey, out var obj) && obj is string s && !string.IsNullOrWhiteSpace(s))
                cid = s;
            else if (ctx.Request.Headers.TryGetValue(CorrelationHeader, out var values) && values.Count > 0)
                cid = values[0]!;
        }

        if (!string.IsNullOrWhiteSpace(cid) && !request.Headers.Contains(CorrelationHeader))
            request.Headers.TryAddWithoutValidation(CorrelationHeader, cid);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            sw.Stop();

            // Собираем «безопасный» URL для логов
            var rawUrl = request.RequestUri?.ToString() ?? "(no uri)";
            var safeUrl = SecretParams.Replace(rawUrl, "$1$2=REDACTED");

            _logger.LogInformation(
                "HTTP {Method} {Url} -> {StatusCode} in {ElapsedMs}ms",
                request.Method.Method,
                safeUrl,
                (int)response.StatusCode,
                sw.ElapsedMilliseconds);

            return response; // <-- НЕ отправляем повторно
        }
        catch (Exception ex)
        {
            sw.Stop();
            var rawUrl = request.RequestUri?.ToString() ?? "(no uri)";
            var safeUrl = SecretParams.Replace(rawUrl, "$1$2=REDACTED");

            _logger.LogWarning(ex,
                "HTTP {Method} {Url} failed after {ElapsedMs}ms",
                request.Method.Method,
                safeUrl,
                sw.ElapsedMilliseconds);

            throw;
        }
    }
}