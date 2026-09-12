using FluentAssertions;
using Moq;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.Tests.TestSupport;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Tests.UseCases;

public class PublishStateBundleUseCaseTests
{
    private const string HubRoot = @"C:\Git\ClaudeCodeAssistants";

    private static readonly DateTimeOffset Now = new(2026, 9, 12, 6, 30, 0, TimeSpan.Zero);

    private const string Registry = """
        ## Active set

        | CCA | Branch | Cadence | Status | State file |
        |-----|--------|---------|--------|-----------|
        | YNE | professional | weekly | active | CCA-YNE/STATE.md |
        | SmoEms | professional | weekly | flagged | CCA-SmoEms/STATE.md |

        ## Quiet (kept on disk, excluded from all roll-ups)

        | CCA | Reason |
        |-----|--------|
        | ChromeBookmarks | Utility, not goal-bearing |
        """;

    private readonly Mock<ISourceFileReader> _files = new(MockBehavior.Strict);
    private readonly Mock<IGitFileFactsReader> _gitFacts = new(MockBehavior.Strict);
    private readonly Mock<IBundlePublisher> _publisher = new(MockBehavior.Strict);
    private readonly FixedClock _clock = new(Now);

    private BundlePayload? _captured;

    private PublishStateBundleUseCase CreateUseCase() =>
        new(_files.Object, _gitFacts.Object, _publisher.Object, _clock);

