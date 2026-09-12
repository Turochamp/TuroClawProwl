namespace TuroClawProwl.Domain;

public static class PublishFailureToast
{
    // ToastContentBuilder caps at 4 text lines total; the header is one of them.
    public const int MaxLines = 3;

    private const int DetailMaxLength = 120;

    public static IReadOnlyList<string> Compose(PublishHealth.Failed failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var header = failure.IsMisconfiguration
            ? "State bundle: misconfigured"
            : "State bundle: publish failed";

        var cause = failure.IsMisconfiguration && failure.SettingName.Length > 0
            ? $"Fix the {failure.SettingName} setting."
            : "Transient error — retries on the next change.";

        return [header, cause, Clip(failure.Detail, DetailMaxLength)];
    }

    private static string Clip(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
