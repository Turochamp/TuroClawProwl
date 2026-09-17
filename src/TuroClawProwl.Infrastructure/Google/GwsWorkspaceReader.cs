using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;
using TuroClawProwl.Infrastructure.Process;

namespace TuroClawProwl.Infrastructure.Google;

// The Google credential lives on this machine only, so the publisher is the one
// place these reads can happen. gws exit code 2 means not authenticated, which
// is a failed read the caller retains the previous snapshot for -- never a crash.
public sealed class GwsWorkspaceReader : IGoogleWorkspaceReader
{
    public const string ExecutableSetting = "gwsExecutablePath";

    private const int NotAuthenticatedExitCode = 2;
    private const int MaxTaskResults = 100;
    private const int MaxCalendarResults = 250;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly ILogger<GwsWorkspaceReader> _logger;

    private volatile string _executablePath;

    public GwsWorkspaceReader(string gwsExecutablePath, ILogger<GwsWorkspaceReader>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gwsExecutablePath);

        _executablePath = gwsExecutablePath;
        _logger = logger ?? NullLogger<GwsWorkspaceReader>.Instance;
    }

    // Live-reloaded from Settings, the same way HttpGatewayClient takes a new
    // base address, so a corrected path takes effect without a restart.
    public void SetExecutablePath(string gwsExecutablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gwsExecutablePath);
        _executablePath = gwsExecutablePath;
    }

    public async Task<GoogleReadResult> ReadTaskListAsync(
        string listId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listId);

        if (GoogleSnapshotPlan.IsExcludedList(listId))
        {
            throw new ArgumentException(
                $"List {listId} is excluded from snapshots and must never be read", nameof(listId));
        }

        var parameters = JsonSerializer.Serialize(new JsonObject
        {
            ["tasklist"] = listId,
            ["showCompleted"] = false,
            ["maxResults"] = MaxTaskResults,
        });

        return await RunAsync(
            ["tasks", "tasks", "list", "--params", parameters, "--format", "json"],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GoogleReadResult> ReadCalendarWindowAsync(
        IReadOnlyList<RegistryCalendar> calendars,
        CalendarWindow window,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendars);
        ArgumentNullException.ThrowIfNull(window);

        if (calendars.Count == 0)
            return new GoogleReadResult.Failure("no calendars are configured in the registry");

        var calendarNodes = new JsonArray();
        var errorNodes = new JsonArray();

        foreach (var calendar in calendars)
        {
            var parameters = JsonSerializer.Serialize(new JsonObject
            {
                ["calendarId"] = calendar.CalendarId,
                ["timeMin"] = window.TimeMin,
                ["timeMax"] = window.TimeMax,
                ["singleEvents"] = true,
                ["orderBy"] = "startTime",
                ["maxResults"] = MaxCalendarResults,
            });

            var read = await RunAsync(
                ["calendar", "events", "list", "--params", parameters, "--format", "json"],
                cancellationToken).ConfigureAwait(false);

            switch (read)
            {
                case GoogleReadResult.Success success:
                    calendarNodes.Add(new JsonObject
                    {
                        ["id"] = calendar.CalendarId,
                        ["name"] = calendar.Name,
                        ["branch"] = calendar.Branch,
                        ["events"] = JsonNode.Parse(success.Json),
                    });
                    break;

                // A single unreachable calendar must not lose the others.
                case GoogleReadResult.Misconfigured misconfigured:
                    return misconfigured;

                default:
                    errorNodes.Add(new JsonObject
                    {
                        ["id"] = calendar.CalendarId,
                        ["detail"] = DetailOf(read),
                    });
                    _logger.LogWarning(
                        "Calendar {CalendarId} could not be read: {Detail}",
                        calendar.CalendarId, DetailOf(read));
                    break;
            }
        }

        if (calendarNodes.Count == 0)
            return new GoogleReadResult.Failure("every configured calendar failed to read");

        var document = new JsonObject
        {
            ["window"] = new JsonObject
            {
                ["time_min"] = window.TimeMin,
                ["time_max"] = window.TimeMax,
            },
            ["calendars"] = calendarNodes,
            ["errors"] = errorNodes,
        };

        return new GoogleReadResult.Success(document.ToJsonString(WriteOptions));
    }

    private async Task<GoogleReadResult> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        ProcessRunner.Result result;
        try
        {
            result = await ProcessRunner
                .RunAsync(_executablePath, args, workingDirectory: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            return new GoogleReadResult.Misconfigured(
                ExecutableSetting, $"{_executablePath} could not be started: {ex.Message}");
        }
        catch (System.IO.FileNotFoundException ex)
        {
            return new GoogleReadResult.Misconfigured(
                ExecutableSetting, $"{_executablePath} was not found: {ex.Message}");
        }

        if (result.ExitCode == NotAuthenticatedExitCode)
        {
            return new GoogleReadResult.NotAuthenticated(
                "gws exited with code " + result.ExitCode.ToString(CultureInfo.InvariantCulture) +
                "; run `gws auth login`");
        }

        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            return new GoogleReadResult.Failure(
                $"gws exited with code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}: {error.Trim()}");
        }

        var json = GwsOutputParser.ExtractJson(result.StdOut);
        if (json is null)
            return new GoogleReadResult.Failure("gws exited 0 but printed no JSON payload");

        return new GoogleReadResult.Success(json);
    }

    private static string DetailOf(GoogleReadResult read) => read switch
    {
        GoogleReadResult.NotAuthenticated n => n.Detail,
        GoogleReadResult.Failure f => f.Detail,
        GoogleReadResult.Misconfigured m => m.Detail,
        GoogleReadResult.Success => "success",
        _ => throw new ArgumentException(
            $"Unknown Google read result variant: {read.GetType().Name}", nameof(read)),
    };
}
