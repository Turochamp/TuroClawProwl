using FluentAssertions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;
using TuroClawProwl.Infrastructure.Google;
using TuroClawProwl.Infrastructure.Tests.TestSupport;

namespace TuroClawProwl.Infrastructure.Tests.Google;

[Trait("Category", "Integration")]
public class GwsWorkspaceReaderTests
{
    private static readonly CalendarWindow Window =
        GoogleSnapshotPlan.WindowFor(new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.FromHours(2)));

    private static readonly RegistryCalendar Primary =
        new("Primary", "professional", "michael.ahs@gmail.com");

    // A .cmd stub stands in for gws: it prints the keyring banner, then a
    // payload, then exits with the code the test wants.
    //
    // The payload's quotes are NOT backslash-escaped here: cmd.exe's `echo`
    // treats `"` as a literal character and does not need (or strip) a `\`
    // before it. Escaping it (as `\"`) makes the stub emit a literal
    // backslash into stdout, which corrupts the JSON at the position that
    // should be a plain quote -- confirmed by running the stub directly
    // through cmd.exe and inspecting its raw stdout bytes.
    private static string WriteStub(TempDirectory tmp, string payload, int exitCode)
    {
        var path = Path.Combine(tmp.Path, "gws-stub.cmd");
        File.WriteAllText(path,
            "@echo off\r\n" +
            "echo Using keyring backend: keyring\r\n" +
            (payload.Length == 0 ? "" : "echo " + payload + "\r\n") +
            $"exit /b {exitCode}\r\n");
        return path;
    }

    [Fact]
    public async Task A_task_list_read_returns_the_json_payload_without_the_keyring_banner()
    {
        using var tmp = new TempDirectory();
        var reader = new GwsWorkspaceReader(WriteStub(tmp, "{\"items\":[]}", 0));

        var result = await reader.ReadTaskListAsync(GoogleSnapshotPlan.YneListId);

        result.Should().BeOfType<GoogleReadResult.Success>()
            .Which.Json.Should().Be("{\"items\":[]}");
    }

    [Fact]
    public async Task Exit_code_two_is_reported_as_not_authenticated()
    {
        using var tmp = new TempDirectory();
        var reader = new GwsWorkspaceReader(WriteStub(tmp, "", 2));

        var result = await reader.ReadTaskListAsync(GoogleSnapshotPlan.YneListId);

        result.Should().BeOfType<GoogleReadResult.NotAuthenticated>()
            .Which.Detail.Should().Contain("2");
    }

    [Fact]
    public async Task Any_other_non_zero_exit_code_is_a_plain_failure()
    {
        using var tmp = new TempDirectory();
        var reader = new GwsWorkspaceReader(WriteStub(tmp, "", 1));

        var result = await reader.ReadTaskListAsync(GoogleSnapshotPlan.YneListId);

        result.Should().BeOfType<GoogleReadResult.Failure>();
    }

    [Fact]
    public async Task A_zero_exit_with_no_json_is_a_failure_not_a_success()
    {
        using var tmp = new TempDirectory();
        var reader = new GwsWorkspaceReader(WriteStub(tmp, "", 0));

        var result = await reader.ReadTaskListAsync(GoogleSnapshotPlan.YneListId);

        result.Should().BeOfType<GoogleReadResult.Failure>()
            .Which.Detail.Should().Contain("no JSON");
    }

    [Fact]
    public async Task A_missing_executable_is_a_misconfiguration_naming_the_setting()
    {
        using var tmp = new TempDirectory();
        var reader = new GwsWorkspaceReader(Path.Combine(tmp.Path, "does-not-exist.cmd"));

        var result = await reader.ReadTaskListAsync(GoogleSnapshotPlan.YneListId);

        var misconfigured = result.Should().BeOfType<GoogleReadResult.Misconfigured>().Subject;
        misconfigured.SettingName.Should().Be(GwsWorkspaceReader.ExecutableSetting);
        misconfigured.Detail.Should().Contain("does-not-exist.cmd");
    }

    [Fact]
    public async Task A_missing_executable_never_throws()
    {
        using var tmp = new TempDirectory();
        var reader = new GwsWorkspaceReader(Path.Combine(tmp.Path, "does-not-exist.cmd"));

        Func<Task> act = () => reader.ReadCalendarWindowAsync([Primary], Window);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_calendar_read_tags_each_calendars_events_with_its_branch()
    {
        using var tmp = new TempDirectory();
        var reader = new GwsWorkspaceReader(
            WriteStub(tmp, "{\"items\":[{\"summary\":\"Standup\"}]}", 0));

        var result = await reader.ReadCalendarWindowAsync(
            [Primary, new RegistryCalendar("Family (Cozi)", "personal", "cozi@import.calendar.google.com")],
            Window);

        var json = result.Should().BeOfType<GoogleReadResult.Success>().Subject.Json;
        json.Should().Contain("\"branch\": \"professional\"");
        json.Should().Contain("\"branch\": \"personal\"");
        json.Should().Contain("Standup");
    }

    [Fact]
    public async Task A_calendar_read_records_the_window_it_asked_for()
    {
        using var tmp = new TempDirectory();
        var reader = new GwsWorkspaceReader(WriteStub(tmp, "{\"items\":[]}", 0));

        var result = await reader.ReadCalendarWindowAsync([Primary], Window);

        var json = result.Should().BeOfType<GoogleReadResult.Success>().Subject.Json;
        json.Should().Contain(Window.TimeMin);
        json.Should().Contain(Window.TimeMax);
    }

    [Fact]
    public async Task A_calendar_read_with_no_configured_calendars_is_a_failure()
    {
        using var tmp = new TempDirectory();
        var reader = new GwsWorkspaceReader(WriteStub(tmp, "{\"items\":[]}", 0));

        var result = await reader.ReadCalendarWindowAsync([], Window);

        result.Should().BeOfType<GoogleReadResult.Failure>()
            .Which.Detail.Should().Contain("no calendars");
    }

    [Fact]
    public async Task Reading_an_excluded_list_is_refused_rather_than_executed()
    {
        using var tmp = new TempDirectory();
        var reader = new GwsWorkspaceReader(WriteStub(tmp, "{\"items\":[]}", 0));

        Func<Task> act = () => reader.ReadTaskListAsync(GoogleSnapshotPlan.ReclaimListId);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task An_empty_list_id_is_rejected()
    {
        using var tmp = new TempDirectory();
        var reader = new GwsWorkspaceReader(WriteStub(tmp, "{}", 0));

        Func<Task> act = () => reader.ReadTaskListAsync("  ");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
