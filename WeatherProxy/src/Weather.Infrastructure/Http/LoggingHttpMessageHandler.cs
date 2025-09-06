using System.Diagnostics;                    // Stopwatch — измеряем длительность
using System.Text.RegularExpressions;        // Regex — для редактирования секрета в query
using Microsoft.Extensions.Logging;          // ILogger
using System.Net.Http;                       // DelegatingHandler

namespace Weather.Infrastructure.Http
{
    /// <summary>
    /// Делегирующий обработчик для логирования исходящих HTTP-запросов к внешнему провайдеру.
    /// - Логируем метод, URL (без секрета), статус и длительность.
    /// - НИКОГДА не пишем в логи тело запроса/ответа и ключ API.
    /// </summary>
    public sealed class LoggingHttpMessageHandler : DelegatingHandler
    {
        private readonly ILogger<LoggingHttpMessageHandler> _logger;
        
        // Редактируем потенциальные секреты в query: ?key=...&access_token=...
        private static readonly Regex SecretParams = new(
            pattern: @"([?&])(key|apikey|token|access_token)=[^&]*",
            options: RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public LoggingHttpMessageHandler(ILogger<LoggingHttpMessageHandler> logger)
            => _logger = logger;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
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

                return response;
            }
            catch (Exception ex)
            {
                sw.Stop();
                var rawUrl = request.RequestUri?.ToString() ?? "(no uri)";
                var safeUrl = SecretParams.Replace(rawUrl, "$1$2=REDACTED");

                // Ошибки сети/таймауты — пишем как Warning, чтобы не зашумлять Error-лог
                _logger.LogWarning(ex,
                    "HTTP {Method} {Url} failed after {ElapsedMs}ms",
                    request.Method.Method,
                    safeUrl,
                    sw.ElapsedMilliseconds);

                throw;
            }
        }
    }
}