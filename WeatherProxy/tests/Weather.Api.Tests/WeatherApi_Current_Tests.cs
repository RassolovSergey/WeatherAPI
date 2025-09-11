using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Weather.Domain;
using Xunit;

namespace Weather.Api.Tests;

public class WeatherApi_Current_Tests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public WeatherApi_Current_Tests(CustomWebApplicationFactory factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task Current_should_return_origin_then_cache()
    {
        // 1-й запрос — источник origin
        var r1 = await _client.GetAsync("/api/v1/weather/current?city=Helsinki");
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = await r1.Content.ReadFromJsonAsync<WeatherReport>();
        first!.Source.Should().Be("origin");

        // 2-й запрос — ответ из кэша
        var r2 = await _client.GetAsync("/api/v1/weather/current?city=Helsinki");
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await r2.Content.ReadFromJsonAsync<WeatherReport>();
        second!.Source.Should().Be("cache");
    }
}