    private static string Abs(string relative) =>
        Path.Combine(HubRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private void SetUpFile(string relative, string content, DateTimeOffset modified) =>
        _files.Setup(f => f.ReadAsync(Abs(relative), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SourceReadResult.Found(content, modified));

    private void SetUpCleanTrackedFacts() =>
        _gitFacts.Setup(g => g.GetFileFactsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitFileFacts(
                InRepository: true, Tracked: true, Dirty: false,
                LastCommitAuthorDate: Now.AddDays(-1)));

    private void SetUpPublisher(string branch)
    {
        _publisher.Setup(p => p.GetSourceBranchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(branch);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<BundlePayload>(), It.IsAny<CancellationToken>()))
            .Callback<BundlePayload, CancellationToken>((p, _) => _captured = p)
            .ReturnsAsync(new BundlePublishResult.Success("abc1234", 5));
    }

    private void SetUpFullHappyPath()
    {
        SetUpPublisher("feature/humanize-design");
        SetUpCleanTrackedFacts();
        SetUpFile(BundleLayout.RegistryRelativePath, Registry, Now.AddMinutes(-10));
        SetUpFile(BundleLayout.CrmIndexRelativePath, "# Contacts", Now.AddHours(-3));
        SetUpFile(BundleLayout.WeeklyRelativePath(Now.ToLocalTime()), "# Week", Now.AddDays(-2));
        SetUpFile("CCA-YNE/STATE.md", "# YNE state", Now.AddHours(-1));
        SetUpFile("CCA-SmoEms/STATE.md", "# SmoEms state", Now.AddHours(-5));
    }

    [Fact]
    public async Task Active_and_flagged_cca_state_files_are_collected_from_the_registry()
    {
        SetUpFullHappyPath();

        await CreateUseCase().ExecuteAsync(new StateBundleRequest(HubRoot, "TuroClawProwl/0.3.0"));

        _captured.Should().NotBeNull();
        _captured!.Files.Select(f => f.RelativePath).Should().Contain(new[]
        {
            "cca/YNE.STATE.md",
            "cca/SmoEms.STATE.md",
        });
    }

    [Fact]
    public async Task A_quiet_cca_is_not_collected()
    {
        SetUpFullHappyPath();

        await CreateUseCase().ExecuteAsync(new StateBundleRequest(HubRoot, "TuroClawProwl/0.3.0"));

        _captured!.Files.Should().NotContain(f => f.RelativePath.Contains("ChromeBookmarks", StringComparison.Ordinal));
        _captured!.Manifest.Sources.Should().NotContain(s => s.Id.Contains("ChromeBookmarks", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Registry_crm_index_and_this_weeks_week_file_are_collected()
    {
        SetUpFullHappyPath();

        await CreateUseCase().ExecuteAsync(new StateBundleRequest(HubRoot, "TuroClawProwl/0.3.0"));

        _captured!.Files.Select(f => f.RelativePath).Should().Contain(new[]
        {
            BundleLayout.RegistryBundlePath,
            BundleLayout.CrmIndexBundlePath,
            BundleLayout.WeeklyBundlePath(Now.ToLocalTime()),
        });
    }

    [Fact]
    public async Task The_collected_content_is_what_the_reader_returned()
    {
        SetUpFullHappyPath();

        await CreateUseCase().ExecuteAsync(new StateBundleRequest(HubRoot, "TuroClawProwl/0.3.0"));

        _captured!.Files.Single(f => f.RelativePath == "cca/YNE.STATE.md").Content
            .Should().Be("# YNE state");
    }

    [Fact]
    public async Task The_manifest_records_the_schema_version_published_at_publisher_and_source_branch()
    {
        SetUpFullHappyPath();

        await CreateUseCase().ExecuteAsync(new StateBundleRequest(HubRoot, "TuroClawProwl/0.3.0"));

        _captured!.Manifest.SchemaVersion.Should().Be(BundleManifest.CurrentSchemaVersion);
        _captured!.Manifest.PublishedAt.Should().Be(Now);
        _captured!.Manifest.Publisher.Should().Be("TuroClawProwl/0.3.0");
        _captured!.Manifest.SourceBranch.Should().Be("feature/humanize-design");
    }

    [Fact]
    public async Task Every_collected_source_gets_a_manifest_entry_with_its_repo_relative_path()
    {
        SetUpFullHappyPath();

        await CreateUseCase().ExecuteAsync(new StateBundleRequest(HubRoot, "TuroClawProwl/0.3.0"));

        _captured!.Manifest.Sources.Should().HaveCount(5);
        _captured!.Manifest.Failures.Should().BeEmpty();
        _captured!.Manifest.Sources.Single(s => s.Id == "cca/YNE").Path
            .Should().Be("CCA-YNE/STATE.md");
        _captured!.Manifest.Sources.Single(s => s.Id == "registry").Modified
            .Should().Be(Now.AddMinutes(-10));
        _captured!.Manifest.Sources.Single(s => s.Id == "registry").Committed
            .Should().Be(Now.AddDays(-1));
    }

    [Fact]
    public async Task Every_manifest_source_states_the_bundle_path_its_file_was_written_to()
    {
        SetUpFullHappyPath();

        await CreateUseCase().ExecuteAsync(new StateBundleRequest(HubRoot, "TuroClawProwl/0.3.0"));

        _captured!.Manifest.Sources.Single(s => s.Id == "registry").BundlePath
            .Should().Be("registry.md");
        _captured!.Manifest.Sources.Single(s => s.Id == "crm-index").BundlePath
            .Should().Be("crm-index.md");
        _captured!.Manifest.Sources.Single(s => s.Id == "cca/YNE").BundlePath
            .Should().Be("cca/YNE.STATE.md");
        _captured!.Manifest.Sources
            .Single(s => s.Id == BundleLayout.WeeklySourceId(Now.ToLocalTime())).BundlePath
            .Should().Be(BundleLayout.WeeklyBundlePath(Now.ToLocalTime()));
    }

    [Fact]
    public async Task Every_manifest_source_bundle_path_matches_a_file_actually_in_the_payload()
    {
        SetUpFullHappyPath();

        await CreateUseCase().ExecuteAsync(new StateBundleRequest(HubRoot, "TuroClawProwl/0.3.0"));

        var payloadPaths = _captured!.Files.Select(f => f.RelativePath).ToArray();
        _captured!.Manifest.Sources.Select(s => s.BundlePath).Should().BeEquivalentTo(payloadPaths);
    }

    [Fact]
    public async Task A_file_source_carries_the_same_value_for_modified_and_verified()
    {
        SetUpFullHappyPath();

        await CreateUseCase().ExecuteAsync(new StateBundleRequest(HubRoot, "TuroClawProwl/0.3.0"));

        _captured!.Manifest.Sources.Should().AllSatisfy(s => s.Verified.Should().Be(s.Modified));
        _captured!.Manifest.Sources.Single(s => s.Id == "cca/YNE").Verified
            .Should().Be(Now.AddHours(-1));
    }

    [Fact]
    public async Task The_publisher_result_is_returned_unchanged()
    {
        SetUpFullHappyPath();

        var result = await CreateUseCase().ExecuteAsync(new StateBundleRequest(HubRoot, "TuroClawProwl/0.3.0"));

        result.Should().BeOfType<BundlePublishResult.Success>()
            .Which.CommitSha.Should().Be("abc1234");
    }

    [Fact]
    public async Task Null_request_is_rejected()
    {
        var useCase = CreateUseCase();

        Func<Task> act = () => useCase.ExecuteAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
