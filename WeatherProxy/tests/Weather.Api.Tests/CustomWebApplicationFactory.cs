#nullable enable
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Weather.Application;   // IWeatherProvider
using Weather.Domain;       // WeatherReport, ForecastReport

namespace Weather.Api.Tests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // 1) Подменяем конфигурацию ДО старта хоста (чтобы прошла проверка Redis).
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:ConnectionString"] = "localhost:6379", // фиктивное значение для проверки в Program.cs
                ["WeatherApi:BaseUrl"] = "https://example.invalid/",
                ["WeatherApi:ApiKey"] = "test-key"
                // при необходимости можно добавить таймауты/TTL и пр.
            });
        });

        // 2) Подменяем инфраструктуру на in-memory и фейковый провайдер.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache>(
                _ => new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()))
            );

            services.RemoveAll<IWeatherProvider>();
            services.AddSingleton<IWeatherProvider, FakeWeatherProvider>();
        });
    }
    private sealed class FakeWeatherProvider : IWeatherProvider
    {
        public Task<WeatherReport> GetCurrentAsync(string city, CancellationToken ct = default)
        {
            var report = new WeatherReport(
                City: city,
                Country: "Testland",
                TempC: 12.3,
                Condition: "Sunny",
                FetchedAtUtc: DateTime.UtcNow,
                Source: "origin"
            );
            return Task.FromResult(report);
        }

        public Task<ForecastReport> GetForecastAsync(string city, int days, CancellationToken ct = default)
        {
            var start = DateTime.UtcNow.Date;
            var itemsJson = string.Join(",", Enumerable.Range(0, days).Select(i => $@"
            {{
            ""Date"": ""{start.AddDays(i):yyyy-MM-dd}"",
            ""MinTempC"": 10.0,
            ""MaxTempC"": 15.0,
            ""Condition"": ""Sunny""
            }}"));

                        var json = $@"
            {{
            ""City"": ""{city}"",
            ""Country"": ""Testland"",
            ""Days"": {days},
            ""Items"": [{itemsJson}],
            ""FetchedAtUtc"": ""{DateTime.UtcNow:o}"",
            ""Source"": ""origin""
            }}";

            var report = JsonSerializer.Deserialize<ForecastReport>(json)
                         ?? throw new InvalidOperationException("Failed to build ForecastReport for test.");
            return Task.FromResult(report);
        }
    }
}
