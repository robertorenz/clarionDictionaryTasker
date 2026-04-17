using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ClarionDctAddin
{
    // Hand-rolled JSON writer so the add-in stays dependency-free.
    // Strategy:
    //   - Tables get a hand-picked scalar schema plus auto-dumped collections (Fields/Keys/Relations/Triggers/Aliases).
    //   - Unknown model objects (DDField, DDKey, ...) are serialised by scanning public scalar properties,
    //     skipping explicit-interface re-implementations (names containing ".Design.") and anything that throws.
    internal static class JsonExporter
    {
        const int MaxDepth = 4;

        public static string TableJson(object table)
        {
            var sb = new StringBuilder();
            WriteTable(sb, table, 0);
            sb.AppendLine();
            return sb.ToString();
        }

        public static string TablesJson(string dictName, string dictFileName, IList<object> tables)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            WriteKey(sb, 1, "dictionary"); sb.Append(JsonStr(dictName)); sb.Append(",\n");
            WriteKey(sb, 1, "fileName");   sb.Append(JsonStr(dictFileName)); sb.Append(",\n");
            WriteKey(sb, 1, "generatedAt"); sb.Append(JsonStr(DateTime.Now.ToString("o", CultureInfo.InvariantCulture))); sb.Append(",\n");
            WriteKey(sb, 1, "tableCount");  sb.Append(tables.Count.ToString(CultureInfo.InvariantCulture)); sb.Append(",\n");
            WriteKey(sb, 1, "tables"); sb.Append("[\n");
            for (int i = 0; i < tables.Count; i++)
            {
                WriteTable(sb, tables[i], 2);
                if (i + 1 < tables.Count) sb.Append(",");
                sb.Append("\n");
            }
            Indent(sb, 1); sb.Append("]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        // --- table ---
        static void WriteTable(StringBuilder sb, object t, int indent)
        {
            Indent(sb, indent); sb.Append("{\n");
            var first = true;
            WriteScalar(sb, indent + 1, ref first, "name",            DictModel.AsString(DictModel.GetProp(t, "Name")));
            WriteScalar(sb, indent + 1, ref first, "label",           DictModel.AsString(DictModel.GetProp(t, "Label")));
            WriteScalar(sb, indent + 1, ref first, "prefix",          DictModel.AsString(DictModel.GetProp(t, "Prefix")));
            WriteScalar(sb, indent + 1, ref first, "fieldPrefix",     DictModel.AsString(DictModel.GetProp(t, "FieldPrefix")));
            WriteScalar(sb, indent + 1, ref first, "description",     DictModel.AsString(DictModel.GetProp(t, "Description")));
            WriteScalar(sb, indent + 1, ref first, "driver",          DictModel.AsString(DictModel.GetProp(t, "FileDriverName")));
            WriteScalar(sb, indent + 1, ref first, "driverOptions",   DictModel.AsString(DictModel.GetProp(t, "DriverOptions")));
            WriteScalar(sb, indent + 1, ref first, "fullPathName",    DictModel.AsString(DictModel.GetProp(t, "FullPathName")));
            WriteScalar(sb, indent + 1, ref first, "defaultFileName", DictModel.AsString(DictModel.GetProp(t, "DefaultFileName")));
            WriteScalar(sb, indent + 1, ref first, "ownerName",       DictModel.AsString(DictModel.GetProp(t, "OwnerName")));
            WriteScalar(sb, indent + 1, ref first, "fileStatement",   DictModel.AsString(DictModel.GetProp(t, "FileStatement")));
            WriteScalar(sb, indent + 1, ref first, "id",              DictModel.AsString(DictModel.GetProp(t, "Id")));
            WriteScalar(sb, indent + 1, ref first, "createdDate",     DictModel.AsString(DictModel.GetProp(t, "CreatedDate")));
            WriteScalar(sb, indent + 1, ref first, "modifiedDate",    DictModel.AsString(DictModel.GetProp(t, "ModifiedDate")));
            WriteBool(sb,   indent + 1, ref first, "encrypt",         DictModel.GetProp(t, "Encrypt"));
            WriteBool(sb,   indent + 1, ref first, "create",          DictModel.GetProp(t, "Create"));
            WriteBool(sb,   indent + 1, ref first, "reclaim",         DictModel.GetProp(t, "Reclaim"));
            WriteBool(sb,   indent + 1, ref first, "oem",             DictModel.GetProp(t, "OEM"));
            WriteBool(sb,   indent + 1, ref first, "threaded",        DictModel.GetProp(t, "Threaded"));
            WriteBool(sb,   indent + 1, ref first, "isAlias",         DictModel.GetProp(t, "IsAlias"));
            WriteBool(sb,   indent + 1, ref first, "hasTriggers",     DictModel.GetProp(t, "HasTriggers"));
            WriteBool(sb,   indent + 1, ref first, "hasPrimaryKey",   DictModel.GetProp(t, "HasPrimaryKey"));
            WriteFields    (sb, indent + 1, ref first,              DictModel.GetProp(t, "Fields"));
            WriteCollection(sb, indent + 1, ref first, "keys",      DictModel.GetProp(t, "Keys"));
            WriteCollection(sb, indent + 1, ref first, "relations", DictModel.GetProp(t, "Relations"));
            WriteCollection(sb, indent + 1, ref first, "triggers",  DictModel.GetProp(t, "Triggers"));
            WriteCollection(sb, indent + 1, ref first, "aliases",   DictModel.GetProp(t, "Aliases"));
            sb.Append("\n");
            Indent(sb, indent); sb.Append("}");
        }

        // --- fields: hand-picked schema based on DDField shape ---
        static void WriteFields(StringBuilder sb, int indent, ref bool outerFirst, object value)
        {
            var en = value as IEnumerable;
            if (en == null) return;
            Comma(sb, ref outerFirst);
            WriteKey(sb, indent, "fields");
            sb.Append("[");
            bool any = false;
            foreach (var field in en)
            {
                if (field == null) continue;
                sb.Append(any ? ",\n" : "\n");
                any = true;
                WriteField(sb, indent + 1, field);
            }
            if (any) { sb.Append("\n"); Indent(sb, indent); }
            sb.Append("]");
        }

        static void WriteField(StringBuilder sb, int indent, object f)
        {
            Indent(sb, indent); sb.Append("{\n");
            var first = true;
            WriteScalar(sb, indent + 1, ref first, "name",             DictModel.AsString(DictModel.GetProp(f, "Label")));
            WriteScalar(sb, indent + 1, ref first, "fullName",         DictModel.AsString(DictModel.GetProp(f, "Name")));
            WriteScalar(sb, indent + 1, ref first, "prefix",           DictModel.AsString(DictModel.GetProp(f, "ParentPrefix")));
            WriteScalar(sb, indent + 1, ref first, "dataType",         DictModel.AsString(DictModel.GetProp(f, "DataType")));
            WriteNumber(sb, indent + 1, ref first, "size",             DictModel.GetProp(f, "FieldSize"));
            WriteNumber(sb, indent + 1, ref first, "places",           DictModel.GetProp(f, "Places"));
            WriteNumber(sb, indent + 1, ref first, "dimensions",       DictModel.GetProp(f, "Dimensions"));
            WriteScalar(sb, indent + 1, ref first, "picture",          DictModel.AsString(DictModel.GetProp(f, "ScreenPicture")));
            WriteScalar(sb, indent + 1, ref first, "rowPicture",       DictModel.AsString(DictModel.GetProp(f, "RowPicture")));
            WriteScalar(sb, indent + 1, ref first, "heading",          DictModel.AsString(DictModel.GetProp(f, "ColumnHeading")));
            WriteScalar(sb, indent + 1, ref first, "prompt",           DictModel.AsString(DictModel.GetProp(f, "PromptText")));
            WriteScalar(sb, indent + 1, ref first, "initialValue",     DictModel.AsString(DictModel.GetProp(f, "InitialValue")));
            WriteScalar(sb, indent + 1, ref first, "description",      DictModel.AsString(DictModel.GetProp(f, "Description")));
            WriteScalar(sb, indent + 1, ref first, "message",          DictModel.AsString(DictModel.GetProp(f, "Message")));
            WriteScalar(sb, indent + 1, ref first, "tooltip",          DictModel.AsString(DictModel.GetProp(f, "ToolTip")));
            WriteScalar(sb, indent + 1, ref first, "externalName",     DictModel.AsString(DictModel.GetProp(f, "ExternalName")));
            WriteScalar(sb, indent + 1, ref first, "scope",            DictModel.AsString(DictModel.GetProp(f, "Scope")));
            WriteScalar(sb, indent + 1, ref first, "justification",    DictModel.AsString(DictModel.GetProp(f, "Justification")));
            WriteScalar(sb, indent + 1, ref first, "case",             DictModel.AsString(DictModel.GetProp(f, "CaseAttribute")));
            WriteScalar(sb, indent + 1, ref first, "location",         DictModel.AsString(DictModel.GetProp(f, "Location")));
            WriteScalar(sb, indent + 1, ref first, "id",               DictModel.AsString(DictModel.GetProp(f, "Id")));
            WriteScalar(sb, indent + 1, ref first, "createdDate",      DictModel.AsString(DictModel.GetProp(f, "CreatedDate")));
            WriteScalar(sb, indent + 1, ref first, "modifiedDate",     DictModel.AsString(DictModel.GetProp(f, "ModifiedDate")));
            WriteBool(sb,   indent + 1, ref first, "isAutoNumber",     DictModel.GetProp(f, "IsAutoNumber"));
            WriteBool(sb,   indent + 1, ref first, "isNumeric",        DictModel.GetProp(f, "IsNumeric"));
            WriteBool(sb,   indent + 1, ref first, "isString",         DictModel.GetProp(f, "IsString"));
            WriteBool(sb,   indent + 1, ref first, "isBlobOrMemo",     DictModel.GetProp(f, "IsBLOBorMEMO"));
            WriteBool(sb,   indent + 1, ref first, "isTimeStamp",      DictModel.GetProp(f, "IsTimeStamp"));
            WriteBool(sb,   indent + 1, ref first, "isInFile",         DictModel.GetProp(f, "IsInFile"));
            WriteBool(sb,   indent + 1, ref first, "isTriggerData",    DictModel.GetProp(f, "IsTriggerData"));
            WriteBool(sb,   indent + 1, ref first, "readOnly",         DictModel.GetProp(f, "FlagReadOnly"));
            WriteBool(sb,   indent + 1, ref first, "immediate",        DictModel.GetProp(f, "FlagImmediate"));
            WriteBool(sb,   indent + 1, ref first, "password",         DictModel.GetProp(f, "FlagPassword"));
            WriteBool(sb,   indent + 1, ref first, "binary",           DictModel.GetProp(f, "Binary"));
            WriteBool(sb,   indent + 1, ref first, "external",         DictModel.GetProp(f, "External"));
            WriteBool(sb,   indent + 1, ref first, "reference",        DictModel.GetProp(f, "Reference"));
            WriteBool(sb,   indent + 1, ref first, "threaded",         DictModel.GetProp(f, "Threaded"));
            WriteBool(sb,   indent + 1, ref first, "static",           DictModel.GetProp(f, "Static"));
            WriteBool(sb,   indent + 1, ref first, "over",             DictModel.GetProp(f, "OverField") != null);
            WriteScalar(sb, indent + 1, ref first, "overFieldName",    DictModel.AsString(DictModel.GetProp(DictModel.GetProp(f, "OverField"), "Name")));
            sb.Append("\n");
            Indent(sb, indent); sb.Append("}");
        }

        static void WriteNumber(StringBuilder sb, int indent, ref bool first, string key, object value)
        {
            if (value == null) return;
            var t = value.GetType();
            if (!(t.IsPrimitive || t == typeof(decimal)) || t == typeof(bool)) return;
            Comma(sb, ref first);
            WriteKey(sb, indent, key);
            if (value is IFormattable) sb.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
            else sb.Append(value);
        }

        // --- generic object/collection writers ---
        static void WriteCollection(StringBuilder sb, int indent, ref bool first, string key, object value)
        {
            var en = value as IEnumerable;
            if (en == null || value is string) return;
            Comma(sb, ref first);
            WriteKey(sb, indent, key);
            sb.Append("[");
            bool any = false;
            foreach (var item in en)
            {
                if (!any) sb.Append("\n"); else sb.Append(",\n");
                any = true;
                WriteUnknown(sb, indent + 1, item, 1);
            }
            if (any) { sb.Append("\n"); Indent(sb, indent); }
            sb.Append("]");
        }

        static void WriteUnknown(StringBuilder sb, int indent, object o, int depth)
        {
            if (o == null) { Indent(sb, indent); sb.Append("null"); return; }
            var t = o.GetType();
            if (IsScalar(t)) { Indent(sb, indent); sb.Append(ScalarLiteral(o)); return; }

            Indent(sb, indent); sb.Append("{\n");
            var first = true;
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                               .Where(pp => pp.CanRead && pp.GetIndexParameters().Length == 0)
                               .OrderBy(pp => pp.Name))
            {
                // Skip explicit interface re-implementations — noisy, near-duplicate data.
                if (p.Name.IndexOf('.') >= 0) continue;
                object v;
                try { v = p.GetValue(o, null); }
                catch { continue; }
                if (v == null) continue;

                var vt = v.GetType();
                if (IsScalar(vt))
                {
                    Comma(sb, ref first);
                    WriteKey(sb, indent + 1, CamelCase(p.Name));
                    sb.Append(ScalarLiteral(v));
                }
                else if (depth < MaxDepth && v is IEnumerable && !(v is string))
                {
                    // Only emit collections of scalars or one-more-level of objects, and only when non-empty.
                    var list = new List<object>();
                    foreach (var x in (IEnumerable)v) list.Add(x);
                    if (list.Count == 0) continue;

                    Comma(sb, ref first);
                    WriteKey(sb, indent + 1, CamelCase(p.Name));
                    sb.Append("[");
                    for (int i = 0; i < list.Count; i++)
                    {
                        sb.Append(i == 0 ? "\n" : ",\n");
                        WriteUnknown(sb, indent + 2, list[i], depth + 1);
                    }
                    sb.Append("\n"); Indent(sb, indent + 1); sb.Append("]");
                }
                // skip nested objects beyond scalar/IEnumerable to keep output focused
            }
            sb.Append("\n");
            Indent(sb, indent); sb.Append("}");
        }

        // --- scalar helpers ---
        static bool IsScalar(Type t)
        {
            if (t.IsEnum) return true;
            if (t.IsPrimitive) return true;
            if (t == typeof(string) || t == typeof(decimal) || t == typeof(Guid)
                || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan))
                return true;
            return false;
        }

        static string ScalarLiteral(object v)
        {
            if (v == null) return "null";
            if (v is bool) return ((bool)v) ? "true" : "false";
            if (v is string) return JsonStr((string)v);
            if (v is Guid) return JsonStr(v.ToString());
            if (v is DateTime) return JsonStr(((DateTime)v).ToString("o", CultureInfo.InvariantCulture));
            if (v is DateTimeOffset) return JsonStr(((DateTimeOffset)v).ToString("o", CultureInfo.InvariantCulture));
            if (v is TimeSpan) return JsonStr(v.ToString());
            if (v is Enum) return JsonStr(v.ToString());
            if (v is IFormattable) return ((IFormattable)v).ToString(null, CultureInfo.InvariantCulture);
            return JsonStr(v.ToString());
        }

        static void WriteScalar(StringBuilder sb, int indent, ref bool first, string key, string value)
        {
            if (value == null) value = "";
            Comma(sb, ref first);
            WriteKey(sb, indent, key);
            sb.Append(JsonStr(value));
        }

        static void WriteBool(StringBuilder sb, int indent, ref bool first, string key, object value)
        {
            if (value == null) return;
            if (!(value is bool)) return;
            Comma(sb, ref first);
            WriteKey(sb, indent, key);
            sb.Append((bool)value ? "true" : "false");
        }

        static void WriteKey(StringBuilder sb, int indent, string key)
        {
            Indent(sb, indent);
            sb.Append(JsonStr(key));
            sb.Append(": ");
        }

        static void Comma(StringBuilder sb, ref bool first)
        {
            if (first) { first = false; }
            else sb.Append(",\n");
        }

        static void Indent(StringBuilder sb, int indent)
        {
            for (int i = 0; i < indent; i++) sb.Append("  ");
        }

        static string JsonStr(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (ch < 0x20) sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)ch);
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        static string CamelCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }
    }
}
