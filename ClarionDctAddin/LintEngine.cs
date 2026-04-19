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

        static void RunTableChecks(object table, List<Finding> f)
        {
            var tName = DictModel.AsString(DictModel.GetProp(table, "Name")) ?? "<?>";

            int fieldCount = DictModel.CountEnumerable(table, "Fields");
            int keyCount   = DictModel.CountEnumerable(table, "Keys");
            int relCount   = DictModel.CountEnumerable(table, "Relations");

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

                    if (label.Contains(" "))
                        f.Add(new Finding { Severity = Severity.Warning, Target = target, Rule = "whitespace-in-label",
                                            Message = "Field label contains whitespace — most generators reject this." });
                }
            }
        }

        static void RunDictionaryChecks(object dict, IList<object> tables, List<Finding> f)
        {
            // Nothing dictionary-global yet beyond aggregations, but slot is here
            // for future rules like "duplicate table names", "orphan relation refs"
            // etc.
            if (tables.Count == 0)
                f.Add(new Finding { Severity = Severity.Error, Target = "<dictionary>", Rule = "no-tables",
                                    Message = "Dictionary contains zero tables." });
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
