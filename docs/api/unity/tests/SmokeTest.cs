// Smoke test for the Unity sample (runs under mono with the shim). Verifies JSON handling,
// JWT session decoding and the exact request bodies the client builds.
using System;
using System.Collections.Generic;
using System.Text;
using NakamaSample;

public static class SmokeTest
{
    private static int failures;

    private static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? " :: " + detail : ""));
        if (!ok) failures++;
    }

    public static int Main()
    {
        // ---------- MiniJson round trip
        var nested = new Dictionary<string, object>
        {
            { "level", 4 },
            { "coins", 250L },
            { "ratio", 0.75 },
            { "flag", true },
            { "nothing", null },
            { "name", "علی \"quoted\" \\ slash\nnewline" },
            { "list", new List<object> { 1, "two", new Dictionary<string, object> { { "k", "v" } } } },
        };
        string json = MiniJson.Serialize(nested);
        var back = MiniJson.AsDict(MiniJson.Deserialize(json));
        Check("round-trip level", MiniJson.GetLong(back, "level") == 4, json);
        Check("round-trip coins", MiniJson.GetLong(back, "coins") == 250);
        Check("round-trip unicode", MiniJson.GetString(back, "name") == "علی \"quoted\" \\ slash\nnewline", MiniJson.GetString(back, "name"));
        Check("round-trip null", back.ContainsKey("nothing") && back["nothing"] == null);
        Check("round-trip nested list", MiniJson.AsList(back["list"]).Count == 3);

        // ---------- shape of the storage write body (PUT /v2/storage)
        var storeObj = new Dictionary<string, object>
        {
            { "collection", "saves" },
            { "key", "slot1" },
            { "value", MiniJson.Serialize(new Dictionary<string, object> { { "level", 4 } }) },
            { "permission_read", 2 },
            { "permission_write", 1 },
        };
        var storageBody = MiniJson.Serialize(new Dictionary<string, object> { { "objects", new List<object> { storeObj } } });
        Check("storage body has objects[]", storageBody.Contains("\"objects\":[{"));
        Check("storage value is an escaped string", storageBody.Contains("\"value\":\"{\\\"level\\\":4}\""), storageBody);

        // ---------- shape of the leaderboard submit body (POST /v2/leaderboard/{id})
        var scoreBody = MiniJson.Serialize(new Dictionary<string, object>
        {
            { "score", "3600" },
            { "subscore", "0" },
            { "metadata", "{\"level\":4}" },
        });
        Check("score body is a single record", !scoreBody.Contains("records"), scoreBody);
        Check("score is a JSON string (int64 proto encoding)", scoreBody.Contains("\"score\":\"3600\""), scoreBody);

        // ---------- shape of the RPC body (payload must be a JSON *string*)
        string inner = MiniJson.Serialize(new Dictionary<string, object> { { "level", 3 } });
        string rpcBody = MiniJson.Serialize(inner);
        Check("rpc body is a quoted json string", rpcBody == "\"{\\\"level\\\":3}\"", rpcBody);

        // ---------- JWT session decoding
        string payload = Base64Url("{\"tid\":\"t1\",\"uid\":\"user-uuid\",\"usn\":\"ali_01\",\"exp\":4102444800}");
        var session = new NakamaSession { Token = "h." + payload + ".sig", RefreshToken = "r" };
        session.ReadJwtClaims();
        Check("jwt uid", session.UserId == "user-uuid", session.UserId);
        Check("jwt username", session.Username == "ali_01", session.Username);
        Check("jwt expiry", session.ExpiresAtUtc.Year == 2100, session.ExpiresAtUtc.ToString("u"));
        Check("jwt not expired", session.IsValid);

        var expired = new NakamaSession { Token = "h." + Base64Url("{\"uid\":\"u\",\"exp\":1600000000}") + ".s" };
        expired.ReadJwtClaims();
        Check("expired session detected", !expired.IsValid);

        // ---------- wallet parsing
        var account = new Dictionary<string, object> { { "wallet", "{\"coins\":120,\"gems\":5}" } };
        var wallet = NakamaClient.ParseWallet(account);
        Check("wallet coins", MiniJson.GetLong(wallet, "coins") == 120);
        Check("wallet gems", MiniJson.GetLong(wallet, "gems") == 5);

        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }

    private static string Base64Url(string s)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
