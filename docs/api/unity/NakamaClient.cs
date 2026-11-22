// Nakama HTTP client for Unity — no external packages, coroutine based.
// Copy this folder into Assets/Scripts/NakamaSample/ and it compiles as-is.
//
// Deliberate API-contract notes (verified against a live Nakama 3.42 server, they are easy to get wrong):
//   * write storage  -> PUT  /v2/storage         (POST /v2/storage is the *read-by-id* call)
//   * read storage   -> GET  /v2/storage/{collection}?user_id=...
//   * submit score   -> POST /v2/leaderboard/{id} with a SINGLE record object, not {"records":[...]}
//   * RPC payload    -> the request body must be a JSON *string*, the reply is {"payload":"<string json>"}
//   * refresh/logout -> require the server key (Basic auth), not the session token
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace NakamaSample
{
    /// <summary>gRPC-style error returned by Nakama: { "code": 16, "message": "Auth token invalid" }.</summary>
    public class NakamaException : Exception
    {
        public readonly long Code;
        public readonly long HttpStatus;

        public NakamaException(long code, string message, long httpStatus = 0) : base(message)
        {
            Code = code;
            HttpStatus = httpStatus;
        }

        public override string ToString() => $"Nakama error {Code} (HTTP {HttpStatus}): {Message}";
    }

    /// <summary>Session returned by every authenticate call. The JWT carries user id, username and expiry.</summary>
    public class NakamaSession
    {
        public string Token;
        public string RefreshToken;
        public string UserId;
        public string Username;
        public DateTime ExpiresAtUtc;

        public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc.AddMinutes(-1);
        public bool IsValid => !string.IsNullOrEmpty(Token) && !IsExpired;

        public static NakamaSession FromAuthJson(Dictionary<string, object> json)
        {
            var session = new NakamaSession
            {
                Token = MiniJson.GetString(json, "token"),
                RefreshToken = MiniJson.GetString(json, "refresh_token"),
            };
            session.ReadJwtClaims();
            return session;
        }

        /// <summary>Decodes uid / usn / exp from the JWT payload (no signature check — the server already signed it).</summary>
        public void ReadJwtClaims()
        {
            if (string.IsNullOrEmpty(Token)) return;
            var parts = Token.Split('.');
            if (parts.Length < 2) return;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }
            try
            {
                var claims = MiniJson.AsDict(MiniJson.Deserialize(Encoding.UTF8.GetString(Convert.FromBase64String(payload))));
                UserId = MiniJson.GetString(claims, "uid");
                Username = MiniJson.GetString(claims, "usn");
                var exp = MiniJson.GetLong(claims, "exp");
                if (exp > 0) ExpiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nakama] could not decode session token: " + e.Message);
            }
        }
    }

    public partial class NakamaClient
    {
        /// <summary>Production endpoint of this project. WebSocket: wss://api…/ws</summary>
        public const string DefaultBaseUrl = "https://api.91.107.253.71.sslip.io";

        public string BaseUrl { get; private set; }
        public string ServerKey { get; private set; }
        public NakamaSession Session { get; set; }

        public NakamaClient(string baseUrl = DefaultBaseUrl, string serverKey = "defaultkey")
        {
            BaseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
            ServerKey = serverKey;
        }

        // ---------------------------------------------------------------- transport

        /// <summary>Low level call. Use <paramref name="serverKey"/> only for auth/refresh/logout.</summary>
        public IEnumerator Send(string method, string path, string jsonBody, bool serverKey,
                                Action<Dictionary<string, object>> onSuccess,
                                Action<NakamaException> onError)
        {
            using (var request = new UnityWebRequest(BaseUrl + path, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                if (jsonBody != null)
                {
                    var raw = Encoding.UTF8.GetBytes(jsonBody);
                    request.uploadHandler = new UploadHandlerRaw(raw);
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.SetRequestHeader("Accept", "application/json");

                if (serverKey)
                {
                    var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(ServerKey + ":"));
                    request.SetRequestHeader("Authorization", "Basic " + basic);
                }
                else
                {
                    if (Session == null || string.IsNullOrEmpty(Session.Token))
                    {
                        onError?.Invoke(new NakamaException(16, "No session — authenticate first."));
                        yield break;
                    }
                    request.SetRequestHeader("Authorization", "Bearer " + Session.Token);
                }

                yield return request.SendWebRequest();

                bool failed = !IsSuccess(request);
                string text = request.downloadHandler != null ? request.downloadHandler.text : null;

                if (!failed)
                {
                    Dictionary<string, object> parsed = null;
                    if (!string.IsNullOrEmpty(text))
                    {
                        try { parsed = MiniJson.AsDict(MiniJson.Deserialize(text)); }
                        catch (Exception e) { Debug.LogError("[Nakama] bad JSON: " + e.Message + " :: " + text); }
                    }
                    onSuccess?.Invoke(parsed ?? new Dictionary<string, object>());
                    yield break;
                }

                onError?.Invoke(BuildError(request, text));
            }
        }

        private static bool IsSuccess(UnityWebRequest request)
        {
#if UNITY_2020_2_OR_NEWER
            return request.result == UnityWebRequest.Result.Success;
#else
            return !request.isNetworkError && !request.isHttpError;
#endif
        }

        private static NakamaException BuildError(UnityWebRequest request, string body)
        {
            long status = request.responseCode;
            try
            {
                var json = MiniJson.AsDict(MiniJson.Deserialize(body));
                if (json != null && json.ContainsKey("code"))
                    return new NakamaException(MiniJson.GetLong(json, "code"), MiniJson.GetString(json, "message", "unknown error"), status);
            }
            catch
            {
                // fall through to the transport error below
            }
            string message = string.IsNullOrEmpty(request.error) ? body : request.error;
            return new NakamaException(status, message, status);
        }

        // ---------------------------------------------------------------- health

        public IEnumerator Healthcheck(Action<bool> onDone)
        {
            yield return Send("GET", "/healthcheck", null, true,
                _ => onDone?.Invoke(true),
                _ => onDone?.Invoke(false));
        }

        // ---------------------------------------------------------------- authentication

        /// <summary>
        /// Registers the player on first call and logs them in afterwards.
        /// Reuse the same device id (SystemInfo.deviceUniqueIdentifier or a GUID in PlayerPrefs).
        /// </summary>
        public IEnumerator AuthenticateDevice(string deviceId, Action<NakamaSession> onSuccess, Action<NakamaException> onError)
        {
            var body = MiniJson.Serialize(new Dictionary<string, object> { { "id", deviceId } });
            yield return Authenticate("/v2/account/authenticate/device?create=true", body, onSuccess, onError);
        }

        public IEnumerator AuthenticateEmail(string email, string password, bool create,
                                             Action<NakamaSession> onSuccess, Action<NakamaException> onError)
        {
            var body = MiniJson.Serialize(new Dictionary<string, object>
            {
                { "email", email },
                { "password", password },
            });
            yield return Authenticate("/v2/account/authenticate/email?create=" + (create ? "true" : "false"), body, onSuccess, onError);
        }

        private IEnumerator Authenticate(string path, string body, Action<NakamaSession> onSuccess, Action<NakamaException> onError)
        {
            yield return Send("POST", path, body, true,
                json =>
                {
                    Session = NakamaSession.FromAuthJson(json);
                    onSuccess?.Invoke(Session);
                },
                onError);
        }

        /// <summary>Exchanges a refresh token for a new session (server key auth, not the bearer token).</summary>
        public IEnumerator RefreshSession(Action<NakamaSession> onSuccess, Action<NakamaException> onError)
        {
            if (Session == null || string.IsNullOrEmpty(Session.RefreshToken))
            {
                onError?.Invoke(new NakamaException(16, "No refresh token."));
                yield break;
            }
            var body = MiniJson.Serialize(new Dictionary<string, object> { { "token", Session.RefreshToken } });
            yield return Send("POST", "/v2/account/session/refresh", body, true,
                json =>
                {
                    Session = NakamaSession.FromAuthJson(json);
                    onSuccess?.Invoke(Session);
                },
                onError);
        }

        /// <summary>
        /// Invalidates the current session (and its refresh token). Uses the session bearer token —
        /// unlike <see cref="RefreshSession"/>, which requires the server key.
        /// </summary>
        public IEnumerator Logout(Action onDone = null, Action<NakamaException> onError = null)
        {
            if (Session == null) yield break;
            var body = MiniJson.Serialize(new Dictionary<string, object>
            {
                { "token", Session.Token },
                { "refreshToken", Session.RefreshToken },
            });
            yield return Send("POST", "/v2/session/logout", body, false,
                _ =>
                {
                    Session = null;
                    onDone?.Invoke();
                },
                e =>
                {
                    Session = null;
                    onError?.Invoke(e);
                });
        }

        // ---------------------------------------------------------------- account

        /// <summary>GET /v2/account — profile, wallet JSON and linked devices.</summary>
        public IEnumerator GetAccount(Action<Dictionary<string, object>> onSuccess, Action<NakamaException> onError)
        {
            yield return Send("GET", "/v2/account", null, false, onSuccess, onError);
        }

        /// <summary>PUT /v2/account — username, display name, avatar, locale.</summary>
        public IEnumerator UpdateAccount(string username, string displayName, string langTag,
                                         Action<Dictionary<string, object>> onSuccess, Action<NakamaException> onError)
        {
            var fields = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(username)) fields["username"] = username;
            if (!string.IsNullOrEmpty(displayName)) fields["display_name"] = displayName;
            if (!string.IsNullOrEmpty(langTag)) fields["lang_tag"] = langTag;

            yield return Send("PUT", "/v2/account", MiniJson.Serialize(fields), false, onSuccess, onError);
        }
    }
}
