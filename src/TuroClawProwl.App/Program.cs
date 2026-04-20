using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using TuroClawProwl.Application;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;
using TuroClawProwl.Infrastructure.Autostart;
using WinFormsApp = System.Windows.Forms.Application;
using TuroClawProwl.Infrastructure.Browser;
using TuroClawProwl.Infrastructure.Configuration;
using TuroClawProwl.Infrastructure.FileSystem;
using TuroClawProwl.Infrastructure.Gateway;
using TuroClawProwl.Infrastructure.Git;
using TuroClawProwl.Infrastructure.Logging;
using TuroClawProwl.Infrastructure.Security;
using TuroClawProwl.Infrastructure.Ssh;
using TuroClawProwl.Infrastructure.Terminal;
using TuroClawProwl.Infrastructure.Time;
using TuroClawProwl.Infrastructure.Toast;

namespace TuroClawProwl.App;

internal static class Program
{
    private static readonly string AppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TuroClawProwl");

    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        WinFormsApp.SetHighDpiMode(HighDpiMode.SystemAware);

        using var guard = new SingleInstanceGuard();
        if (!guard.IsPrimary)
        {
            SingleInstanceGuard.SignalExistingInstanceToShowSettings();
            return 1;
        }

        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(Path.Combine(AppDataDirectory, "logs"));

        using var serilogLogger = ConfigureSerilog();
        using var loggerFactory = new SerilogLoggerFactory(serilogLogger, dispose: false);

        var configStore = new JsonConfigStore(JsonConfigStore.DefaultConfigPath());
        var config = configStore.LoadAsync().GetAwaiter().GetResult();

        var tokenStore = new DpapiTokenStore(Path.Combine(AppDataDirectory, "token.dat"));
        var autostart = new RegistryAutostartManager();
        EnsureFirstRunAutostart(autostart, config);

        var uiContext = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(uiContext);
        var dispatcher = new WinFormsUiDispatcher(uiContext);

        using var trayController = new TrayController(dispatcher);

        var clock = new WallClock();
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        IGatewayClient gatewayClient = string.IsNullOrWhiteSpace(config.GatewayUrl)
            ? new NullGatewayClient()
            : new HttpGatewayClient(httpClient, tokenStore, new Uri(config.GatewayUrl));

        var gitRunner = new GitProcessRunner();
        var sshRunner = new OpenSshRunner();
        var sshTunnelLauncher = new OpenSshTunnelLauncher(
            logger: loggerFactory.CreateLogger<OpenSshTunnelLauncher>());
        var browserLauncher = new DefaultBrowserLauncher();
        var terminal = new WindowsTerminalLauncher();
        var toasts = new ToastNotificationsToastService();

        var sshTarget = new SshTarget(config.SshHost, config.SshUser);
        var trackedPaths = ResolveTrackedTodayPaths(config, loggerFactory);

        var healthUseCase = new HandleHealthPollUseCase(
            gatewayClient, clock, toasts, trayController,
            loggerFactory.CreateLogger<HandleHealthPollUseCase>());
        var resolveTodayUseCase = new ResolveTodayFileStatusesUseCase(
            gitRunner, trayController,
            loggerFactory.CreateLogger<ResolveTodayFileStatusesUseCase>());
        var pushUseCase = new PushTodayFilesUseCase(
            gitRunner, toasts,
            loggerFactory.CreateLogger<PushTodayFilesUseCase>());
        var openTuiUseCase = new OpenTuiUseCase(sshTarget, terminal);
        var openControlUiUseCase = new OpenControlUiUseCase(
            sshTarget, sshTunnelLauncher, browserLauncher, tokenStore,
            loggerFactory.CreateLogger<OpenControlUiUseCase>());
        var restartUseCase = new RestartGatewayUseCase(sshTarget, sshRunner, toasts);

        var trackedForOrchestrator = trackedPaths
            .Select(p => (AbsolutePath: p, RepoPath: FindContainingRepo(p) ?? ""))
            .Where(t => t.RepoPath.Length > 0)
            .ToArray();

