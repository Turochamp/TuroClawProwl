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
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        var retryPipeline = HttpGatewayClient.BuildDefaultRetryPipeline(
            loggerFactory.CreateLogger("TuroClawProwl.Infrastructure.Gateway.HttpGatewayClient"));
        IGatewayClient gatewayClient = string.IsNullOrWhiteSpace(config.GatewayUrl)
            ? new NullGatewayClient()
            : new HttpGatewayClient(httpClient, tokenStore, new Uri(config.GatewayUrl), retryPipeline);

        var gitRunner = new GitProcessRunner();
        var sshRunner = new OpenSshRunner();
        var browserLauncher = new DefaultBrowserLauncher();
        var toasts = new ToastNotificationsToastService();

        var sshTarget = new SshTarget(config.SshHost, config.SshUser);
        var trackedPaths = TodayPathsResolver.Resolve(config, loggerFactory);

        var healthUseCase = new HandleHealthPollUseCase(
            gatewayClient, clock, toasts, trayController,
            loggerFactory.CreateLogger<HandleHealthPollUseCase>());
        var resolveTodayUseCase = new ResolveTodayFileStatusesUseCase(
            gitRunner, trayController,
            loggerFactory.CreateLogger<ResolveTodayFileStatusesUseCase>());
        var pushUseCase = new PushTodayFilesUseCase(
            gitRunner, toasts,
            loggerFactory.CreateLogger<PushTodayFilesUseCase>());
        var openControlUiUseCase = new OpenControlUiUseCase(
            new Uri(config.GatewayUrl), browserLauncher, tokenStore,
            loggerFactory.CreateLogger<OpenControlUiUseCase>());
        var restartUseCase = new RestartGatewayUseCase(sshTarget, sshRunner, toasts);

        var trackedForOrchestrator = TodayPathsResolver.ToOrchestratorPairs(trackedPaths);

        using var orchestrator = new AppOrchestrator(
            config, healthUseCase, resolveTodayUseCase, pushUseCase,
            openControlUiUseCase, restartUseCase,
            trackedForOrchestrator,
            loggerFactory.CreateLogger<AppOrchestrator>());

        using var todaySyncerHandle = new TodaySyncerHandle(gitRunner, toasts, loggerFactory);
        todaySyncerHandle.Start(trackedPaths);

        using var liveConfigApplier = new LiveConfigApplier(
            config, gatewayClient, openControlUiUseCase, restartUseCase,
            orchestrator, todaySyncerHandle, loggerFactory);
        configStore.ConfigSaved += (_, c) => _ = liveConfigApplier.ApplyAsync(c);

        trayController.PushTodayFilesRequested += async (_, _) => await orchestrator.PushTodayFilesAsync();
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
            .Filter.With(new RepeatedMessageDeduplicator(TimeSpan.FromMinutes(5)))
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
