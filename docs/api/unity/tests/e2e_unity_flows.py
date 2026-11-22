#!/usr/bin/env python3
"""End-to-end check of the exact requests the Unity sample makes (same verbs, paths, bodies).
Run: python3 e2e_unity_flows.py
"""
import base64
import json
import urllib.error
import urllib.request

API = "https://api.91.107.253.71.sslip.io"
SERVER_KEY = "defaultkey"
BASIC = "Basic " + base64.b64encode(f"{SERVER_KEY}:".encode()).decode()

ok = fail = 0


def check(name, cond, detail=""):
    global ok, fail
    if cond:
        ok += 1
        print(f"PASS {name}" + (f" :: {detail}" if detail else ""))
    else:
        fail += 1
        print(f"FAIL {name}" + (f" :: {detail}" if detail else ""))


def call(method, path, body=None, token=None, server_key=False, raw_body=None):
    data = raw_body.encode() if raw_body is not None else (json.dumps(body).encode() if body is not None else None)
    req = urllib.request.Request(API + path, data=data, method=method)
    req.add_header("Accept", "application/json")
    if data is not None:
        req.add_header("Content-Type", "application/json")
    if server_key:
        req.add_header("Authorization", BASIC)
    elif token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return r.status, json.loads(r.read() or b"{}")
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read() or b"{}")


# 1. AuthenticateDevice
status, auth = call("POST", "/v2/account/authenticate/device?create=true",
                    {"id": "unity-e2e-1"}, server_key=True)
check("1 device auth", status == 200 and "token" in auth, f"{status} uid={auth.get('token', '')[:0]}")
token, refresh = auth["token"], auth["refresh_token"]

payload_b64 = token.split(".")[1] + "=" * (-len(token.split(".")[1]) % 4)
claims = json.loads(base64.urlsafe_b64decode(payload_b64))
uid = claims["uid"]
check("1b jwt claims decode", bool(uid) and claims.get("usn"), f"uid={uid[:8]} usn={claims.get('usn')}")

# 2. UpdateAccount
status, _ = call("PUT", "/v2/account", {"username": "unity_player", "display_name": "Unity Player", "lang_tag": "en"}, token)
check("2 update account", status == 200, str(status))

# 3. WriteStorage (PUT)
status, acks = call("PUT", "/v2/storage", {"objects": [{
    "collection": "saves", "key": "slot1",
    "value": json.dumps({"level": 4, "coins": 250, "unlocked": ["sword", "shield"]}),
    "permission_read": 2, "permission_write": 1}]}, token)
check("3 storage write", status == 200 and acks.get("acks"), f"{status} {json.dumps(acks)[:80]}")

# 4. ListStorage (GET collection)
status, listing = call("GET", f"/v2/storage/saves?user_id={uid}", token=token)
objs = listing.get("objects", [])
check("4 storage list", status == 200 and len(objs) == 1, f"{status} objects={len(objs)} value={objs[0]['value'] if objs else None}")

# 5. SubmitScore (single record, int64 as string)
status, record = call("POST", "/v2/leaderboard/global_score",
                      {"score": "3600", "subscore": "0", "metadata": json.dumps({"level": 4})}, token)
check("5 submit score", status == 200 and str(record.get("score")) == "3600", f"{status} score={record.get('score')} rank={record.get('rank')}")

# 6. ReadLeaderboard
status, board = call("GET", "/v2/leaderboard/global_score?limit=5", token=token)
records = board.get("records", [])
check("6 read leaderboard", status == 200 and len(records) > 0,
      f"{status} rows={len(records)} top={[(r['username'], r['score']) for r in records[:3]]}")

# 7. Rpc (body must be a JSON string)
inner = json.dumps({"reason": "unity-sample"})
status, rpc = call("POST", "/v2/rpc/daily_bonus", token=token, raw_body=json.dumps(inner))
rpc_payload = json.loads(rpc.get("payload", "{}")) if status == 200 else {}
check("7 rpc daily_bonus", status == 200 and rpc_payload.get("granted") == 100, f"{status} {rpc.get('payload', '')[:90]}")

# 8. GetAccount -> wallet
status, account = call("GET", "/v2/account", token=token)
wallet = json.loads(account.get("wallet") or "{}")
check("8 wallet after rpc", status == 200 and wallet.get("coins", 0) >= 100, f"{status} wallet={wallet}")

# 9. RefreshSession (server key)
status, refreshed = call("POST", "/v2/account/session/refresh", {"token": refresh}, server_key=True)
check("9 refresh session", status == 200 and "token" in refreshed, str(status))

# 10. Logout (bearer auth, camelCase refreshToken) then confirm the old token is dead
status, _ = call("POST", "/v2/session/logout", {"token": token, "refreshToken": refresh}, token)
check("10 logout", status == 200, str(status))
status, err = call("GET", "/v2/account", token=token)
check("10b token revoked", status == 401 and err.get("code") == 16, f"{status} {err.get('message')}")

print(f"\n{ok} passed, {fail} failed")
raise SystemExit(0 if fail == 0 else 1)
