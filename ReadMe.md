# Weather Proxy API + Next.js UI

Учебный проект: минимальный прокси к weatherapi.com с кэшированием в Redis, устойчивыми HTTP‑вызовами и фронтендом на Next.js.

## Технологии и архитектура
- **Backend:** .NET 9, Minimal API, Swagger/Swashbuckle, rate limiting (FixedWindow), health checks, CORS.
- **Инфраструктура:** Redis через IDistributedCache (StackExchange.Redis), Polly‑resilience для исходящих запросов, логирование исходящих HTTP, middleware для X-Correlation-ID.
- **Архитектура:** условный clean‑split — Weather.Api (вход), Weather.Application (контракты), Weather.Domain (модели), Weather.Infrastructure (провайдер WeatherAPI, кэш-декоратор).
- **Паттерны:** cache-aside, decorator (CachedWeatherServiceDecorator), typed HttpClient с resilience pipeline.
- **Frontend:** Next.js (App Router, TS, Tailwind), экран запроса текущей погоды и прогноза (1–3 дня).

## Структура
```
WeatherAPI/WeatherProxy/         # backend (.NET)
  src/Weather.Api                # Minimal API, Program.cs, Swagger, rate limiting, health
  src/Weather.Infrastructure     # WeatherApiClient, кэш-декоратор, опции
  src/Weather.Application        # IWeatherService, IWeatherProvider
  src/Weather.Domain             # WeatherReport, ForecastReport, ForecastDay
  tests/                         # unit-тесты декоратора кэша
  docker-compose*.yml            # API + Redis, опционально Prometheus/Grafana
frontend/                        # frontend (Next.js + Tailwind, TS)
```

## Backend: конфигурация
Основные переменные окружения (или appsettings/launchSettings):
```
WEATHERAPI__BASEURL=https://api.weatherapi.com/v1/
WEATHERAPI__APIKEY=<ваш ключ>
WEATHERAPI__MAXFORECASTDAYS=3                   # сколько дней поддерживает ваш тариф
REDIS__CONNECTIONSTRING=localhost:6379,abortConnect=false   # или redis:6379 в Docker
CACHING__CURRENTTTLMINUTES=15
CACHING__FORECASTTTLMINUTES=60
HTTP__TIMEOUTSECONDS=8
CORS__ALLOWEDORIGINS=<опционально, CSV списка Origins>
```
Важно: бесплатный план WeatherAPI обычно отдаёт максимум 3 дня прогноза (некоторые ключи ограничены одним). Настройте `WEATHERAPI__MAXFORECASTDAYS`, чтобы сервис валидировал допустимый диапазон без похода к провайдеру.

## Backend: запуск
Локально:
```
dotnet restore WeatherProxy.sln
dotnet run --project WeatherProxy/src/Weather.Api
# Swagger: http://localhost:5277/swagger
# Health:  http://localhost:5277/healthz
# Metrics (Prometheus): /metrics
```
Docker Compose (API + Redis):
```
docker compose up --build
# API на 8080: http://localhost:8080
```

## Backend: эндпоинты
- GET /api/v1/weather/current?city={name} — текущая погода, source: origin или cache.
- GET /api/v1/weather/forecast?city={name}&days=1..N — прогноз до N дней (лимит провайдера, задаётся `WEATHERAPI__MAXFORECASTDAYS`), source: origin или cache.
- Dev (в Development): DELETE /api/v1/dev/cache/current?city=..., DELETE /api/v1/dev/cache/forecast?city=...&days=... — инвалидация кэша.

## Backend: ключевые файлы
- src/Weather.Api/Program.cs — DI, Swagger, rate limiting, CORS, ProblemDetails, маршруты /api/v1/weather.
- src/Weather.Infrastructure/WeatherApiClient.cs — вызовы current.json / forecast.json с Polly‑resilience.
- src/Weather.Infrastructure/Caching/CachedWeatherServiceDecorator.cs — cache-aside в Redis, ключи weather:current:{slug} и weather:forecast:{slug}:{days}.
- src/Weather.Infrastructure/Options/*.cs — опции WeatherAPI и кэша.
- tests/Weather.Tests/CachedWeatherServiceDecoratorTests.cs — юнит‑тесты кэш-декоратора.

## Frontend
Расположение: frontend (Next.js + Tailwind).

.env.local:
```
NEXT_PUBLIC_API_BASE_URL=http://localhost:5277/api/v1   # или http://localhost:8080/api/v1 при Docker
```

Запуск:
```
cd frontend
npm install   # первый раз
npm run dev   # http://localhost:3000
```
Экран: ввод города, слайдер дней (1–3), кнопка «Получить данные», карточки текущей погоды и прогноза, обработка ошибок/загрузки.

## Тесты
```
dotnet test WeatherProxy.sln
```

## Метрики и наблюдаемость
- /metrics (Prometheus), опционально docker-compose.metrics.yml для Prometheus + Grafana.
- Логи исходящих HTTP без утечек ключа (маскирование query params).
- Корреляция запросов через X-Correlation-ID.

## Безопасность и ограничители
- Валидация входных данных (город: non-empty, <=64 символов, тримминг).
- Rate limiting per-IP: 60 req/min без очереди.
- CORS: если CORS__ALLOWEDORIGINS не задан, в Dev разрешены все; для продакшена задайте список Origin.
- Секреты — только из ENV/Secret Manager; API key не логируется.

## Быстрый старт (tl;dr)
1. Задайте WEATHERAPI__APIKEY и REDIS__CONNECTIONSTRING.
2. Запустите API (dotnet run или docker compose up).
3. В frontend/.env.local пропишите NEXT_PUBLIC_API_BASE_URL.
4. Запустите фронт npm run dev и откройте http://localhost:3000.
