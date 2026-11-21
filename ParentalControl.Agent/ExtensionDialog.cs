namespace ParentalControl.Agent;

internal sealed class ExtensionDialog : Form
{
    private readonly TextBox _password = new();
    private readonly Label _logoffRemaining = new()
    {
        AutoSize = true,
        Location = new Point(12, 38)
    };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly DateTime? _logoffDeadlineUtc;
    private readonly bool _statusAvailable;

    public string Password => _password.Text;
    public string Action { get; private set; } = "";

    public ExtensionDialog(TimeSpan? logoffRemaining, bool statusAvailable)
    {
        Text = "Kontrola rodzicielska";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        KeyPreview = true;
        ClientSize = new Size(360, 180);
        _logoffDeadlineUtc = logoffRemaining is { } remaining
            ? DateTime.UtcNow + remaining
            : null;
        _statusAvailable = statusAvailable;
        var version = typeof(Program).Assembly.GetName().Version
            ?? throw new InvalidOperationException("Nie można odczytać wersji programu.");
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        var versionLabel = new Label
        {
            Text = $"Wersja programu: {version.ToString(2)}",
            AutoSize = true,
            Location = new Point(12, 12)
        };
        var passwordLabel = new Label
        {
            Text = "Wpisz hasło:",
            AutoSize = true,
            Location = new Point(12, 65)
        };
        _password.Location = new Point(12, 88);
        _password.Size = new Size(336, 25);
        _password.UseSystemPasswordChar = true;

        var buttons = new[]
        {
            CreateButton("+15 min", "15", 12),
            CreateButton("+30 min", "30", 98),
            CreateButton("+1 godz.", "60", 184),
            CreateButton("Wyłącz", "OFF", 270)
        };

        Controls.Add(versionLabel);
        Controls.Add(_logoffRemaining);
        Controls.Add(passwordLabel);
        Controls.Add(_password);
        Controls.AddRange(buttons);

        UpdateLogoffRemaining();
        _timer.Tick += (_, _) => UpdateLogoffRemaining();
        _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();
    }

    private Button CreateButton(string label, string action, int left)
    {
        var button = new Button
        {
            Text = label,
            Location = new Point(left, 130),
            Size = new Size(78, 35)
        };
        button.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(_password.Text))
            {
                MessageBox.Show("Wpisz hasło.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _password.Focus();
                return;
            }

            Action = action;
            DialogResult = DialogResult.OK;
            Close();
        };
        return button;
    }

    private void UpdateLogoffRemaining()
    {
        if (!_statusAvailable)
        {
            _logoffRemaining.Text = "Czas do wylogowania: brak danych";
            return;
        }

        if (_logoffDeadlineUtc is not { } deadline)
        {
            _logoffRemaining.Text = "Czas do wylogowania: brak aktywnego odliczania";
            return;
        }

        var seconds = Math.Max(0, (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds));
        _logoffRemaining.Text =
            $"Czas do wylogowania: {seconds / 60:00}:{seconds % 60:00}";
    }
}
