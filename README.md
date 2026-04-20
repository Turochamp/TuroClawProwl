# TuroClawProwl

[![CI](https://github.com/Turochamp/TuroClawProwl/actions/workflows/ci.yml/badge.svg)](https://github.com/Turochamp/TuroClawProwl/actions/workflows/ci.yml)

A Windows tray app that keeps a remote OpenClaw Gateway on a Mac Mini in sight, and auto-commits + pushes the files a Claude "today" skill reads so a remote Claude Code instance can always pull the latest state without you stopping to `git commit && git push` by hand.

Built as a personal pilot by Michael Åhs.

## What it does

- **Tray indicator** — icon colour reflects combined health: green when the gateway is reachable *and* every tracked today-file is committed + pushed; amber/red otherwise. Tooltip breaks down which repo has what outstanding.
- **Gateway poller** — hits the OpenClaw Gateway HTTP endpoint on a configurable interval and surfaces up/down transitions as toasts.
- **Today-file sync** — parses the Claude `today` `SKILL.md` to discover referenced files, watches them with `FileSystemWatcher`, and on change runs a per-repo 60s-debounced `git add && git commit && git push` scoped to just those paths.
- **Manual push** — tray menu "Push Today files" commits any dirty tracked files, then pushes repos with unpushed commits.
- **Remote gateway restart** — tray menu runs an SSH command against the Mac; result shows as a toast.
- **Open Control UI** — tray menu opens the gateway's web UI in the default browser with the DPAPI-stored token pre-filled in the URL fragment.

## Architecture

Clean Architecture, .NET 10, x64, WinForms:

```
src/
  TuroClawProwl.Domain/          pure types + functions — no I/O, no DI
  TuroClawProwl.Application/     use cases + ports (interfaces)
  TuroClawProwl.Infrastructure/  adapters (HTTP, SSH, git, DPAPI, toast, Serilog)
  TuroClawProwl.App/             WinForms tray, settings dialog, composition root
tests/
  *.Tests/                       xUnit + FluentAssertions + Moq; FsCheck for property tests
```

Domain depends on nothing. Application depends only on Domain + ports. Infrastructure implements the ports. The App project is the composition root — nowhere else constructs adapters directly.

## Target platform

- Windows 10 1903+ / Windows 11 (Toast APIs require the `10.0.19041.0` target platform)
- .NET 10 SDK
- x64

## Build

```powershell
dotnet build TuroClawProwl.sln
dotnet test  TuroClawProwl.sln
pwsh installer\build.ps1      # self-contained single-file exe + Inno Setup installer
```

The [installer/](installer/) folder contains an Inno Setup 6 script that bundles the single-file publish into a per-user installer (no UAC, lands in `%LOCALAPPDATA%\Programs\TuroClawProwl`).

## Configuration

Config lives in `%APPDATA%\TuroClawProwl\config.json`; the auth token is stored separately under DPAPI (never in the JSON). Fields are edited through the Settings dialog (tray → Settings…):

| Field              | Purpose                                                              |
| ------------------ | -------------------------------------------------------------------- |
| Gateway URL        | OpenClaw Gateway HTTP endpoint, e.g. `http://192.168.1.43:18789/`    |
| Token              | Bearer token for the gateway (DPAPI-sealed, never in `config.json`)  |
| SSH host / user    | For `Restart Gateway…` and the SSH Test button                       |
| Poll interval      | Seconds between gateway health polls                                 |
| Start with Windows | Registers `HKCU\...\Run` autostart                                   |
| SKILL.md           | Path to the today skill's `SKILL.md` — parsed at startup             |
| CCA_ROOT           | Root for `{CCA_ROOT}/...` placeholders in `SKILL.md`                 |
| CRM index          | Resolved path for the `{CRM_INDEX}` placeholder                      |

`SKILL.md` itself is **not** auto-synced — it's user-curated and only ever pushed manually.

## Logging

Serilog writes to a rolling file under `%APPDATA%\TuroClawProwl\logs\`. A custom `IDestructuringPolicy` masks any property whose name matches `token`, `password`, `secret`, `apikey`, or `authorization`.

## Networking notes

- `.NET HttpClient` does **not** resolve mDNS (`Fox.local`). Use an IP address for the gateway URL. `ssh.exe` resolves mDNS fine, so the SSH target can stay as `Fox.local`.
- The Mac-side OpenClaw Gateway binds to loopback by default; set `gateway.bind = "lan"` in `~/.openclaw-data/openclaw.json` to accept LAN connections.

## License

MIT — see [LICENSE](LICENSE).
