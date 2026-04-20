using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;

namespace TuroClawProwl.App;

public sealed class TrayController : ITrayView, IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _pushItem;
    private readonly ToolStripMenuItem _openTuiItem;
    private readonly ToolStripMenuItem _openControlUiItem;
    private readonly ToolStripMenuItem _restartItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _exitItem;

    private GatewayHealth _gateway = new GatewayHealth.NeverReached();
    private IReadOnlyCollection<TodayFileStatus> _todayFiles = Array.Empty<TodayFileStatus>();

    public event EventHandler? PushTodayFilesRequested;
    public event EventHandler? OpenTuiRequested;
    public event EventHandler? OpenControlUiRequested;
    public event EventHandler? RestartGatewayRequested;
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? ExitRequested;

    public TrayController(IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;

        _pushItem = new ToolStripMenuItem("Push Today files");
        _pushItem.Click += (_, _) => PushTodayFilesRequested?.Invoke(this, EventArgs.Empty);
        _pushItem.Enabled = false;

        _openTuiItem = new ToolStripMenuItem("Open TUI");
        _openTuiItem.Click += (_, _) => OpenTuiRequested?.Invoke(this, EventArgs.Empty);

        _openControlUiItem = new ToolStripMenuItem("Open Control UI");
        _openControlUiItem.Click += (_, _) => OpenControlUiRequested?.Invoke(this, EventArgs.Empty);

        _restartItem = new ToolStripMenuItem("Restart Gateway...");
        _restartItem.Click += (_, _) =>
        {
            var confirm = MessageBox.Show(
                "Restart the OpenClaw Gateway on the Mac?",
                "Restart Gateway",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm == DialogResult.Yes)
                RestartGatewayRequested?.Invoke(this, EventArgs.Empty);
        };

        _settingsItem = new ToolStripMenuItem("Settings...");
        _settingsItem.Click += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

        _exitItem = new ToolStripMenuItem("Exit");
        _exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _pushItem,
            _openTuiItem,
            _openControlUiItem,
            _restartItem,
            new ToolStripSeparator(),
            _settingsItem,
            new ToolStripSeparator(),
            _exitItem,
        });

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.ForColor(TrayColor.Grey),
            Text = "TuroClawProwl",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

        Refresh();
    }

    public Task SetGatewayHealthAsync(GatewayHealth health, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(health);
        return _dispatcher.PostAsync(() =>
        {
            _gateway = health;
            Refresh();
        }, cancellationToken);
    }

    public Task SetTodayFileStatusesAsync(
        IReadOnlyCollection<TodayFileStatus> statuses,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        return _dispatcher.PostAsync(() =>
        {
            _todayFiles = statuses;
            Refresh();
        }, cancellationToken);
    }

    private void Refresh()
    {
        var color = TrayColorResolver.Resolve(_gateway, _todayFiles);
        var tooltip = TooltipComposer.Compose(_gateway, _todayFiles);

        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Icon = TrayIconFactory.ForColor(color);
        _notifyIcon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;

        _pushItem.Enabled = _todayFiles.Any(f => f.NeedsAttention);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}
