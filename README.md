# TuroClawProwl

A Windows tray app that monitors an **OpenClaw Gateway** on a Mac Mini and auto-syncs the files a Claude "today" skill reads, so a remote Claude Code instance can pull the latest state without manual commits.

Status: pilot, private repo.

## What it does

- **Tray indicator** — icon colour reflects combined health: green when the gateway is reachable *and* every tracked today-file is committed + pushed; amber/red otherwise.
- **Gateway poller** — hits the OpenClaw Gateway HTTP endpoint on a configurable interval and surfaces transitions as toasts.
- **Today-file sync** — parses the Claude `today` `SKILL.md` to discover the referenced files (under `{CCA_ROOT}/...` and `{CRM_INDEX}`), watches them with `FileSystemWatcher`, and on change runs a per-repo 60s-debounced `git add && git commit && git push` scoped to just those paths.
- **Manual push** — tray menu "Push Today files" commits any dirty tracked files, then pushes repos that have unpushed commits (Option X: commit-then-push).
- **Remote gateway restart** — tray menu runs an SSH command against the Mac; result is shown as a toast.

## Architecture

Clean Architecture, .NET 10, x64:

```
src/
  TuroClawProwl.Domain/          pure types + functions (GatewayHealth, TodayFileStatus, TooltipComposer, TrayColorResolver, TodaySkillParser)
  TuroClawProwl.Application/     use cases + ports (HandleHealthPoll, ResolveTodayFileStatuses, PushTodayFiles, RestartGateway)
  TuroClawProwl.Infrastructure/  adapters (HTTP gateway client, OpenSSH runner, Git process runner, DPAPI token store, toast service, Serilog)
  TuroClawProwl.App/             WinForms tray, settings dialog, composition root, FileSystemWatcher plumbing
tests/
  *.Tests/                       xUnit + FluentAssertions + Moq; FsCheck for property tests
```

Domain has no dependencies on anything Windows- or I/O-related. Application depends only on Domain + ports. Infrastructure implements the ports. The App project is the composition root.

## Target platform

- Windows 10 1903+ / Windows 11 (Toast APIs require `10.0.19041.0` target platform)
- .NET 10 SDK
- x64

## Build

```powershell
dotnet build TuroClawProwl.sln
dotnet test  TuroClawProwl.sln
.\build.ps1               # release build + installer staging
```

The `installer/` folder contains an Inno Setup 6 script for the signed installer.

## Configuration

Configuration lives in `%APPDATA%\TuroClawProwl\config.json`; the auth token is stored separately under DPAPI. Fields are edited through the Settings dialog (tray -> Settings...):

| Field               | Purpose                                                              |
| ------------------- | -------------------------------------------------------------------- |
| Gateway URL         | OpenClaw Gateway HTTP endpoint, e.g. `http://192.168.1.43:18789/`    |
| Token               | Bearer token for the gateway (stored under DPAPI, never in config)   |
| SSH host / user     | For `Restart Gateway...` and SSH health probes                       |
| Poll interval       | Seconds between gateway health polls                                 |
| Start with Windows  | Registers `HKCU\...\Run` autostart                                   |
| SKILL.md            | Path to the today skill's `SKILL.md` — parsed at startup             |
| CCA_ROOT            | Root for `{CCA_ROOT}/...` placeholders in SKILL.md                   |
| CRM index           | Resolved path for `{CRM_INDEX}` placeholder                          |

SKILL.md itself is **not** auto-synced — it's user-curated and only ever pushed manually.

## Logging

Serilog writes to a rolling file under `%APPDATA%\TuroClawProwl\logs\`. A custom `IDestructuringPolicy` masks any property whose name matches `token`, `password`, `secret`, `apikey`, or `authorization`.

## Networking notes

- `.NET HttpClient` does **not** resolve mDNS (`Fox.local`); the gateway URL must use an IP address. `ssh.exe` resolves mDNS fine, so the SSH target can stay as `Fox.local`.
- The Mac-side OpenClaw Gateway binds to loopback by default; set `gateway.bind = "lan"` in `~/.openclaw-data/openclaw.json` to accept LAN connections.

## License

Private. Not yet licensed for distribution.
