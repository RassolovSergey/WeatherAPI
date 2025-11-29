// Назначение: единое место генерации ключей для Redis.
// ВАЖНО: в Program.cs мы настроили IDistributedCache.InstanceName = "weather:",
// поэтому в коде используем ключи без этого префикса: "current:{slug}".
// Итоговый реальный ключ в Redis будет "weather:current:{slug}".

using System.Text;
using System.Text.RegularExpressions;

namespace Weather.Infrastructure.Caching;

public static class CacheKeyBuilder
{
    private static readonly Regex NonSlugChars = new("[^a-z0-9\\-]+", RegexOptions.Compiled);

    /// <summary>
    /// Превращаю ввод пользователя в безопасный slug для ключа.
    /// 1) Trim + ToLowerInvariant
    /// 2) Пробелы → '-'
    /// 3) Удаляю неподходящие символы
    /// 4) Сжимаю повторяющиеся '-'
    /// 5) Ограничиваю длину (на всякий случай)
    /// </summary>
    public static string CitySlug(string city)
    {
        var s = (city ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '-');
        s = NonSlugChars.Replace(s, "-");
        // сжатие повторяющихся '-'
        var sb = new StringBuilder(s.Length);
        char prev = '\0';
        foreach (var ch in s)
        {
            if (ch == '-' && prev == '-') continue;
            sb.Append(ch);
            prev = ch;
        }
        s = sb.ToString().Trim('-');
        return s.Length > 64 ? s.Substring(0, 64) : s;
    }

    public static string CurrentKey(string city) => $"current:{CitySlug(city)}";
    // на будущее:
    public static string ForecastKey(string city, int days) => $"forecast:{CitySlug(city)}:{days}";
}
