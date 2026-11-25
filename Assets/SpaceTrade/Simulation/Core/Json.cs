using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SpaceTrade.Sim
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// Minimal JSON DOM so the simulation has no third-party or UnityEngine dependency.
    /// Object member order is preserved, which keeps serialized saves byte-stable.
    /// </summary>
    public sealed class JsonNode
    {
        public JsonKind Kind { get; private set; }
        private bool _bool;
        private string _raw;
        private List<JsonNode> _items;
        private List<KeyValuePair<string, JsonNode>> _members;

        private JsonNode(JsonKind kind) { Kind = kind; }

        public static JsonNode Null() { return new JsonNode(JsonKind.Null); }
        public static JsonNode Array() { return new JsonNode(JsonKind.Array) { _items = new List<JsonNode>() }; }
        public static JsonNode Object() { return new JsonNode(JsonKind.Object) { _members = new List<KeyValuePair<string, JsonNode>>() }; }
        public static JsonNode From(bool v) { return new JsonNode(JsonKind.Bool) { _bool = v }; }
        public static JsonNode From(string v) { return v == null ? Null() : new JsonNode(JsonKind.String) { _raw = v }; }
        public static JsonNode From(long v) { return new JsonNode(JsonKind.Number) { _raw = v.ToString(CultureInfo.InvariantCulture) }; }
        public static JsonNode From(int v) { return From((long)v); }
        public static JsonNode From(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("JSON cannot encode NaN/Infinity");
            return new JsonNode(JsonKind.Number) { _raw = v.ToString("R", CultureInfo.InvariantCulture) };
        }

        public JsonNode Add(JsonNode item) { _items.Add(item); return this; }
        public JsonNode Set(string key, JsonNode value) { _members.Add(new KeyValuePair<string, JsonNode>(key, value)); return this; }
        public JsonNode Set(string key, string v) { return Set(key, From(v)); }
        public JsonNode Set(string key, long v) { return Set(key, From(v)); }
        public JsonNode Set(string key, int v) { return Set(key, From(v)); }
        public JsonNode Set(string key, double v) { return Set(key, From(v)); }
        public JsonNode Set(string key, bool v) { return Set(key, From(v)); }

        public IReadOnlyList<JsonNode> Items { get { return _items ?? (IReadOnlyList<JsonNode>)new JsonNode[0]; } }

        public bool Has(string key) { return Find(key) != null; }

        public JsonNode this[string key]
        {
            get
            {
                var n = Find(key);
                if (n == null) throw new FormatException("Missing JSON member '" + key + "'");
                return n;
            }
        }

        private JsonNode Find(string key)
        {
            if (_members == null) return null;
            for (int i = 0; i < _members.Count; i++) if (_members[i].Key == key) return _members[i].Value;
            return null;
        }

        public string AsString() { return Kind == JsonKind.Null ? null : _raw; }
        public bool AsBool() { return _bool; }
        public long AsLong() { return long.Parse(_raw, NumberStyles.Integer, CultureInfo.InvariantCulture); }
        public int AsInt() { return int.Parse(_raw, NumberStyles.Integer, CultureInfo.InvariantCulture); }
        public double AsDouble() { return double.Parse(_raw, NumberStyles.Float, CultureInfo.InvariantCulture); }

        public string GetString(string key, string fallback) { var n = Find(key); return n == null ? fallback : n.AsString(); }
        public bool GetBool(string key, bool fallback) { var n = Find(key); return n == null ? fallback : n.AsBool(); }
        public long GetLong(string key, long fallback) { var n = Find(key); return n == null ? fallback : n.AsLong(); }
        public int GetInt(string key, int fallback) { var n = Find(key); return n == null ? fallback : n.AsInt(); }
        public double GetDouble(string key, double fallback) { var n = Find(key); return n == null ? fallback : n.AsDouble(); }

        public string ToJson(bool pretty = true)
        {
            var sb = new StringBuilder();
            Write(sb, pretty, 0);
            return sb.ToString();
        }

        private void Write(StringBuilder sb, bool pretty, int indent)
        {
            switch (Kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(_bool ? "true" : "false"); break;
                case JsonKind.Number: sb.Append(_raw); break;
                case JsonKind.String: WriteString(sb, _raw); break;
                case JsonKind.Array:
                    sb.Append('[');
                    for (int i = 0; i < _items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty) { sb.Append('\n').Append(' ', (indent + 1) * 2); }
                        _items[i].Write(sb, pretty, indent + 1);
                    }
                    if (pretty && _items.Count > 0) sb.Append('\n').Append(' ', indent * 2);
                    sb.Append(']');
                    break;
                case JsonKind.Object:
                    sb.Append('{');
                    for (int i = 0; i < _members.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty) { sb.Append('\n').Append(' ', (indent + 1) * 2); }
                        WriteString(sb, _members[i].Key);
                        sb.Append(pretty ? ": " : ":");
                        _members[i].Value.Write(sb, pretty, indent + 1);
                    }
                    if (pretty && _members.Count > 0) sb.Append('\n').Append(' ', indent * 2);
                    sb.Append('}');
                    break;
            }
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
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static JsonNode Parse(string text)
        {
            int pos = 0;
            var node = ParseValue(text, ref pos);
            SkipWs(text, ref pos);
            if (pos != text.Length) throw new FormatException("Trailing characters in JSON at " + pos);
            return node;
        }

        private static void SkipWs(string s, ref int p)
        {
            while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
        }

        private static JsonNode ParseValue(string s, ref int p)
        {
            SkipWs(s, ref p);
            if (p >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[p];
            if (c == '{')
            {
                p++;
                var obj = Object();
                SkipWs(s, ref p);
                if (p < s.Length && s[p] == '}') { p++; return obj; }
                while (true)
                {
                    SkipWs(s, ref p);
                    string key = ParseString(s, ref p);
                    SkipWs(s, ref p);
                    Expect(s, ref p, ':');
                    obj.Set(key, ParseValue(s, ref p));
                    SkipWs(s, ref p);
                    if (p < s.Length && s[p] == ',') { p++; continue; }
                    Expect(s, ref p, '}');
                    return obj;
                }
            }
            if (c == '[')
            {
                p++;
                var arr = Array();
                SkipWs(s, ref p);
                if (p < s.Length && s[p] == ']') { p++; return arr; }
                while (true)
                {
                    arr.Add(ParseValue(s, ref p));
                    SkipWs(s, ref p);
                    if (p < s.Length && s[p] == ',') { p++; continue; }
                    Expect(s, ref p, ']');
                    return arr;
                }
            }
            if (c == '"') return From(ParseString(s, ref p));
            if (Match(s, ref p, "true")) return From(true);
            if (Match(s, ref p, "false")) return From(false);
            if (Match(s, ref p, "null")) return Null();
            int start = p;
            while (p < s.Length && "+-0123456789.eE".IndexOf(s[p]) >= 0) p++;
            if (start == p) throw new FormatException("Unexpected character '" + c + "' at " + p);
            return new JsonNode(JsonKind.Number) { _raw = s.Substring(start, p - start) };
        }

        private static bool Match(string s, ref int p, string word)
        {
            if (string.CompareOrdinal(s, p, word, 0, word.Length) == 0) { p += word.Length; return true; }
            return false;
        }

        private static void Expect(string s, ref int p, char c)
        {
            if (p >= s.Length || s[p] != c) throw new FormatException("Expected '" + c + "' at " + p);
            p++;
        }

        private static string ParseString(string s, ref int p)
        {
            Expect(s, ref p, '"');
            var sb = new StringBuilder();
            while (p < s.Length)
            {
                char c = s[p++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[p++];
                switch (e)
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
                        sb.Append((char)int.Parse(s.Substring(p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        p += 4;
                        break;
                    default: throw new FormatException("Bad escape at " + p);
                }
            }
            throw new FormatException("Unterminated string");
        }
    }
}
