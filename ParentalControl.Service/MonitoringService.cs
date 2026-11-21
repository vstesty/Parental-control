using System.Globalization;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ParentalControl.Service;

internal sealed class MonitoringService(
    DailyPolicy policy,
    ILogger<MonitoringService> logger) : BackgroundService
{
    private const string PipeName = "ParentalControl.Control";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LogoffGracePeriod = TimeSpan.FromSeconds(60);
    private StatusSnapshot _status = new(null, null);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = AppConfiguration.Load();
        var today = DateOnly.FromDateTime(DateTime.Now);
        policy.Load(today);

        var pipeTask = ServeRequestsAsync(() => configuration, stoppingToken);
        var previousSample = DateTime.UtcNow;
        var lastSaved = previousSample;
        uint? previousSession = null;
        DateTime? nextLogoffAttempt = null;
        var lastConfigurationWarning = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var currentDate = DateOnly.FromDateTime(now);
            if (currentDate != today)
            {
                today = currentDate;
                policy.ResetFor(today);
                nextLogoffAttempt = null;
            }

            try
            {
                configuration = AppConfiguration.Load();
                lastConfigurationWarning = DateTime.MinValue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
            {
                if (DateTime.UtcNow - lastConfigurationWarning >= TimeSpan.FromMinutes(1))
                {
                    logger.LogError(exception, "Nie można odczytać konfiguracji. Pozostaje ostatnia poprawna wersja.");
                    lastConfigurationWarning = DateTime.UtcNow;
                }
            }

            var activeSession = WindowsSession.GetActiveConsoleSession();
            var utcNow = DateTime.UtcNow;
            if (activeSession is not null)
            {
                policy.AddUsage(utcNow - previousSample);
                if (previousSession != activeSession)
                {
                    nextLogoffAttempt = null;
                }

                var remaining = policy.GetRemaining(configuration.GetLimit(now));
                if (remaining is { } timeRemaining && timeRemaining <= TimeSpan.Zero)
                {
                    nextLogoffAttempt ??= utcNow + LogoffGracePeriod;
                    var graceRemaining = nextLogoffAttempt.Value - utcNow;
                    if (graceRemaining <= TimeSpan.Zero)
                    {
                        try
                        {
                            WindowsSession.Logoff(activeSession.Value);
                            logger.LogInformation("Limit dzienny został wykorzystany. Zlecono wylogowanie sesji {SessionId}.", activeSession);
                        }
                        catch (System.ComponentModel.Win32Exception exception)
                        {
                            logger.LogError(exception, "Nie udało się wylogować sesji {SessionId}.", activeSession);
                        }

                        nextLogoffAttempt = DateTime.UtcNow + LogoffGracePeriod;
                        graceRemaining = LogoffGracePeriod;
                    }

                    Volatile.Write(ref _status, new StatusSnapshot(
                        Math.Max(0, timeRemaining.Ticks),
                        graceRemaining.Ticks));
                }
                else
                {
                    nextLogoffAttempt = null;
                    Volatile.Write(ref _status, new StatusSnapshot(remaining?.Ticks, null));
                }
            }
            else
            {
                nextLogoffAttempt = null;
                Volatile.Write(ref _status, new StatusSnapshot(null, null));
            }

            previousSession = activeSession;
            previousSample = utcNow;
            if (utcNow - lastSaved >= TimeSpan.FromSeconds(10))
            {
                await policy.SaveIfDirtyAsync(stoppingToken);
                lastSaved = utcNow;
            }

            await Task.Delay(PollInterval, stoppingToken);
        }

        await policy.SaveIfDirtyAsync(CancellationToken.None);
        await pipeTask;
    }

    private async Task ServeRequestsAsync(Func<AppConfiguration> getConfiguration, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = CreatePipe();
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var request = await reader.ReadLineAsync(cancellationToken);
                var response = HandleRequest(request, getConfiguration);
                await writer.WriteLineAsync(response);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "Błąd komunikacji z programem użytkownika.");
            }
        }
    }

    private string HandleRequest(string? request, Func<AppConfiguration> getConfiguration)
    {
        if (request is null || request.Length > 8192)
        {
            return "INVALID";
        }

        if (request == "STATUS")
        {
            var status = Volatile.Read(ref _status);
            return string.Format(
                CultureInfo.InvariantCulture,
                "STATUS|{0}|{1}",
                status.RemainingTicks?.ToString(CultureInfo.InvariantCulture) ?? "-1",
                status.LogoffTicks?.ToString(CultureInfo.InvariantCulture) ?? "-1");
        }

        var parts = request.Split('|', 3);
        if (parts.Length != 3 || parts[0] != "EXTEND")
        {
            return "INVALID";
        }

        try
        {
            var password = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
            return policy.ApplyRequest(getConfiguration(), parts[1], password);
        }
        catch (FormatException)
        {
            return "INVALID";
        }
    }

    private sealed record StatusSnapshot(long? RemainingTicks, long? LogoffTicks);

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            4096,
            4096,
            security);
    }
}
