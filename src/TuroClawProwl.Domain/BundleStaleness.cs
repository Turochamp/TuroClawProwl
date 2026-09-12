using System.Globalization;

namespace TuroClawProwl.Domain;

public static class BundleStaleness
{
    public static readonly TimeSpan BundleStaleAfter = TimeSpan.FromHours(36);
    public static readonly TimeSpan SourceStaleAfter = TimeSpan.FromDays(7);

    public static TimeSpan AgeAt(DateTimeOffset asOf, DateTimeOffset timestamp)
    {
        var age = asOf - timestamp;
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }

    public static bool IsBundleStale(DateTimeOffset asOf, BundleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return AgeAt(asOf, manifest.PublishedAt) > BundleStaleAfter;
    }

    // Measured on Verified, never on Modified: content that has not changed in a
    // fortnight but was confirmed current this morning is correct data, not old
    // data, and a source nobody has managed to re-read is stale whatever its
    // content says.
    public static IReadOnlyList<BundleSource> StaleSources(DateTimeOffset asOf, BundleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Sources
            .Where(s => AgeAt(asOf, s.Verified) > SourceStaleAfter)
            .ToArray();
    }

    public static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;

        if (age.TotalMinutes < 60)
            return ((int)age.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m";
        if (age.TotalHours < 48)
            return ((int)age.TotalHours).ToString(CultureInfo.InvariantCulture) + "h";
        return ((int)age.TotalDays).ToString(CultureInfo.InvariantCulture) + "d";
    }
}
