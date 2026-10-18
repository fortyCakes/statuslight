#!/usr/bin/env bash
# Forwards a slimmed-down Claude Code hook event to ntfy, where the StatusLight daemon polls for it.
# Does nothing unless STATUSLIGHT_NTFY_TOPIC is set, so it's inert in local sessions.
# Always exits 0: a status light must never block or break a session.

[ -n "$STATUSLIGHT_NTFY_TOPIC" ] || exit 0

payload=$(python3 -c '
import json, os, sys
e = json.load(sys.stdin)
print(json.dumps({
    "hook_event_name": e.get("hook_event_name"),
    "session_id": e.get("session_id"),
    "notification_type": e.get("notification_type"),
    "repo": os.path.basename((e.get("cwd") or "").rstrip("/")),
}))
' 2>/dev/null) || exit 0

curl -s --max-time 3 -o /dev/null -d "$payload" "${STATUSLIGHT_NTFY_URL:-https://ntfy.sh}/$STATUSLIGHT_NTFY_TOPIC" || true
exit 0
