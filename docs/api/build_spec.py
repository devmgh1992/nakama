#!/usr/bin/env python3
"""Turn Nakama's generated grpc-gateway spec into a clean, documented OpenAPI 3 file."""
import json
import yaml

SRC = "/root/nakama-docs/nakama-openapi.yaml"
OUT_YAML = "/root/nakama-docs/openapi.yaml"
OUT_JSON = "/root/nakama-docs/openapi.json"
PUBLIC_API = "https://api.91.107.253.71.sslip.io"

d = yaml.safe_load(open(SRC))

INTRO = """
## What this is

The complete HTTP/JSON surface of the **Nakama** game backend running for this project.
Every endpoint here is the same API the official Unity / Unreal / JS / Godot SDKs call under the hood —
so anything you can do with the SDK you can also do with plain `curl`, `UnityWebRequest` or `HttpClient`.

## Base URLs

| Environment | URL |
|---|---|
| Production (HTTPS) | `https://api.91.107.253.71.sslip.io` |
| Local / docker | `http://127.0.0.1:7350` |

Realtime (WebSocket) uses the same host on the `/ws` path:
`wss://api.91.107.253.71.sslip.io/ws?token=<session_token>&format=json`

## Two kinds of authentication

| Credential | Header | Used for |
|---|---|---|
| **Server key** | `Authorization: Basic ZGVmYXVsdGtleTo=` (username `defaultkey`, empty password) | Login and registration endpoints only. Never ship it inside a game client. |
| **Session token** | `Authorization: Bearer <token>` | Every endpoint that acts on behalf of a player. |

Server key as a ready-to-paste header: `Authorization: Basic ZGVmYXVsdGtleTo=`

## Player lifecycle in three calls

```bash
BASE=https://api.91.107.253.71.sslip.io

# 1. Log in (creates the player on first call)
curl -X POST "$BASE/v2/account/authenticate/device?create=true" \\
  -u "defaultkey:" -H 'Content-Type: application/json' \\
  -d '{"id":"device-uuid-from-client"}'

# 2. Set a username / display name shown to other players
curl -X PUT "$BASE/v2/account" \\
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \\
  -d '{"username":"ali_01","display_name":"Ali","lang_tag":"fa"}'

# 3. Read the profile back
curl "$BASE/v2/account" -H "Authorization: Bearer $TOKEN"
```

## Errors

Every failure returns the same envelope, with an HTTP status to match:

```json
{ "code": 16, "message": "Auth token invalid" }
```

`code` is a gRPC status code: `3` invalid argument, `5` not found, `6` already exists,
`7` permission denied, `16` unauthenticated, `13` internal.

## Tokens

Session tokens live **2 hours**; `refresh_token` lives 7 days and is exchanged at
`POST /v2/account/session/refresh`. Refresh before expiry rather than re-authenticating,
so the player keeps the same session and in-flight realtime socket.

## Notes for the Unity developer

* Prefer the official **Nakama Unity SDK** for game code; use this page when you need an
  endpoint the SDK wraps oddly, or when debugging from Postman.
* Device IDs come from the client (`SystemInfo.deviceUniqueIdentifier` or a GUID kept in
  `PlayerPrefs`) — reuse the same ID so the player keeps their account across sessions.
* Always `POST` with `Content-Type: application/json`; the gateway does not accept form data.
* Storage objects, leaderboards, tournaments and RPCs are all here; RPC ids are the ones
  registered by the server runtime module.
"""

TAGS = {
    "Authentication": "Log in, link and unlink providers, refresh sessions. These are the only endpoints that accept the server key.",
    "Account & Profile": "Read and update the logged-in player's profile: username, display name, avatar, metadata, locale, and public lookups of other players.",
    "Storage": "Per-player and shared JSON collections — the general-purpose persistence layer (inventory, settings, save games). Supports owner-only, public and conditional reads/writes.",
    "Leaderboards": "Create, list, submit scores to and delete leaderboards. Bucket leaderboards split players into cohorts by score range.",
    "Tournaments": "Scheduled, time-boxed competitions with join, score submission and an authoritative leaderboard that resets each cycle.",
    "Wallet & IAP": "Validate App Store / Play Store / Huawei / Samsung receipts and grant virtual currency through the server-authoritative wallet.",
    "Friends & Groups": "Friend lists, blocks, and clans/guilds with roles, membership and join requests.",
    "Chat": "Channel messages and history: room, group, direct and persistent channels.",
    "Realtime & Matchmaking": "Match listings, matchmaker statistics, parties and channel metadata — the HTTP side of the realtime socket.",
    "Notifications": "Server-sent notifications per player: list, delete, and in-app inbox for reward grants.",
    "RPC": "Call server-side functions registered by the runtime module — the place for custom game logic (economy, matchmaking rules, admin actions).",
    "Events": "Submit custom analytics events attributed to the logged-in player.",
    "System": "Health and readiness probes used by load balancers and uptime checks.",
}

# order matters: sidebar follows this order inside each tag group
TAG_GROUP = {
    "Player & Account": ["Authentication", "Account & Profile", "System"],
    "Game Systems": ["Storage", "Leaderboards", "Tournaments", "Wallet & IAP", "RPC", "Events"],
    "Social & Realtime": ["Friends & Groups", "Chat", "Realtime & Matchmaking", "Notifications"],
}