        using var orchestrator = new AppOrchestrator(
            config, healthUseCase, resolveTodayUseCase, pushUseCase,
            openTuiUseCase, openControlUiUseCase, restartUseCase,
            trackedForOrchestrator,
            loggerFactory.CreateLogger<AppOrchestrator>());

        using var todaySyncer = trackedPaths.Count > 0
            ? new TodaySyncer(trackedPaths, gitRunner, loggerFactory.CreateLogger<TodaySyncer>())
            : null;
        todaySyncer?.Start();

        trayController.PushTodayFilesRequested += async (_, _) => await orchestrator.PushTodayFilesAsync();
        trayController.OpenTuiRequested += async (_, _) => await orchestrator.OpenTuiAsync();
        trayController.OpenControlUiRequested += async (_, _) => await orchestrator.OpenControlUiAsync();
        trayController.RestartGatewayRequested += async (_, _) => await orchestrator.RestartGatewayAsync();
        trayController.OpenSettingsRequested += (_, _) => ShowSettings(configStore, tokenStore, autostart);
        trayController.ExitRequested += (_, _) => WinFormsApp.Exit();

        guard.ShowSettingsRequested += (_, _) =>
            dispatcher.PostAsync(() => ShowSettings(configStore, tokenStore, autostart)).GetAwaiter().GetResult();
        guard.StartListening();

        _ = orchestrator.StartAsync();

        WinFormsApp.Run();
        return 0;
    }

    private static void EnsureFirstRunAutostart(IAutostartManager autostart, TuroClawProwlConfig config)
    {
        if (!config.AutostartEnabled) return;
        if (autostart.IsEnabled()) return;

        var exePath = Environment.ProcessPath ?? WinFormsApp.ExecutablePath;
        autostart.Enable(exePath);
    }

    private static void ShowSettings(IConfigStore configStore, ITokenStore tokenStore, IAutostartManager autostart)
    {
        using var form = new SettingsForm(configStore, tokenStore, autostart);
        form.ShowDialog();
    }

    private static IReadOnlyList<string> ResolveTrackedTodayPaths(
        TuroClawProwlConfig config,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("TodayPaths");

        if (string.IsNullOrWhiteSpace(config.TodaySkillPath) ||
            string.IsNullOrWhiteSpace(config.TodayCcaRoot) ||
            string.IsNullOrWhiteSpace(config.TodayCrmIndexPath))
        {
            logger.LogInformation("Today sync disabled (one or more Today settings are empty)");
            return Array.Empty<string>();
        }

        if (!File.Exists(config.TodaySkillPath))
        {
            logger.LogWarning("Today sync disabled: SKILL.md not found at {Path}", config.TodaySkillPath);
            return Array.Empty<string>();
        }

        string skillMarkdown;
        try
        {
            skillMarkdown = File.ReadAllText(config.TodaySkillPath);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Today sync disabled: could not read {Path}", config.TodaySkillPath);
            return Array.Empty<string>();
        }

        return TodaySkillParser.ExtractSyncPaths(
            skillMarkdown,
            config.TodayCcaRoot,
            config.TodayCrmIndexPath);
    }

    private static string? FindContainingRepo(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static Serilog.Core.Logger ConfigureSerilog()
    {
#if DEBUG
        var minLevel = LogEventLevel.Debug;
#else
        var minLevel = LogEventLevel.Information;
#endif
        return new LoggerConfiguration()
            .MinimumLevel.Is(minLevel)
            .Destructure.With(new SensitivePropertyMaskingPolicy())
            .WriteTo.File(
                path: Path.Combine(AppDataDirectory, "logs", "prowl-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}")
            .CreateLogger();
    }
}

internal sealed class NullGatewayClient : IGatewayClient
{
    public Task<GatewayPollResult> GetHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<GatewayPollResult>(new GatewayPollResult.Failure("gateway URL not configured"));
}
