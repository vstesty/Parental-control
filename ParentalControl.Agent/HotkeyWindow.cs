using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace ParentalControl.Agent;

internal sealed class HotkeyWindow : Form
{
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint VkH = 0x48;
    private static readonly TimeSpan EarlyWarningThreshold = TimeSpan.FromMinutes(5);
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 1000 };
    private readonly NotifyIcon _notifyIcon = new()
    {
        Icon = SystemIcons.Application,
        Text = "Kontrola rodzicielska"
    };
    private readonly CountdownWindow _countdownWindow = new();
    private bool _warningShown;
    private bool _statusRequestInProgress;
    private bool _serviceUnavailableReported;
    private bool _extensionDialogOpen;
    private LimitStatus? _lastStatus;

    public HotkeyWindow()
    {
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        WindowState = FormWindowState.Minimized;
        Opacity = 0;
        _notifyIcon.Visible = true;
        _statusTimer.Tick += async (_, _) => await PollStatusAsync();
        Load += async (_, _) =>
        {
            if (!RegisterHotKey(Handle, HotkeyId, ModControl, VkH))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Nie można zarejestrować skrótu Ctrl+H.");
            }

            await PollStatusAsync();
            _statusTimer.Start();
        };
        FormClosed += (_, _) =>
        {
            _statusTimer.Stop();
            _statusTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _countdownWindow.CloseForApplicationExit();
            UnregisterHotKey(Handle, HotkeyId);
        };
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId)
        {
            _extensionDialogOpen = true;
            _countdownWindow.HideCountdown();
            try
            {
                using var dialog = new ExtensionDialog(
                    _lastStatus?.LimitRemaining,
                    _lastStatus is not null);
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    var response = SendRequestAsync(dialog.Action, dialog.Password).GetAwaiter().GetResult();
                    var messageText = response switch
                    {
                        "OK" => "Zmieniono limit czasu na dzisiaj.",
                        "DENIED" => "Nieprawidłowe hasło.",
                        "NO_LIMIT" => "Limit na dzisiaj jest wyłączony w konfiguracji.",
                        "SERVICE_UNAVAILABLE" => "Nie można połączyć się z usługą. Sprawdź, czy działa.",
                        _ => "Nie udało się zmienić limitu. Sprawdź, czy usługa działa."
                    };
                    MessageBox.Show(messageText, "Kontrola rodzicielska", MessageBoxButtons.OK,
                        response == "OK" ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
            }
            finally
            {
                _extensionDialogOpen = false;
            }
        }

        base.WndProc(ref message);
    }

    private static async Task<string> SendRequestAsync(string action, string password)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", "ParentalControl.Control", PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(3000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            var encodedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
            await writer.WriteLineAsync($"EXTEND|{action}|{encodedPassword}");
            return await reader.ReadLineAsync() ?? "INVALID";
        }
        catch (IOException)
        {
            return "SERVICE_UNAVAILABLE";
        }
        catch (TimeoutException)
        {
            return "SERVICE_UNAVAILABLE";
        }
        catch (UnauthorizedAccessException)
        {
            return "SERVICE_UNAVAILABLE";
        }
    }

    private async Task PollStatusAsync()
    {
        if (_statusRequestInProgress)
        {
            return;
        }

        _statusRequestInProgress = true;
        try
        {
            LimitStatus status;
            try
            {
                status = await ReadStatusAsync();
            }
            catch (Exception exception) when (
                exception is IOException or TimeoutException or UnauthorizedAccessException)
            {
                _lastStatus = null;
                if (!_serviceUnavailableReported)
                {
                    Trace.TraceError(
                        "Nie można odczytać statusu limitu z usługi kontroli rodzicielskiej: {0}",
                        exception.Message);
                    _serviceUnavailableReported = true;
                }

                return;
            }

            _lastStatus = status;
            _serviceUnavailableReported = false;
            if (status.LogoffRemaining is { } graceRemaining)
            {
                if (_extensionDialogOpen)
                {
                    _countdownWindow.HideCountdown();
                }
                else
                {
                    _countdownWindow.ShowCountdown(graceRemaining);
                }

                return;
            }

            _countdownWindow.HideCountdown();
            if (status.LimitRemaining is { } remaining && remaining > TimeSpan.Zero &&
                remaining <= EarlyWarningThreshold)
            {
                if (!_warningShown)
                {
                    _notifyIcon.ShowBalloonTip(
                        10000,
                        "Kontrola rodzicielska",
                        "Limit czasu kończy się za mniej niż 5 minut. Zapisz swoją pracę.",
                        ToolTipIcon.Warning);
                    _warningShown = true;
                }
            }
            else
            {
                _warningShown = false;
            }
        }
        finally
        {
            _statusRequestInProgress = false;
        }
    }

    private static async Task<LimitStatus> ReadStatusAsync()
    {
        await using var pipe = new NamedPipeClientStream(
            ".", "ParentalControl.Control", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(3000);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync("STATUS");
        var response = await reader.ReadLineAsync();
        var parts = response?.Split('|');
        if (parts is not { Length: 3 } || parts[0] != "STATUS" ||
            !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var remainingTicks) ||
            !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var logoffTicks))
        {
            throw new InvalidDataException("Usługa zwróciła nieprawidłowy status limitu.");
        }

        return new LimitStatus(
            remainingTicks >= 0 ? TimeSpan.FromTicks(remainingTicks) : null,
            logoffTicks >= 0 ? TimeSpan.FromTicks(logoffTicks) : null);
    }

    private sealed record LimitStatus(TimeSpan? LimitRemaining, TimeSpan? LogoffRemaining);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
