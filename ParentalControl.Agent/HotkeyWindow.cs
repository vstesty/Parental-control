using System.ComponentModel;
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

    public HotkeyWindow()
    {
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        WindowState = FormWindowState.Minimized;
        Opacity = 0;
        Load += (_, _) =>
        {
            if (!RegisterHotKey(Handle, HotkeyId, ModControl, VkH))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Nie można zarejestrować skrótu Ctrl+H.");
            }
        };
        FormClosed += (_, _) => UnregisterHotKey(Handle, HotkeyId);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId)
        {
            using var dialog = new ExtensionDialog();
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
