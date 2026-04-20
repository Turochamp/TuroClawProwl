using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class TodaySkillParserTests
{
    private const string CcaRoot = @"C:\Git\sample";
    private const string CrmIndex = @"C:\Git\sample\crm\data\contacts\_index.md";

    [Fact]
    public void SKILL_md_file_itself_is_never_included_in_the_sync_set()
    {
        var md = @"Read `{CCA_ROOT}/repo-a/STATE.md`.";
        var paths = TodaySkillParser.ExtractSyncPaths(md, CcaRoot, CrmIndex);
        paths.Should().NotContain(p => p.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Single_CCA_ROOT_reference_resolves_against_ccaRoot()
    {
        var md = @"Read `{CCA_ROOT}/repo-a/STATE.md` for the priorities.";
        var paths = TodaySkillParser.ExtractSyncPaths(md, CcaRoot, CrmIndex);
        paths.Should().ContainSingle().Which.Should().Be(@"C:\Git\sample\repo-a\STATE.md");
    }

    [Fact]
    public void Multiple_CCA_ROOT_references_are_collected_and_deduplicated()
    {
        var md = @"
- `{CCA_ROOT}/repo-a/STATE.md`
- `{CCA_ROOT}/repo-b/STATE.md`
- `{CCA_ROOT}/repo-c/STATE.md`
- `{CCA_ROOT}/repo-a/STATE.md`
";
        var paths = TodaySkillParser.ExtractSyncPaths(md, CcaRoot, CrmIndex);
        paths.Should().BeEquivalentTo(new[]
        {
            @"C:\Git\sample\repo-a\STATE.md",
            @"C:\Git\sample\repo-b\STATE.md",
            @"C:\Git\sample\repo-c\STATE.md",
        });
    }

    [Fact]
    public void CRM_INDEX_reference_maps_to_the_configured_crm_index_path()
    {
        var md = "Read `{CRM_INDEX}` for the CRM contacts.";
        var paths = TodaySkillParser.ExtractSyncPaths(md, CcaRoot, CrmIndex);
        paths.Should().ContainSingle().Which.Should().Be(CrmIndex);
    }

    [Fact]
    public void No_CRM_INDEX_reference_means_CRM_index_is_not_included()
    {
        var md = "No crm mentions here, just `{CCA_ROOT}/a/b.md`.";
        var paths = TodaySkillParser.ExtractSyncPaths(md, CcaRoot, CrmIndex);
        paths.Should().NotContain(CrmIndex);
    }

    [Fact]
    public void Forward_slashes_in_skill_references_are_normalized_to_platform_separator()
    {
        var md = @"Read `{CCA_ROOT}/repo-a/STATE.md`.";
        var paths = TodaySkillParser.ExtractSyncPaths(md, CcaRoot, CrmIndex);
        paths.Should().AllSatisfy(p => p.Should().NotContain("/"));
    }

    [Fact]
    public void Trailing_punctuation_after_CCA_ROOT_reference_is_stripped()
    {
        var md = "Read `{CCA_ROOT}/repo-c/STATE.md`, then `{CCA_ROOT}/repo-b/STATE.md`.";
        var paths = TodaySkillParser.ExtractSyncPaths(md, CcaRoot, CrmIndex);
        paths.Should().Contain(@"C:\Git\sample\repo-c\STATE.md");
        paths.Should().Contain(@"C:\Git\sample\repo-b\STATE.md");
    }

    [Fact]
    public void Real_today_skill_markdown_produces_six_referenced_paths()
    {
        var md = @"
Read STATE.md from each of these repos:
- `{CCA_ROOT}/repo-a/STATE.md`
- `{CCA_ROOT}/repo-b/STATE.md`
- `{CCA_ROOT}/repo-c/STATE.md`
- `{CCA_ROOT}/repo-d/STATE.md`
- `{CCA_ROOT}/repo-e/STATE.md`

## Step 2 - Read the CRM
Read `{CRM_INDEX}`.
";
        var paths = TodaySkillParser.ExtractSyncPaths(md, CcaRoot, CrmIndex);

        paths.Should().HaveCount(6, "the five STATE.md files plus the CRM index; SKILL.md itself is excluded");
        paths.Should().NotContain(p => p.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase));
        paths.Should().Contain(CrmIndex);
        paths.Should().Contain(@"C:\Git\sample\repo-a\STATE.md");
        paths.Should().Contain(@"C:\Git\sample\repo-b\STATE.md");
        paths.Should().Contain(@"C:\Git\sample\repo-c\STATE.md");
        paths.Should().Contain(@"C:\Git\sample\repo-d\STATE.md");
        paths.Should().Contain(@"C:\Git\sample\repo-e\STATE.md");
    }

    [Fact]
    public void Empty_cca_root_is_rejected()
    {
        Action act = () => TodaySkillParser.ExtractSyncPaths("", "", CrmIndex);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Empty_crm_index_is_rejected()
    {
        Action act = () => TodaySkillParser.ExtractSyncPaths("", CcaRoot, "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Null_markdown_is_rejected()
    {
        Action act = () => TodaySkillParser.ExtractSyncPaths(null!, CcaRoot, CrmIndex);
        act.Should().Throw<ArgumentNullException>();
    }
}
