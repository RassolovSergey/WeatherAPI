"use client";

import { useMemo, useState } from "react";

type CurrentWeather = {
  city: string;
  country: string;
  tempC: number;
  condition: string;
  fetchedAtUtc: string;
  source: string;
};

type ForecastDay = {
  date: string;
  minTempC: number;
  maxTempC: number;
  condition: string;
};

type ForecastReport = {
  city: string;
  country: string;
  days: number;
  items: ForecastDay[];
  fetchedAtUtc: string;
  source: string;
};

const API_BASE =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5277/api/v1";

export default function Home() {
  const [city, setCity] = useState("Perm");
  const [days, setDays] = useState(3);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [current, setCurrent] = useState<CurrentWeather | null>(null);
  const [forecast, setForecast] = useState<ForecastReport | null>(null);

  const formattedFetchedAt = useMemo(() => {
    if (!current) return "";
    return new Date(current.fetchedAtUtc).toLocaleString();
  }, [current]);

  async function loadWeather() {
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams({ city, days: String(days) });

      const [currentResp, forecastResp] = await Promise.all([
        fetch(`${API_BASE}/weather/current?${params.toString()}`),
        fetch(`${API_BASE}/weather/forecast?${params.toString()}`),
      ]);

      if (!currentResp.ok) {
        const detail = await safeError(currentResp);
        throw new Error(detail ?? "Не удалось получить текущую погоду");
      }
      if (!forecastResp.ok) {
        const detail = await safeError(forecastResp);
        throw new Error(detail ?? "Не удалось получить прогноз");
      }

      const currentJson: CurrentWeather = await currentResp.json();
      const forecastJson: ForecastReport = await forecastResp.json();

      setCurrent(currentJson);
      setForecast(forecastJson);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unexpected error");
      setCurrent(null);
      setForecast(null);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="min-h-screen bg-gradient-to-b from-[#f7f7f2] via-white to-[#e9eef5] text-slate-900">
      <div className="mx-auto max-w-5xl px-4 py-16 sm:px-8">
        <header className="mb-10 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <p className="text-sm uppercase tracking-[0.2em] text-slate-500">
              Weather Proxy
            </p>
            <h1 className="text-3xl font-semibold text-slate-900">
              Быстрый прогноз на 3 дня
            </h1>
            <p className="text-sm text-slate-600">
              Бесплатный план провайдера отдаёт максимум 3 дня. Источник:
              WeatherAPI + Redis-кэш.
            </p>
          </div>
          <span className="rounded-full bg-slate-900 px-3 py-1 text-xs font-medium text-white">
            API: {API_BASE}
          </span>
        </header>

        <main className="grid gap-6 lg:grid-cols-[360px,1fr]">
          <section className="rounded-2xl bg-white p-6 shadow-[0_15px_60px_-35px_rgba(30,41,59,0.35)]">
            <h2 className="mb-4 text-lg font-semibold text-slate-900">
              Параметры запроса
            </h2>
            <form
              className="flex flex-col gap-4"
              onSubmit={(e) => {
                e.preventDefault();
                loadWeather();
              }}
            >
              <label className="flex flex-col gap-2">
                <span className="text-sm font-medium text-slate-700">
                  Город
                </span>
                <input
                  value={city}
                  onChange={(e) => setCity(e.target.value)}
                  className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-base outline-none transition focus:border-slate-400 focus:ring-2 focus:ring-slate-200"
                  placeholder="Например: Perm"
                  required
                  maxLength={64}
                />
              </label>

              <label className="flex flex-col gap-2">
                <div className="flex items-center justify-between">
                  <span className="text-sm font-medium text-slate-700">
                    Дней прогноза (1–3)
                  </span>
                  <span className="text-xs text-slate-500">план провайдера</span>
                </div>
                <input
                  type="range"
                  min={1}
                  max={3}
                  value={days}
                  onChange={(e) => setDays(Number(e.target.value))}
                  className="accent-slate-900"
                />
                <div className="flex justify-between text-xs text-slate-600">
                  <span>1</span>
                  <span>2</span>
                  <span>3</span>
                </div>
              </label>

              <button
                type="submit"
                disabled={loading}
                className="mt-2 inline-flex items-center justify-center gap-2 rounded-xl bg-slate-900 px-4 py-3 text-sm font-semibold text-white shadow-lg shadow-slate-900/10 transition hover:-translate-y-[1px] hover:shadow-slate-900/20 disabled:translate-y-0 disabled:opacity-60"
              >
                {loading ? "Запрашиваем..." : "Получить данные"}
              </button>
              {error && (
                <p className="rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700">
                  {error}
                </p>
              )}
            </form>
          </section>

          <section className="space-y-4">
            <div className="rounded-2xl bg-white p-6 shadow-[0_15px_60px_-35px_rgba(30,41,59,0.35)]">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <p className="text-xs uppercase tracking-[0.2em] text-slate-500">
                    Current
                  </p>
                  <h2 className="text-xl font-semibold text-slate-900">
                    {current ? current.city : "Нет данных"}
                  </h2>
                  <p className="text-sm text-slate-600">
                    {current
                      ? `${current.country} · ${formattedFetchedAt}`
                      : "Выберите город и нажмите «Получить данные»"}
                  </p>
                </div>
                {current && (
                  <span className="rounded-full bg-slate-100 px-3 py-1 text-xs font-semibold text-slate-800">
                    {current.tempC.toFixed(1)}°C · {current.condition}
                  </span>
                )}
              </div>
              {current && (
                <dl className="mt-4 grid grid-cols-1 gap-3 text-sm text-slate-700">
                  <div className="rounded-lg bg-slate-50 p-3">
                    <dt className="text-xs uppercase text-slate-500">
                      Источник
                    </dt>
                    <dd className="font-semibold">{current.source}</dd>
                  </div>
                </dl>
              )}
            </div>

            <div className="rounded-2xl bg-white p-6 shadow-[0_15px_60px_-35px_rgba(30,41,59,0.35)]">
              <div className="mb-4 flex items-center justify-between">
                <div>
                  <p className="text-xs uppercase tracking-[0.2em] text-slate-500">
                    Forecast
                  </p>
                  <h2 className="text-xl font-semibold text-slate-900">
                    {forecast ? `${forecast.days} дня` : "Нет прогноза"}
                  </h2>
                  <p className="text-xs text-slate-600">
                    {forecast
                      ? `Источник: ${forecast.source}. Обновлено ${new Date(
                          forecast.fetchedAtUtc,
                        ).toLocaleString()}`
                      : "Выберите параметры и запросите прогноз."}
                  </p>
                </div>
                {forecast && (
                  <span className="rounded-full bg-slate-900 px-3 py-1 text-xs font-semibold text-white">
                    {forecast.city}
                  </span>
                )}
              </div>
              {forecast && forecast.items.length > 0 ? (
                <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                  {forecast.items.map((day) => (
                    <div
                      key={day.date}
                      className="rounded-xl border border-slate-100 bg-slate-50 px-4 py-3"
                    >
                      <p className="text-sm font-semibold text-slate-900">
                        {new Date(day.date).toLocaleDateString()}
                      </p>
                      <p className="text-sm text-slate-600">{day.condition}</p>
                      <p className="mt-2 text-xs text-slate-500">
                        min {day.minTempC.toFixed(1)}°C · max{" "}
                        {day.maxTempC.toFixed(1)}°C
                      </p>
                    </div>
                  ))}
                </div>
              ) : (
                <p className="text-sm text-slate-600">
                  Прогноз появится после запроса.
                </p>
              )}
            </div>
          </section>
        </main>
      </div>
    </div>
  );
}

async function safeError(resp: Response): Promise<string | null> {
  try {
    const json = await resp.json();
    return json?.detail ?? json?.title ?? null;
  } catch {
    return null;
  }
}
