# Code signing policy

**Status:** Featherweight is applying for free code signing for open-source projects from the
[SignPath Foundation](https://signpath.org). Until that is approved, releases are **not** Authenticode-signed. Every
release since 0.1.0-alpha.3 comes with **build provenance** instead: a record, signed by GitHub, that the exe was built
by this repository's GitHub Actions workflow from a specific public commit. See
[README → Download and run](../README.md#download-and-run) for how to check a file.

Once signing is in place, this page will say so, and release binaries will be signed with a certificate issued to
SignPath Foundation (shown by Windows as the publisher). Every signing request is approved by hand.

## What gets signed

Only `Featherweight.exe` from this repository's [Releases page](https://github.com/workingwithbunnies/Featherweight/releases).
It is built on GitHub's servers by [`.github/workflows/release.yml`](../.github/workflows/release.yml) from a version tag
on `main`. Nothing is built or signed on a personal computer, and no third-party binaries are signed.

## Team and roles

| Role | Who |
|---|---|
| Authors (committers) | [@workingwithbunnies](https://github.com/workingwithbunnies) |
| Reviewers (every change from anyone else, e.g. a pull request, is reviewed before merging) | [@workingwithbunnies](https://github.com/workingwithbunnies) |
| Approvers (each signing request is approved by hand) | [@workingwithbunnies](https://github.com/workingwithbunnies) |

Changes reach `main` only through pull requests that build successfully on GitHub. Much of the code is written with
Claude (Anthropic's AI coding assistant), and every change is reviewed and tested by the maintainer before it's merged
or released. Accounts with access to this repository and to the signing service must use multi-factor authentication.

## What the program does

Featherweight shows memory use, switches off background apps and services that the user chooses (each change is backed
up first and can be undone), frees memory, and offers a **read-only** security check of the PC it runs on: it reads that
PC's own settings (antivirus, firewall, updates, sharing, startup entries and similar) and explains them in plain
language. It does not scan networks or other computers, and it does not look for or exploit vulnerabilities. Every
change to the system is shown to the user before it is made. It has no installer: removing it means deleting the exe
and its data folder (see the README).

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user
or the person installing or operating it. Its only connection is an update check with GitHub, explained on first start
and easy to switch off; there is no telemetry. Full details: [Privacy policy](PRIVACY.md).

## Reporting fake or tampered copies

See [SECURITY.md](../SECURITY.md#fake-copies).
