# Item descriptions (knowledge base)

The Optimize page finds background software from any brand. Every found item gets a description from two sources:

1. **Facts read from the PC** (always, offline, no guessing): the maker's own description of the program, the product it
   belongs to, what starts it, whether a driver installed it (and for which kind of hardware), what a Store app is allowed
   to do, and what it is doing right now (memory, processor time, open window, internet connections). See `Facts.cs`.
2. **Our reviewed entries** in [`src/Featherweight/knowledge.json`](../src/Featherweight/knowledge.json): what the
   software is for, what you lose if you switch it off, and when to keep it. Written and checked by the project.

There are no AI-generated descriptions and no server: the only download is this file, from GitHub, during the optional
daily update check.

## Writing an entry

```json
{
  "id": "hp-comm-recovery",
  "match": { "service": "HP Comm Recover" },
  "title": "HP Comm Recovery",
  "about": "What it is and what it does, in plain words.",
  "lose": "What stops working if it's switched off.",
  "keepIf": "you use … (finishes the sentence \"Keep it if …\")",
  "suggest": "optional",
  "confidence": "based on HP community descriptions",
  "reviewed": "2026-11-06",
  "sources": [ "https://…" ]
}
```

**`match`**: every key listed must match (`*` and `?` wildcards, not case-sensitive). The values are shown in the
diagnostics export under each found item (`ids: …`), so a user's report has everything needed:

| Key | Matches |
|---|---|
| `service` | Windows service name (not the display name) |
| `task` | Scheduled task name, without the per-user `-sid-S-1-5-…` ending |
| `package` | Store app package family, e.g. `AD2F1837.OMENCommandCenter_*` |
| `startup` | Startup app entry name (Run key value or Startup folder file) |
| `exe` | Program file name, e.g. `SysInfoCap.exe` |
| `publisher` | Company as Featherweight shows it, e.g. `HP` |

**`suggest`**: `recommended` (included in *Apply recommended*), `optional`, `advanced` (only under *Show advanced*), or
`keep` (shown as *Keep*; switching it off needs an extra confirmation).

**`allowChange`**: driver helpers are view-only; `true` lets Featherweight change one. Use only after real testing.

## Rules

- Every sentence must be true on **any** PC. Prefer "may" when the source is a forum, and say so in `confidence`.
- Never claim something is safe to switch off without a source or a test. If the maker doesn't document it, say that.
- Don't use the user's own setup as a rule for everyone ("keep it if you use …" instead).
- Bump `version` at the top of the file whenever entries change, so installed copies download the new file.
- A downloaded file can't mark anything `recommended` or set `allowChange`; those only work in the copy built into a
  release, so a change to `main` alone can never make the app switch more things off in one click.
