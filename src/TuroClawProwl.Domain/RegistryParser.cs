namespace TuroClawProwl.Domain;

public static class RegistryParser
{
    private const string ActiveSetHeading = "## Active set";
    private const int ExpectedColumnCount = 5;

    private static readonly string[] PublishedStatuses = ["active", "flagged"];

    public static IReadOnlyList<RegistryEntry> ParseActiveSet(string registryMarkdown)
    {
        ArgumentNullException.ThrowIfNull(registryMarkdown);

        var entries = new List<RegistryEntry>();
        var insideActiveSet = false;

        foreach (var rawLine in registryMarkdown.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("##", StringComparison.Ordinal))
            {
                insideActiveSet = line.StartsWith(ActiveSetHeading, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!insideActiveSet || !line.StartsWith('|')) continue;

            var cells = line.Trim('|').Split('|');
            if (cells.Length != ExpectedColumnCount) continue;

            var name = cells[0].Trim().Trim('`');
            var status = cells[3].Trim();
            var stateFile = cells[4].Trim().Trim('`').Replace('\\', '/');

            if (name.Length == 0 || stateFile.Length == 0) continue;
            if (!PublishedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase)) continue;

            entries.Add(new RegistryEntry(name, status.ToLowerInvariant(), stateFile));
        }

        return entries;
    }
}
