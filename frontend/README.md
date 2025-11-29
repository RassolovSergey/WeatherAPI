## Weather Proxy UI (Next.js)

Небольшой Next 16 + Tailwind клиент для запросов к Weather Proxy API.

## Быстрый старт
1) Скопируйте пример окружения и поправьте адрес API:
```
cp .env.local.example .env.local
# dotnet run (launchSettings): NEXT_PUBLIC_API_BASE_URL=http://localhost:5277/api/v1
# docker compose up:          NEXT_PUBLIC_API_BASE_URL=http://localhost:8080/api/v1
```
2) Установите зависимости и запустите dev-сервер:
```
npm install
npm run dev  # http://localhost:3000 (Next поднимет 3001, если порт занят)
```

## Типовые проблемы
- `Failed to fetch` / CORS: убедитесь, что API слушает тот же адрес, что в `NEXT_PUBLIC_API_BASE_URL`, и что CORS на бэке разрешает ваш Origin. В Dev теперь принимаются любые loopback-Origins (localhost/127.0.0.1, любой порт).
- API-бейз выводится в правом верхнем углу страницы для быстрой проверки.
