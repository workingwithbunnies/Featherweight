# Contributing to Featherweight

Thanks for helping! There are many ways to contribute, and most don't need any coding.

## Without coding

- **Report bugs** using the *Bug report* form under Issues.
- **Help describe an app** Featherweight found on your PC (ASUS, MSI, Lenovo, HP, Razer…): in the app, open
  **Optimize**, select it, click **Copy for a report**, and paste that into the *Describe an app* form. It contains
  only that app's details. Tell us what it does that people might still want.
- **Test on your PC** and tell us what Featherweight detected and suggested. Reports from different PC models are very valuable.
- **Improve the wording** of descriptions or docs.

## Privacy when you share

Everything on GitHub issues and discussions is **public**. Featherweight never sends anything by itself, and what it
copies or exports has your user name, PC name, Windows account IDs and email addresses removed. Still, read it before
posting, and share only what the report needs:

- **Copy for a report** (Optimize): one app's names and facts. Best for describing an app.
- **Export diagnostics** (Settings & about): you choose what to include. "Featherweight's activity" is usually
  enough for a bug; tick "Background software" only when asking for your PC's apps to be supported.
- Security problems: please use private vulnerability reporting (see [SECURITY.md](SECURITY.md)), not a public issue.

## Writing descriptions (JSON, no C#)

- Descriptions of found apps: [`src/Featherweight/knowledge.json`](src/Featherweight/knowledge.json), guide in
  [docs/KNOWLEDGE.md](docs/KNOWLEDGE.md). Every sentence must be true on any PC, with a source or a test.
- Built-in optimizations: [`src/Featherweight/catalog.json`](src/Featherweight/catalog.json), guide in
  [docs/CATALOG.md](docs/CATALOG.md).

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
