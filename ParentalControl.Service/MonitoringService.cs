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
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = AppConfiguration.Load();
        var today = DateOnly.FromDateTime(DateTime.Now);
        policy.Load(today);

        var pipeTask = ServeRequestsAsync(() => configuration, stoppingToken);
        var previousSample = DateTime.UtcNow;
        var lastSaved = previousSample;
        uint? previousSession = null;
        uint? logoffRequestedSession = null;
        var lastLogoffAttempt = DateTime.MinValue;
        var lastConfigurationWarning = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var currentDate = DateOnly.FromDateTime(now);
            if (currentDate != today)
            {
                today = currentDate;
                policy.ResetFor(today);
                logoffRequestedSession = null;
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
                    logoffRequestedSession = null;
                }

                if (policy.IsOverLimit(configuration.GetLimit(now)) &&
                    (logoffRequestedSession != activeSession ||
                     utcNow - lastLogoffAttempt >= TimeSpan.FromMinutes(1)))
                {
                    lastLogoffAttempt = utcNow;
                    try
                    {
                        WindowsSession.Logoff(activeSession.Value);
                        logoffRequestedSession = activeSession;
                        logger.LogInformation("Limit dzienny został wykorzystany. Wylogowano sesję {SessionId}.", activeSession);
                    }
                    catch (System.ComponentModel.Win32Exception exception)
                    {
                        logger.LogError(exception, "Nie udało się wylogować sesji {SessionId}.", activeSession);
                    }
                }
            }
            else
            {
                logoffRequestedSession = null;
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
                var response = HandleRequest(request, getConfiguration());
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

    private string HandleRequest(string? request, AppConfiguration configuration)
    {
        if (request is null || request.Length > 8192)
        {
            return "INVALID";
        }

        var parts = request.Split('|', 3);
        if (parts.Length != 3 || parts[0] != "EXTEND")
        {
            return "INVALID";
        }

        try
        {
            var password = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
            return policy.ApplyRequest(configuration, parts[1], password);
        }
        catch (FormatException)
        {
            return "INVALID";
        }
    }

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
