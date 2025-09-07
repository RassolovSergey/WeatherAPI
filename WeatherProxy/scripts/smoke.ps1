<# 
  Лёгкий smoke-тест для WeatherProxy в docker-compose.
  Проверяет:
   - /healthz = 200
   - инвалидацию кэша (dev endpoints)
   - origin → cache для current и forecast
   - TTL ключей в Redis
#>

param(
    [string]$City = "Helsinki",
    [int]$Days = 3,
    [string]$BaseUrl = "http://127.0.0.1:8080"
)

function Invoke-Json([string]$url, [string]$method = "GET") {
    try {
        if ($method -eq "DELETE") {
            return Invoke-RestMethod -Method DELETE -Uri $url -SkipHttpErrorCheck
        }
        else {
            return Invoke-RestMethod -Method GET -Uri $url -SkipHttpErrorCheck
        }
    }
    catch {
        throw "HTTP error for $method ${url}: $($_.Exception.Message)"
    }
}

function Slug([string]$c) { $c.Trim().ToLowerInvariant() }

Write-Host "== healthz ==" -ForegroundColor Cyan
$h = Invoke-WebRequest "${BaseUrl}/healthz" -SkipHttpErrorCheck
if ($h.StatusCode -ne 200) { throw "Healthz failed: $($h.StatusCode)" }
Write-Host "healthz: 200 OK"

# Инвалидация кэша через dev endpoints
Write-Host "== invalidate cache ==" -ForegroundColor Cyan
$null = Invoke-Json "${BaseUrl}/api/v1/dev/cache/current?city=${City}" "DELETE"
$null = Invoke-Json "${BaseUrl}/api/v1/dev/cache/forecast?city=${City}&days=${Days}" "DELETE"
Write-Host "dev cache cleared for ${City} / ${Days}"

# 1) current: origin → cache
Write-Host "== current ==" -ForegroundColor Cyan
$current1 = Invoke-Json "${BaseUrl}/api/v1/weather/current?city=${City}"
$current2 = Invoke-Json "${BaseUrl}/api/v1/weather/current?city=${City}"

$current1 | ConvertTo-Json -Depth 6
$current2 | ConvertTo-Json -Depth 6

if ($current1.source -ne "origin") { throw "current[1] expected 'origin', got '$($current1.source)'" }
if ($current2.source -ne "cache") { throw "current[2] expected 'cache', got '$($current2.source)'" }
Write-Host "current: origin → cache ✅" -ForegroundColor Green

# 2) forecast: origin → cache
Write-Host "== forecast ==" -ForegroundColor Cyan
$fc1 = Invoke-Json "${BaseUrl}/api/v1/weather/forecast?city=${City}&days=${Days}"
$fc2 = Invoke-Json "${BaseUrl}/api/v1/weather/forecast?city=${City}&days=${Days}"

$fc1 | ConvertTo-Json -Depth 6
$fc2 | ConvertTo-Json -Depth 6

if ($fc1.source -ne "origin") { throw "forecast[1] expected 'origin', got '$($fc1.source)'" }
if ($fc2.source -ne "cache") { throw "forecast[2] expected 'cache', got '$($fc2.source)'" }
Write-Host "forecast: origin → cache ✅" -ForegroundColor Green

# TTL проверяем прямо в Redis через docker compose
Write-Host "== TTL in Redis ==" -ForegroundColor Cyan
$slug = Slug $City

# Напоминание: InstanceName = "weather:", а в коде ключи "current:{slug}" и "forecast:{slug}:{days}"
$currentKey = "weather:current:${slug}"
$forecastKey = "weather:forecast:${slug}:${Days}"

$ttlCurrent = docker compose exec redis redis-cli TTL $currentKey  2>$null
$ttlForecast = docker compose exec redis redis-cli TTL $forecastKey 2>$null

Write-Host ("TTL {0} = {1}s" -f $currentKey, $ttlCurrent)
Write-Host ("TTL {0} = {1}s" -f $forecastKey, $ttlForecast)

if ([int]$ttlCurrent -le 0) { throw "Unexpected TTL for current key" }
if ([int]$ttlForecast -le 0) { throw "Unexpected TTL for forecast key" }

Write-Host "All checks passed ✅" -ForegroundColor Green
