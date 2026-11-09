# Security policy

Featherweight changes Windows services and registry settings and can run as administrator, so security
reports are taken seriously.

## Reporting a vulnerability

**Please don't open a public issue for security problems.**

Use GitHub's private reporting instead: go to the repository's **Security** tab → **Report a vulnerability**.
Only the maintainers can see the report.

Please include:
- what an attacker could do, and what they'd need first (for example "a local user without admin rights could…");
- the Featherweight version and Windows version;
- steps to reproduce, if you have them.

You'll get a reply as soon as possible. Once a fix is released, the report can be published with credit to you
(if you want).

## Supported versions

Only the latest release receives security fixes.

## What Featherweight does on your PC

To make reviewing easier:
- It changes only what's listed in the Optimize page's "What it changes" section, and saves the original values first.
- Its only network requests are an optional update check (at most once a day): `api.github.com` for the latest release,
  and `raw.githubusercontent.com` for the newest `knowledge.json` (item descriptions, text only). The user chooses it on first
  start and can turn it off in Settings. No telemetry; nothing about the PC is sent. It never downloads or installs programs.
  A downloaded `knowledge.json` can only describe items: it can't mark anything Recommended or unlock driver helpers.
- It doesn't install drivers or services, and doesn't run at startup.
