# Unity client for the Nakama server

Drop-in C# client for the game backend at `https://api.91.107.253.71.sslip.io` — **no SDK, no packages, no Newtonsoft.**
Everything is `UnityWebRequest` + coroutines, compatible with Unity 2019.4 and newer.

```
unity/
├── MiniJson.cs                  # small JSON reader/writer (Nakama payloads are dictionaries, JsonUtility can't do that)
├── NakamaClient.cs              # session, transport, auth, account
├── NakamaClient.Gameplay.cs     # storage, leaderboards, RPC
├── NakamaSample.cs              # MonoBehaviour demo scene script (press 1..6)
├── server/main.js               # Nakama JS runtime module that seeds the demo content (leaderboards + RPCs)
└── tests/                       # headless compile + end-to-end checks used to verify this folder
```

## Install

1. Copy `MiniJson.cs`, `NakamaClient.cs`, `NakamaClient.Gameplay.cs` (and optionally `NakamaSample.cs`) into
   `Assets/Scripts/NakamaSample/`.
2. Create an empty GameObject → **Add Component → Nakama Sample**.
3. `baseUrl` = `https://api.91.107.253.71.sslip.io`, `serverKey` = `defaultkey`.
4. Press Play, then `1` (login) → `2` (username) → `3` (save/load) → `4` (score) → `5` (RPC) → `6` (refresh) → `0` (logout).

`NakamaSample.cs` is only a demo; the three library files are what the game code needs:

```csharp
var client = new NakamaClient("https://api.91.107.253.71.sslip.io", "defaultkey");

yield return client.AuthenticateDevice(SystemInfo.deviceUniqueIdentifier,
    session => Debug.Log("logged in as " + session.UserId),
    error   => Debug.LogError(error));

yield return client.Rpc("daily_bonus", new Dictionary<string, object> { { "reason", "level_up" } },
    result => Debug.Log("wallet: " + MiniJson.Serialize(result)),
    error  => Debug.LogError(error));
```

## What each method calls

| C# method | HTTP |
|---|---|
| `Healthcheck` | `GET /healthcheck` |
| `AuthenticateDevice` | `POST /v2/account/authenticate/device?create=true` (server key) |
| `AuthenticateEmail` | `POST /v2/account/authenticate/email?create=true` (server key) |
| `RefreshSession` | `POST /v2/account/session/refresh` (**server key**) |
| `Logout` | `POST /v2/session/logout` (**bearer token**, field `refreshToken`) |
| `GetAccount` / `UpdateAccount` | `GET` / `PUT /v2/account` |
| `WriteStorage` | `PUT /v2/storage` |
| `ListStorage` | `GET /v2/storage/{collection}?user_id=…` |
| `ReadStorage` (by keys) | `POST /v2/storage` with `object_ids` |
| `DeleteStorage` | `PUT /v2/storage/delete` |
| `SubmitScore` | `POST /v2/leaderboard/{id}` — **one record object**, not `{"records":[…]}` |
| `ReadLeaderboard` | `GET /v2/leaderboard/{id}?limit=…&owner_id=…` |
| `Rpc` | `POST /v2/rpc/{id}` — body is a JSON **string**, reply is `{"payload":"…"}` |

## Contract traps this client already handles (each one cost a debugging session)

| Trap | Reality |
|---|---|
| Assuming `POST /v2/storage` writes | It **reads by id**. Writes are `PUT`. A wrong verb returns `200 {}` and silently writes nothing. |
| Sending `{"records":[{…}]}` to a leaderboard | The endpoint takes a **single** record body. The array form is accepted but the score is stored as `0` — no error. |
| Sending `"score": 1500` | int64 fields are proto-JSON **strings**: `"score": "1500"`. |
| Posting an RPC payload as a JSON object | The body must be a JSON **string**; otherwise `400 json: cannot unmarshal object into Go value of type string`. |
| Using the bearer token for refresh/logout | `refresh` needs the **server key**; `logout` needs the **bearer token**. Opposite credentials, same style of endpoint. |
| Reading `wallet` as an object | `account.wallet` is a **JSON string** — `ParseWallet()` unescapes it. |
| `GET /v2/leaderboard` (list all) | Returns `404` on server 3.42 — use known leaderboard ids. |

## Server side (what makes the demo work)

`server/main.js` is a Nakama **JavaScript runtime module**. It creates two leaderboards on boot and registers three RPCs:

| Item | Meaning |
|---|---|
| `global_score` | descending, operator `best`, weekly reset (Mondays) |
| `level_speedrun` | ascending (fastest time wins), no reset |
| RPC `server_time` | returns the server clock — proves RPC wiring |
| RPC `daily_bonus` | server-authoritative wallet grant of 100 coins |
| RPC `submit_and_echo` | echoes the user id, username and the payload the client sent |

Mount it read-only and point the runtime at it:

```yaml
# docker-compose.yml (nakama service)
    volumes:
      - ./data/modules:/nakama/data/modules:ro
    entrypoint: [ ... ]
      --runtime.path /nakama/data/modules
      --runtime.js_entrypoint main.js
```

`--runtime.js_entrypoint` is the part everyone misses: without it Nakama logs
`Found runtime modules count: 0` even though the file is in place.

## Verifying this folder without Unity

`tests/` contains the harness used to ship it:

```bash
# 1. compile every file against a UnityEngine stub (catches typos, wrong overloads)
mcs -langversion:7.2 -out:/tmp/sample.exe \
  tests/UnityShim.cs tests/SmokeTest.cs \
  MiniJson.cs NakamaClient.cs NakamaClient.Gameplay.cs NakamaSample.cs

# 2. JSON / JWT / request-body assertions
mono /tmp/sample.exe

# 3. replay the exact HTTP flows the client makes against the live server
python3 tests/e2e_unity_flows.py
```

Current state: **17/17** unit assertions and **12/12** live HTTP checks pass.

## Notes

- Tokens: session 2 h, refresh 7 days. `NakamaSession.IsExpired` flips one minute early, so call `RefreshSession`
  before the socket drops.
- Errors surface as `NakamaException` with the gRPC `code` (16 unauthenticated, 6 already exists, 5 not found,
  3 invalid argument) plus the HTTP status.
- Device id: derive once per install and store it in `PlayerPrefs` — the sample does this, so re-running keeps the
  same account instead of creating a new player.
- For realtime (matches, chat, presence) use the official **Nakama Unity SDK**; this client deliberately covers the
  HTTP surface only.
