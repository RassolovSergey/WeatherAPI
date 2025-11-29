using System;
using System.Text.Json;                                   // <— десериализация ForecastReport
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;               // in-memory IDistributedCache
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Weather.Application;                                // IWeatherService
using Weather.Domain;                                     // WeatherReport, ForecastReport
using Weather.Infrastructure.Caching;                    // CachedWeatherServiceDecorator
using Weather.Infrastructure.Options;                    // CachingOptions
using Xunit;

namespace Weather.Tests;

/// <summary>
/// Юнит-тесты кэширующего декоратора (cache-aside).
/// </summary>
public class CachedWeatherServiceDecoratorTests
{
    private sealed class FakeWeatherService : IWeatherService
    {
        public int CurrentCalls { get; private set; }
        public int ForecastCalls { get; private set; }

        public Task<WeatherReport> GetCurrentAsync(string city, CancellationToken ct = default)
        {
            CurrentCalls++;
            var report = new WeatherReport(
                City: city,
                Country: "XX",
                TempC: 10.0,
                Condition: "Sunny",
                FetchedAtUtc: DateTime.UtcNow,
                Source: "origin"
            );
            return Task.FromResult(report);
        }

        public Task<ForecastReport> GetForecastAsync(string city, int days, CancellationToken ct = default)
        {
            ForecastCalls++;

            // ⚠️ Не знаем точные имена внутреннего типа элементов прогноза и сигнатуру конструктора.
            // Создаём объект через JSON — это обойдёт жёсткую привязку к типам.
            var json = $@"
{{
  ""City"": ""{city}"",
  ""Country"": ""XX"",
  ""Days"": {days},
  ""Items"": [
    {{
      ""Date"": ""{DateTime.UtcNow.Date:yyyy-MM-dd}"",
      ""MinTempC"": 5.0,
      ""MaxTempC"": 10.0,
      ""Condition"": ""Sunny""
    }}
  ],
  ""FetchedAtUtc"": ""{DateTime.UtcNow:o}"",
  ""Source"": ""origin""
}}";

            var report = JsonSerializer.Deserialize<ForecastReport>(json)
                         ?? throw new InvalidOperationException("Failed to build ForecastReport for test.");

            return Task.FromResult(report);
        }
    }

    [Fact]
    public async Task GetCurrentAsync_should_return_from_cache_on_second_call()
    {
        // Arrange
        var inner = new FakeWeatherService();
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug))
                                   .CreateLogger<CachedWeatherServiceDecorator>();
        var options = Options.Create(new CachingOptions { CurrentTtlMinutes = 15, ForecastTtlMinutes = 180 });
        var sut = new CachedWeatherServiceDecorator(inner, cache, logger, options);

        // Act
        var first = await sut.GetCurrentAsync("Helsinki");
        var second = await sut.GetCurrentAsync("Helsinki");

        // Assert
        first.Source.Should().Be("origin");
        second.Source.Should().Be("cache");
        inner.CurrentCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetForecastAsync_should_return_from_cache_on_second_call()
    {
        // Arrange
        var inner = new FakeWeatherService();
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug))
                                   .CreateLogger<CachedWeatherServiceDecorator>();
        var options = Options.Create(new CachingOptions { CurrentTtlMinutes = 15, ForecastTtlMinutes = 180 });
        var sut = new CachedWeatherServiceDecorator(inner, cache, logger, options);

        // Act
        var first = await sut.GetForecastAsync("Helsinki", 3);
        var second = await sut.GetForecastAsync("Helsinki", 3);

        // Assert
        first.Source.Should().Be("origin");
        second.Source.Should().Be("cache");
        inner.ForecastCalls.Should().Be(1);
    }
}
