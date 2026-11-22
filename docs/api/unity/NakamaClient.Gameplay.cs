// Storage, leaderboards and RPC — the three systems a client dev touches first.
// Every request shape here was verified against a live Nakama 3.42 server.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NakamaSample
{
    /// <summary>One leaderboard row as returned by Nakama (score arrives as a string in JSON).</summary>
    public class LeaderboardRecord
    {
        public string LeaderboardId;
        public string OwnerId;
        public string Username;
        public long Score;
        public long Subscore;
        public long Rank;
        public string Metadata;

        public static LeaderboardRecord FromJson(Dictionary<string, object> d)
        {
            if (d == null) return null;
            return new LeaderboardRecord
            {
                LeaderboardId = MiniJson.GetString(d, "leaderboard_id"),
                OwnerId = MiniJson.GetString(d, "owner_id"),
                Username = MiniJson.GetString(d, "username"),
                Score = MiniJson.GetLong(d, "score"),
                Subscore = MiniJson.GetLong(d, "subscore"),
                Rank = MiniJson.GetLong(d, "rank"),
                Metadata = MiniJson.GetString(d, "metadata"),
            };
        }

        public override string ToString() => $"#{Rank} {Username} — {Score}";
    }

    public partial class NakamaClient
    {
        // ---------------------------------------------------------------- storage

        /// <summary>
        /// Writes one object. <paramref name="value"/> is any object MiniJson can serialize
        /// (Dictionary, List, string, number) and is stored as a JSON string.
        /// permission_read/write: 0 = owner only, 1 = owner, 2 = public.
        /// </summary>
        public IEnumerator WriteStorage(string collection, string key, object value,
                                        int permissionRead = 2, int permissionWrite = 1,
                                        Action<Dictionary<string, object>> onSuccess = null,
                                        Action<NakamaException> onError = null)
        {
            var obj = new Dictionary<string, object>
            {
                { "collection", collection },
                { "key", key },
                { "value", value is string s ? s : MiniJson.Serialize(value) },
                { "permission_read", permissionRead },
                { "permission_write", permissionWrite },
            };
            var body = MiniJson.Serialize(new Dictionary<string, object>
            {
                { "objects", new List<object> { obj } },
            });

            // PUT (not POST) — POST /v2/storage is the read-by-id call.
            yield return Send("PUT", "/v2/storage", body, false, onSuccess, onError);
        }

        /// <summary>Lists every object of one collection owned by <paramref name="userId"/> (defaults to the session user).</summary>
        public IEnumerator ListStorage(string collection, Action<List<Dictionary<string, object>>> onSuccess,
                                       Action<NakamaException> onError = null, string userId = null)
        {
            string owner = string.IsNullOrEmpty(userId) ? (Session != null ? Session.UserId : null) : userId;
            string path = "/v2/storage/" + UnityWebRequestSafe(collection) + "?user_id=" + UnityWebRequestSafe(owner);

            yield return Send("GET", path, null, false,
                json =>
                {
                    var list = new List<Dictionary<string, object>>();
                    var objects = MiniJson.AsList(json != null && json.ContainsKey("objects") ? json["objects"] : null);
                    if (objects != null)
                        foreach (var o in objects)
                        {
                            var d = MiniJson.AsDict(o);
                            if (d != null) list.Add(d);
                        }
                    onSuccess?.Invoke(list);
                },
                onError);
        }

        /// <summary>Reads specific keys by id: POST /v2/storage with object_ids (this is the POST route).</summary>
        public IEnumerator ReadStorage(string collection, string[] keys,
                                       Action<List<Dictionary<string, object>>> onSuccess,
                                       Action<NakamaException> onError = null, string userId = null)
        {
            string owner = string.IsNullOrEmpty(userId) ? (Session != null ? Session.UserId : null) : userId;
            var ids = new List<object>();
            foreach (var key in keys)
                ids.Add(new Dictionary<string, object>
                {
                    { "collection", collection },
                    { "key", key },
                    { "user_id", owner },
                });

            var body = MiniJson.Serialize(new Dictionary<string, object> { { "object_ids", ids } });
            yield return Send("POST", "/v2/storage", body, false,
                json =>
                {
                    var list = new List<Dictionary<string, object>>();
                    var objects = MiniJson.AsList(json != null && json.ContainsKey("objects") ? json["objects"] : null);
                    if (objects != null)
                        foreach (var o in objects)
                        {
                            var d = MiniJson.AsDict(o);
                            if (d != null) list.Add(d);
                        }
                    onSuccess?.Invoke(list);
                },
                onError);
        }

        /// <summary>Deletes keys of a collection owned by the session user.</summary>
        public IEnumerator DeleteStorage(string collection, string[] keys,
                                         Action<Dictionary<string, object>> onSuccess = null,
                                         Action<NakamaException> onError = null)
        {
            var ids = new List<object>();
            foreach (var key in keys)
                ids.Add(new Dictionary<string, object>
                {
                    { "collection", collection },
                    { "key", key },
                    { "user_id", Session.UserId },
                });

            var body = MiniJson.Serialize(new Dictionary<string, object> { { "object_ids", ids } });
            yield return Send("PUT", "/v2/storage/delete", body, false, onSuccess, onError);
        }

        // ---------------------------------------------------------------- leaderboards

        /// <summary>Submits a score. The body is ONE record object — not {"records":[...]}.</summary>
        public IEnumerator SubmitScore(string leaderboardId, long score, long subscore = 0, string metadata = null,
                                       Action<LeaderboardRecord> onSuccess = null,
                                       Action<NakamaException> onError = null)
        {
            var record = new Dictionary<string, object>
            {
                { "score", score.ToString() },
                { "subscore", subscore.ToString() },
            };
            if (!string.IsNullOrEmpty(metadata)) record["metadata"] = metadata;

            yield return Send("POST", "/v2/leaderboard/" + UnityWebRequestSafe(leaderboardId),
                MiniJson.Serialize(record), false,
                json => onSuccess?.Invoke(LeaderboardRecord.FromJson(json)),
                onError);
        }

        /// <summary>Top records of a leaderboard; pass <paramref name="ownerId"/> to read a single player's row.</summary>
        public IEnumerator ReadLeaderboard(string leaderboardId, int limit = 10, string ownerId = null,
                                          Action<List<LeaderboardRecord>> onSuccess = null,
                                          Action<NakamaException> onError = null)
        {
            string path = "/v2/leaderboard/" + UnityWebRequestSafe(leaderboardId) + "?limit=" + limit;
            if (!string.IsNullOrEmpty(ownerId)) path += "&owner_id=" + UnityWebRequestSafe(ownerId);

            yield return Send("GET", path, null, false,
                json =>
                {
                    var list = new List<LeaderboardRecord>();
                    var records = MiniJson.AsList(json != null && json.ContainsKey("records") ? json["records"] : null);
                    if (records != null)
                        foreach (var r in records)
                        {
                            var rec = LeaderboardRecord.FromJson(MiniJson.AsDict(r));
                            if (rec != null) list.Add(rec);
                        }
                    onSuccess?.Invoke(list);
                },
                onError);
        }

        // ---------------------------------------------------------------- rpc

        /// <summary>
        /// Calls a server-side function registered by the JS runtime module.
        /// The HTTP body must be a JSON *string*; the reply wraps the result in {"payload":"…"}.
        /// </summary>
        public IEnumerator Rpc(string rpcId, Dictionary<string, object> payload = null,
                               Action<Dictionary<string, object>> onSuccess = null,
                               Action<NakamaException> onError = null)
        {
            string inner = payload == null ? "{}" : MiniJson.Serialize(payload);
            string body = MiniJson.Serialize(inner); // JSON-encode the string itself

            yield return Send("POST", "/v2/rpc/" + UnityWebRequestSafe(rpcId), body, false,
                json =>
                {
                    var raw = MiniJson.GetString(json, "payload");
                    Dictionary<string, object> parsed = null;
                    if (!string.IsNullOrEmpty(raw))
                    {
                        try { parsed = MiniJson.AsDict(MiniJson.Deserialize(raw)); }
                        catch (Exception e) { Debug.LogWarning("[Nakama] RPC payload was not JSON: " + e.Message); }
                    }
                    onSuccess?.Invoke(parsed ?? new Dictionary<string, object> { { "raw", raw } });
                },
                onError);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Wallet is a JSON string inside the account payload: {"coins":120,"gems":5}</summary>
        public static Dictionary<string, object> ParseWallet(Dictionary<string, object> accountJson)
        {
            if (accountJson == null || !accountJson.ContainsKey("wallet")) return new Dictionary<string, object>();
            var wallet = accountJson["wallet"] as string;
            if (string.IsNullOrEmpty(wallet)) return new Dictionary<string, object>();
            return MiniJson.AsDict(MiniJson.Deserialize(wallet)) ?? new Dictionary<string, object>();
        }

        private static string UnityWebRequestSafe(string segment)
        {
            return string.IsNullOrEmpty(segment) ? string.Empty : Uri.EscapeDataString(segment);
        }
    }
}
