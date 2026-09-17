using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class BundleLayoutTests
{
    [Fact]
    public void Bundle_directory_and_manifest_name_are_the_published_contract()
    {
        BundleLayout.BundleDirectory.Should().Be("hub/bundle");
        BundleLayout.ManifestFileName.Should().Be("manifest.json");
    }

    [Fact]
    public void The_registry_path_is_repo_relative_and_carries_no_placeholders()
    {
        BundleLayout.RegistryRelativePath.Should().Be("hub/registry.md");
    }
}
