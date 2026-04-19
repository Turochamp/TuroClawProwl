using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class PushPlannerTests
{
    private const string RepoA = @"C:\Git\alpha";
    private const string RepoB = @"C:\Git\mango";
    private const string RepoC = @"C:\Git\zebra";

    [Fact]
    public void Empty_input_produces_empty_plan()
    {
        var plan = PushPlanner.Plan(Array.Empty<TodayFileStatus>());
        plan.Repos.Should().BeEmpty();
        plan.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Only_unpushed_files_cause_their_repos_to_be_selected()
    {
        var files = new[]
        {
            new TodayFileStatus(@"C:\Git\alpha\a.md", RepoA, false, false),       // synced
            new TodayFileStatus(@"C:\Git\alpha\b.md", RepoA, true, false),        // dirty only
            new TodayFileStatus(@"C:\Git\mango\c.md", RepoB, false, true),        // unpushed
            new TodayFileStatus(@"C:\Git\zebra\d.md", RepoC, true, true),         // both
        };

        var plan = PushPlanner.Plan(files);

        plan.Repos.Should().BeEquivalentTo(new[] { RepoB, RepoC });
    }

    [Fact]
    public void Uncommitted_only_files_do_not_cause_their_repo_to_be_selected()
    {
        var files = new[]
        {
            new TodayFileStatus(@"C:\Git\alpha\a.md", RepoA, true, false),
        };
        PushPlanner.Plan(files).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Multiple_unpushed_files_in_the_same_repo_produce_one_selection_for_that_repo()
    {
        var files = new[]
        {
            new TodayFileStatus(@"C:\Git\alpha\a.md", RepoA, false, true),
            new TodayFileStatus(@"C:\Git\alpha\b.md", RepoA, false, true),
            new TodayFileStatus(@"C:\Git\alpha\c.md", RepoA, true,  true),
        };

        PushPlanner.Plan(files).Repos.Should().ContainSingle().Which.Should().Be(RepoA);
    }

    [Fact]
    public void Plan_orders_repos_deterministically_by_ordinal_key()
    {
        var files = new[]
        {
            new TodayFileStatus(@"C:\Git\zebra\a.md", RepoC, false, true),
            new TodayFileStatus(@"C:\Git\alpha\b.md", RepoA, false, true),
            new TodayFileStatus(@"C:\Git\mango\c.md", RepoB, false, true),
        };

        PushPlanner.Plan(files).Repos.Should().ContainInOrder(RepoA, RepoB, RepoC);
    }

    [Fact]
    public void Plan_rejects_null_input()
    {
        Action act = () => PushPlanner.Plan(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
