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
| Working        | `UserPromptSubmit`, `PreToolUse`, `PostToolUse`  | blue pulse         |
| NeedsAttention | `Notification` (permission prompt, question)     | amber blink        |
| Done           | `Stop`                                           | green              |
| (removed)      | `SessionEnd`                                     | off                |

Colours, effects, zones, TTLs and brightness are all in `src/StatusLight.Host/appsettings.json` and reload live.

## Running

```powershell
dotnet run --project src/StatusLight.Host
```

To work without the hardware, set `"Device": "Console"` and frames are logged instead.

## Local Claude Code sessions

Merge [hooks/local/settings.json](hooks/local/settings.json) into `~/.claude/settings.json`. Each hook pipes its event
JSON to `http://127.0.0.1:5789/claude/hook`, and fails silently and instantly if the daemon isn't running.

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

## HTTP API

| Method   | Path                       | Body                                              |
|----------|----------------------------|---------------------------------------------------|
| `POST`   | `/claude/hook`             | Claude Code hook payload                          |
| `GET`    | `/signals`                 |                                                   |
| `PUT`    | `/signals/{source}/{key}`  | `{"state": "Working", "ttlSeconds": 300, "label": "…"}` |
| `DELETE` | `/signals/{source}/{key}`  |                                                   |
| `GET`    | `/device`                  |                                                   |

A source only shows up on the light if a zone in `Compositor:Zones` is assigned to it.

## Adding a source

Write a background service (see `NtfyPollingSource`) that calls `SignalStore.Upsert`/`Remove`,
or have an external script call the HTTP API. Then give the source a zone in `appsettings.json`.
