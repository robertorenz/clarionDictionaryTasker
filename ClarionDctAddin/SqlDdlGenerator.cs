using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ClarionDctAddin
{
    // Generates CREATE TABLE + CREATE INDEX statements for every table in an
    // open Clarion dictionary. Supports three dialects: SQL Server, PostgreSQL,
    // and SQLite. Strictly read-only; no dictionary mutation.
    internal static class SqlDdlGenerator
    {
        public enum Dialect { SqlServer, Postgres, SQLite, MySql, MariaDb }

        public sealed class Options
        {
            public Dialect Dialect          = Dialect.SqlServer;
            public bool    IncludeDropTable = true;
            public bool    IncludeIndexes   = true;
            public bool    IncludeComments  = true;
            public bool    UseFullPathName  = true;
        }

        public static string Generate(object dict, Options opt)
        {
            var sb = new StringBuilder();
            var dictName = DictModel.GetDictionaryName(dict);

            sb.AppendLine("-- ================================================================");
            sb.AppendLine("-- SQL DDL generated from Clarion dictionary '" + dictName + "'");
            sb.AppendLine("-- Generated:   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("-- Dialect:     " + opt.Dialect);
            sb.AppendLine("-- ================================================================");
            sb.AppendLine();

            var tables = DictModel.GetTables(dict)
                .OrderBy(t => DictModel.AsString(DictModel.GetProp(t, "Name")), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var t in tables)
            {
                GenerateTable(sb, t, opt);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static void GenerateTable(StringBuilder sb, object table, Options opt)
        {
            var label   = DictModel.AsString(DictModel.GetProp(table, "Label")) ?? "?";
            var clName  = DictModel.AsString(DictModel.GetProp(table, "Name"))  ?? label;
            var fullPath = DictModel.AsString(DictModel.GetProp(table, "FullPathName")) ?? "";
            var sqlName = (opt.UseFullPathName && !string.IsNullOrEmpty(fullPath)) ? fullPath : label;

            if (opt.IncludeComments)
            {
                sb.AppendLine("-- ----------------------------------------------------------------");
                sb.AppendLine("-- " + label + (string.IsNullOrEmpty(fullPath) ? "" : "   (" + fullPath + ")"));
                var desc = DictModel.AsString(DictModel.GetProp(table, "Description")) ?? "";
                if (!string.IsNullOrEmpty(desc)) sb.AppendLine("-- " + desc.Replace("\r", " ").Replace("\n", " "));
                sb.AppendLine("-- ----------------------------------------------------------------");
            }

            if (opt.IncludeDropTable)
                sb.AppendLine(DropStatement(sqlName, opt.Dialect));

            sb.AppendLine("CREATE TABLE " + QuoteIdent(sqlName, opt.Dialect) + " (");

            var fields = DictModel.GetProp(table, "Fields") as IEnumerable;
            var lines = new List<string>();
            var pkCols = GetKeyColumnLabels(GetPrimaryKeyObject(table));

            if (fields != null)
            {
                foreach (var f in fields)
                {
                    if (f == null) continue;
                    var flabel = DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "?";
                    var line   = "  " + PadRight(QuoteIdent(flabel, opt.Dialect), 32) + " "
                               + PadRight(MapType(f, opt.Dialect), 18);

                    bool isPk = pkCols.Any(c => string.Equals(c, flabel, StringComparison.OrdinalIgnoreCase));
                    if (pkCols.Count == 1 && isPk) line += " NOT NULL";

                    if (opt.IncludeComments)
                    {
                        var desc = DictModel.AsString(DictModel.GetProp(f, "Description")) ?? "";
                        if (!string.IsNullOrEmpty(desc))
                            line += "  -- " + desc.Replace("\r", " ").Replace("\n", " ");
                    }
                    lines.Add(line);
                }
            }

            if (pkCols.Count > 0)
            {
                var quoted = pkCols.Select(c => QuoteIdent(c, opt.Dialect));
                lines.Add("  CONSTRAINT " + QuoteIdent("PK_" + label, opt.Dialect)
                    + " PRIMARY KEY (" + string.Join(", ", quoted.ToArray()) + ")");
            }

            sb.AppendLine(string.Join(",\r\n", lines.ToArray()));
            sb.AppendLine(");");

            if (opt.IncludeIndexes)
            {
                var keys = DictModel.GetProp(table, "Keys") as IEnumerable;
                if (keys != null)
                {
                    foreach (var k in keys)
                    {
                        if (k == null) continue;
                        var isPrimary = string.Equals(
                            DictModel.AsString(DictModel.GetProp(k, "AttributePrimary")),
                            "True", StringComparison.OrdinalIgnoreCase);
                        if (isPrimary) continue;

                        var kLabel = DictModel.AsString(DictModel.GetProp(k, "Label")) ?? "?";
                        var isUnique = string.Equals(
                            DictModel.AsString(DictModel.GetProp(k, "AttributeUnique")),
                            "True", StringComparison.OrdinalIgnoreCase);

                        var cols = GetKeyColumnLabels(k);
                        if (cols.Count == 0) continue;

                        var idxName = label + "_" + kLabel;
                        var stmt = (isUnique ? "CREATE UNIQUE INDEX " : "CREATE INDEX ")
                                 + QuoteIdent(idxName, opt.Dialect)
                                 + " ON " + QuoteIdent(sqlName, opt.Dialect)
                                 + " (" + string.Join(", ", cols.Select(c => QuoteIdent(c, opt.Dialect)).ToArray()) + ");";
                        sb.AppendLine(stmt);
                    }
                }
            }
        }

        // ---------------- type mapping ----------------
        static string MapType(object field, Dialect dialect)
        {
            var t        = DictModel.AsString(DictModel.GetProp(field, "DataType")) ?? "";
            var size     = ParseULong(DictModel.AsString(DictModel.GetProp(field, "FieldSize")));
            var chars    = ParseULong(DictModel.AsString(DictModel.GetProp(field, "Characters")));
            var places   = ParseInt(DictModel.AsString(DictModel.GetProp(field, "Places")));
            var picture  = DictModel.AsString(DictModel.GetProp(field, "ScreenPicture")) ?? "";

            string upperType = t.ToUpperInvariant();

            // LONG with a date picture == Clarion date
            if ((upperType == "LONG" || upperType == "ULONG") &&
                !string.IsNullOrEmpty(picture) && picture.StartsWith("@D", StringComparison.OrdinalIgnoreCase))
                return "DATE";

            bool isMySqlFamily = dialect == Dialect.MySql || dialect == Dialect.MariaDb;

            switch (upperType)
            {
                case "STRING":
                    return "VARCHAR(" + Clamp(chars > 0 ? chars : size, 1, 8000) + ")";
                case "CSTRING":
                    return "VARCHAR(" + Clamp(size > 0 ? size - 1 : chars, 1, 8000) + ")";
                case "PSTRING":
                    return "VARCHAR(" + Clamp(size > 0 ? size - 1 : chars, 1, 8000) + ")";
                case "BYTE":
                    return (dialect == Dialect.SqlServer || isMySqlFamily) ? "TINYINT" : "SMALLINT";
                case "SHORT":
                    return "SMALLINT";
                case "USHORT":
                    return "INT";
                case "LONG":
                    return dialect == Dialect.SQLite ? "INTEGER" : "INT";
                case "ULONG":
                    return "BIGINT";
                case "REAL":
                    if (dialect == Dialect.Postgres)   return "DOUBLE PRECISION";
                    if (isMySqlFamily)                 return "DOUBLE";
                    return "FLOAT";
                case "SREAL":
                    return isMySqlFamily ? "FLOAT" : "REAL";
                case "BFLOAT4":
                    return isMySqlFamily ? "FLOAT" : "REAL";
                case "BFLOAT8":
                    if (dialect == Dialect.Postgres)   return "DOUBLE PRECISION";
                    if (isMySqlFamily)                 return "DOUBLE";
                    return "FLOAT";
                case "DECIMAL":
                case "PDECIMAL":
                {
                    int precision = (int)(chars > 0 ? chars : 10);
                    int scale     = Math.Max(0, places);
                    if (scale > precision) scale = precision;
                    return "DECIMAL(" + precision + "," + scale + ")";
                }
                case "DATE":
                    return "DATE";
                case "TIME":
                    return "TIME";
                case "MEMO":
                    switch (dialect)
                    {
                        case Dialect.SqlServer: return "NVARCHAR(MAX)";
                        case Dialect.Postgres:  return "TEXT";
                        case Dialect.MySql:
                        case Dialect.MariaDb:   return "LONGTEXT";
                        default:                return "TEXT";
                    }
                case "BLOB":
                    switch (dialect)
                    {
                        case Dialect.SqlServer: return "VARBINARY(MAX)";
                        case Dialect.Postgres:  return "BYTEA";
                        case Dialect.MySql:
                        case Dialect.MariaDb:   return "LONGBLOB";
                        default:                return "BLOB";
                    }
                case "GROUP":
                    return "/* GROUP - manual */ " + (dialect == Dialect.SqlServer ? "NVARCHAR(100)" : "VARCHAR(100)");
            }
            return "/* " + t + " */ VARCHAR(50)";
        }

        // ---------------- helpers ----------------
        static string DropStatement(string tableName, Dialect dialect)
        {
            switch (dialect)
            {
                case Dialect.SqlServer:
                    return "IF OBJECT_ID(N'" + tableName.Replace("'", "''") + "', N'U') IS NOT NULL DROP TABLE "
                         + QuoteIdent(tableName, dialect) + ";";
                case Dialect.Postgres:
                case Dialect.SQLite:
                case Dialect.MySql:
                case Dialect.MariaDb:
                    return "DROP TABLE IF EXISTS " + QuoteIdent(tableName, dialect) + ";";
            }
            return "";
        }

        static string QuoteIdent(string name, Dialect dialect)
        {
            if (string.IsNullOrEmpty(name)) return name;

            // Split "schema.table" — quote parts separately so we get [dbo].[BITACORA]
            // rather than [dbo.BITACORA] on SQL Server.
            var dotIdx = name.IndexOf('.');
            if (dotIdx > 0 && dotIdx < name.Length - 1)
                return QuoteSingle(name.Substring(0, dotIdx), dialect) + "."
                     + QuoteSingle(name.Substring(dotIdx + 1), dialect);
            return QuoteSingle(name, dialect);
        }

        static string QuoteSingle(string name, Dialect dialect)
        {
            switch (dialect)
            {
                case Dialect.SqlServer: return "[" + name + "]";
                case Dialect.Postgres:
                case Dialect.SQLite:    return "\"" + name + "\"";
                case Dialect.MySql:
                case Dialect.MariaDb:   return "`" + name.Replace("`", "``") + "`";
            }
            return name;
        }

        static object GetPrimaryKeyObject(object table)
        {
            return DictModel.GetProp(table, "PrimaryKey")
                ?? DictModel.GetProp(table, "PrimaryOrUniqueKey");
        }

        static List<string> GetKeyColumnLabels(object key)
        {
            var result = new List<string>();
            if (key == null) return result;
            var comps = FindComponents(key);
            if (comps == null) return result;
            foreach (var c in comps)
            {
                if (c == null) continue;
                var fld = DictModel.GetProp(c, "Field") ?? DictModel.GetProp(c, "DDField");
                var lbl = fld != null
                    ? DictModel.AsString(DictModel.GetProp(fld, "Label"))
                    : DictModel.AsString(DictModel.GetProp(c, "Label"))
                      ?? DictModel.AsString(DictModel.GetProp(c, "Name"));
                if (!string.IsNullOrEmpty(lbl)) result.Add(lbl);
            }
            return result;
        }

        static IEnumerable FindComponents(object key)
        {
            string[] names = { "Components", "KeyComponents", "Fields", "KeyFields", "Segments" };
            foreach (var n in names)
            {
                var v = DictModel.GetProp(key, n) as IEnumerable;
                if (v != null && !(v is string)) return v;
            }
            return null;
        }

        static ulong ParseULong(string s)
        {
            ulong v; return ulong.TryParse(s, out v) ? v : 0UL;
        }
        static int ParseInt(string s)
        {
            int v; return int.TryParse(s, out v) ? v : 0;
        }
        static ulong Clamp(ulong v, ulong min, ulong max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
        static string PadRight(string s, int width)
        {
            return s == null ? new string(' ', width) : (s.Length >= width ? s : s + new string(' ', width - s.Length));
        }
    }
}
