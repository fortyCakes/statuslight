# StatusLight

Drives a BlinkStick Square as a status light for Claude Code sessions (local and cloud), with room for other sources later.

## How it works

```
sources ──▶ SignalStore ──▶ ZonedCompositor ──▶ Renderer ──▶ ILedDevice
```

- **Sources** report signals: `(source, key, state, expiry)`. Claude Code hooks POST to the local HTTP API, and cloud
  sessions arrive through an ntfy topic that the daemon polls.
- **SignalStore** keeps the current signals and drops them when their TTL runs out, so a crashed session can't leave
  a light stuck on.
- **ZonedCompositor** gives each source a zone of LEDs, with one LED per signal. Slots are stable, and when a zone
  overflows, its last LED shows the most urgent of the signals that didn't get one.
- **Renderer** evaluates effects (solid, pulse, blink) at 30 fps and sends frames to the device.
- **BlinkStickDevice** tolerates the device disappearing (e.g. a USB switcher) and repaints the full frame when it
  comes back.

Default layout: LEDs 0–4 are Claude sessions, and LEDs 5–7 are reserved.

| State          | Claude event                                     | Default look       |
|----------------|--------------------------------------------------|--------------------|
| Idle           | `SessionStart`, or Done after 10 minutes         | dim white          |
| Working        | `UserPromptSubmit`, `PreToolUse`, `PostToolUse`  | subtle blue pulse  |
| NeedsAttention | `Notification` (permission prompt, question)     | amber blink        |
| Unread         | local `Stop` while the session's tab isn't in view | slow amber pulse |
| Done           | `Stop` while in view or from the cloud, or Unread once you look | dim green |
| (removed)      | `SessionEnd`                                     | off                |

Colours, effects, zones, TTLs and brightness are all in `src/StatusLight.Host/appsettings.json` and reload live.

## Running

```powershell
dotnet run --project src/StatusLight.Host
```

To work without the hardware, set `"Device": "Console"` and frames are logged instead.

### Starting at logon

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install-startup.ps1
```

This publishes a Release build (windowless) to `%LOCALAPPDATA%\StatusLight` and registers a `StatusLight` Scheduled
Task that starts it at logon and restarts it if it crashes. The task's working directory is `src/StatusLight.Host`,
so it reads the repo's `appsettings*.json` and config changes still reload live. Code changes need the script to be
run again. Pass `-Uninstall` to remove it.

To use `dotnet run` while the task is installed, run `Stop-ScheduledTask StatusLight` first, because both use port 5789.
Restart the task afterwards with `Start-ScheduledTask StatusLight`.

## Local Claude Code sessions

Merge [hooks/local/settings.json](hooks/local/settings.json) into `~/.claude/settings.json`. Each hook pipes its event
JSON to `http://127.0.0.1:5789/claude/hook`, and fails silently and instantly if the daemon isn't running.

### Unread vs Done (VS Code companion extension)

The daemon can only tell whether you've seen a finished reply if it knows what's on screen. The extension in
[vscode-extension/](vscode-extension/) reports the active Claude tab of each editor group (while the window is
focused) to `POST /vscode/view`, on every change plus a 30s heartbeat. The daemon matches those tab labels against
the session's title from its transcript (`custom-title`, else `ai-title`). VS Code truncates long labels to a prefix
plus "…", so sessions whose titles share that prefix count as viewed together. A window's report goes stale after 90s.

Without the extension, every `Stop` shows as Unread. Unlike Done, Unread doesn't fade to Idle.

**Limitation:** this only works for Claude Code *editor tabs* ("Claude Code: Open in New Tab"). Other extensions
can't see the Claude Code chat panel in the sidebar, so a session there always shows as Unread.

To build and install it (vsce 3.2.1 is the last version that runs on Node 20.10):

```powershell
cd vscode-extension
npx --yes @vscode/vsce@3.2.1 package --skip-license --allow-missing-repository -o statuslight-vscode.vsix
code --install-extension statuslight-vscode.vsix --force
```

Then reload the VS Code window.

## Cloud Claude Code sessions

1. Pick an unguessable ntfy topic and put it in `src/StatusLight.Host/appsettings.Local.json` (git-ignored):
   ```json
   { "CloudRelay": { "Topic": "statuslight-…" } }
   ```
2. In each repo you use in the cloud, copy [hooks/cloud/statuslight-ntfy.sh](hooks/cloud/statuslight-ntfy.sh) to
   `.claude/hooks/` and merge [hooks/cloud/settings.json](hooks/cloud/settings.json) into `.claude/settings.json`.
3. In the cloud environment's settings, set `STATUSLIGHT_NTFY_TOPIC` to the same topic and allow network access to
   `ntfy.sh`. The script does nothing when the variable is unset, so local sessions in the same repo don't
   double-report.

Nothing reports when you view a cloud session (or archive it; the container is usually asleep by then), so a
finished cloud turn goes straight to Done rather than Unread. Questions and permission prompts still show as
NeedsAttention. Updates arrive up to one poll interval (15s) late.

## HTTP API

| Method   | Path                       | Body                                              |
|----------|----------------------------|---------------------------------------------------|
| `POST`   | `/claude/hook`             | Claude Code hook payload                          |
| `GET`    | `/signals`                 |                                                   |
| `PUT`    | `/signals/{source}/{key}`  | `{"state": "Working", "ttlSeconds": 300, "label": "…"}` |
| `DELETE` | `/signals/{source}/{key}`  |                                                   |
| `GET`    | `/device`                  |                                                   |
| `POST`   | `/vscode/view`             | `{"windowId": "…", "titles": ["…"]}`               |
| `GET`    | `/vscode/view`             | (diagnostics: each window's latest report)        |

A source only shows up on the light if a zone in `Compositor:Zones` is assigned to it.

## Adding a source

Write a background service (see `NtfyPollingSource`) that calls `SignalStore.Upsert`/`Remove`,
or have an external script call the HTTP API. Then give the source a zone in `appsettings.json`.
