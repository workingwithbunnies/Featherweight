# Adding optimizations to the catalog

Everything the Optimize page shows comes from [`src/Featherweight/catalog.json`](../src/Featherweight/catalog.json).
Adding support for another vendor's background app means adding a JSON entry. No C# needed.

Users can also add their own items without rebuilding: put a file named `custom-optimizations.json`
(same format) in `%LocalAppData%\Featherweight`. An entry with the same `id` as a built-in one replaces it.

## An entry

```json
{
  "id": "vendor-thing",                 // unique, lowercase-with-dashes
  "category": "Vendor name",            // groups items in the list
  "title": "Name people recognise",
  "level": "recommended",               // recommended | optional | advanced (see below)
  "description": "What it is, and what someone loses by turning it off. Must be true on ANY PC.",
  "estimateMB": 80,                     // typical memory use while running (shown when it isn't running)
  "risk": "Safe",                       // Safe | Low | Moderate: shown in the details panel
  "processes": ["ProcessName"],         // process names without .exe: used to show live memory use
  "detect": { "service": "ServiceName" },
  "checks": [ ... ],                    // optional per-PC rules, see below
  "actions": [ ... ]                    // what Optimize changes (Restore default puts it back from the backup)
}
```

### Levels

| Level | Meaning | Shown |
|---|---|---|
| `recommended` | Safe for almost everyone; included in **Apply recommended**. | Always |
| `optional` | Depends on whether the user uses the feature; the description must say how to decide. | Always |
| `advanced` | Effects vary by PC model or aren't fully understood. | Only with *Show advanced*, or if already optimized |

### Detection: only show what's installed

`detect` keys (any one match is enough; a value may also be a list of alternatives):

| Key | Matches when |
|---|---|
| `service` | a Windows service with that name exists |
| `file` | the file exists (environment variables like `%ProgramFiles%` work) |
| `regkey` | the registry key exists, e.g. `HKLM:\\SOFTWARE\\Vendor` |
| `package` | a Store/packaged app with that name prefix is installed |
| `process` | the process is running right now |
| `dell-agent` | a Dell TechHub agent with that program file name is registered |

Without `detect`, the item is shown if at least one action applies to this PC.

### Checks: adapt the advice to this PC

The first check whose probe matches decides the suggestion, and its `note` is shown as "On this PC: …".
`{text}` is replaced with what the probe found.

```json
"checks": [
  { "probe": "hypervVms", "is": true,  "verdict": "avoid",       "note": "{text} on this PC. Turning this off would stop them." },
  { "probe": "hypervVms", "is": false, "verdict": "recommended", "note": "{text}." }
]
```

Verdicts: `recommended`, `optional`, `avoid` (shown as *Not advised here*; optimizing it needs an extra confirmation).

Available probes (more can be added in `Probes.cs`):

| Probe | True when |
|---|---|
| `hypervVms` | Hyper-V virtual machines exist on this PC |
| `firmwareViaWindowsUpdate` | Windows Update has installed BIOS/UEFI firmware from this PC's maker |
| `phonePaired` | a phone has been paired with Phone Link |

### Actions

```json
{ "type": "service",    "name": "ServiceName", "start": "Manual", "stop": true }
{ "type": "registry",   "path": "HKCU:\\Software\\...", "name": "Value", "kind": "DWord", "value": 1 }
{ "type": "task",       "path": "\\", "name": "ScheduledTaskName" }
{ "type": "dell-agent", "exe": "Some.SubAgent.exe", "start": "Manual" }
```

- `start` for services: `Manual` (starts when something needs it), `Disabled`, `Automatic`, `AutomaticDelayed`.
  Prefer **Manual**: it's the gentlest option.
- `HKCU` changes always go to the signed-in user, even if someone elevates with a different admin account.
- For a setting that lives inside another app (no registry/service to change), use
  `"manual": "Step-by-step instructions"` instead of `actions`; the item is "optimized" when none of its `processes` run.

## Before opening a pull request

1. Test **Optimize**, a reboot, and **Restore default** on a real PC that has the software.
2. Check the description is true for everyone, not just your PC.
3. Pick the most cautious level that's honest. When unsure, use `optional` or `advanced`.
