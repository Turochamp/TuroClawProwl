namespace TuroClawProwl.Domain;

public static class PorcelainParser
{
    public static PorcelainSummary Parse(string? porcelainOutput)
    {
        if (string.IsNullOrWhiteSpace(porcelainOutput))
            return new PorcelainSummary(HasUncommitted: false);

        foreach (var line in porcelainOutput.Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
                return new PorcelainSummary(HasUncommitted: true);
        }

        return new PorcelainSummary(HasUncommitted: false);
    }
}
