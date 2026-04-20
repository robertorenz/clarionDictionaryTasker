using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TpsParse.Tps;
using TpsParse.Tps.Record;

namespace ClarionDctAddin
{
    // Direct TPS reader that bypasses Clarion's native runtime entirely.
    // Uses the Apache-2-licensed tps-parse-net library (bundled under
    // TpsParse/) which implements the reverse-engineered TPS file format
    // in pure managed code.
    //
    // The payoff: we don't need ClaTPS.dll, CLegacy, CFile subclasses, or
    // the native callback contract that blocked the inline Clarion-runtime
    // path. This reads TPS bytes directly from disk. The tradeoff: the
    // parser is community-reverse-engineered, so edge-case TPS files
    // (encrypted, weird schema variants, unusual memos) may fail — but for
    // the common case it covers every field type we care about.
    //
    // The output conforms to ClarionFileAccessor.ReadResult so ViewDataDialog
    // can render it identically to the SQL path.
    internal static class TpsDirectReader
    {
        public static ClarionFileAccessor.ReadResult Read(object dict, object table, int maxRows)
        {
            var r = new ClarionFileAccessor.ReadResult();
            try { DoRead(dict, table, maxRows, r); }
            catch (Exception ex)
            {
                r.Ok = false;
                r.Error = ex.GetType().Name + ": " + ex.Message;
                r.Log.Add("unhandled: " + ex);
            }
            return r;
        }

        static void DoRead(object dict, object table, int maxRows, ClarionFileAccessor.ReadResult r)
        {
            string path = ResolveTpsPath(dict, table);
            r.Log.Add("path=" + (path ?? "<null>"));
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            { r.Ok = false; r.Error = "TPS file not found: " + path; return; }

            // Column metadata from DCT (so columns render in DCT order even
            // if the TPS file's internal order differs).
            var dctFields = new List<object>();
            var fieldsEnum = DictModel.GetProp(table, "Fields") as IEnumerable;
            if (fieldsEnum != null) foreach (var f in fieldsEnum) if (f != null) dctFields.Add(f);
            foreach (var f in dctFields)
            {
                r.ColumnLabels.Add(DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "");
                r.ColumnTypes .Add(DictModel.AsString(DictModel.GetProp(f, "DataType")) ?? "");
            }

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                TpsFile tps;
                try { tps = new TpsFile(fs); }
                catch (Exception ex) { r.Ok = false; r.Error = "Could not open as TPS: " + ex.Message; return; }

                List<TableDefinitionRecord> defs;
                try { defs = tps.GetTableDefinitions(); }
                catch (Exception ex) { r.Ok = false; r.Error = "Could not parse TPS table definitions: " + ex.Message; return; }

                if (defs == null || defs.Count == 0)
                { r.Ok = false; r.Error = "TPS file contains no table definitions."; return; }

                // Most TPS files hold a single table. If there's more than
                // one, pick the one with the richest field list.
                var def = defs[0];
                for (int i = 1; i < defs.Count; i++)
                    if (defs[i].Fields != null && def.Fields != null && defs[i].Fields.Count > def.Fields.Count)
                        def = defs[i];

                r.Log.Add("TPS schema: " + (def.Fields == null ? 0 : def.Fields.Count) + " fields, "
                    + (def.Memos == null ? 0 : def.Memos.Count) + " memos, "
                    + (def.Indexes == null ? 0 : def.Indexes.Count) + " indexes, recordLen="
                    + def.RecordLength);

                // Map DCT field index -> TPS field index via case-insensitive
                // label match. TPS field names often carry a "PRE:FIELD"
                // prefix (the Clarion file prefix + colon); compare against
                // the suffix after the colon.
                int[] tpsFieldIndex = BuildDctToTpsMap(dctFields, def, r);

                int scanned = 0;
                foreach (var rec in tps.GetDataRecords(def))
                {
                    if (scanned >= maxRows) break;
                    scanned++;
                    try { rec.ParseValues(def); }
                    catch (Exception ex) { r.Log.Add("ParseValues failed on row " + scanned + ": " + ex.Message); continue; }

                    var row = new List<object>(dctFields.Count);
                    for (int fi = 0; fi < dctFields.Count; fi++)
                    {
                        int tpsIdx = tpsFieldIndex[fi];
                        if (tpsIdx < 0 || rec.Values == null || tpsIdx >= rec.Values.Count)
                        {
                            // Unmatched DCT column — likely a MEMO/BLOB or a
                            // field name that doesn't exist in the TPS file.
                            row.Add(MaybeMemoPlaceholder(dctFields[fi]));
                            continue;
                        }
                        row.Add(FormatValue(rec.Values[tpsIdx]));
                    }
                    r.Rows.Add(row);
                }
                r.TotalScanned = scanned;
            }

