# Contributing to Featherweight

Thanks for helping! There are many ways to contribute, and most don't need any coding.

## Without coding

- **Report bugs** using the *Bug report* form under Issues.
- **Ask for a vendor app to be supported** (ASUS, MSI, Lenovo, HP, Razer…) using the *Support a new app or vendor* form.
  Telling us which processes and services you see is often all that's needed.
- **Test on your PC** and tell us what Featherweight detected and suggested. Reports from different PC models are very valuable.
- **Improve the wording** of descriptions or docs.

## Adding to the catalog (JSON, no C#)

The optimization list is [`src/Featherweight/catalog.json`](src/Featherweight/catalog.json). See
[docs/CATALOG.md](docs/CATALOG.md) for the format and the testing checklist.

## Code changes

1. Build with `powershell -ExecutionPolicy Bypass -File build.ps1` (needs Visual Studio 2022 or Build Tools).
2. Keep changes focused; one pull request per topic.
3. Match the surrounding code style.
4. Add a line to `CHANGELOG.md` under *Unreleased* if users will notice.

### Ground rules for anything that changes Windows

- Every change must be **backed up first** and **undoable**.
- Prefer the gentlest option (e.g. a service set to *Manual* rather than *Disabled*).
- Never turn off Windows Update, Microsoft Defender or other security features.
- No telemetry, no tracking, no network requests beyond the update check.

## Be kind

Be respectful and patient, especially with people who are new to GitHub or to Windows internals.
Harassment or insults aren't welcome here.
