using System.Text.RegularExpressions;

namespace TuroClawProwl.Domain;

public static class TodaySkillParser
{
    private static readonly Regex CcaRootRef = new(
        @"\{CCA_ROOT\}/([^\s`]+?)(?=[\s`])",
        RegexOptions.Compiled);

    // The SKILL.md file itself is intentionally NOT included. The skill
    // definition is curated by hand; only the files it references (via
    // {CCA_ROOT}/... and {CRM_INDEX}) are auto-synced to the remote.
    public static IReadOnlyList<string> ExtractSyncPaths(
        string skillMarkdown,
        string ccaRoot,
        string crmIndexPath)
    {
        ArgumentNullException.ThrowIfNull(skillMarkdown);
        ArgumentException.ThrowIfNullOrWhiteSpace(ccaRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(crmIndexPath);

        var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in CcaRootRef.Matches(skillMarkdown))
        {
            var relative = match.Groups[1].Value.TrimEnd('.', ',', ';', ')');
            if (relative.Length == 0) continue;
            var absolute = Path.Combine(ccaRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            paths.Add(NormalizeSeparators(absolute));
        }

        if (skillMarkdown.Contains("{CRM_INDEX}", StringComparison.Ordinal))
            paths.Add(NormalizeSeparators(crmIndexPath));

        return paths.ToArray();
    }

    private static string NormalizeSeparators(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);
}
