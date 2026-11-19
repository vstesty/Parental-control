using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ParentalControl.Service;

internal sealed class DailyPolicy
{
    private static readonly string StateDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ParentalControl");
    private static readonly string StatePath = Path.Combine(StateDirectory, "state.json");

    private readonly object _lock = new();
    private DateOnly _date;
    private TimeSpan _used;
    private TimeSpan _extra;
    private bool _disabled;
    private bool _dirty;
    private long _version;

    public void Load(DateOnly today)
    {
        lock (_lock)
        {
            _date = today;
            if (File.Exists(StatePath))
            {
                var saved = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(StatePath));
                if (saved?.Date == today)
                {
                    _used = TimeSpan.FromTicks(saved.UsedTicks);
                    _extra = TimeSpan.FromTicks(saved.ExtraTicks);
                    _disabled = saved.Disabled;
                    return;
                }
            }

            ResetFor(today);
        }
    }

    public void ResetFor(DateOnly today)
    {
        lock (_lock)
        {
            _date = today;
            _used = TimeSpan.Zero;
            _extra = TimeSpan.Zero;
            _disabled = false;
            _dirty = true;
            _version++;
        }
    }

    public void AddUsage(TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        lock (_lock)
        {
            _used += elapsed;
            _dirty = true;
            _version++;
        }
    }

    public TimeSpan Used
    {
        get
        {
            lock (_lock)
            {
                return _used;
            }
        }
    }

    public bool IsOverLimit(TimeSpan? configuredLimit)
    {
        lock (_lock)
        {
            return !_disabled && configuredLimit is { } limit && _used >= limit + _extra;
        }
    }

    public string ApplyRequest(AppConfiguration config, string action, string password)
    {
        if (!PasswordsMatch(config.Password, password))
        {
            return "DENIED";
        }

        lock (_lock)
        {
            switch (action)
            {
                case "15":
                case "30":
                case "60":
                    if (config.GetLimit(DateTime.Now) is null)
                    {
                        return "NO_LIMIT";
                    }

                    _extra += TimeSpan.FromMinutes(int.Parse(action));
                    _dirty = true;
                    _version++;
                    return "OK";
                case "OFF":
                    _disabled = true;
                    _dirty = true;
                    _version++;
                    return "OK";
                default:
                    return "INVALID";
            }
        }
    }

    public async Task SaveIfDirtyAsync(CancellationToken cancellationToken)
    {
        SavedState snapshot;
        long version;
        lock (_lock)
        {
            if (!_dirty)
            {
                return;
            }

            snapshot = new SavedState(_date, _used.Ticks, _extra.Ticks, _disabled);
            version = _version;
        }

        Directory.CreateDirectory(StateDirectory);
        var temporaryPath = StatePath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(snapshot), cancellationToken);
        File.Move(temporaryPath, StatePath, overwrite: true);
        lock (_lock)
        {
            if (_version == version)
            {
                _dirty = false;
            }
        }
    }

    private static bool PasswordsMatch(string expected, string supplied)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private sealed record SavedState(DateOnly Date, long UsedTicks, long ExtraTicks, bool Disabled);
}
