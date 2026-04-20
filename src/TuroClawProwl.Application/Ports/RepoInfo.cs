namespace TuroClawProwl.Application.Ports;

public sealed record RepoInfo(string PorcelainOutput, int UnpushedCount, bool HasUpstream);
