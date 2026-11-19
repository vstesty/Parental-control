namespace ParentalControl.Agent;

internal sealed class ExtensionDialog : Form
{
    private readonly TextBox _password = new();

    public string Password => _password.Text;
    public string Action { get; private set; } = "";

    public ExtensionDialog()
    {
        Text = "Kontrola rodzicielska";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        KeyPreview = true;
        ClientSize = new Size(360, 150);
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        var passwordLabel = new Label
        {
            Text = "Wpisz hasło:",
            AutoSize = true,
            Location = new Point(12, 15)
        };
        _password.Location = new Point(12, 38);
        _password.Size = new Size(336, 25);
        _password.UseSystemPasswordChar = true;

        var buttons = new[]
        {
            CreateButton("+15 min", "15", 12),
            CreateButton("+30 min", "30", 98),
            CreateButton("+1 godz.", "60", 184),
            CreateButton("Wyłącz", "OFF", 270)
        };

        Controls.Add(passwordLabel);
        Controls.Add(_password);
        Controls.AddRange(buttons);
    }

    private Button CreateButton(string label, string action, int left)
    {
        var button = new Button
        {
            Text = label,
            Location = new Point(left, 82),
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
}
