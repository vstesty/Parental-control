using System.Globalization;

namespace ParentalControl.Service;

internal sealed record AppConfiguration(string Password, IReadOnlyDictionary<DayOfWeek, TimeSpan?> DailyLimits)
{
    public const string FilePath = @"C:\DD\config.txt";

    public TimeSpan? GetLimit(DateTime localNow) => DailyLimits[localNow.DayOfWeek];

    public static AppConfiguration Load()
    {
        if (!File.Exists(FilePath))
        {
            throw new FileNotFoundException($"Nie znaleziono pliku konfiguracji: {FilePath}");
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(FilePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                throw new FormatException($"Nieprawidłowy wpis w {FilePath}: {rawLine}");
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!values.TryAdd(key, value))
            {
                throw new FormatException($"Powtórzony klucz „{key}” w {FilePath}.");
            }
        }

        if (!values.TryGetValue("Password", out var password) || string.IsNullOrWhiteSpace(password))
        {
            throw new FormatException("W konfiguracji musi być ustawione niepuste Password.");
        }

        var limits = new Dictionary<DayOfWeek, TimeSpan?>();
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var key = day.ToString();
            if (!values.TryGetValue(key, out var value))
            {
                throw new FormatException($"Brak limitu dla dnia {key} w {FilePath}.");
            }

            if (value.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                limits.Add(day, null);
                continue;
            }

            if (!TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out var duration))
            {
                throw new FormatException($"Limit {key} musi mieć format HH:mm albo wartość off.");
            }

            limits.Add(day, duration);
        }

        return new AppConfiguration(password, limits);
    }
}
