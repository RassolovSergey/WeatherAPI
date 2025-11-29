# Dockerfile для Weather.Api
# Сборка образа в два шага: build (SDK) и final (runtime)
# ===========================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Копирую *только* .csproj и .sln — слой для быстрого restore (кэшируется)
# Это best practice: если код изменился, но ссылки/пакеты нет — restore возьмётся из кэша
COPY WeatherProxy.sln ./
COPY src/Weather.Api/Weather.Api.csproj src/Weather.Api/
COPY src/Weather.Application/Weather.Application.csproj src/Weather.Application/
COPY src/Weather.Domain/Weather.Domain.csproj src/Weather.Domain/
COPY src/Weather.Infrastructure/Weather.Infrastructure.csproj src/Weather.Infrastructure/

# Восстанавливаю зависимости решения
RUN dotnet restore src/Weather.Api/Weather.Api.csproj

# Теперь копирую *весь* исходный код
COPY . .

# Публикую только API в Release (UseAppHost=false — кроссплатформенный self-contained хост не создаём)
RUN dotnet publish src/Weather.Api/Weather.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ===========================
# STAGE 2: runtime (ASP.NET)
# ===========================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
# Рабочая директория внутри контейнера
WORKDIR /app

# Копирую из билдового контейнера только опубликованные файлы
COPY --from=build /app/publish .

# Настраиваю запуск приложения
# Приложение будет слушать на порту 8080
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Точка входа приложения
ENTRYPOINT ["dotnet","Weather.Api.dll"]
