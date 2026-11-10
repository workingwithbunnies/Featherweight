# Privacy policy

*Applies to Featherweight, the Windows app published at
[github.com/workingwithbunnies/Featherweight](https://github.com/workingwithbunnies/Featherweight). Last updated 10 November 2026.*

**In short:** Featherweight has no telemetry, no analytics, no ads and no account. It does not collect information
about you or your PC. Its only internet connection is an update check with GitHub, which you can switch off.

This program will not transfer any information to other networked systems unless specifically requested by the user
or the person installing or operating it.

## The update check (the only connection the app makes)

- **What it does:** it asks GitHub (`api.github.com`) whether a newer version of Featherweight exists, and downloads
  the newest item descriptions (`knowledge.json`, a text file) from `raw.githubusercontent.com`. It never downloads
  or installs programs.
- **When:** at most once a day when Featherweight starts, and whenever you click **Check for updates now** in
  **Settings & about**.
- **Your choice:** the welcome screen on first start explains it, with a box that is ticked by default. Untick it
  there, or any time later in **Settings & about**, and the app makes no automatic connection at all.
- **What GitHub sees:** like any visit to a website, GitHub sees your internet (IP) address and the app's version
  number (it is sent as `Featherweight/<version>`, because GitHub requires apps to identify themselves). Nothing else
  is sent: no name, no PC details, no list of your programs, no usage data. GitHub handles those requests under the
  [GitHub Privacy Statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement).
  Featherweight has no server of its own, so the maintainer never receives anything.

## What stays on your PC

Featherweight keeps its own files in `%LocalAppData%\Featherweight` (or next to the exe in portable mode):
settings, backups of every change it makes (so you can undo them), snapshots you take, and activity and error logs.
They never leave your PC on their own. To remove them, delete that folder (see *Removing Featherweight* in the README).

## Things that happen only when you click them

- **Export diagnostics** creates a text file on your PC for a bug report. You choose what it includes; your user name,
  PC name, account IDs and email addresses are removed; the file opens so you can read it first. It is sent nowhere:
  you decide whether to share it. Anything attached to a GitHub issue is public.
- **Links** (report a problem, project page, release notes, the Windows settings pages) open in your web browser or
  in Windows itself; the app only opens them.
- **Run a quick scan now** in the Security check asks Microsoft Defender to scan. Defender works under Microsoft's
  own privacy terms, as it does without Featherweight.

## Changes and questions

Changes to this policy are listed in the repository's history and in the [changelog](../CHANGELOG.md). Questions:
open a [discussion](https://github.com/workingwithbunnies/Featherweight/discussions), or use the private
vulnerability report described in [SECURITY.md](../SECURITY.md) for anything sensitive.
