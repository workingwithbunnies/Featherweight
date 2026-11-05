# Featherweight: notes for AI coding assistants and contributors

Featherweight is a Windows 10 (22H2) / 11 desktop app: memory dashboard, one-click RAM cleaner, an "Optimize"
page that switches off background apps/services (backed up, undoable), snapshots, and a read-only security check.
Public repo: `workingwithbunnies/Featherweight`, MIT license.

The maintainer is not a programmer: explain changes in plain language, say why they matter to users, and suggest
improvements proactively. Be fully transparent about limitations and anything untested.

## Tech and build

- C# on **.NET Framework 4.8** (built into Windows), **WinForms built in code** (no designer/XAML), x64.
- No .NET SDK and no .csproj: `build.ps1` calls Visual Studio's Roslyn `csc.exe` (found via vswhere) and writes
  `artifacts\Featherweight.exe`. Version comes from `version.txt`, or `$env:FW_VERSION` in CI.
- `src/Featherweight/catalog.json` is embedded in the exe as resource `Featherweight.catalog.json`.
- CI: `.github/workflows/build.yml` (every push/PR, uploads the exe as an artifact) and `release.yml`
  (tag `vX.Y.Z` → build, SHA-256, **draft** release with that version's CHANGELOG section).

## Code map (`src/Featherweight/`)

| File | What |
|---|---|
| `App.cs` | `Paths` (data in `%LocalAppData%\Featherweight`, or next to the exe if a `portable` file exists), `Admin` (elevation, signed-in user SID), `Snapshots`, `Program.Main` |
| `PlanEngine.cs` | `Engine`: apply/check/restore actions (service, registry, task, cert), backups (`backup-*.json`), `RestoreItem` |
| `Catalog.cs` | Loads catalog (+ user `custom-optimizations.json`), detection, per-PC verdicts, Optimize / Restore default |
| `Probes.cs` | Per-PC questions used by catalog `checks` (Hyper-V VMs, firmware via Windows Update, phone paired) |
| `OptimizePage.cs`, `Pages.cs`, `Welcome.cs` | UI pages; `MainForm.cs` = sidebar, banners, update check |
| `Settings.cs` | `settings.json` and System Restore point creation |
| `Updates.cs`, `Diagnostics.cs` | GitHub release check; diagnostics export, `Privacy.Redact`, crash handling |
| `Native.cs`, `Memory.cs`, `Cleaner.cs`, `SecurityScan.cs`, `Json.cs`, `Ui.cs` | Win32 calls, memory figures, RAM cleaner, security sweep, JSON, theme/controls |

Catalog format and contribution rules: `docs/CATALOG.md`. Roadmap: `docs/ROADMAP.md`.

## Rules for anything that changes Windows

- Back up first (`Engine.Apply` records originals before each change) and keep everything undoable.
- Gentlest option: services → `Manual`, not `Disabled`. Never touch Windows Update, Defender or security features.
- Catalog descriptions must be true on **any** PC; pick the most cautious honest level; use `checks` for per-PC risks.
- Unknown Dell TechHub agents are matched by exe name (`dell-agent`), never by GUID; missing items are never created.
- HKCU writes go to the signed-in user's hive even when elevated with another account (`Engine.Hive`).
- Must work in any Windows display language: no parsing of localized command output or counter names.

## Privacy

- Never put user names, PC names, emails or paths with user names in code, docs, screenshots or logs.
  On-screen paths use `Paths.Display`; anything exported goes through `Privacy.Redact`.
- Only network request: the optional update check (`Updates.Check`). No telemetry.

## Workflow

- Work on a feature branch, push it (CI builds it), merge to `main` via pull request.
- Commit messages end with `Co-Authored-By: Claude <model> <noreply@anthropic.com>`; write the message to a temp
  file and use `git commit -F <file>` (PowerShell mangles multi-line `-m`).
- Update `CHANGELOG.md` under *Unreleased* for user-visible changes.
- Releases: bump `version.txt`, move *Unreleased* to `## [x.y.z]`, tag `vx.y.z`, push the tag, then publish the draft.

## Testing tips (no test project yet)

- Load the built exe in Windows PowerShell 5.1 via `[Reflection.Assembly]::LoadFrom` and call internal types with
  reflection (`BindingFlags NonPublic`). Beware PowerShell unrolling returned lists (wrap with `, (...)`), and
  case-insensitive variable names clashing with parameters.
- Render UI without showing it: construct `MainForm` off-screen, call `ShowPage`, pump `Application.DoEvents`,
  then `DrawToBitmap`. Read the PNG to check layout.
- Admin-only paths (service changes, full RAM cleaner, restore point creation) can't be tested without elevation;
  say so explicitly when reporting.
- For `gh`/`git` with complex quoting, use Git Bash rather than PowerShell.

## WinForms gotchas seen here

- Docking happens in reverse order of `Controls.Add`: add the `Fill` control first, edges after.
- Set `UseMnemonic = false` on labels/buttons that may contain `&`.
- Flat buttons with custom colours need explicit disabled styling (`Theme.Button` does this).
- `.ico` with PNG frames can't be read by `System.Drawing.Icon.ToBitmap()` on .NET Framework.
