# CLAUDE.md

Project-specific guidance for Claude Code. Read this before changing code.

## What this project is

TuroClawProwl is a **Windows tray app** that monitors an OpenClaw Gateway on a Mac Mini and auto-syncs the files referenced by a Claude `today` skill so remote Claude Code instances can pull latest state. Pilot, private repo.

## Stack

- **.NET 10**, C# latest, `TreatWarningsAsErrors=true` solution-wide.
- **WinForms**, **x64 only**. The `TuroClawProwl.App` project targets **`net10.0-windows10.0.19041.0`** — don't drop the `10.0.19041.0` suffix; `ToastContentBuilder.Show()` requires it.
- Other projects target `net10.0` unless they touch Win32 (Infrastructure needs the windows TFM for DPAPI + toasts).

## Architecture — Clean Architecture, four projects

```
src/TuroClawProwl.Domain/          pure types + functions, no I/O, no DI
src/TuroClawProwl.Application/     use cases + ports (interfaces)
src/TuroClawProwl.Infrastructure/  adapters implementing ports (HTTP, SSH, git, DPAPI, toast, Serilog)
src/TuroClawProwl.App/             WinForms tray, settings dialog, composition root
```

Rules:

1. **Domain depends on nothing.** No `Microsoft.Extensions.*`, no `System.IO.*` I/O, no `HttpClient`. Pure records + functions only.
2. **Application depends only on Domain + ports.** Ports live under `Application/Ports/`.
3. **Infrastructure depends on Application.** Implements the ports. Anything OS-touching lives here.
4. **App is the composition root.** `Program.cs` wires everything; nowhere else should `new` an adapter.

## Tests

- **xUnit + FluentAssertions** for all assertions.
- **Moq** for mocks. **NOT NSubstitute** — if any old doc or spec says NSubstitute, ignore it. This is load-bearing feedback from the user.
- **FsCheck** for property-based tests where it helps (state machines etc.).
- **Xunit.SkippableFact** for tests that depend on live external systems.
- **Live tests** under `tests/TuroClawProwl.Infrastructure.Tests/Live/` require the Mac to be reachable. They **must `Skip.If(...)` when `LiveEnv` says the environment isn't present** — they never fail CI when the Mac is offline.

Before declaring a change done:
- `dotnet build TuroClawProwl.sln` — zero warnings, zero errors.
- `dotnet test TuroClawProwl.sln` — Domain + Application + Infrastructure offline tests green. Live tests skipping is fine.

## Code style

- **Sealed record hierarchies** for discriminated unions: `public abstract record X { public sealed record A(...) : X; public sealed record B(...) : X; }`. Pattern-match with a `_ => throw new ArgumentException(...)` default so a new variant forces a compile/test failure somewhere.
- **Null checks at public entry points**: `ArgumentNullException.ThrowIfNull(arg);`.
- **No comments explaining *what*** — identifiers do that. Only comment *why* when non-obvious (hidden constraint, workaround, subtle invariant).
- **Default to `NullLogger<T>.Instance`** when a logger isn't supplied; don't make loggers required.
- **No interpolated strings in `ILogger` call sites** — use message templates `logger.LogInformation("thing {X}", x)`. (The QR-O1 analyzer that enforces this is TODO'd but the rule is in effect now.)

## Known gotchas — do not rediscover these

- **`.NET HttpClient` does not resolve mDNS reliably.** Use the IP for the gateway URL (`http://192.168.1.121:18789/` — the DHCP reservation pins `fox` to that address), not `fox.local`. `ssh.exe` resolves mDNS fine, so SSH targets can stay on `.local`.
- **Mac power management masquerades as health flapping.** macOS defaults (`sleep 1`, `powernap 1`) sleep an idle Mac Mini after 1 minute and wake it briefly every ~14 minutes for "Maintenance Sleep". Symptom in `prowl-*.log`: a regular ~2 min `Healthy` / ~13 min `Unreachable: timeout` cycle that lines up exactly with `pmset -g log` Sleep/DarkWake entries. Fix on the Mac, not in the app: `sudo pmset -a sleep 0 disksleep 0 powernap 0`. The in-app flap suppression only papers over residual Wi-Fi/OS-update blips.
- **OpenClaw Gateway binds loopback by default.** For LAN access set `gateway.bind = "lan"` in `~/.openclaw-data/openclaw.json` on the Mac and `launchctl kickstart` the service.
- **`ToastContentBuilder` caps at 4 text lines total** (header + 3 content). `NotifyPushSummaryAsync` collapses to header + 1 success line + 1 failure line for that reason — don't let it grow.
- **WinForms dock order**: `DockStyle.Bottom` controls must be `Controls.Add`'d **before** `DockStyle.Fill` or they'll be occluded. See [SettingsForm.cs](src/TuroClawProwl.App/SettingsForm.cs) for the pattern.
- **`Application` namespace collision**: `using TuroClawProwl.Application` shadows `System.Windows.Forms.Application`. Use `using WinFormsApp = System.Windows.Forms.Application;` in any `.App` file that needs both.
- **SKILL.md itself is excluded from the sync set** — user-curated, manual push only. Don't add it to `TodaySkillParser`'s output.
- **A file that's both uncommitted and unpushed is classified as uncommitted only** in the tooltip — commit is the primary blocker and `PushTodayFilesUseCase` (Option X) commits then pushes in one flow.
- **Smart App Control (SAC)** on the user's machine can block locally-built unsigned exes. It's a user-environment issue, not a build issue — don't "fix" it by disabling warnings.

## Sensitive data

Serilog has a `SensitivePropertyMaskingPolicy` that masks any property named `token`, `password`, `secret`, `apikey`, or `authorization`. If you add a new sensitive property, either match one of those names or extend the policy — never log secrets raw.

The gateway auth token is stored under **DPAPI** (`DpapiTokenStore`), **not** in `config.json`. Never persist it to JSON.

## Config

`%APPDATA%\TuroClawProwl\config.json` holds `GatewayUrl`, `SshHost`, `SshUser`, `PollInterval`, `AutostartEnabled`, `TodaySkillPath`, `TodayCcaRoot`, `TodayCrmIndexPath`. Token is separate (DPAPI). If you add a field, update:

1. [TuroClawProwlConfig.cs](src/TuroClawProwl.Application/TuroClawProwlConfig.cs)
2. [JsonConfigStore.cs](src/TuroClawProwl.Infrastructure/Configuration/JsonConfigStore.cs)
3. [SettingsForm.cs](src/TuroClawProwl.App/SettingsForm.cs) — load + save
4. Composition root in [Program.cs](src/TuroClawProwl.App/Program.cs) if it feeds an adapter

## Branch / PR conventions

- Feature branches off `main`.
- Small, focused commits — one concern per commit, imperative subject line ("`SettingsForm: AutoSize buttons...`").
- PR bodies: Summary bullets, non-obvious decisions, deferred work, test plan checklist. See PR #1 for the template.

## Deferred work (TODO in code)

- **QR-R2**: 500-poll fault-injection stress test for `HandleHealthPollUseCase`.
- **QR-O1**: Roslyn analyzer rejecting interpolated strings in `ILogger` call sites.
- **Startup sweep** in `TodaySyncer` so pre-existing dirty files trigger auto-commit without a manual Push click.