            r.Ok = true;
        }

        // Case-insensitive suffix match. Returns -1 for unmatched DCT fields.
        static int[] BuildDctToTpsMap(List<object> dctFields, TableDefinitionRecord def, ClarionFileAccessor.ReadResult r)
        {
            var map = new int[dctFields.Count];
            for (int i = 0; i < map.Length; i++) map[i] = -1;
            if (def.Fields == null) return map;

            for (int fi = 0; fi < dctFields.Count; fi++)
            {
                var label = (DictModel.AsString(DictModel.GetProp(dctFields[fi], "Label")) ?? "").Trim();
                if (label.Length == 0) continue;
                for (int ti = 0; ti < def.Fields.Count; ti++)
                {
                    var tpsName = def.Fields[ti].FieldName ?? "";
                    var colon = tpsName.LastIndexOf(':');
                    var tpsSuffix = colon >= 0 ? tpsName.Substring(colon + 1) : tpsName;
                    if (string.Equals(tpsSuffix, label, StringComparison.OrdinalIgnoreCase))
                    { map[fi] = ti; break; }
                }
                if (map[fi] < 0) r.Log.Add("no TPS match for DCT field '" + label + "'");
            }
            return map;
        }

        static string MaybeMemoPlaceholder(object dctField)
        {
            var dt = (DictModel.AsString(DictModel.GetProp(dctField, "DataType")) ?? "").ToUpperInvariant();
            if (dt == "MEMO" || dt == "BLOB") return "<memo>";
            return "";
        }

        // TpsParse returns native types already converted (short / int /
        // DateTime / TimeSpan / decimal / string). We just render them.
        static object FormatValue(object v)
        {
            if (v == null) return "";
            if (v is DateTime) return ((DateTime)v).ToString("yyyy-MM-dd");
            if (v is TimeSpan) return ((TimeSpan)v).ToString();
            if (v is byte[])
            {
                var b = (byte[])v;
                if (b.Length == 0) return "";
                // Try ASCII first (most Clarion text fields). If the bytes
                // are non-printable, fall back to hex preview.
                bool printable = true;
                for (int i = 0; i < b.Length; i++)
                {
                    var c = b[i];
                    if (c == 0) break;
                    if (c < 0x09 || (c > 0x0D && c < 0x20)) { printable = false; break; }
                }
                if (printable) return Encoding.GetEncoding(1252).GetString(b).TrimEnd('\0', ' ');
                return "<" + b.Length + " bytes>";
            }
            return v;
        }

        static string ResolveTpsPath(object dict, object table)
        {
            var full = DictModel.AsString(DictModel.GetProp(table, "FullPathName")) ?? "";
            if (!string.IsNullOrEmpty(full) && File.Exists(full)) return full;

            var defName = DictModel.AsString(DictModel.GetProp(table, "DefaultFileName")) ?? "";
            var fallbackName = !string.IsNullOrEmpty(defName) ? defName : full;
            if (string.IsNullOrEmpty(fallbackName))
                fallbackName = (DictModel.AsString(DictModel.GetProp(table, "Name")) ?? "table") + ".tps";

            var dctPath = DictModel.GetDictionaryFileName(dict);
            if (!string.IsNullOrEmpty(dctPath))
            {
                var dctDir = Path.GetDirectoryName(dctPath);
                if (!string.IsNullOrEmpty(dctDir))
                {
                    var c1 = Path.Combine(dctDir, fallbackName);
                    if (File.Exists(c1)) return c1;
                    var c2 = Path.Combine(dctDir, Path.GetFileName(fallbackName));
                    if (File.Exists(c2)) return c2;
                }
            }
            return full;
        }
    }
}
