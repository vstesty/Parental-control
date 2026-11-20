namespace ParentalControl.Agent;

internal sealed class CountdownWindow : Form
{
    private readonly Label _message = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 15, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleCenter,
        Padding = new Padding(20)
    };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private DateTime _deadlineUtc = DateTime.UtcNow;
    private bool _allowClose;

    public CountdownWindow()
    {
        Text = "Kontrola rodzicielska";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 180);
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        Controls.Add(_message);

        _timer.Tick += (_, _) => UpdateMessage();
        _timer.Start();
        FormClosing += (_, e) =>
        {
            if (!_allowClose)
            {
                e.Cancel = true;
            }
        };
        FormClosed += (_, _) => _timer.Dispose();
    }

    public void ShowCountdown(TimeSpan remaining)
    {
        _deadlineUtc = DateTime.UtcNow + remaining;
        UpdateMessage();
        if (!Visible)
        {
            Show();
            Activate();
        }
    }

    public void HideCountdown()
    {
        Hide();
    }

    public void CloseForApplicationExit()
    {
        _allowClose = true;
        Close();
    }

    private void UpdateMessage()
    {
        var seconds = Math.Max(0, (int)Math.Ceiling((_deadlineUtc - DateTime.UtcNow).TotalSeconds));
        _message.Text = $"Limit dzienny został wykorzystany.\nWylogowanie za: {seconds / 60:00}:{seconds % 60:00}";
    }
}
