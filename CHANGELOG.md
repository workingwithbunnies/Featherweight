# Changelog

All notable changes to Featherweight are listed here. Versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- A full [privacy policy](docs/PRIVACY.md): what the update check sends (your IP address and the app version, as with
  any website visit), that it is on by default and how to switch it off, what stays on your PC, and what happens only
  when you click something.

## [0.1.0-alpha.3] - 2026-11-10

### Added
- **Proof of origin for every download.** Each release's `Featherweight.exe` now comes with build provenance: a
  record, signed by GitHub, that the file was built by this repository's own build system from its public source.
  Anyone can check a download with `gh attestation verify Featherweight.exe -R workingwithbunnies/Featherweight`. A
  look-alike or modified file fails the check, even if it comes with its own checksum.
- The README and SECURITY.md say clearly that the only official download is this repository's Releases page, and how
  to report fake copies. A code signing policy page (`docs/CODE_SIGNING.md`) was added for the code signing
  application.

### Changed
- Nothing in the app itself: this version is about checking that a download is genuine.

## [0.1.0-alpha.2] - 2026-11-09

### Fixed
- **Check for updates** found nothing when running an alpha ("No releases have been published yet"), so testers
  wouldn't have been told about new versions. It now finds the newest release, including alphas.
- Release notes on GitHub show arrows and other special characters correctly.

## [0.1.0-alpha] - 2026-11-09

First public test version (alpha). Tested on two laptops so far (Dell G15, HP OMEN 16, both Windows 11); feedback from other PCs is very welcome.

### Added
- **Themes** (Settings & about → Theme): six looks, switched with one click and remembered. *Featherweight* (the
  standard look), *Classic 9x* (grey bevelled buttons, navy highlights and a green-on-black Task Manager style graph,
  inspired by Windows 95/98), *XP Blue* (beige windows, a blue side pane, rounded buttons and a green main button,
  inspired by Windows XP), *Frutiger Aero* (sky-blue glass and glossy buttons, inspired by Windows 7), *Midnight*
  (dark mode) and *Synthwave* (neon pink and cyan on purple). Themes only change how the app looks.
- Dashboard: click a company in "By company" to see its programs (how many copies of each are running and their
  memory); click again to close it.
- Featherweight tidies its data folder on start: it keeps the 30 newest unnamed snapshots (named ones stay until you
  delete them), removes backups that were undone more than 90 days ago (a backup that's still your undo is never
  removed), and keeps the activity and crash logs under about 1 MB. Snapshots has a Delete button.
- Dashboard with live memory, commit and kernel memory, history graph, usage by app/vendor and top processes.
- Optimize page: detects supported background apps/services, shows live memory use, Optimize / Restore default per item,
  and flags items that an update switched back on.
- Backups / Undo page: every change is backed up first and can be undone.
- One-click RAM cleaner (quick and deep).
- Snapshots: save and compare memory use.
- Read-only security check.
- Portable mode (file named `portable` next to the exe).
- Catalog v2: every item has a level (Recommended / Optional / Advanced) and per-PC checks that can mark it
  *Not advised here*, with an "On this PC" explanation. First checks: Hyper-V VMs present, BIOS delivered by
  Windows Update, phone paired with Phone Link.
- "Apply recommended" only applies items judged Recommended for this PC; optimizing a *Not advised here* item
  needs an extra confirmation.
- Dell TechHub agents are matched by program name, so they're found across Dell software versions.
- `docs/CATALOG.md`: guide to adding optimizations without writing code.
- A System Restore point is created before changes (a recent one is reused; can be turned off in Settings).
- Welcome screen on first start: what the app does and never does, and the choice to check for updates.
- Optional update check (at most once a day) with a banner when a new version is available.
- Settings & about page: version, update check, restore points, data folder, links.
- Export diagnostics: a bug-report text file with user and PC names removed, opened for review before sharing.
- Friendly error dialog and `crash.log` instead of raw .NET errors; details are copied for bug reports.
- Automatic builds on GitHub for every push, and draft releases with a SHA-256 checksum for version tags.
- Code of conduct.
- Optimize finds background software from **any brand**, not only the apps in the catalog: services that start
  with Windows, startup apps, scheduled tasks that run at startup or sign-in, and Store apps running in the background
  or at sign-in. They're grouped by the company that made them and marked *Not reviewed*; switching one off needs
  an extra confirmation, is backed up, and Restore default puts it back. Windows parts, Microsoft's own services and
  Store apps, antivirus/firewall software and updaters are never offered.
- Dashboard groups memory by the company that made each program (HP, AMD, Lenovo, Realtek…), so other brands
  no longer end up in "Everything else".

- Export diagnostics also lists what Optimize found, every service from other companies, and memory by company,
  so a report from a new brand of PC has everything needed to add reviewed entries for it.

- Optimize also lists helpers that hardware drivers install (AMD, Intel, Realtek, HP…) under *Show advanced*, with their
  memory use. They're for information only: Featherweight won't change one until it has a reviewed entry.
- Every item Optimize finds gets its own description, read from the PC without guessing: the maker's own description,
  the product it belongs to, what starts it, which driver installed it, what a Store app is allowed to do, and what
  it's doing right now (memory, processor time, open window, internet connections). Works offline.
