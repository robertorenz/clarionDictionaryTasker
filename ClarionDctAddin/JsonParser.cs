using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ClarionDctAddin
{
    // Compact, dependency-free JSON parser. Produces a tree of JsonNode
    // subclasses that the preview dialog renders as a TreeView. Good enough
    // for the well-formed output of JsonExporter; throws ArgumentException
    // on malformed input (caller shows the message in the tree root).
    internal static class JsonParser
    {
        public abstract class JsonNode
        {
            public abstract string Summary { get; } // what to show at the right of the key
        }

        public sealed class JsonObject : JsonNode
        {
            public readonly List<KeyValuePair<string, JsonNode>> Members = new List<KeyValuePair<string, JsonNode>>();
            public override string Summary { get { return "{ " + Members.Count + (Members.Count == 1 ? " member" : " members") + " }"; } }
        }

        public sealed class JsonArray : JsonNode
        {
            public readonly List<JsonNode> Items = new List<JsonNode>();
            public override string Summary { get { return "[ " + Items.Count + (Items.Count == 1 ? " item" : " items") + " ]"; } }
        }

        public sealed class JsonString : JsonNode
        {
            public string Value;
            public override string Summary { get { return "\"" + Escape(Value) + "\""; } }
        }

        public sealed class JsonNumber : JsonNode
        {
            public string Raw;  // keep original text so "1.0" doesn't become "1"
            public override string Summary { get { return Raw; } }
        }

        public sealed class JsonBool : JsonNode
        {
            public bool Value;
            public override string Summary { get { return Value ? "true" : "false"; } }
        }

        public sealed class JsonNull : JsonNode
        {
            public override string Summary { get { return "null"; } }
        }

        public static JsonNode Parse(string json)
        {
            var p = new Cursor { S = json ?? "", I = 0 };
            p.SkipWs();
            var node = p.ReadValue();
            p.SkipWs();
            if (p.I != p.S.Length) throw new ArgumentException("Trailing garbage at position " + p.I);
            return node;
        }

        sealed class Cursor
        {
            public string S;
            public int I;

            public void SkipWs()
            {
                while (I < S.Length)
                {
                    char c = S[I];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') I++;
                    else break;
                }
            }

            public JsonNode ReadValue()
            {
                SkipWs();
                if (I >= S.Length) throw new ArgumentException("Unexpected end of input");
                char c = S[I];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return new JsonString { Value = ReadQuoted() };
                if (c == 't' || c == 'f') return ReadBool();
                if (c == 'n') return ReadNull();
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                throw new ArgumentException("Unexpected character '" + c + "' at position " + I);
            }

            JsonObject ReadObject()
            {
                var o = new JsonObject();
                I++; // {
                SkipWs();
                if (I < S.Length && S[I] == '}') { I++; return o; }
                while (true)
                {
                    SkipWs();
                    if (I >= S.Length || S[I] != '"') throw new ArgumentException("Expected object key at position " + I);
                    var key = ReadQuoted();
                    SkipWs();
                    if (I >= S.Length || S[I] != ':') throw new ArgumentException("Expected ':' at position " + I);
                    I++;
                    SkipWs();
                    var value = ReadValue();
                    o.Members.Add(new KeyValuePair<string, JsonNode>(key, value));
                    SkipWs();
                    if (I >= S.Length) throw new ArgumentException("Unexpected end inside object");
                    char c = S[I];
                    if (c == ',') { I++; continue; }
                    if (c == '}') { I++; return o; }
                    throw new ArgumentException("Expected ',' or '}' at position " + I);
                }
            }

            JsonArray ReadArray()
            {
                var a = new JsonArray();
                I++; // [
                SkipWs();
                if (I < S.Length && S[I] == ']') { I++; return a; }
                while (true)
                {
                    SkipWs();
                    a.Items.Add(ReadValue());
                    SkipWs();
                    if (I >= S.Length) throw new ArgumentException("Unexpected end inside array");
                    char c = S[I];
                    if (c == ',') { I++; continue; }
                    if (c == ']') { I++; return a; }
                    throw new ArgumentException("Expected ',' or ']' at position " + I);
                }
            }

            string ReadQuoted()
            {
                // Assumes S[I] == '"'. Consumes the closing quote.
                I++; // opening "
                var sb = new StringBuilder();
                while (I < S.Length)
                {
                    char c = S[I++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (I >= S.Length) throw new ArgumentException("Truncated escape at end of input");
                    char e = S[I++];
                    switch (e)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/');  break;
                        case 'b':  sb.Append('\b'); break;
                        case 'f':  sb.Append('\f'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'u':
                            if (I + 4 > S.Length) throw new ArgumentException("Truncated \\u escape");
                            var hex = S.Substring(I, 4); I += 4;
                            int code;
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                throw new ArgumentException("Invalid \\u escape '" + hex + "'");
                            sb.Append((char)code);
                            break;
                        default: throw new ArgumentException("Invalid escape '\\" + e + "' at position " + (I - 1));
                    }
                }
                throw new ArgumentException("Unterminated string");
            }

            JsonBool ReadBool()
            {
                if (S.Length - I >= 4 && S.Substring(I, 4) == "true")  { I += 4; return new JsonBool { Value = true }; }
                if (S.Length - I >= 5 && S.Substring(I, 5) == "false") { I += 5; return new JsonBool { Value = false }; }
                throw new ArgumentException("Expected 'true' or 'false' at position " + I);
            }

            JsonNull ReadNull()
            {
                if (S.Length - I >= 4 && S.Substring(I, 4) == "null") { I += 4; return new JsonNull(); }
                throw new ArgumentException("Expected 'null' at position " + I);
            }

            JsonNumber ReadNumber()
            {
                int start = I;
                if (S[I] == '-') I++;
                while (I < S.Length && (S[I] >= '0' && S[I] <= '9')) I++;
                if (I < S.Length && S[I] == '.') { I++; while (I < S.Length && (S[I] >= '0' && S[I] <= '9')) I++; }
                if (I < S.Length && (S[I] == 'e' || S[I] == 'E'))
                {
                    I++;
                    if (I < S.Length && (S[I] == '+' || S[I] == '-')) I++;
                    while (I < S.Length && (S[I] >= '0' && S[I] <= '9')) I++;
                }
                return new JsonNumber { Raw = S.Substring(start, I - start) };
            }
        }

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 4);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:   sb.Append(c);      break;
                }
            }
            return sb.ToString();
        }
    }
}
