// Minimal UnityEngine shim so the sample can be compiled and smoke-tested with mono on a headless box.
// NOT shipped to Unity — it only exists to catch syntax/type errors in CI.
using System;
using System.Collections;

namespace UnityEngine
{
    public class Object { }
    public class Component : Object { }
    public class Coroutine { }

    public class MonoBehaviour : Component
    {
        public Coroutine StartCoroutine(IEnumerator routine) => new Coroutine();
    }

    public enum LogType { Log, Warning, Error }

    public static class Debug
    {
        public static void Log(object m) => Console.WriteLine("[log] " + m);
        public static void LogWarning(object m) => Console.WriteLine("[warn] " + m);
        public static void LogError(object m) => Console.WriteLine("[error] " + m);
    }

    public static class Application
    {
        public delegate void LogCallback(string condition, string stackTrace, LogType type);
        public static event LogCallback logMessageReceived;
        public static void Emit(string msg) => logMessageReceived?.Invoke(msg, "", LogType.Log);
    }

    public static class PlayerPrefs
    {
        private static readonly System.Collections.Generic.Dictionary<string, string> Store = new System.Collections.Generic.Dictionary<string, string>();
        public static string GetString(string key, string fallback) => Store.TryGetValue(key, out var v) ? v : fallback;
        public static void SetString(string key, string value) => Store[key] = value;
        public static void Save() { }
    }

    public static class SystemInfo
    {
        public static string deviceUniqueIdentifier = "shim-device";
        public static string unsupportedIdentifier = "";
    }

    public enum KeyCode { Alpha0, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6 }

    public static class Input
    {
        public static bool GetKeyDown(KeyCode k) => false;
    }

    public struct Color
    {
        public Color(float r, float g, float b, float a) { }
        public Color(float r, float g, float b) { }
        public static Color white => new Color(1, 1, 1, 1);
    }

    public static class Random
    {
        private static readonly System.Random Rng = new System.Random(1234);
        public static int Range(int minInclusive, int maxExclusive) => Rng.Next(minInclusive, maxExclusive);
        public static float Range(float min, float max) => (float)(min + Rng.NextDouble() * (max - min));
    }

    public struct Rect
    {
        public Rect(float x, float y, float w, float h) { }
    }

    public class Texture2D
    {
        public static Texture2D whiteTexture => new Texture2D();
    }

    public class GUIStyleState { public Color textColor; }

    public class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public int fontSize;
        public bool richText;
        public GUIStyleState normal = new GUIStyleState();
    }

    public class GUISkin { public GUIStyle label = new GUIStyle(); }

    public static class GUI
    {
        public static Color color;
        public static GUISkin skin = new GUISkin();
        public static void DrawTexture(Rect r, Texture2D t) { }
        public static void Label(Rect r, string text, GUIStyle style) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class HeaderAttribute : Attribute { public HeaderAttribute(string header) { } }

    [AttributeUsage(AttributeTargets.Field)]
    public class TooltipAttribute : Attribute { public TooltipAttribute(string tooltip) { } }
}

namespace UnityEngine.Networking
{
    public class DownloadHandler { public string text; }
    public class UploadHandler { }
    public class DownloadHandlerBuffer : DownloadHandler { }
    public class UploadHandlerRaw : UploadHandler { public UploadHandlerRaw(byte[] data) { } }
    public class UnityWebRequestAsyncOperation { }

    public class UnityWebRequest : IDisposable
    {
        public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }

        public DownloadHandler downloadHandler;
        public UploadHandler uploadHandler;
        public string error;
        public long responseCode;
        public bool isNetworkError;
        public bool isHttpError;
        public Result result = Result.Success;

        public UnityWebRequest(string url, string method) { }
        public void SetRequestHeader(string name, string value) { }
        public UnityWebRequestAsyncOperation SendWebRequest() => new UnityWebRequestAsyncOperation();
        public void Dispose() { }
    }
}
