using System.Text;

namespace ClarionDctAddin
{
    // Small, dependency-free JSON re-formatter. Walks the input as a
    // state machine (tracking whether the cursor is inside a quoted string
    // and whether the previous char was an escape) so it correctly
    // preserves whitespace inside strings while rewriting whitespace between
    // structural tokens. Used by JsonPreviewDialog to toggle pretty / tabs /
    // minified views of an already-valid JSON document.
    internal static class JsonFormatter
    {
        public static string Pretty(string json, string indentUnit)
        {
            return Reformat(json, indentUnit ?? "  ", minified: false);
        }

        public static string Minified(string json)
        {
            return Reformat(json, "", minified: true);
        }

        static string Reformat(string json, string indentUnit, bool minified)
        {
            if (string.IsNullOrEmpty(json)) return json ?? "";
            var sb = new StringBuilder(json.Length);
            int depth = 0;
            bool inString = false, escape = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (inString)
                {
                    sb.Append(c);
                    if (escape) escape = false;
                    else if (c == '\\') escape = true;
                    else if (c == '"')  inString = false;
                    continue;
                }

                // Outside a string: drop every whitespace in the source;
                // we reinsert our own below based on the requested style.
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') continue;

                if (c == '"')
                {
                    sb.Append(c);
                    inString = true;
                    continue;
                }

                if (c == '{' || c == '[')
                {
                    sb.Append(c);
                    char? next = NextNonWs(json, i + 1);
                    // Collapse empty {} / [] so the output doesn't waste a line.
                    if (next.HasValue && (next.Value == '}' || next.Value == ']')) continue;
                    depth++;
                    if (!minified) { sb.Append('\n'); AppendIndent(sb, depth, indentUnit); }
                    continue;
                }

                if (c == '}' || c == ']')
                {
                    // If we collapsed the matching opener, no indent change needed.
                    // Detect by checking whether the previous emitted char was the opener.
                    if (sb.Length > 0 && (sb[sb.Length - 1] == '{' || sb[sb.Length - 1] == '['))
                    {
                        sb.Append(c);
                        continue;
                    }
                    depth--;
                    if (depth < 0) depth = 0;
                    if (!minified) { sb.Append('\n'); AppendIndent(sb, depth, indentUnit); }
                    sb.Append(c);
                    continue;
                }

                if (c == ',')
                {
                    sb.Append(c);
                    if (!minified) { sb.Append('\n'); AppendIndent(sb, depth, indentUnit); }
                    continue;
                }

                if (c == ':')
                {
                    sb.Append(c);
                    if (!minified) sb.Append(' ');
                    continue;
                }

                // numbers, true / false / null, anything else
                sb.Append(c);
            }
            if (!minified) sb.Append('\n');
            return sb.ToString();
        }

        static char? NextNonWs(string s, int from)
        {
            for (int i = from; i < s.Length; i++)
            {
                var c = s[i];
                if (c != ' ' && c != '\t' && c != '\r' && c != '\n') return c;
            }
            return null;
        }

        static void AppendIndent(StringBuilder sb, int depth, string indentUnit)
        {
            if (string.IsNullOrEmpty(indentUnit)) return;
            for (int i = 0; i < depth; i++) sb.Append(indentUnit);
        }
    }
}
