# Changelog

All notable changes to Featherweight are listed here. Versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
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

### Removed
- "Resume from phone" no longer writes a device-management (MDM) policy key; it only uses the per-user setting.
- The sidebar no longer shows the PC's name (it shows the app version), so screenshots are safe to share.

### Changed (from the private prototype "RamTool")
- User data moved to `%LocalAppData%\Featherweight`.
- The optimization catalog is built into the exe; users can add a `custom-optimizations.json`.
- Works in every Windows display language (no more reliance on translated counter names or `netstat` text).
- Security check recognises third-party antivirus (Defender in passive mode is no longer reported as "off").
- Per-user settings go to the signed-in user even when elevating with a different admin account.
