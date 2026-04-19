using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ClarionDctAddin
{
    // Alternative renderings of an already-parsed JSON document.
    // Each method takes the JsonParser root and returns the text to show
    // in the preview textbox. Tree view has its own path in the dialog.
    internal static class JsonViewTransforms
    {
        // ----------------------------------------------------------------
        // YAML — YAML 1.2-ish, good enough for viewing. Strings are
        // single-quoted when they could otherwise be misread; numbers,
        // bools, null pass through.
        // ----------------------------------------------------------------
        public static string ToYaml(JsonParser.JsonNode root)
        {
            if (root == null) return "";
            var sb = new StringBuilder();
            YamlNode(sb, root, 0, false);
            return sb.ToString();
        }

        static void YamlNode(StringBuilder sb, JsonParser.JsonNode node, int indent, bool inlinePrefix)
        {
            var obj = node as JsonParser.JsonObject;
            if (obj != null)
            {
                if (obj.Members.Count == 0) { sb.Append(inlinePrefix ? " {}\n" : "{}\n"); return; }
                if (inlinePrefix) sb.Append('\n');
                foreach (var kv in obj.Members)
                {
                    Pad(sb, indent);
                    sb.Append(YamlKey(kv.Key)); sb.Append(':');
                    YamlScalarOrDescend(sb, kv.Value, indent + 1);
                }
                return;
            }
            var arr = node as JsonParser.JsonArray;
            if (arr != null)
            {
                if (arr.Items.Count == 0) { sb.Append(inlinePrefix ? " []\n" : "[]\n"); return; }
                if (inlinePrefix) sb.Append('\n');
                foreach (var item in arr.Items)
                {
                    Pad(sb, indent); sb.Append("- ");
                    if (item is JsonParser.JsonObject || item is JsonParser.JsonArray)
                    {
                        // Write first member/item on the same line with "- " prefix.
                        var sub = new StringBuilder();
                        YamlNode(sub, item, indent + 1, false);
                        var firstLine = true;
                        foreach (var line in sub.ToString().Split('\n'))
                        {
                            if (firstLine)
                            {
                                // strip leading spaces on first line since "- " already indents
                                sb.Append(line.TrimStart(' '));
                                sb.Append('\n');
                                firstLine = false;
                            }
                            else if (line.Length > 0) { sb.Append(line); sb.Append('\n'); }
                        }
                    }
                    else
                    {
                        sb.Append(YamlScalar(item)); sb.Append('\n');
                    }
                }
                return;
            }
            // scalar at root
            sb.Append(YamlScalar(node)); sb.Append('\n');
        }

        static void YamlScalarOrDescend(StringBuilder sb, JsonParser.JsonNode value, int indent)
        {
            if (value is JsonParser.JsonObject || value is JsonParser.JsonArray)
            {
                YamlNode(sb, value, indent, true);
            }
            else
            {
                sb.Append(' '); sb.Append(YamlScalar(value)); sb.Append('\n');
            }
        }

        static string YamlKey(string k)
        {
            // Quote if empty, contains special chars, or looks numeric/bool/null
            if (string.IsNullOrEmpty(k)) return "''";
            if (NeedsYamlQuotes(k) || LooksLikeReservedScalar(k)) return "'" + k.Replace("'", "''") + "'";
            return k;
        }

        static string YamlScalar(JsonParser.JsonNode n)
        {
            var s = n as JsonParser.JsonString;
            if (s != null)
            {
                if (string.IsNullOrEmpty(s.Value)) return "''";
                if (NeedsYamlQuotes(s.Value) || LooksLikeReservedScalar(s.Value))
                    return "'" + s.Value.Replace("'", "''") + "'";
                return s.Value;
            }
            var num = n as JsonParser.JsonNumber;
            if (num != null) return num.Raw;
            var b = n as JsonParser.JsonBool;
            if (b != null) return b.Value ? "true" : "false";
            if (n is JsonParser.JsonNull) return "null";
            return "";
        }

        static bool NeedsYamlQuotes(string s)
        {
            if (s.IndexOfAny(new[] { ':', '#', '&', '*', '!', '|', '>', '%', '\'', '"', '\n', '\r', '\t' }) >= 0) return true;
            if (s.StartsWith(" ") || s.EndsWith(" ")) return true;
            if (s.StartsWith("-") || s.StartsWith("?") || s.StartsWith("[") || s.StartsWith("{") || s.StartsWith("@")) return true;
            return false;
        }

        static bool LooksLikeReservedScalar(string s)
        {
            if (s == "true" || s == "false" || s == "null" || s == "~" || s == "yes" || s == "no") return true;
            double d;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return true;
            return false;
        }

        static void Pad(StringBuilder sb, int indent)
        {
            for (int i = 0; i < indent; i++) sb.Append("  ");
        }

        // ----------------------------------------------------------------
        // Path list — flatten to one line per leaf. Compact + greppable.
        //   tables[0].name = "CLIENTES"
        //   tables[0].fields[3].type = "LONG"
        // ----------------------------------------------------------------
        public static string ToPathList(JsonParser.JsonNode root)
        {
            var sb = new StringBuilder();
            PathEmit(sb, root, "$");
            return sb.ToString();
        }

        static void PathEmit(StringBuilder sb, JsonParser.JsonNode node, string path)
        {
            var obj = node as JsonParser.JsonObject;
            if (obj != null)
            {
                if (obj.Members.Count == 0) { sb.Append(path); sb.Append(" = {}\n"); return; }
                foreach (var kv in obj.Members)
                    PathEmit(sb, kv.Value, path + "." + EscapeSegment(kv.Key));
                return;
            }
            var arr = node as JsonParser.JsonArray;
            if (arr != null)
            {
                if (arr.Items.Count == 0) { sb.Append(path); sb.Append(" = []\n"); return; }
                for (int i = 0; i < arr.Items.Count; i++)
                    PathEmit(sb, arr.Items[i], path + "[" + i + "]");
                return;
            }
            sb.Append(path);
            sb.Append(" = ");
            sb.Append(node.Summary);
            sb.Append('\n');
        }

        static string EscapeSegment(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            bool needsQuotes = false;
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (!(char.IsLetterOrDigit(c) || c == '_')) { needsQuotes = true; break; }
            }
            if (!needsQuotes) return s;
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        // ----------------------------------------------------------------
        // Schema view — strip values, keep structure + types. Good for
        // seeing what a document *shape* looks like without the noise.
        // ----------------------------------------------------------------
        public static string ToSchema(JsonParser.JsonNode root)
        {
            var sb = new StringBuilder();
            SchemaEmit(sb, root, 0);
            return sb.ToString();
        }

        static void SchemaEmit(StringBuilder sb, JsonParser.JsonNode node, int indent)
        {
            var obj = node as JsonParser.JsonObject;
            if (obj != null)
            {
                if (obj.Members.Count == 0) { sb.Append("{}\n"); return; }
                sb.Append("{\n");
                foreach (var kv in obj.Members)
                {
                    Pad(sb, indent + 1);
                    sb.Append(kv.Key); sb.Append(" : ");
                    SchemaEmit(sb, kv.Value, indent + 1);
                }
                Pad(sb, indent); sb.Append("}\n");
                return;
            }
            var arr = node as JsonParser.JsonArray;
            if (arr != null)
            {
                if (arr.Items.Count == 0) { sb.Append("[]\n"); return; }
                // Show shape of the first item as representative; note if items differ.
                sb.Append("[ ");
                sb.Append(arr.Items.Count.ToString(CultureInfo.InvariantCulture));
                sb.Append(" × ");
                // Sample all items to see if their shape is uniform.
                var shape = ShapeLabel(arr.Items[0]);
                bool uniform = true;
                for (int i = 1; i < arr.Items.Count; i++)
                    if (ShapeLabel(arr.Items[i]) != shape) { uniform = false; break; }

                if (!uniform) { sb.Append("mixed ]\n"); return; }
                if (arr.Items[0] is JsonParser.JsonObject || arr.Items[0] is JsonParser.JsonArray)
                {
                    sb.Append("] ");
                    SchemaEmit(sb, arr.Items[0], indent);
                }
                else
                {
                    sb.Append(shape);
                    sb.Append(" ]\n");
                }
                return;
            }
            sb.Append(ShapeLabel(node));
            sb.Append('\n');
        }

        static string ShapeLabel(JsonParser.JsonNode n)
        {
            if (n is JsonParser.JsonString) return "string";
            if (n is JsonParser.JsonNumber) return "number";
            if (n is JsonParser.JsonBool)   return "bool";
            if (n is JsonParser.JsonNull)   return "null";
            if (n is JsonParser.JsonObject) return "object";
            if (n is JsonParser.JsonArray)  return "array";
            return "?";
        }

        // ----------------------------------------------------------------
        // Table view — for arrays-of-objects, flatten to a Markdown/CSV-ish
        // table so you can scan them at a glance. Picks the largest array
        // in the document (by item count) and uses the union of its member
        // keys as columns.
        // ----------------------------------------------------------------
        public static string ToTable(JsonParser.JsonNode root)
        {
            var arr = FindBestArray(root, null, "$");
            if (arr == null) return "No array-of-objects was found in this document.";

            var items = arr.Array.Items.OfType<JsonParser.JsonObject>().ToList();
            if (items.Count == 0) return "The best candidate array at " + arr.Path
                + " contains no objects to tabulate.";

            // Union of keys, preserving first-seen order.
            var columns = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var obj in items)
                foreach (var kv in obj.Members)
                    if (seen.Add(kv.Key)) columns.Add(kv.Key);

            // Measure column widths.
            var widths = new int[columns.Count];
            for (int c = 0; c < columns.Count; c++) widths[c] = columns[c].Length;
            var rows = new string[items.Count][];
            for (int r = 0; r < items.Count; r++)
            {
                rows[r] = new string[columns.Count];
                for (int c = 0; c < columns.Count; c++)
                {
                    var v = ValueFor(items[r], columns[c]);
                    rows[r][c] = v;
                    if (v.Length > widths[c]) widths[c] = v.Length;
                }
            }
            // Cap very wide columns.
            for (int c = 0; c < widths.Length; c++) if (widths[c] > 60) widths[c] = 60;

            var sb = new StringBuilder();
            sb.AppendLine("# Tabulated from " + arr.Path + "   (" + items.Count + " rows × " + columns.Count + " columns)");
            sb.AppendLine();
            // Header
            for (int c = 0; c < columns.Count; c++)
            {
                sb.Append(PadRight(columns[c], widths[c])); sb.Append("  ");
            }
            sb.AppendLine();
            for (int c = 0; c < columns.Count; c++)
            {
                sb.Append(new string('-', widths[c])); sb.Append("  ");
            }
            sb.AppendLine();
            for (int r = 0; r < rows.Length; r++)
            {
                for (int c = 0; c < columns.Count; c++)
                {
                    sb.Append(PadRight(Clip(rows[r][c], widths[c]), widths[c])); sb.Append("  ");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        sealed class ArrayCandidate
        {
            public JsonParser.JsonArray Array;
            public string Path;
            public int ObjectCount;
        }

        static ArrayCandidate FindBestArray(JsonParser.JsonNode node, ArrayCandidate best, string path)
        {
            var arr = node as JsonParser.JsonArray;
            if (arr != null)
            {
                int objCount = arr.Items.Count(x => x is JsonParser.JsonObject);
                if (objCount > 0 && (best == null || objCount > best.ObjectCount))
                    best = new ArrayCandidate { Array = arr, Path = path, ObjectCount = objCount };
                for (int i = 0; i < arr.Items.Count; i++)
                    best = FindBestArray(arr.Items[i], best, path + "[" + i + "]");
                return best;
            }
            var obj = node as JsonParser.JsonObject;
            if (obj != null)
            {
                foreach (var kv in obj.Members)
                    best = FindBestArray(kv.Value, best, path + "." + kv.Key);
            }
            return best;
        }

        static string ValueFor(JsonParser.JsonObject obj, string key)
        {
            for (int i = 0; i < obj.Members.Count; i++)
                if (obj.Members[i].Key == key) return ScalarText(obj.Members[i].Value);
            return "";
        }

        static string ScalarText(JsonParser.JsonNode n)
        {
            var s = n as JsonParser.JsonString;
            if (s != null) return s.Value ?? "";
            var num = n as JsonParser.JsonNumber;
            if (num != null) return num.Raw;
            var b = n as JsonParser.JsonBool;
            if (b != null) return b.Value ? "true" : "false";
            if (n is JsonParser.JsonNull) return "";
            if (n is JsonParser.JsonArray) return "[" + ((JsonParser.JsonArray)n).Items.Count + "]";
            if (n is JsonParser.JsonObject) return "{" + ((JsonParser.JsonObject)n).Members.Count + "}";
            return "";
        }

        static string PadRight(string s, int width)
        {
            if (s == null) s = "";
            return s.Length >= width ? s : s + new string(' ', width - s.Length);
        }

        static string Clip(string s, int width)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            return s.Length <= width ? s : s.Substring(0, Math.Max(0, width - 1)) + "…";
        }
    }
}
