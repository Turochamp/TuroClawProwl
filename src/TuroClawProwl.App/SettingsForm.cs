using TuroClawProwl.Application;
using TuroClawProwl.Application.Ports;
using WinFormsApp = System.Windows.Forms.Application;

namespace TuroClawProwl.App;

public sealed class SettingsForm : Form
{
    private readonly IConfigStore _configStore;
    private readonly ITokenStore _tokenStore;
    private readonly IAutostartManager _autostart;

    private readonly TextBox _gatewayUrl = new() { Width = 320 };
    private readonly TextBox _token = new() { Width = 320, UseSystemPasswordChar = true };
    private readonly TextBox _sshHost = new() { Width = 320 };
    private readonly TextBox _sshUser = new() { Width = 320 };
    private readonly TextBox _reposRoot = new() { Width = 320 };
    private readonly NumericUpDown _pollSeconds = new() { Minimum = 5, Maximum = 600, Value = 15, Width = 80 };
    private readonly CheckBox _autostartEnabled = new() { Text = "Start with Windows" };
    private readonly TextBox _todaySkillPath = new() { Width = 320 };
    private readonly TextBox _todayCcaRoot = new() { Width = 320 };
    private readonly TextBox _todayCrmIndex = new() { Width = 320 };
    private readonly Button _saveButton = new() { Text = "Save", DialogResult = DialogResult.OK, Width = 90 };
    private readonly Button _cancelButton = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };

    public SettingsForm(IConfigStore configStore, ITokenStore tokenStore, IAutostartManager autostart)
    {
        ArgumentNullException.ThrowIfNull(configStore);
        ArgumentNullException.ThrowIfNull(tokenStore);
        ArgumentNullException.ThrowIfNull(autostart);

        _configStore = configStore;
        _tokenStore = tokenStore;
        _autostart = autostart;

        Text = "TuroClawProwl Settings";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(480, 360);

        BuildLayout();

        _saveButton.Click += async (_, _) => await OnSaveAsync();

        Load += async (_, _) => await LoadFromStoresAsync();
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(16),
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));

        void AddRow(string label, Control control)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 6),
            });
            control.Margin = new Padding(0, 4, 0, 4);
            layout.Controls.Add(control);
        }

        AddRow("Gateway URL:", _gatewayUrl);
        AddRow("Token:", _token);
        AddRow("SSH host:", _sshHost);
        AddRow("SSH user:", _sshUser);
        AddRow("Repos root:", _reposRoot);
        AddRow("Poll interval (s):", _pollSeconds);
        AddRow("Autostart:", _autostartEnabled);
        AddRow("Today SKILL.md:", _todaySkillPath);
        AddRow("Today CCA_ROOT:", _todayCcaRoot);
        AddRow("Today CRM index:", _todayCrmIndex);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Top,
            Padding = new Padding(16, 0, 16, 16),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_saveButton);

        Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private async Task LoadFromStoresAsync()
    {
        var config = await _configStore.LoadAsync();
        _gatewayUrl.Text = config.GatewayUrl;
        _sshHost.Text = config.SshHost;
        _sshUser.Text = config.SshUser;
        _reposRoot.Text = config.ReposRoot;
        _pollSeconds.Value = Math.Clamp((decimal)config.PollInterval.TotalSeconds, 5, 600);
        _autostartEnabled.Checked = _autostart.IsEnabled();
        _todaySkillPath.Text = config.TodaySkillPath;
        _todayCcaRoot.Text = config.TodayCcaRoot;
        _todayCrmIndex.Text = config.TodayCrmIndexPath;

        var token = await _tokenStore.GetTokenAsync();
        _token.Text = token ?? "";
    }

    private async Task OnSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_gatewayUrl.Text) ||
            !Uri.TryCreate(_gatewayUrl.Text, UriKind.Absolute, out _))
        {
            MessageBox.Show("Gateway URL must be a valid absolute URL.", "Invalid input",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        var config = new TuroClawProwlConfig
        {
            GatewayUrl = _gatewayUrl.Text.Trim(),
            SshHost = _sshHost.Text.Trim(),
            SshUser = _sshUser.Text.Trim(),
            ReposRoot = _reposRoot.Text.Trim(),
            PollInterval = TimeSpan.FromSeconds((double)_pollSeconds.Value),
            AutostartEnabled = _autostartEnabled.Checked,
            TodaySkillPath = _todaySkillPath.Text.Trim(),
            TodayCcaRoot = _todayCcaRoot.Text.Trim(),
            TodayCrmIndexPath = _todayCrmIndex.Text.Trim(),
        };

        await _configStore.SaveAsync(config);
        if (!string.IsNullOrWhiteSpace(_token.Text))
            await _tokenStore.SetTokenAsync(_token.Text);

        var exePath = Environment.ProcessPath ?? WinFormsApp.ExecutablePath;
        if (_autostartEnabled.Checked)
            _autostart.Enable(exePath);
        else
            _autostart.Disable();
    }
}
