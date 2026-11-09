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
- `src/Featherweight/catalog.json` and `knowledge.json` are embedded as resources `Featherweight.catalog.json` / `Featherweight.knowledge.json`.
- CI: `.github/workflows/build.yml` (every push/PR, uploads the exe as an artifact) and `release.yml`
  (tag `vX.Y.Z` → build, SHA-256, **draft** release with that version's CHANGELOG section).

## Code map (`src/Featherweight/`)

| File | What |
|---|---|
| `App.cs` | `Paths` (data in `%LocalAppData%\Featherweight`, or next to the exe if a `portable` file exists), `Admin` (elevation, signed-in user SID), `Snapshots`, `Program.Main` |
| `PlanEngine.cs` | `Engine`: apply/check/restore actions (service, registry, task, cert), backups (`backup-*.json`), `RestoreItem` |
| `Catalog.cs` | Loads catalog (+ user `custom-optimizations.json`), detection, per-PC verdicts, Optimize / Restore default |
| `Discovery.cs` | Finds non-catalog background software on any PC (services, startup apps, startup tasks, Store apps) as "Not reviewed" items; `Publisher` = brand from signature / file details |
| `Facts.cs` | Offline, assumption-free description of each found item: maker's own text, product, triggers, driver class, live behaviour |
| `Knowledge.cs` | Loads `knowledge.json` (our reviewed descriptions, embedded; newer copy downloadable), matches found items by stable ids |
| `Probes.cs` | Per-PC questions used by catalog `checks` (Hyper-V VMs, firmware via Windows Update, phone paired) |
| `OptimizePage.cs`, `Pages.cs`, `Welcome.cs` | UI pages; `MainForm.cs` = sidebar, banners, update check |
| `Settings.cs` | `settings.json` and System Restore point creation |
| `Housekeeping.cs` | Tidies the data folder on start (old unnamed snapshots, long-undone backups, long logs) |
| `Updates.cs`, `Diagnostics.cs` | GitHub release check; diagnostics export, `Privacy.Redact`, crash handling |
| `SecurityScan.cs`, `SecurityChecks.cs` | Security check (read-only): Defender, certificates, startup, tasks, hijack points, network; Windows protections, updates, sign-in/sharing, browser extensions. Fixes go through the engine; Windows security settings only get an "Open in Windows" link |
| `Native.cs`, `Memory.cs`, `Cleaner.cs`, `Json.cs`, `Ui.cs` | Win32 calls, memory figures, RAM cleaner, security sweep, JSON, theme/controls |
| `Themes.cs` | Six palettes (every colour/font), `ThemedButton`, theme tiles, `ThemeApply` (text boxes, lists, dark scrollbars). `Theme.Use(palette)` runs before a window is built; switching rebuilds `MainForm` (`Rebuild`, loop in `Program.Main`) |

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
- Only network requests: the optional update check (`Updates.Check`) and, with the same opt-in, downloading the newest
  `knowledge.json` from GitHub (`Knowledge.TryUpdate`). Nothing about the PC is sent. No telemetry.
- No AI services and no server of our own, ever (maintainer decision): descriptions come from facts read on the PC
  (`Facts.cs`) plus the knowledge base we write ourselves (`knowledge.json`, `docs/KNOWLEDGE.md`).

## Workflow

- Work on a feature branch, push it (CI builds it), merge to `main` via pull request.
- Commit messages end with `Co-Authored-By: Claude <model> <noreply@anthropic.com>`; write the message to a temp
  file and use `git commit -F <file>` (PowerShell mangles multi-line `-m`).
- Update `CHANGELOG.md` under *Unreleased* for user-visible changes.
- Releases: bump `version.txt`, move *Unreleased* to `## [x.y.z]`, tag `vx.y.z`, push the tag, then publish the draft.
  Versions with a suffix (`0.1.0-alpha`, `0.1.0-alpha.2`, `0.2.0-beta`) become GitHub pre-releases; the update check
  shows pre-releases only to people running a pre-release (`Updates.Check`, semantic-version compare in `Updates.Compare`).

## Testing tips (no test project yet)

- Load the built exe in Windows PowerShell 5.1 via `[Reflection.Assembly]::LoadFrom` and call internal types with
  reflection (`BindingFlags NonPublic`). Beware PowerShell unrolling returned lists (wrap with `, (...)`), and
  case-insensitive variable names clashing with parameters.
- Render UI without showing it: construct `MainForm` off-screen, call `ShowPage`, pump `Application.DoEvents`,
  then `DrawToBitmap`. Read the PNG to check layout.
- Admin-only paths (service changes, full RAM cleaner, restore point creation) can't be tested without elevation;
  say so explicitly when reporting.
- For `gh`/`git` with complex quoting, use Git Bash rather than PowerShell.

## Display scale and themes

- The app is system-DPI-aware and WinForms auto-scaling doesn't run for these code-built windows
  (`AutoScaleMode.None`): every fixed pixel size goes through `Theme.S()` / `Theme.Pad()`.
- Never hard-code colours or fonts: use `Theme.*` (or `Theme.Current.*` for palette-only fields) so all six themes work.
- Simulate a scale off-screen by setting `Theme.Dpi` and swapping the `Theme` fonts via reflection before building a window;
  call `Application.EnableVisualStyles()` in test scripts or list groups won't show.

## WinForms gotchas seen here

- Docking happens in reverse order of `Controls.Add`: add the `Fill` control first, edges after.
- Set `UseMnemonic = false` on labels/buttons that may contain `&`.
- Flat buttons with custom colours need explicit disabled styling (`Theme.Button` does this).
- `.ico` with PNG frames can't be read by `System.Drawing.Icon.ToBitmap()` on .NET Framework.
