// Drop this on an empty GameObject in a scene and press Play.
// Keys: 1 login · 2 set username · 3 save+load storage · 4 submit score + leaderboard
//       5 RPC (server-authoritative wallet grant) · 6 refresh session · 0 logout
// Everything is logged to the Console and to the on-screen overlay.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NakamaSample
{
    public class NakamaSample : MonoBehaviour
    {
        [Header("Endpoint")]
        [Tooltip("Nakama API base URL (no trailing slash)")]
        public string baseUrl = NakamaClient.DefaultBaseUrl;
        [Tooltip("Server key from the Nakama config — login endpoints only")]
        public string serverKey = "defaultkey";

        [Header("Demo content")]
        public string collection = "saves";
        public string storageKey = "slot1";
        public string leaderboardId = "global_score";

        private NakamaClient client;
        private readonly List<string> logLines = new List<string>();

        private void Awake()
        {
            client = new NakamaClient(baseUrl, serverKey);
            Application.logMessageReceived += (msg, stack, type) => AddLog(msg);
        }

        private void Start()
        {
            AddLog("Nakama sample ready — press 1 to log in.");
            StartCoroutine(client.Healthcheck(ok => AddLog("healthcheck: " + (ok ? "OK" : "FAILED"))));
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) StartCoroutine(Login());
            if (Input.GetKeyDown(KeyCode.Alpha2)) StartCoroutine(SetUsername());
            if (Input.GetKeyDown(KeyCode.Alpha3)) StartCoroutine(SaveAndLoad());
            if (Input.GetKeyDown(KeyCode.Alpha4)) StartCoroutine(SubmitScore());
            if (Input.GetKeyDown(KeyCode.Alpha5)) StartCoroutine(ClaimBonus());
            if (Input.GetKeyDown(KeyCode.Alpha6)) StartCoroutine(Refresh());
            if (Input.GetKeyDown(KeyCode.Alpha0)) StartCoroutine(Logout());
        }

        // ------------------------------------------------------------ flows

        private IEnumerator Login()
        {
            // Reuse one id per install so the player keeps the same account across sessions.
            const string prefsKey = "nakama.device_id";
            string deviceId = PlayerPrefs.GetString(prefsKey, null);
            if (string.IsNullOrEmpty(deviceId))
            {
                deviceId = SystemInfo.deviceUniqueIdentifier;
                if (string.IsNullOrEmpty(deviceId) || deviceId == SystemInfo.unsupportedIdentifier)
                    deviceId = Guid.NewGuid().ToString();
                PlayerPrefs.SetString(prefsKey, deviceId);
                PlayerPrefs.Save();
            }

            yield return client.AuthenticateDevice(deviceId,
                session => AddLog($"logged in: userId={session.UserId} username={session.Username} expires={session.ExpiresAtUtc:u}"),
                error => AddLog("login failed: " + error));
        }

        private IEnumerator SetUsername()
        {
            string username = "player_" + UnityEngine.Random.Range(1000, 9999);
            yield return client.UpdateAccount(username, "Unity Player", "en",
                _ => AddLog("username set to " + username),
                error => AddLog("update failed: " + error));
        }

        private IEnumerator SaveAndLoad()
        {
            var save = new Dictionary<string, object>
            {
                { "level", 4 },
                { "coins", 250 },
                { "unlocked", new List<object> { "sword", "shield" } },
            };

            yield return client.WriteStorage(collection, storageKey, save,
                onSuccess: acks => AddLog("storage written: " + MiniJson.Serialize(acks)),
                onError: error => AddLog("write failed: " + error));

            yield return client.ListStorage(collection,
                objects =>
                {
                    foreach (var o in objects)
                        AddLog($"loaded {MiniJson.GetString(o, "collection")}/{MiniJson.GetString(o, "key")} = {MiniJson.GetString(o, "value")}");
                },
                error => AddLog("read failed: " + error));
        }

        private IEnumerator SubmitScore()
        {
            long score = UnityEngine.Random.Range(100, 5000);
            yield return client.SubmitScore(leaderboardId, score, 0, "{\"level\":4}",
                record => AddLog("score accepted: " + record),
                error => AddLog("submit failed: " + error));

            yield return client.ReadLeaderboard(leaderboardId, 5,
                onSuccess: records =>
                {
                    AddLog($"top {records.Count} of {leaderboardId}:");
                    foreach (var r in records) AddLog("  " + r);
                },
                onError: error => AddLog("leaderboard read failed: " + error));
        }

        private IEnumerator ClaimBonus()
        {
            yield return client.Rpc("daily_bonus", new Dictionary<string, object> { { "reason", "unity-sample" } },
                result => AddLog("rpc daily_bonus -> " + MiniJson.Serialize(result)),
                error => AddLog("rpc failed: " + error));

            yield return client.GetAccount(
                account =>
                {
                    var wallet = NakamaClient.ParseWallet(account);
                    AddLog("wallet: " + MiniJson.Serialize(wallet));
                },
                error => AddLog("account read failed: " + error));
        }

        private IEnumerator Refresh()
        {
            yield return client.RefreshSession(
                session => AddLog($"session refreshed, valid until {session.ExpiresAtUtc:u}"),
                error => AddLog("refresh failed: " + error));
        }

        private IEnumerator Logout()
        {
            yield return client.Logout(() => AddLog("logged out (tokens revoked)"));
        }

        // ------------------------------------------------------------ overlay

        private void AddLog(string line)
        {
            Debug.Log("[NakamaSample] " + line);
            logLines.Add(line);
            if (logLines.Count > 14) logLines.RemoveAt(0);
        }

        private void OnGUI()
        {
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(8, 8, 640, 250), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = false };
            style.normal.textColor = new Color(0.85f, 0.95f, 0.9f);
            float y = 14;
            GUI.Label(new Rect(16, y, 620, 18), "1 login · 2 username · 3 storage · 4 score · 5 RPC · 6 refresh · 0 logout", style);
            y += 20;
            foreach (var line in logLines)
            {
                GUI.Label(new Rect(16, y, 620, 16), line, style);
                y += 16;
            }
        }
    }
}
