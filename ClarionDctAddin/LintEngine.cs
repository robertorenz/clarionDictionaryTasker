using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ClarionDctAddin
{
    // Structural / style checks over an open dictionary. All checks are
    // strictly read-only — no mutation, no model side effects.
    internal static class LintEngine
    {
        public enum Severity { Info, Warning, Error }

        public sealed class Finding
        {
            public Severity Severity;
            public string   Target;   // "Table.Field" or "Table.Key" or "Table"
            public string   Rule;     // short rule name
            public string   Message;
        }

        public static List<Finding> RunFullScan(object dict)
        {
            var results = new List<Finding>();
            var tables = DictModel.GetTables(dict);
            foreach (var t in tables) RunTableChecks(t, results);
            RunDictionaryChecks(dict, tables, results);
            return results
                .OrderByDescending(f => (int)f.Severity)
                .ThenBy(f => f.Target, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<Finding> RunTableScan(object table)
        {
            var results = new List<Finding>();
            RunTableChecks(table, results);
            return results;
        }

        // Overload that also surfaces cross-table duplicate ExternalName findings
        // involving this table — you don't notice them from a single-table scan
        // unless the other offender is also in this table.
        public static List<Finding> RunTableScan(object dict, object table)
        {
            var results = new List<Finding>();
            RunTableChecks(table, results);
            if (dict != null)
            {
                var dictFindings = new List<Finding>();
                CheckDuplicateKeyExternalNames(DictModel.GetTables(dict), dictFindings);
                var tName = DictModel.AsString(DictModel.GetProp(table, "Name")) ?? "";
                var prefix = tName + ".";
                foreach (var finding in dictFindings)
                    if (finding.Target != null
                        && finding.Target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        results.Add(finding);
            }
            return results
                .OrderByDescending(x => (int)x.Severity)
                .ThenBy(x => x.Target, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static void RunTableChecks(object table, List<Finding> f)
        {
            var tName = DictModel.AsString(DictModel.GetProp(table, "Name")) ?? "<?>";
            var driver = DictModel.AsString(DictModel.GetProp(table, "FileDriverName")) ?? "";

            int fieldCount = DictModel.CountEnumerable(table, "Fields");
            int keyCount   = DictModel.CountEnumerable(table, "Keys");
            int relCount   = DictModel.CountEnumerable(table, "Relations");

            // Table's own external / physical name — FullPathName carries the
            // schema-qualified SQL identifier for SQL drivers.
            var tableExt = DictModel.AsString(DictModel.GetProp(table, "FullPathName")) ?? "";
            CheckExternalNameShape(f, tName, "table", tableExt, driver);

            if (fieldCount == 0)
                f.Add(new Finding { Severity = Severity.Error,   Target = tName, Rule = "empty-table",
                                    Message = "Table has zero fields." });

            if (keyCount == 0)
                f.Add(new Finding { Severity = Severity.Warning, Target = tName, Rule = "no-keys",
                                    Message = "Table has no keys — no index, no uniqueness constraint." });

            var hasPrimary = DictModel.AsString(DictModel.GetProp(table, "HasPrimaryKey"));
            if (string.Equals(hasPrimary, "False", StringComparison.OrdinalIgnoreCase))
                f.Add(new Finding { Severity = Severity.Warning, Target = tName, Rule = "no-primary-key",
                                    Message = "Table has no primary key." });

            if (relCount == 0 && fieldCount > 0)
                f.Add(new Finding { Severity = Severity.Info,    Target = tName, Rule = "no-relations",
                                    Message = "Table participates in no relations — consider deletion if truly unused." });

            // Key-level checks
            var keys = DictModel.GetProp(table, "Keys") as IEnumerable;
            if (keys != null)
            {
                var seenSigs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var k in keys)
                {
                    if (k == null) continue;
                    var kName = DictModel.AsString(DictModel.GetProp(k, "Name")) ?? "<?>";
                    var target = tName + "." + kName;

                    var comps = FindComponents(k);
                    int compCount = 0;
                    var sigBuilder = new List<string>();
                    if (comps != null)
                    {
                        foreach (var c in comps)
                        {
                            if (c == null) continue;
                            compCount++;
                            var fld = DictModel.GetProp(c, "Field") ?? DictModel.GetProp(c, "DDField");
                            var lbl = fld != null
                                ? DictModel.AsString(DictModel.GetProp(fld, "Label"))
                                : DictModel.AsString(DictModel.GetProp(c, "Label")) ?? DictModel.AsString(DictModel.GetProp(c, "Name"));
                            if (!string.IsNullOrEmpty(lbl)) sigBuilder.Add(lbl);
                        }
                    }

                    if (compCount == 0)
                        f.Add(new Finding { Severity = Severity.Error, Target = target, Rule = "empty-key",
                                            Message = "Key has zero components — will not index anything." });

                    var sig = string.Join(",", sigBuilder.ToArray());
                    if (!string.IsNullOrEmpty(sig))
                    {
                        string prior;
                        if (seenSigs.TryGetValue(sig, out prior))
                            f.Add(new Finding { Severity = Severity.Warning, Target = target, Rule = "duplicate-key",
                                                Message = "Key component set (" + sig + ") duplicates " + prior + "." });
                        else
                            seenSigs[sig] = kName;
                    }

                    var extName = DictModel.AsString(DictModel.GetProp(k, "ExternalName")) ?? "";
                    if (string.IsNullOrEmpty(extName))
                        f.Add(new Finding { Severity = Severity.Info, Target = target, Rule = "empty-external-name",
                                            Message = "Key has no ExternalName — SQL drivers usually require one." });
                    else
                        CheckExternalNameShape(f, target, "key", extName, driver);
                }
            }

            // Field-level checks
            var prefix = DictModel.AsString(DictModel.GetProp(table, "Prefix")) ?? "";
            var fields = DictModel.GetProp(table, "Fields") as IEnumerable;
            if (fields != null)
            {
                foreach (var fld in fields)
                {
                    if (fld == null) continue;
                    var label = DictModel.AsString(DictModel.GetProp(fld, "Label")) ?? "<?>";
                    var target = tName + "." + label;

                    var description = DictModel.AsString(DictModel.GetProp(fld, "Description")) ?? "";
                    if (string.IsNullOrWhiteSpace(description))
                        f.Add(new Finding { Severity = Severity.Info, Target = target, Rule = "no-description",
                                            Message = "Field has no description — add one for self-documenting dictionaries." });

                    var picture = DictModel.AsString(DictModel.GetProp(fld, "ScreenPicture")) ?? "";
                    var dataType = DictModel.AsString(DictModel.GetProp(fld, "DataType")) ?? "";
                    if (string.IsNullOrEmpty(picture) && !string.Equals(dataType, "MEMO", StringComparison.OrdinalIgnoreCase)
                                                     && !string.Equals(dataType, "BLOB", StringComparison.OrdinalIgnoreCase))
                        f.Add(new Finding { Severity = Severity.Info, Target = target, Rule = "no-picture",
                                            Message = "Field has no picture — displays will use the driver default." });

                    // Picture category sanity — catch pictures that don't match their data type.
                    CheckPictureShape(f, target, label, dataType, picture);

                    if (label.Contains(" "))
                        f.Add(new Finding { Severity = Severity.Warning, Target = target, Rule = "whitespace-in-label",
                                            Message = "Field label contains whitespace — most generators reject this." });

                    var fldExt = DictModel.AsString(DictModel.GetProp(fld, "ExternalName")) ?? "";
                    if (!string.IsNullOrEmpty(fldExt))
                        CheckExternalNameShape(f, target, "field", fldExt, driver);
                }
            }
        }

        // Driver-aware sanity check on an ExternalName. Catches the Clarion
        // prefix leak (BIT:guidkey landing in the index name as "BIT:GUIDKEY"),
        // whitespace, leading digit, unusual characters for a bare SQL identifier,
        // and length limits that differ across drivers. Mirrored by the live
        // checker in LintFixKeysDialog so the two grids agree.
        static void CheckExternalNameShape(List<Finding> f, string target, string kind, string ext, string driver)
        {
            if (string.IsNullOrWhiteSpace(ext)) return;

            // Clarion field-prefix leak — colons have no legal unquoted place in
            // any of the SQL dialects we generate for. MSSQL in particular refuses
            // to parse them even inside brackets.
            if (ext.IndexOf(':') >= 0)
                f.Add(new Finding
                {
                    Severity = Severity.Error, Target = target, Rule = "illegal-external-name",
                    Message = kind + " ExternalName '" + ext + "' contains ':' — will break every SQL driver (MSSQL / Postgres / MySQL / MariaDB)."
                });

            // Whitespace inside a bare identifier — would need quoting on every
            // statement; very few code generators do that reliably.
            bool hasWs = false;
            foreach (var c in ext) { if (char.IsWhiteSpace(c)) { hasWs = true; break; } }
            if (hasWs)
                f.Add(new Finding
                {
                    Severity = Severity.Error, Target = target, Rule = "illegal-external-name",
                    Message = kind + " ExternalName '" + ext + "' contains whitespace — won't work as a bare SQL identifier."
                });

            if (ext.Length > 0 && char.IsDigit(ext[0]))
                f.Add(new Finding
                {
                    Severity = Severity.Warning, Target = target, Rule = "illegal-external-name",
                    Message = kind + " ExternalName '" + ext + "' starts with a digit — most SQL parsers reject this without quoting."
                });

            if (!LooksLikeSafeSqlIdentifier(ext))
                f.Add(new Finding
                {
                    Severity = Severity.Warning, Target = target, Rule = "unusual-external-name-chars",
                    Message = kind + " ExternalName '" + ext + "' contains characters outside [A-Za-z0-9_$#] — may need quoting on every statement."
                });

            int limit = SqlIdentifierLengthLimit(driver);
            if (ext.Length > limit)
                f.Add(new Finding
                {
                    Severity = Severity.Warning, Target = target, Rule = "long-external-name",
                    Message = kind + " ExternalName is " + ext.Length + " chars, exceeds " + driver + "'s identifier length limit of " + limit + "."
                });
        }

        static bool LooksLikeSafeSqlIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                bool ok = char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '#' || c == '.';
                if (!ok) return false;
            }
            return true;
        }

        static int SqlIdentifierLengthLimit(string driver)
        {
            var d = (driver ?? "").ToUpperInvariant();
            if (d.Contains("POSTGRES")) return 63;
            if (d.Contains("MYSQL") || d.Contains("MARIADB")) return 64;
            if (d.Contains("MSSQL") || d.Contains("SQLSERVER")) return 128;
            if (d.Contains("SQLITE")) return 128;
            return 64; // safe default for non-SQL drivers (TopSpeed etc. don't care, but cap anyway)
        }

        // Verify that the picture string matches the category of the data type.
        // DATE needs @d*, TIME needs @t*, numeric needs @n*, and strings must
        // not carry a non-string picture. Mirrors the rules in
        // PictureConsistencyDialog so the unified lint covers both.
        static void CheckPictureShape(List<Finding> f, string target, string label, string dataType, string picture)
        {
            if (string.IsNullOrEmpty(picture)) return;
            var dt = (dataType ?? "").ToUpperInvariant();
            var p  = picture.ToLowerInvariant();

            if (dt == "DATE")
            {
                if (!p.StartsWith("@d"))
                    f.Add(new Finding { Severity = Severity.Error, Target = target, Rule = "picture-date-shape",
                                        Message = "DATE field has picture '" + picture + "'; expected @d*." });
                return;
            }
            if (dt == "TIME")
            {
                if (!p.StartsWith("@t"))
                    f.Add(new Finding { Severity = Severity.Error, Target = target, Rule = "picture-time-shape",
                                        Message = "TIME field has picture '" + picture + "'; expected @t*." });
                return;
            }
            if (dt == "DECIMAL" || dt == "PDECIMAL" || dt == "REAL" || dt == "SREAL")
            {
                if (!p.StartsWith("@n"))
                    f.Add(new Finding { Severity = Severity.Error, Target = target, Rule = "picture-numeric-shape",
                                        Message = "Numeric field has picture '" + picture + "'; expected @n*." });
                else if (LooksLikeMoney(label) && p.IndexOf('$') < 0)
                    f.Add(new Finding { Severity = Severity.Info, Target = target, Rule = "picture-money-no-currency",
                                        Message = "Money-looking label '" + label + "' has no currency marker; consider @n$*.*." });
                return;
            }
            if (dt == "LONG" || dt == "ULONG")
            {
                // LONG is Clarion's native underlying storage for DATE (days since
                // epoch) and TIME (centiseconds since midnight), so @d* and @t* are
                // perfectly legitimate pictures on a LONG/ULONG. Only flag when the
                // picture isn't numeric, date, or time.
                if (!p.StartsWith("@n") && !p.StartsWith("@d") && !p.StartsWith("@t"))
                    f.Add(new Finding { Severity = Severity.Warning, Target = target, Rule = "picture-int-shape",
                                        Message = "Integer field has picture '" + picture + "'; expected @n*, @d*, or @t*." });
                return;
            }
            if (dt == "BYTE" || dt == "SHORT" || dt == "USHORT")
            {
                if (!p.StartsWith("@n"))
                    f.Add(new Finding { Severity = Severity.Warning, Target = target, Rule = "picture-int-shape",
                                        Message = "Integer field has picture '" + picture + "'; expected @n*." });
                return;
            }
            if (dt == "STRING" || dt == "CSTRING" || dt == "PSTRING")
            {
                if (p.StartsWith("@d") || p.StartsWith("@t") || p.StartsWith("@n"))
                    f.Add(new Finding { Severity = Severity.Error, Target = target, Rule = "picture-string-shape",
                                        Message = "STRING field has a non-string picture '" + picture + "'." });
                return;
            }
        }

        static bool LooksLikeMoney(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;
            var l = label.ToLowerInvariant();
            string[] hints = { "amount", "amt", "price", "cost", "total", "balance",
                               "money", "salary", "fee", "charge", "payment",
                               "importe", "monto", "precio", "costo" };
            for (int i = 0; i < hints.Length; i++)
                if (l.IndexOf(hints[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        static void RunDictionaryChecks(object dict, IList<object> tables, List<Finding> f)
        {
            if (tables.Count == 0)
                f.Add(new Finding { Severity = Severity.Error, Target = "<dictionary>", Rule = "no-tables",
                                    Message = "Dictionary contains zero tables." });

            CheckDuplicateKeyExternalNames(tables, f);
        }

        // ExternalName collisions across keys will break SQL drivers — most
        // databases forbid two indexes with the same name inside one schema.
        // Emit one finding per occurrence so the user sees both sides of every
        // pair in the report.
        static void CheckDuplicateKeyExternalNames(IList<object> tables, List<Finding> f)
        {
            // External-name -> list of "TableName.KeyName" owners
            var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tables)
            {
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "<?>";
                var keys = DictModel.GetProp(t, "Keys") as IEnumerable;
                if (keys == null) continue;
                foreach (var k in keys)
                {
                    if (k == null) continue;
                    var ext = DictModel.AsString(DictModel.GetProp(k, "ExternalName")) ?? "";
                    if (string.IsNullOrWhiteSpace(ext)) continue; // empty-external-name covers these
                    var kName = DictModel.AsString(DictModel.GetProp(k, "Name")) ?? "<?>";
                    List<string> bucket;
                    if (!byName.TryGetValue(ext, out bucket))
                        byName[ext] = bucket = new List<string>();
                    bucket.Add(tName + "." + kName);
                }
            }

            foreach (var kv in byName)
            {
                if (kv.Value.Count < 2) continue;
                var ext = kv.Key;
                var owners = kv.Value;
                for (int i = 0; i < owners.Count; i++)
                {
                    var self = owners[i];
                    var others = new List<string>(owners.Count - 1);
                    for (int j = 0; j < owners.Count; j++) if (j != i) others.Add(owners[j]);
                    f.Add(new Finding
                    {
                        Severity = Severity.Error,
                        Target   = self,
                        Rule     = "duplicate-external-name",
                        Message  = "ExternalName '" + ext + "' is also used by: " + string.Join(", ", others.ToArray())
                    });
                }
            }
        }

        // Reuse the same component name list KeyCopier uses so the engine works
        // across Clarion builds that name the collection slightly differently.
        static IEnumerable FindComponents(object key)
        {
            string[] names = { "Components", "KeyComponents", "Fields", "KeyFields",
                               "Segments", "Parts", "Children", "Items", "FieldList" };
            foreach (var n in names)
            {
                var v = DictModel.GetProp(key, n) as IEnumerable;
                if (v != null && !(v is string)) return v;
            }
            return null;
        }
    }
}
