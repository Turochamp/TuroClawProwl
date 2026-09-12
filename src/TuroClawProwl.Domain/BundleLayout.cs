using System.Globalization;

namespace TuroClawProwl.Domain;

public static class BundleLayout
{
    public const string BundleDirectory = "hub/bundle";
    public const string ManifestFileName = "manifest.json";

    public const string RegistrySourceId = "registry";
    public const string CrmIndexSourceId = "crm-index";

    public const string RegistryRelativePath = "hub/registry.md";
    public const string CrmIndexRelativePath = "crm/data/contacts/_index.md";
    public const string RegistryBundlePath = "registry.md";
    public const string CrmIndexBundlePath = "crm-index.md";

    public static string WeeklyFileName(DateTimeOffset instant)
    {
        var date = instant.Date;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:D4}-W{1:D2}.md",
            ISOWeek.GetYear(date),
            ISOWeek.GetWeekOfYear(date));
    }

    public static string WeeklyRelativePath(DateTimeOffset instant) =>
        "hub/weekly/" + WeeklyFileName(instant);

    public static string WeeklyBundlePath(DateTimeOffset instant) =>
        "weekly/" + WeeklyFileName(instant);

    public static string WeeklySourceId(DateTimeOffset instant)
    {
        var fileName = WeeklyFileName(instant);
        return "weekly/" + fileName[..^".md".Length];
    }

    public static string CcaSourceId(string ccaName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ccaName);
        return "cca/" + ccaName;
    }

    public static string CcaBundlePath(string ccaName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ccaName);
        return $"cca/{ccaName}.STATE.md";
    }
}
