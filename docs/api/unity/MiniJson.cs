// Minimal JSON reader/writer for Unity — no external packages.
// Serializes Dictionary<string,object>, List<object>, string, bool, null, and any numeric type.
// Deserializes to Dictionary<string,object> / List<object> / string / bool / long / double / null.
//
// Why not JsonUtility: Nakama payloads are dictionaries (wallet, metadata, arbitrary RPC JSON),
// and JsonUtility cannot represent them.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NakamaSample
{
    public static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            return Parser.ParseValue(new Parser(json));
        }

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Serializer.Write(sb, value);
            return sb.ToString();
        }

        // ---- helpers used by the client ----
        public static Dictionary<string, object> AsDict(object o) => o as Dictionary<string, object>;

        public static List<object> AsList(object o) => o as List<object>;

        public static string GetString(Dictionary<string, object> d, string key, string fallback = null)
        {
            if (d != null && d.TryGetValue(key, out var v) && v != null) return v as string ?? v.ToString();
            return fallback;
        }

        public static long GetLong(Dictionary<string, object> d, string key, long fallback = 0)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return fallback;
            if (v is long l) return l;
            if (v is double db) return (long)db;
            if (v is string s && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)) return p;
            return fallback;
        }

        private sealed class Parser
        {
            private readonly string json;
            private int index;

            public Parser(string json) { this.json = json; }

            public static object ParseValue(Parser p)
            {
                p.SkipWhitespace();
                if (p.index >= p.json.Length) throw new FormatException("Unexpected end of JSON");
                char c = p.json[p.index];
                switch (c)
                {
                    case '{': return p.ParseObject();
                    case '[': return p.ParseArray();
                    case '"': return p.ParseString();
                    case 't': p.Expect("true"); return true;
                    case 'f': p.Expect("false"); return false;
                    case 'n': p.Expect("null"); return null;
                    default: return p.ParseNumber();
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var dict = new Dictionary<string, object>();
                index++; // {
                SkipWhitespace();
                if (index < json.Length && json[index] == '}') { index++; return dict; }
                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    if (json[index] != ':') throw new FormatException("Expected ':' at " + index);
                    index++;
                    dict[key] = ParseValue(this);
                    SkipWhitespace();
                    if (index >= json.Length) throw new FormatException("Unterminated object");
                    if (json[index] == ',') { index++; continue; }
                    if (json[index] == '}') { index++; return dict; }
                    throw new FormatException("Expected ',' or '}' at " + index);
                }
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                index++; // [
                SkipWhitespace();
                if (index < json.Length && json[index] == ']') { index++; return list; }
                while (true)
                {
                    list.Add(ParseValue(this));
                    SkipWhitespace();
                    if (index >= json.Length) throw new FormatException("Unterminated array");
                    if (json[index] == ',') { index++; continue; }
                    if (json[index] == ']') { index++; return list; }
                    throw new FormatException("Expected ',' or ']' at " + index);
                }
            }

            private string ParseString()
            {
                if (json[index] != '"') throw new FormatException("Expected string at " + index);
                index++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (index >= json.Length) throw new FormatException("Unterminated string");
                    char c = json[index++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    char esc = json[index++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            sb.Append((char)Convert.ToInt32(json.Substring(index, 4), 16));
                            index += 4;
                            break;
                        default: throw new FormatException("Bad escape \\" + esc);
                    }
                }
            }

            private object ParseNumber()
            {
                int start = index;
                while (index < json.Length && "+-0123456789.eE".IndexOf(json[index]) >= 0) index++;
                string raw = json.Substring(start, index - start);
                if (raw.Length == 0) throw new FormatException("Bad number at " + start);
                if (raw.IndexOf('.') < 0 && raw.IndexOf('e') < 0 && raw.IndexOf('E') < 0)
                {
                    if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
                }
                return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            private void Expect(string token)
            {
                if (index + token.Length > json.Length || json.Substring(index, token.Length) != token)
                    throw new FormatException("Expected '" + token + "' at " + index);
                index += token.Length;
            }

            public void SkipWhitespace()
            {
                while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
            }
        }

        private static class Serializer
        {
            // Written with explicit `as` casts (no pattern-matching switch) so the file compiles on
            // every Unity C# version, including the oldest compilers still in the wild.
            public static void Write(StringBuilder sb, object value)
            {
                if (value == null) { sb.Append("null"); return; }

                var s = value as string;
                if (s != null) { WriteString(sb, s); return; }

                if (value is bool) { sb.Append(((bool)value) ? "true" : "false"); return; }

                if (value is char) { WriteString(sb, value.ToString()); return; }

                if (value is float) { sb.Append(((float)value).ToString("R", CultureInfo.InvariantCulture)); return; }
                if (value is double) { sb.Append(((double)value).ToString("R", CultureInfo.InvariantCulture)); return; }
                if (value is decimal) { sb.Append(((decimal)value).ToString(CultureInfo.InvariantCulture)); return; }

                if (value is sbyte || value is byte || value is short || value is ushort ||
                    value is int || value is uint || value is long || value is ulong)
                {
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                }

                var dict = value as IDictionary<string, object>;
                if (dict != null)
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, kv.Key);
                        sb.Append(':');
                        Write(sb, kv.Value);
                    }
                    sb.Append('}');
                    return;
                }

                var plainDict = value as IDictionary;
                if (plainDict != null)
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (DictionaryEntry e in plainDict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, e.Key.ToString());
                        sb.Append(':');
                        Write(sb, e.Value);
                    }
                    sb.Append('}');
                    return;
                }

                var list = value as IEnumerable<object>;
                if (list != null)
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var item in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    return;
                }

                var raw = value as IEnumerable;
                if (raw != null)
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var item in raw)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    return;
                }

                WriteString(sb, value.ToString());
            }

            private static void WriteString(StringBuilder sb, string s)
            {
                sb.Append('"');
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
                sb.Append('"');
            }
        }
    }
}
