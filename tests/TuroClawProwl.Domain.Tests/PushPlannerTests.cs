using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class PushPlannerTests
{
    [Fact]
    public void Empty_state_dictionary_produces_empty_plan()
    {
        var plan = PushPlanner.Plan(new Dictionary<string, RepoState>());
        plan.Repos.Should().BeEmpty();
        plan.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Only_repos_with_unpushed_commits_are_selected()
    {
        var states = new Dictionary<string, RepoState>
        {
            ["repo-clean"]               = new(0, false, true),
            ["repo-uncommitted-only"]    = new(0, true, true),
            ["repo-no-upstream"]         = new(0, false, false),
            ["repo-unpushed"]            = new(2, false, true),
            ["repo-dirty-and-unpushed"]  = new(1, true, true),
        };

        var plan = PushPlanner.Plan(states);

        plan.Repos.Should().BeEquivalentTo(new[] { "repo-unpushed", "repo-dirty-and-unpushed" });
    }

    [Fact]
    public void Uncommitted_only_repo_is_excluded_from_plan()
    {
        var states = new Dictionary<string, RepoState>
        {
            ["dirty"] = new(0, true, true),
        };
        PushPlanner.Plan(states).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void No_upstream_with_zero_unpushed_is_excluded()
    {
        var states = new Dictionary<string, RepoState>
        {
            ["lonely"] = new(0, false, false),
        };
        PushPlanner.Plan(states).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Plan_orders_repos_deterministically_by_ordinal_key()
    {
        var states = new Dictionary<string, RepoState>
        {
            ["zebra"] = new(1, false, true),
            ["alpha"] = new(1, false, true),
            ["mango"] = new(1, false, true),
        };
        PushPlanner.Plan(states).Repos.Should().ContainInOrder("alpha", "mango", "zebra");
    }

    [Fact]
    public void Planner_rejects_null_states()
    {
        Action act = () => PushPlanner.Plan(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