- Featherweight's own knowledge base (`knowledge.json`, see `docs/KNOWLEDGE.md`): reviewed descriptions saying what an
  item is for, what you lose without it and when to keep it. First entries cover HP/OMEN software, DTS audio, Google
  Play Games, Steam, the EA app, MSI Afterburner, Universal x86 Tuning Utility and Process Lasso. Newer descriptions
  are downloaded from GitHub with the optional update check (nothing about the PC is sent; a downloaded file can only
  describe items, never mark them Recommended).
- Settings & about says when Windows' System Protection is off (so no restore points can be made) and opens its settings.

- Security check, much more thorough: firewall per network type (or another firewall), SmartScreen, User Account
  Control, Secure Boot, TPM and drive encryption (as administrator), Core isolation, Windows Update (last install,
  switched off, restart pending), Remote Desktop, SMB1, automatic sign-in with a stored password, services running
  from user folders, browser extensions (Chrome, Edge, Brave, Vivaldi, Firefox) and when Defender last scanned, with
  a "Run a quick scan now" button that uses Defender itself. A summary at the top says what needs attention; levels
  read Act now / Look at / Note / OK. "Open in Windows" goes to the exact Settings or Windows Security page, and
  startup entries or tasks running from user folders can be switched off (backed up, undoable).
- Optimize: **Copy for a report** copies just the selected app's names, facts and changes (personal details removed)
  and offers to open the "Describe an app" request form.

### Changed
- Export diagnostics asks what to include (PC details, background software, memory by program, Featherweight's
  activity) with only the basics ticked, and warns that GitHub issues are public. The request forms and
  CONTRIBUTING.md explain what to share and why.
- The Clean RAM page is now part of the Dashboard: "Free up RAM" does a quick clean, and the arrow next to it offers
  the deep clean and an explanation of the difference. The result shows under the button.
- New welcome screen: a header with the logo, and three cards (what it does, what it never does, good to know).
- Dashboard only lists companies that have something running (no more grey 0 MB rows), and programs that drivers put
  in the Windows folder are grouped under their maker instead of "Windows & Microsoft".
- Store apps are grouped by their publisher, and per-user task names no longer show the account's ID.

### Fixed
- Security check: the hint under the buttons is normal-size text across the page instead of a narrow block of large
  grey text.
- Export diagnostics: the dialog has even margins, each description lines up with its checkbox, the public-sharing
  warning is a soft yellow box instead of orange text, and the buttons sit on the right like in Windows' own dialogs.
- Welcome screen and Export diagnostics at 150 % and above: they now grow with the display scale instead of keeping
  their 100 % size with bigger text (which overlapped the header text, cut off "What it never does" and ran under the
  taskbar). The welcome screen also gets wider if it would be taller than the screen, and scrolls as a last resort.
- At display scales above 100 % (125 %, 150 %…) the sidebar, list columns and the Optimize details panel grow with the
  text, so the app name and words like "Recommended" or "Not optimized" are no longer cut off. Dashboard cards no
  longer show stray border lines after the window is resized.
- The "Restart as administrator" (and update) banner buttons no longer have their text cut off.
- The security check no longer warns about Featherweight itself being unsigned; it says so as information instead.
- Optimize's details now say what each change does in plain words (for example "Background permission for “Phone
  Link” → off, right now: allowed") instead of registry paths, and every item, including the built-in Windows ones,
  shows the same "On this PC" facts. Store apps are named the way Windows shows them.
- The details text no longer gets highlighted after Optimize or Restore default.
- Snapshots: taking one now compares it with the previous one straight away (newest as "After", the one before as
  "Before"; the lists used to keep the same position, so both could show the same snapshot). A plain summary says
  what changed and which program grew or shrank most, a new "By program" section lists every program that changed
  by 5 MB or more (started / closed included), companies with nothing running are hidden, a Swap button was added,
  and snapshots with the same name no longer overwrite each other.
- Seven more reviewed descriptions (knowledge base version 3): AMD Crash Defender, AMD External Events Utility, AMD
  Provisioning Packages, the ELAN touchpad service, the Realtek audio service, NVIDIA Display Container and the NVIDIA
  app background service. Driver helpers stay information-only.
- Optimize: hover text is short (the full details stay in the side panel), and info-only items no longer show
  "Running" in orange.
- Task descriptions no longer show the Windows account ID, and odd characters in program details are cleaned up.
- When a restore point can't be made, the activity log now says why (System Protection off, or the actual error)
  instead of an empty message.
- Items that are a setting in another app (such as the NVIDIA overlay) show *Running* / *Not running* and are no
  longer counted as "optimized" or as memory saved, since Featherweight didn't switch them.

### Removed
- "Resume from phone" no longer writes a device-management (MDM) policy key; it only uses the per-user setting.
- The sidebar no longer shows the PC's name (it shows the app version), so screenshots are safe to share.

### Changed (from the private prototype "RamTool")
- User data moved to `%LocalAppData%\Featherweight`.
- The optimization catalog is built into the exe; users can add a `custom-optimizations.json`.
- Works in every Windows display language (no more reliance on translated counter names or `netstat` text).
- Security check recognises third-party antivirus (Defender in passive mode is no longer reported as "off").
- Per-user settings go to the signed-in user even when elevating with a different admin account.
