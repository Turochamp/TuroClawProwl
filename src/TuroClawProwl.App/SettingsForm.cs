using System.Drawing;
using TuroClawProwl.Application;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Infrastructure.Gateway;
using TuroClawProwl.Infrastructure.Ssh;
using WinFormsApp = System.Windows.Forms.Application;

namespace TuroClawProwl.App;

public sealed class SettingsForm : Form
{
    private static readonly Font DialogFont = new("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Size ButtonSize = new(96, 28);
    private static readonly Size TestButtonSize = new(80, 26);
    private static readonly Color OkColor = Color.FromArgb(16, 124, 16);
    private static readonly Color ErrColor = Color.FromArgb(196, 43, 28);
    private static readonly Color MutedColor = Color.FromArgb(96, 96, 96);

    private readonly IConfigStore _configStore;
    private readonly ITokenStore _tokenStore;
    private readonly IAutostartManager _autostart;

    // Inputs
    private readonly TextBox _gatewayUrl = new() { Dock = DockStyle.Fill };
    private readonly TextBox _token = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _sshHost = new() { Dock = DockStyle.Fill };
    private readonly TextBox _sshUser = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _pollSeconds = new() { Minimum = 5, Maximum = 600, Value = 15, Width = 70 };
    private readonly CheckBox _autostartEnabled = new() { Text = "Start with Windows", AutoSize = true };
    private readonly TextBox _todaySkillPath = new() { Dock = DockStyle.Fill };
    private readonly TextBox _todayCcaRoot = new() { Dock = DockStyle.Fill };
    private readonly TextBox _todayCrmIndex = new() { Dock = DockStyle.Fill };

    // Test buttons + status labels
    private readonly Button _testGatewayButton = new() { Text = "Test", Size = TestButtonSize, UseVisualStyleBackColor = true };
    private readonly Label _gatewayStatus = new() { AutoSize = true, ForeColor = MutedColor };
    private readonly Button _testSshButton = new() { Text = "Test", Size = TestButtonSize, UseVisualStyleBackColor = true };
    private readonly Label _sshStatus = new() { AutoSize = true, ForeColor = MutedColor };

    // Commit buttons
    private readonly Button _saveButton = new() { Text = "Save", DialogResult = DialogResult.OK, Size = ButtonSize, UseVisualStyleBackColor = true };
    private readonly Button _cancelButton = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = ButtonSize, UseVisualStyleBackColor = true };

    public SettingsForm(IConfigStore configStore, ITokenStore tokenStore, IAutostartManager autostart)
    {
        ArgumentNullException.ThrowIfNull(configStore);
        ArgumentNullException.ThrowIfNull(tokenStore);
        ArgumentNullException.ThrowIfNull(autostart);

        _configStore = configStore;
        _tokenStore = tokenStore;
        _autostart = autostart;

        Text = "TuroClawProwl Settings";
        Font = DialogFont;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = true;
        ClientSize = new Size(640, 820);
        MinimumSize = new Size(620, 740);

        BuildLayout();

        _testGatewayButton.Click += async (_, _) => await OnTestGatewayAsync();
        _testSshButton.Click += async (_, _) => await OnTestSshAsync();
        _saveButton.Click += async (_, _) => await OnSaveAsync();

        Load += async (_, _) => await LoadFromStoresAsync();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(12, 12, 12, 0),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildGatewayGroup(), 0, 0);
        root.Controls.Add(BuildMonitoringGroup(), 0, 1);
        root.Controls.Add(BuildTodayGroup(), 0, 2);

