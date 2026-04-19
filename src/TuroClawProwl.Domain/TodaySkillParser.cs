using System.Text.RegularExpressions;

namespace TuroClawProwl.Domain;

public static class TodaySkillParser
{
    private static readonly Regex CcaRootRef = new(
        @"\{CCA_ROOT\}/([^\s`]+?)(?=[\s`])",
        RegexOptions.Compiled);

    public static IReadOnlyList<string> ExtractSyncPaths(
        string skillMarkdown,
        string skillFilePath,
        string ccaRoot,
        string crmIndexPath)
    {
        ArgumentNullException.ThrowIfNull(skillMarkdown);
        ArgumentException.ThrowIfNullOrWhiteSpace(skillFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ccaRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(crmIndexPath);

        var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizeSeparators(skillFilePath),
        };

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
