using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using TuroClawProwl.Application;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Infrastructure.Autostart;
using WinFormsApp = System.Windows.Forms.Application;
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
        var discovery = new FileSystemRepoDiscovery();
        var sshRunner = new OpenSshRunner();
        var terminal = new WindowsTerminalLauncher();
        var toasts = new ToastNotificationsToastService();
        var watcher = new FileSystemRepoWatcher();

        var sshTarget = new SshTarget(config.SshHost, config.SshUser);

        var healthUseCase = new HandleHealthPollUseCase(
            gatewayClient, clock, toasts, trayController,
            loggerFactory.CreateLogger<HandleHealthPollUseCase>());
        var reconcileUseCase = new ReconcileRepoStatesUseCase(
            discovery, gitRunner, trayController,
            loggerFactory.CreateLogger<ReconcileRepoStatesUseCase>());
        var pushUseCase = new PushUnpushedReposUseCase(
            gitRunner, toasts,
            loggerFactory.CreateLogger<PushUnpushedReposUseCase>());
        var openTuiUseCase = new OpenTuiUseCase(sshTarget, terminal);
        var restartUseCase = new RestartGatewayUseCase(sshTarget, sshRunner, toasts);

        using var orchestrator = new AppOrchestrator(
            config, healthUseCase, reconcileUseCase, pushUseCase, openTuiUseCase, restartUseCase, watcher,
            loggerFactory.CreateLogger<AppOrchestrator>());

        trayController.PushUnpushedRequested += async (_, _) => await orchestrator.PushUnpushedAsync();
        trayController.OpenTuiRequested += async (_, _) => await orchestrator.OpenTuiAsync();
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
