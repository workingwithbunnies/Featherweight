# Featherweight roadmap

Plain-language plan for the project. Each item says **why it matters** and **what it costs or risks**.
Order is roughly priority. Users are welcome to suggest more in GitHub Issues.

---

## Before the first public release (v1.0)

✅ = done (in the next release). These are about one promise: *it works correctly and safely on anyone's PC, not just the one it was built on.*

### 1. Catalog v2: decide per PC, not per laptop ✅
**Why:** today's list was written for one specific laptop. On someone else's PC, "turn off AlienFX" could kill RGB
lighting they love, and "turn off Hyper-V VM manager" could break their virtual machines.
**What:** every optimization gets *conditions* (e.g. "only recommend if no Hyper-V VMs exist") and a level:
- **Recommended** – safe for almost everyone, preselected.
- **Optional** – only if you don't use the feature; explained clearly.
- **Advanced** – hidden unless you ask for it.

Descriptions become generic ("If your keyboard only has a single colour…") instead of "this laptop".
**Cost:** moderate. **Risk if skipped:** the biggest one; wrong advice on other PCs.

### 2. System Restore point before the first change ✅
**Why:** a second safety net on top of our own backups. If anything ever goes wrong, Windows itself can roll back.
**Cost:** small. **Note:** some PCs have System Restore turned off; the app will say so instead of failing.

### 3. First-run welcome + honest expectations ✅
**Why:** new users should know in 30 seconds what the app does, what it never does, and that everything is undoable.
Also an honest note: *RAM cleaning is a short-term boost; the lasting win is fewer background apps.*
**Cost:** small.

### 4. Version number + "Check for updates" ✅
**Why:** you'll ship fixes; users need to know they exist.
**What:** a version shown in the app, and a button that asks GitHub Releases if a newer version exists and opens the download page.
No silent auto-updating at first, because that's a security-sensitive feature to get right.
**Cost:** small. **Privacy:** it's the only network request the app makes, and it can be turned off.

### 5. "Export diagnostics" for bug reports ✅
**Why:** when a user reports "it didn't work", you need facts: Windows version, what was detected, what failed.
**What:** one button that saves a text/zip report, with **user name and PC name removed**, so users can attach it to a GitHub issue.
**Cost:** small. **Biggest payoff** for handling feedback.

### 6. Code signing (applying; build provenance added in 0.1.0-alpha.3)
**Why:** unsigned apps show a scary "Windows protected your PC" (SmartScreen) warning, and tools that change
services/registry are often **falsely flagged by antivirus**. A signature proves the exe really comes from you.
**Options:** [SignPath Foundation](https://signpath.org/) (free for open-source projects, needs an application),
or Azure Trusted Signing (about $10/month). Plus submitting each release to Microsoft as a false positive if flagged.
**Cost:** paperwork/money. **Risk if skipped:** many users won't trust or even be able to run it.

### 7. Automatic builds + releases on GitHub (CI) ✅
**Why:** every release is built the same clean way on GitHub's servers, not on your PC, and gets published
with a checksum (SHA-256) so users can verify the download.
**Cost:** small, one-time setup.

### 8. Crash safety ✅
**Why:** if something unexpected happens on a stranger's PC, the app should show a friendly message,
write the details to a log, and offer "copy error for a bug report" instead of a scary .NET error box.
**Cost:** small.

### 9. Project docs ✅
README with screenshots, FAQ ("Is a RAM cleaner snake oil?" answered honestly), SECURITY.md (how to report
security issues privately), CONTRIBUTING.md, and issue templates that ask for the diagnostics export.

---

## After v1.0 (ideas, in rough priority)

| Idea | Why users would like it | Cost |
|---|---|---|
| **Startup apps manager** | Partly done: Optimize now lists startup apps, services, startup tasks and Store apps from any brand ("Not reviewed"). Still to do: a dedicated view with startup impact and sorting. | Small–medium |
| **More vendors in the catalog** | ASUS Armoury Crate, MSI Center, Lenovo Vantage, HP Omen, Acer, Razer, Logitech G Hub, Corsair iCUE, game launchers at startup, OneDrive, Teams, Copilot, Xbox Game Bar, Edge startup boost… | Ongoing; community can add these as JSON without coding |
| ~~Dark mode~~ ✅ | Done: *Midnight* is one of six themes (Settings & about → Theme). | – |
| **Tray icon** | Live RAM % next to the clock; right-click → Free up RAM. | Medium |
| **Game mode** | One click before gaming: clean RAM and pause chosen background apps; one click to bring them back. | Medium |
| **Opt-in auto clean** | e.g. "when RAM in use goes above 85%". Off by default, with an honest explanation. | Small |
| **Translations** | Non-English users. | Medium, ongoing |
| **Accessibility** | Keyboard-only use, screen readers, high-contrast themes. | Medium |
| **Install options** | winget / Scoop / installer, in addition to the portable zip. | Medium |

---

## Things Featherweight will never do

Being upfront about this builds trust:

- **No telemetry or tracking.** The only network request is the optional update check (which also fetches item descriptions).
- **No fake "PC health score", registry cleaner, or driver updater.** Those are scareware patterns.
- **No turning off Windows Update, Defender, or other security features.**
- **No change without showing it first and backing it up.**
