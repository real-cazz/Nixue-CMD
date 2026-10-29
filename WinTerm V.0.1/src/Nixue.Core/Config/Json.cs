using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Nixue.Core.Config
{
    /// <summary>
    /// A small, dependency-free JSON reader/writer. The object model is:
    /// <c>Dictionary&lt;string,object&gt;</c>, <c>List&lt;object&gt;</c>, <c>string</c>,
    /// <c>double</c>, <c>bool</c> and <c>null</c>. It exists so the app can persist
    /// settings without pulling in a serializer, and so parsing can be unit-tested
    /// deterministically.
    /// </summary>
    public static class Json
    {
        // ---- Writing --------------------------------------------------------

        public static string Serialize(object value, bool pretty)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, value, pretty, 0);
            return sb.ToString();
        }

        private static void Indent(StringBuilder sb, bool pretty, int depth)
        {
            if (!pretty) return;
            sb.Append('\n');
            for (int i = 0; i < depth; i++) sb.Append("  ");
        }

        private static void WriteValue(StringBuilder sb, object value, bool pretty, int depth)
        {
            if (value == null) { sb.Append("null"); return; }

            Dictionary<string, object> obj = value as Dictionary<string, object>;
            if (obj != null) { WriteObject(sb, obj, pretty, depth); return; }

            List<object> arr = value as List<object>;
            if (arr != null) { WriteArray(sb, arr, pretty, depth); return; }

            if (value is string) { WriteString(sb, (string)value); return; }
            if (value is bool) { sb.Append(((bool)value) ? "true" : "false"); return; }

            if (value is double || value is float || value is int || value is long)
            {
                double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (d == Math.Floor(d) && !double.IsInfinity(d))
                    sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                else
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            // Fallback: stringify.
            WriteString(sb, value.ToString());
        }

        private static void WriteObject(StringBuilder sb, Dictionary<string, object> obj, bool pretty, int depth)
        {
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> kv in obj)
            {
                if (!first) sb.Append(',');
                first = false;
                Indent(sb, pretty, depth + 1);
                WriteString(sb, kv.Key);
                sb.Append(pretty ? ": " : ":");
                WriteValue(sb, kv.Value, pretty, depth + 1);
            }
            if (!first) Indent(sb, pretty, depth);
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, List<object> arr, bool pretty, int depth)
        {
            sb.Append('[');
            bool first = true;
            foreach (object item in arr)
            {
                if (!first) sb.Append(',');
                first = false;
                Indent(sb, pretty, depth + 1);
                WriteValue(sb, item, pretty, depth + 1);
            }
            if (!first) Indent(sb, pretty, depth);
            sb.Append(']');
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
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- Parsing --------------------------------------------------------

        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            Parser p = new Parser(text);
            object result = p.ParseValue();
            p.SkipWhitespace();
            if (!p.AtEnd) throw new FormatException("Trailing content after JSON value at index " + p.Index);
            return result;
        }

        public static bool TryParse(string text, out object result)
        {
            try { result = Parse(text); return true; }
            catch { result = null; return false; }
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s) { _s = s; _i = 0; }
            public int Index { get { return _i; } }
            public bool AtEnd { get { return _i >= _s.Length; } }

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _i++;
                    else break;
                }
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (_i >= _s.Length) throw new FormatException("Unexpected end of JSON");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': case 'f': return ParseBool();
                    case 'n': Expect("null"); return null;
                    default: return ParseNumber();
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> obj = new Dictionary<string, object>();
                _i++; // {
                SkipWhitespace();
                if (Peek() == '}') { _i++; return obj; }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"') throw new FormatException("Expected string key at index " + _i);
                    string key = ParseString();
                    SkipWhitespace();
                    if (Peek() != ':') throw new FormatException("Expected ':' at index " + _i);
                    _i++;
                    object val = ParseValue();
                    obj[key] = val;
                    SkipWhitespace();
                    char n = Peek();
                    if (n == ',') { _i++; continue; }
                    if (n == '}') { _i++; break; }
                    throw new FormatException("Expected ',' or '}' at index " + _i);
                }
                return obj;
            }

            private List<object> ParseArray()
            {
                List<object> arr = new List<object>();
                _i++; // [
                SkipWhitespace();
                if (Peek() == ']') { _i++; return arr; }
                while (true)
                {
                    object val = ParseValue();
                    arr.Add(val);
                    SkipWhitespace();
                    char n = Peek();
                    if (n == ',') { _i++; continue; }
                    if (n == ']') { _i++; break; }
                    throw new FormatException("Expected ',' or ']' at index " + _i);
                }
                return arr;
            }

            private string ParseString()
            {
                StringBuilder sb = new StringBuilder();
                _i++; // opening quote
                while (_i < _s.Length)
                {
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (_i >= _s.Length) break;
                        char e = _s[_i++];
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
                                if (_i + 4 > _s.Length) throw new FormatException("Bad \\u escape");
                                string hex = _s.Substring(_i, 4);
                                _i += 4;
                                int code = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                                sb.Append((char)code);
                                break;
                            default: throw new FormatException("Invalid escape \\" + e);
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                throw new FormatException("Unterminated string");
            }

            private bool ParseBool()
            {
                if (Peek() == 't') { Expect("true"); return true; }
                Expect("false"); return false;
            }

            private object ParseNumber()
            {
                int start = _i;
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') _i++;
                    else break;
                }
                string num = _s.Substring(start, _i - start);
                double d;
                if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw new FormatException("Invalid number '" + num + "'");
                return d;
            }

            private char Peek() { return _i < _s.Length ? _s[_i] : '\0'; }

            private void Expect(string literal)
            {
                if (_i + literal.Length > _s.Length || _s.Substring(_i, literal.Length) != literal)
                    throw new FormatException("Expected '" + literal + "' at index " + _i);
                _i += literal.Length;
            }
        }
    }
}
