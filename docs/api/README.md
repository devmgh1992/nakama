# Nakama API — developer reference

Generated, human-readable API documentation for the Nakama game backend used by this project.

**Hosted reference:** https://docs.91.107.253.71.sslip.io
**API base URL:** `https://api.91.107.253.71.sslip.io` (WebSocket: `wss://api.91.107.253.71.sslip.io/ws`)

| File | What it is |
|---|---|
| `openapi.yaml` | Cleaned OpenAPI 3.0 spec: 79 operations, 13 grouped tags, three auth schemes documented |
| `index.html` | Static [Scalar](https://scalar.com) reference UI — open it locally with `python3 -m http.server` or serve it from GitHub Pages |

## What was cleaned up vs. the upstream spec

Upstream ships `apigrpc/apigrpc.swagger.json` (Swagger 2.0, `host: 127.0.0.1:7350`, one flat list of 79 endpoints,
no tag grouping). It is converted here and then:

- `servers` → production HTTPS host + local docker host
- every operation grouped into 13 readable tags, wired into 3 sidebar groups via `x-tagGroups`
- auth schemes explained (`BasicAuth` = server key, `BearerJwt` = 2h player session, `HttpKeyAuth` = server-to-server RPC)
- introductory guide added (auth flow, token lifetime, error envelope, Unity notes)
- starter notes on the operations a client dev touches first (`authenticate/device`, `/v2/account`, `/v2/storage`, `/v2/rpc/{id}`)
- grpc-gateway noise dropped (`x-codegen-request-body-name`)

## Regenerate

```bash
# 1. pull the upstream spec
curl -sL -o apigrpc.swagger.json \
  https://raw.githubusercontent.com/heroiclabs/nakama/master/apigrpc/apigrpc.swagger.json

# 2. Swagger 2.0 -> OpenAPI 3
npx --yes swagger2openapi apigrpc.swagger.json -o nakama-openapi.yaml --yaml

# 3. enrich (tags, servers, auth docs, intro) and emit openapi.yaml + openapi.json
python3 build_spec.py
```

`build_spec.py` is the single source of truth for the cleanup, so re-running it after a Nakama upgrade keeps the
docs consistent.

## Auth cheat-sheet

```bash
BASE=https://api.91.107.253.71.sslip.io

# create-or-login a player with a device id (server key = basic auth, empty password)
TOKEN=$(curl -s -X POST "$BASE/v2/account/authenticate/device?create=true" \
  -u "defaultkey:" -H 'Content-Type: application/json' -d '{"id":"device-uuid"}' \
  | python3 -c 'import sys,json;print(json.load(sys.stdin)["token"])')

# then act as that player
curl -X PUT "$BASE/v2/account" -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"username":"ali_01","display_name":"Ali"}'
```

- `Authorization: Basic ZGVmYXVsdGtleTo=` — server key, login endpoints only, never in a shipped client
- `Authorization: Bearer <token>` — player session, 2 hours, refresh at `POST /v2/account/session/refresh`
- errors are `{"code": <grpc code>, "message": "..."}` (`16` unauthenticated, `6` already exists, `5` not found)

## Attribution

Spec derived from [heroiclabs/nakama](https://github.com/heroiclabs/nakama) (Apache-2.0). This directory only adds
documentation metadata; the API itself is unchanged.
