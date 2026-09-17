using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class BundleSourcePlanTests
{
    [Fact]
    public void The_only_watched_hub_file_is_the_registry()
    {
        BundleSourcePlan.WatchedRelativePaths.Should().Equal("hub/registry.md");
    }
}