        var buttonBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
        };
        var buttonFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 12, 12, 16),
        };
        _saveButton.Margin = new Padding(8, 0, 0, 0);
        _cancelButton.Margin = new Padding(0);
        buttonFlow.Controls.Add(_cancelButton);
        buttonFlow.Controls.Add(_saveButton);
        buttonBar.Controls.Add(buttonFlow);

        Controls.Add(root);
        Controls.Add(buttonBar);

        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private GroupBox BuildGatewayGroup()
    {
        var group = NewGroupBox("Gateway");
        var grid = NewFieldGrid();

        AddField(grid, "Gateway URL:", _gatewayUrl, _testGatewayButton);
        AddField(grid, "Token:", _token);
        AddStatusRow(grid, _gatewayStatus);

        group.Controls.Add(grid);
        return group;
    }

    private GroupBox BuildMonitoringGroup()
    {
        var group = NewGroupBox("Monitoring");
        var grid = NewFieldGrid();

        AddField(grid, "SSH host:", _sshHost);
        AddField(grid, "SSH user:", _sshUser, _testSshButton);
        AddStatusRow(grid, _sshStatus);
        AddField(grid, "Poll interval (s):", _pollSeconds);
        AddCheckBoxRow(grid, _autostartEnabled);

        group.Controls.Add(grid);
        return group;
    }

    private GroupBox BuildTodayGroup()
    {
        var group = NewGroupBox("Today repo files");
        var grid = NewFieldGrid();

        AddField(grid, "SKILL.md:", _todaySkillPath);
        AddField(grid, "CCA_ROOT:", _todayCcaRoot);
        AddField(grid, "CRM index:", _todayCrmIndex);

        group.Controls.Add(grid);
        return group;
    }

    private static GroupBox NewGroupBox(string title) => new()
    {
        Text = title,
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(0, 0, 0, 10),
        Padding = new Padding(10, 8, 10, 10),
    };

    private static TableLayoutPanel NewFieldGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        return grid;
    }

    private static void AddField(TableLayoutPanel grid, string label, Control control, Control? trailing = null)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var row = grid.RowCount++;

        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 6, 6),
        }, 0, row);

        control.Margin = new Padding(0, 3, 6, 3);
        grid.Controls.Add(control, 1, row);

        if (trailing is not null)
        {
            trailing.Margin = new Padding(0, 2, 0, 2);
            grid.Controls.Add(trailing, 2, row);
        }
    }

    private static void AddStatusRow(TableLayoutPanel grid, Label status)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var row = grid.RowCount++;
        status.Margin = new Padding(0, 0, 0, 6);
        grid.Controls.Add(status, 1, row);
        grid.SetColumnSpan(status, 2);
    }

    private static void AddCheckBoxRow(TableLayoutPanel grid, CheckBox check)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var row = grid.RowCount++;
        check.Margin = new Padding(0, 4, 0, 4);
        grid.Controls.Add(check, 1, row);
        grid.SetColumnSpan(check, 2);
    }

    private async Task LoadFromStoresAsync()
    {
        var config = await _configStore.LoadAsync();
        _gatewayUrl.Text = config.GatewayUrl;
        _sshHost.Text = config.SshHost;
        _sshUser.Text = config.SshUser;
        _pollSeconds.Value = Math.Clamp((decimal)config.PollInterval.TotalSeconds, 5, 600);
        _autostartEnabled.Checked = _autostart.IsEnabled();
        _todaySkillPath.Text = config.TodaySkillPath;
        _todayCcaRoot.Text = config.TodayCcaRoot;
        _todayCrmIndex.Text = config.TodayCrmIndexPath;

        var token = await _tokenStore.GetTokenAsync();
        _token.Text = token ?? "";
    }

    private async Task OnTestGatewayAsync()
    {
        if (!Uri.TryCreate(_gatewayUrl.Text.Trim(), UriKind.Absolute, out var uri))
        {
            SetStatus(_gatewayStatus, "Enter a valid URL first.", isError: true);
            return;
        }

        _testGatewayButton.Enabled = false;
        SetStatus(_gatewayStatus, "Testing...", isError: false, muted: true);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var tokenStore = new StaticTokenStore(_token.Text);
            var client = new HttpGatewayClient(http, tokenStore, uri);
            var result = await client.GetHealthAsync();

            switch (result)
            {
                case GatewayPollResult.Success s:
                    var suffix = s.Uptime is { } up ? $" (uptime {up})" : "";
                    SetStatus(_gatewayStatus, "OK - gateway reachable" + suffix, isError: false);
                    break;
                case GatewayPollResult.Failure f:
                    SetStatus(_gatewayStatus, "Failed: " + f.Reason, isError: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            SetStatus(_gatewayStatus, "Error: " + ex.Message, isError: true);
        }
        finally
        {
            _testGatewayButton.Enabled = true;
        }
    }

    private async Task OnTestSshAsync()
    {
        if (string.IsNullOrWhiteSpace(_sshHost.Text) || string.IsNullOrWhiteSpace(_sshUser.Text))
        {
            SetStatus(_sshStatus, "Enter SSH host and user first.", isError: true);
            return;
        }

        _testSshButton.Enabled = false;
        SetStatus(_sshStatus, "Testing...", isError: false, muted: true);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var runner = new OpenSshRunner();
            var target = new SshTarget(_sshHost.Text.Trim(), _sshUser.Text.Trim());
            var result = await runner.RunCommandAsync(target, "echo", new[] { "turoclawprowl-test" }, cts.Token);

            switch (result)
            {
                case SshCommandResult.Success:
                    SetStatus(_sshStatus, "OK - exit 0", isError: false);
                    break;
                case SshCommandResult.Failure f:
                    SetStatus(_sshStatus, $"Failed: exit {f.ExitCode}", isError: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            SetStatus(_sshStatus, "Error: " + ex.Message, isError: true);
        }
        finally
        {
            _testSshButton.Enabled = true;
        }
    }

    private static void SetStatus(Label status, string text, bool isError, bool muted = false)
    {
        status.Text = text;
        status.ForeColor = muted ? MutedColor : isError ? ErrColor : OkColor;
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

    private sealed class StaticTokenStore : ITokenStore
    {
        private readonly string? _token;
        public StaticTokenStore(string? token) => _token = token;
        public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(_token);
        public Task SetTokenAsync(string token, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
