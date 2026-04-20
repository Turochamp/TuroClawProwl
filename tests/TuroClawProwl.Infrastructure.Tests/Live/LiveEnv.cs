namespace TuroClawProwl.Infrastructure.Tests.Live;

// Live tests skip when these env vars are unset, so default
// `dotnet test` stays hermetic. Opt in by exporting them in your
// shell before running:
//   TURO_LIVE_GATEWAY_URL   - e.g. http://macmini.lan:8080/
//   TURO_LIVE_GATEWAY_TOKEN - optional bearer token
//   TURO_LIVE_SSH_HOST      - e.g. macmini.lan
//   TURO_LIVE_SSH_USER      - e.g. turo
//   TURO_LIVE_REPO_PATH     - e.g. C:\Git\ClaudeCodeAssistants\some-repo
internal static class LiveEnv
{
    public static string? GatewayUrl => Environment.GetEnvironmentVariable("TURO_LIVE_GATEWAY_URL");
    public static string? GatewayToken => Environment.GetEnvironmentVariable("TURO_LIVE_GATEWAY_TOKEN");
    public static string? SshHost => Environment.GetEnvironmentVariable("TURO_LIVE_SSH_HOST");
    public static string? SshUser => Environment.GetEnvironmentVariable("TURO_LIVE_SSH_USER");
    public static string? LiveRepoPath => Environment.GetEnvironmentVariable("TURO_LIVE_REPO_PATH");
}