def tag_for(path: str) -> str:
    p = path.rstrip("/")
    if p in ("/healthcheck", "/v2/status"):
        return "System"
    if p.startswith("/v2/account/authenticate") or p in ("/v2/account/session/refresh", "/v2/session/logout"):
        return "Authentication"
    if p.startswith("/v2/account") or p.startswith("/v2/user"):
        return "Account & Profile"
    if p.startswith("/v2/storage"):
        return "Storage"
    if p.startswith("/v2/leaderboard"):
        return "Leaderboards"
    if p.startswith("/v2/tournament"):
        return "Tournaments"
    if p.startswith("/v2/iap") or p.startswith("/v2/wallet") or p.startswith("/v2/purchase") or p.startswith("/v2/subscription"):
        return "Wallet & IAP"
    if p.startswith("/v2/friend") or p.startswith("/v2/group"):
        return "Friends & Groups"
    if p.startswith("/v2/channel") or p.startswith("/v2/chat"):
        return "Chat"
    if p.startswith("/v2/match") or p.startswith("/v2/party") or p.startswith("/v2/rt"):
        return "Realtime & Matchmaking"
    if p.startswith("/v2/notification"):
        return "Notifications"
    if p.startswith("/v2/rpc"):
        return "RPC"
    if p.startswith("/v2/event"):
        return "Events"
    if p.startswith("/v2/console"):
        return "Console"
    return "Other"


d["openapi"] = "3.0.3"
d["info"] = {
    "title": "Nakama Server API",
    "version": "3.42.0",
    "description": INTRO.strip(),
    "license": {"name": "Apache 2.0", "url": "https://github.com/heroiclabs/nakama/blob/master/LICENSE"},
}
d["externalDocs"] = {
    "description": "Official Nakama docs",
    "url": "https://heroiclabs.com/docs/nakama/",
}
d["servers"] = [
    {"url": PUBLIC_API, "description": "Production (HTTPS, WebSocket on /ws)"},
    {"url": "http://127.0.0.1:7350", "description": "Local docker instance"},
]

# group operations under readable tags, drop grpc-gateway noise
for path, ops in d["paths"].items():
    tag = tag_for(path)
    for method, op in ops.items():
        if method not in ("get", "post", "put", "delete", "patch"):
            continue
        op["tags"] = [tag]
        op.pop("x-codegen-request-body-name", None)
    d["paths"][path] = {k: v for k, v in ops.items() if k in ("get", "post", "put", "delete", "parameters")}

d["tags"] = [{"name": n, "description": desc} for n, desc in TAGS.items()]
d["x-tagGroups"] = [{"name": g, "tags": t} for g, t in TAG_GROUP.items()]

# make the three auth schemes self-explanatory in the UI
sec = d["components"]["securitySchemes"]
sec["BasicAuth"] = {
    "type": "http",
    "scheme": "basic",
    "description": "HTTP Basic where the username is the **server key** (`defaultkey`) and the password is empty. Only login/registration endpoints accept it.",
}
sec["BearerJwt"] = {
    "type": "http",
    "scheme": "bearer",
    "bearerFormat": "JWT",
    "description": "Player session token returned by any `/v2/account/authenticate/*` call. Valid for 2 hours.",
}
sec["HttpKeyAuth"] = {
    "type": "apiKey",
    "in": "query",
    "name": "http_key",
    "description": "Runtime HTTP key for server-to-server calls to RPC endpoints. Keep it on the server only.",
}

# annotate the operations a client dev touches first
STARTER = {
    "/v2/account/authenticate/device": "**Start here.** One call registers the player if the device id is unknown, otherwise it logs them in. Returns `token` (use as `Bearer`) and `refresh_token`.",
    "/v2/account/authenticate/email": "Email + password sign-in. Add `?create=true` to register.",
    "/v2/account": "The logged-in player's profile. `PUT` is how you set `username`, `display_name`, `avatar_url`, `lang_tag` and metadata.",
    "/v2/storage": "Write up to 100 objects per call. `permission_read` / `permission_write` accept `0` owner-only, `1` public, `2` owner-read... see the schema for the full bitmask.",
    "/v2/rpc/{id}": "Calls a function registered by the runtime module. The `id` is exactly the name passed to `initializer.registerRpc`.",
    "/healthcheck": "Returns 200 when the server can serve traffic. No auth.",
}

count = 0
for path, ops in d["paths"].items():
    for method, op in ops.items():
        if method not in ("get", "post", "put", "delete", "patch"):
            continue
        if path in STARTER and method in ("post", "put", "get"):
            op["description"] = STARTER[path] + ("\n\n" + op["description"] if op.get("description") else "")
            count += 1

with open(OUT_YAML, "w") as f:
    yaml.safe_dump(d, f, sort_keys=False, allow_unicode=True, width=1000)
with open(OUT_JSON, "w") as f:
    json.dump(d, f, ensure_ascii=False, indent=1)

print(f"paths={len(d['paths'])} annotated={count} tags={len(d['tags'])}")
print("bytes yaml/json:", __import__("os").path.getsize(OUT_YAML), __import__("os").path.getsize(OUT_JSON))
