using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace ClarionDctAddin
{
    // Opens a Clarion data file (TPS initially) via Clarion's own runtime —
    // SoftVelocity.Clarion.FileIO.Clarion.CFile — using reflection so we stay
    // free of compile-time deps on the SoftVelocity assemblies.
    //
    // Architecture note: Clarion's file-driver DLLs (ClaTPS.dll, etc.) are
    // 32-bit native. This code therefore only works from inside the 32-bit
    // Clarion IDE process; outside-process probing will hit BadImageFormatException
    // and that's fine — we only call it from the add-in DLL that's loaded by
    // Clarion at runtime.
    //
    // Strategy is deliberately defensive: every step is wrapped, each attempt is
    // logged to the result log, and the OpenForRead entry point never throws —
    // it returns a ReadResult with success / diagnostic info so the UI can show
    // the user exactly which reflection step failed if any.
    internal static class ClarionFileAccessor
    {
        public sealed class ReadResult
        {
            public bool         Ok;
            public string       Error;
            public List<object> Rows          = new List<object>();   // one entry per row, shape below
            public List<string> ColumnLabels  = new List<string>();   // DDField labels in the order they're read
            public List<string> ColumnTypes   = new List<string>();
            public List<string> Log           = new List<string>();
            public int          TotalScanned;
        }

        // Each "row" is a List<object> of values aligned with ColumnLabels/Types.

        public static ReadResult OpenForRead(object dict, object table, int maxRows)
        {
            var r = new ReadResult();
            try { DoOpenForRead(dict, table, maxRows, r); }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                r.Ok = false;
                r.Error = inner.GetType().Name + ": " + inner.Message;
                r.Log.Add("unhandled: " + ex);
            }
            return r;
        }

        static void DoOpenForRead(object dict, object table, int maxRows, ReadResult r)
        {
            var driver = DictModel.AsString(DictModel.GetProp(table, "FileDriverName")) ?? "";
            r.Log.Add("driver=" + driver);
            if (!IsSupportedDriver(driver))
            {
                r.Ok = false;
                r.Error = "Driver '" + driver + "' isn't supported yet (TPS only in this version).";
                return;
            }

            // 1. Resolve the physical file path.
            string path = ResolvePath(dict, table, r);
            r.Log.Add("path=" + path);
            if (string.IsNullOrEmpty(path))
            {
                r.Ok = false;
                r.Error = "Could not resolve a physical file path for this table.";
                return;
            }
            if (!File.Exists(path))
            {
                r.Ok = false;
                r.Error = "File not found on disk: " + path;
                return;
            }

            // 2. Build the DDField catalogue up front so we know the columns.
            var fields = new List<object>();
            var fieldsEnum = DictModel.GetProp(table, "Fields") as IEnumerable;
            if (fieldsEnum != null) foreach (var f in fieldsEnum) if (f != null) fields.Add(f);
            foreach (var f in fields)
            {
                r.ColumnLabels.Add(DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "");
                r.ColumnTypes .Add(DictModel.AsString(DictModel.GetProp(f, "DataType")) ?? "");
            }

            // 3. Load Clarion's runtime FileIO assembly + LINQToFileProvider.
            var fileIo = LoadClarionAssembly("SoftVelocity.Clarion.FileIO.dll", r);
            if (fileIo == null) { r.Ok = false; r.Error = "Could not load SoftVelocity.Clarion.FileIO.dll."; return; }

            var cfileType = fileIo.GetType("Clarion.CFile");
            if (cfileType == null) { r.Ok = false; r.Error = "Clarion.CFile type not found in FileIO.dll."; return; }
            r.Log.Add("CFile type: " + cfileType.AssemblyQualifiedName);

            // 4. Compute a record buffer sized to the sum of DDField sizes. This
            //    is our best-effort layout — native Clarion carries its own
            //    internal padding which we don't model. If reading the buffer
            //    back produces garbage we know this is where to look.
            int recordSize;
            var offsets = BuildFieldOffsets(fields, out recordSize, r);

            byte[] record = new byte[Math.Max(1, recordSize)];
            r.Log.Add("record size: " + recordSize + " bytes");

            // 5. Construct CFile. Try the 4-arg ctor first.
            object cfile;
            try
            {
                var claStringType = fileIo.GetType("Clarion.ClaString");
                var fourArgCtor = cfileType.GetConstructor(new[]
                {
                    typeof(byte[]), claStringType, claStringType, claStringType
                });
                if (fourArgCtor != null)
                {
                    cfile = fourArgCtor.Invoke(new object[]
                    {
                        record,
                        ToClaString(claStringType, driver),
                        ToClaString(claStringType, path),
                        ToClaString(claStringType, "")
                    });
                    r.Log.Add("ctor(byte[], driver, path, options) ok");
                }
                else
                {
                    // Fall back to default ctor + property sets.
                    cfile = Activator.CreateInstance(cfileType);
                    TrySet(cfile, "Driver", driver, r);
                    TrySet(cfile, "Name",   path, r);
                    r.Log.Add("ctor(): default ok");
                }
            }
            catch (Exception ex)
            {
                r.Ok = false;
                r.Error = "Failed to construct CFile: " + (ex.InnerException ?? ex).Message;
                return;
            }

            // 6. Open / iterate / close. Open() with no args is the typical Clarion flow.
            var openMethod  = FindMethod(cfileType, "Open",  Type.EmptyTypes) ?? FindMethod(cfileType, "Open",  new[] { typeof(short) }) ?? FindMethod(cfileType, "Open", new[] { typeof(int) });
            var closeMethod = FindMethod(cfileType, "Close", Type.EmptyTypes);
            var setMethod   = FindMethod(cfileType, "Set",   Type.EmptyTypes);
            var nextMethod  = FindMethod(cfileType, "Next",  Type.EmptyTypes);

            if (openMethod == null || nextMethod == null)
            {
                r.Ok = false;
                r.Error = "CFile is missing Open() or Next() methods; can't iterate.";
                r.Log.Add("openMethod=" + openMethod + ", nextMethod=" + nextMethod);
                return;
            }

            try
            {
                Invoke(openMethod, cfile, "Open");
                r.Log.Add("Open() ok");
                if (setMethod != null) { Invoke(setMethod, cfile, "Set"); r.Log.Add("Set() ok"); }

                for (int i = 0; i < maxRows; i++)
                {
                    var ret = nextMethod.Invoke(cfile, null);
                    // Clarion's Next() typically returns 0 on success, non-zero on EOF/error.
                    int code = 0;
                    if (ret != null) try { code = Convert.ToInt32(ret); } catch { }
                    r.TotalScanned++;
                    if (code != 0) { r.Log.Add("Next() returned " + code + " at row " + (i + 1)); break; }

                    // Record buffer should now hold the current row. Project into values.
                    var row = new List<object>(fields.Count);
                    for (int fi = 0; fi < fields.Count; fi++)
                    {
                        try { row.Add(UnpackField(record, offsets[fi], fields[fi])); }
                        catch (Exception ex) { row.Add("<err:" + ex.GetType().Name + ">"); }
                    }
                    r.Rows.Add(row);
                }
            }
            finally
            {
                if (closeMethod != null) { try { closeMethod.Invoke(cfile, null); } catch { } }
            }

            r.Ok = true;
        }

        static bool IsSupportedDriver(string driver)
        {
            var d = (driver ?? "").ToUpperInvariant();
            // Start with TPS — the user's stated target. Others can be unblocked
            // later by easing this predicate; the rest of the pipeline is
            // driver-agnostic since CFile abstracts over all Clarion drivers.
            return d == "TOPSPEED" || d == "TPS" || d == "TOPSCAN" || d == "TPSCAN";
        }

        static string ResolvePath(object dict, object table, ReadResult r)
        {
            var full = DictModel.AsString(DictModel.GetProp(table, "FullPathName")) ?? "";
            if (!string.IsNullOrEmpty(full))
            {
                if (File.Exists(full)) return full;
                r.Log.Add("FullPathName exists on table but file missing: " + full);
            }

            // Default file name — often just "clientes.tps" with no directory.
            var defName = DictModel.AsString(DictModel.GetProp(table, "DefaultFileName")) ?? "";
            var fallbackName = !string.IsNullOrEmpty(defName) ? defName : full;
            if (string.IsNullOrEmpty(fallbackName)) fallbackName = (DictModel.AsString(DictModel.GetProp(table, "Name")) ?? "table") + ".tps";

            // Look for the file next to the .DCT — that's the "same folder" case the user mentioned.
            var dctPath = DictModel.GetDictionaryFileName(dict);
            if (!string.IsNullOrEmpty(dctPath))
            {
                var dctDir = Path.GetDirectoryName(dctPath);
                if (!string.IsNullOrEmpty(dctDir))
                {
                    // Try literal join.
                    var candidate1 = Path.Combine(dctDir, fallbackName);
                    r.Log.Add("try " + candidate1);
                    if (File.Exists(candidate1)) return candidate1;

                    // Try just the bare file name in the dct dir.
                    var candidate2 = Path.Combine(dctDir, Path.GetFileName(fallbackName));
                    r.Log.Add("try " + candidate2);
                    if (File.Exists(candidate2)) return candidate2;
                }
            }
            return full; // caller verifies existence; this is only useful for the error message
        }

        // Compute running byte offsets for each field in declaration order. Falls
        // back to zeros if a field has no FieldSize we can parse. This is a naïve
        // model — native Clarion may align certain types — but it's our starting
        // point; we'll tune if/when reads come out garbled.
        static int[] BuildFieldOffsets(IList<object> fields, out int recordSize, ReadResult r)
        {
            var offsets = new int[fields.Count];
            int cursor = 0;
            for (int i = 0; i < fields.Count; i++)
            {
                offsets[i] = cursor;
                cursor += LogicalFieldSize(fields[i]);
            }
            recordSize = cursor;
            r.Log.Add("computed field offsets; last cursor=" + cursor);
            return offsets;
        }

        // Map DDField metadata to a byte-size the record buffer needs to allow
        // for. Strings take their FieldSize; numerics take a fixed width based on
        // data type (Clarion LONG=4, SHORT=2, BYTE=1, REAL=8, DATE/TIME=4).
        static int LogicalFieldSize(object field)
        {
            var dt = (DictModel.AsString(DictModel.GetProp(field, "DataType")) ?? "").ToUpperInvariant();
            int declared;
            int.TryParse(DictModel.AsString(DictModel.GetProp(field, "FieldSize")) ?? "0", out declared);
            switch (dt)
            {
                case "BYTE":    return 1;
                case "SHORT": case "USHORT":   return 2;
                case "LONG":  case "ULONG":    return 4;
                case "DATE":                    return 4;
                case "TIME":                    return 4;
                case "REAL":                    return 8;
                case "SREAL":                   return 4;
                case "DECIMAL": case "PDECIMAL":
                    // Packed decimal: ceil((digits+1)/2). Declared size usually holds digits count.
                    return Math.Max(1, (declared + 1) / 2);
                case "STRING": case "CSTRING": case "PSTRING":
                    return Math.Max(0, declared);
                case "MEMO": case "BLOB":
                    return 0;  // stored out of line; placeholder
                default:
                    return Math.Max(0, declared);
            }
        }

        static object UnpackField(byte[] buf, int offset, object field)
        {
            if (buf == null || offset < 0 || offset >= buf.Length) return null;
            var dt = (DictModel.AsString(DictModel.GetProp(field, "DataType")) ?? "").ToUpperInvariant();
            int size = LogicalFieldSize(field);
            int end = offset + size;
            if (end > buf.Length) return "<out-of-range>";
            switch (dt)
            {
                case "BYTE":   return buf[offset];
                case "SHORT":  return BitConverter.ToInt16(buf, offset);
                case "USHORT": return BitConverter.ToUInt16(buf, offset);
                case "LONG":   return BitConverter.ToInt32(buf, offset);
                case "ULONG":  return BitConverter.ToUInt32(buf, offset);
                case "REAL":   return BitConverter.ToDouble(buf, offset);
                case "SREAL":  return BitConverter.ToSingle(buf, offset);
                case "DATE":
                    int days = BitConverter.ToInt32(buf, offset);
                    return ClarionDateToString(days);
                case "TIME":
                    int centi = BitConverter.ToInt32(buf, offset);
                    return ClarionTimeToString(centi);
                case "STRING": case "CSTRING":
                    return ReadAsciiField(buf, offset, size, dt);
                case "PSTRING":
                    // Pascal string: first byte is length, rest is payload.
                    if (size <= 1) return "";
                    int plen = buf[offset];
                    if (plen > size - 1) plen = size - 1;
                    return Encoding.ASCII.GetString(buf, offset + 1, plen).TrimEnd();
                case "DECIMAL": case "PDECIMAL":
                    return ReadPackedDecimal(buf, offset, size, field);
                case "MEMO": case "BLOB":
                    return "<memo>";
                default:
                    return "<" + dt + ">";
            }
        }

        static string ReadAsciiField(byte[] buf, int offset, int size, string dataType)
        {
            if (size <= 0) return "";
            var s = Encoding.GetEncoding(1252).GetString(buf, offset, size);
            if (dataType == "CSTRING")
            {
                // Null-terminated
                var nul = s.IndexOf('\0');
                if (nul >= 0) s = s.Substring(0, nul);
            }
            return s.TrimEnd();
        }

        static string ReadPackedDecimal(byte[] buf, int offset, int size, object field)
        {
            try
            {
                int places;
                int.TryParse(DictModel.AsString(DictModel.GetProp(field, "Places")) ?? "0", out places);
                // Build digit string: each nibble is a digit, last nibble is sign.
                var sb = new StringBuilder(size * 2);
                for (int i = 0; i < size; i++)
                {
                    sb.Append(((buf[offset + i] >> 4) & 0x0F).ToString());
                    sb.Append(((buf[offset + i]     ) & 0x0F).ToString());
                }
                var raw = sb.ToString();
                if (raw.Length == 0) return "";
                var signNibble = raw[raw.Length - 1];
                raw = raw.Substring(0, raw.Length - 1);
                var negative = signNibble == 'D' || signNibble == 'B';
                if (places > 0 && raw.Length > places)
                    raw = raw.Substring(0, raw.Length - places) + "." + raw.Substring(raw.Length - places);
                // Trim leading zeros except the one before the decimal point.
                int firstNonZero = 0;
                while (firstNonZero < raw.Length - 1 && raw[firstNonZero] == '0' && raw[firstNonZero + 1] != '.') firstNonZero++;
                raw = raw.Substring(firstNonZero);
                return negative ? "-" + raw : raw;
            }
            catch { return "<decimal>"; }
        }

        // Clarion dates are days since 28-Dec-1800 (with value 1 == 28-Dec-1800
        // in some rigs, or offset-by-one in others). Using the common convention:
        //   day 4 == 1-Jan-1801
        //   day 58120 == 1-Jan-1960  (close to common reality)
        // Empirically Clarion uses days since Dec 28, 1800 where day 1 = 1801-01-01.
        // We use DateTime(1800,12,28) + days.
        static readonly DateTime ClarionEpoch = new DateTime(1800, 12, 28);
        static string ClarionDateToString(int days)
        {
            if (days <= 0) return "";
            try { return ClarionEpoch.AddDays(days).ToString("yyyy-MM-dd"); }
            catch { return days.ToString(); }
        }

        static string ClarionTimeToString(int centiseconds)
        {
            if (centiseconds <= 0) return "";
            // Clarion TIME is centiseconds since midnight + 1.
            var total = centiseconds - 1;
            if (total < 0) total = 0;
            int h  = total / 360000;      total -= h * 360000;
            int m  = total / 6000;        total -= m * 6000;
            int s  = total / 100;         total -= s * 100;
            return string.Format("{0:D2}:{1:D2}:{2:D2}.{3:D2}", h, m, s, total);
        }

        // ---- Reflection helpers ----

        static Assembly LoadClarionAssembly(string fileName, ReadResult r)
        {
            var cands = new[]
            {
                Path.Combine(@"C:\clarion12\bin", fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName)
            };
            foreach (var p in cands)
            {
                if (!File.Exists(p)) continue;
                try
                {
                    var a = Assembly.LoadFrom(p);
                    r.Log.Add("loaded " + fileName + " from " + p);
                    return a;
                }
                catch (Exception ex) { r.Log.Add("loadFrom " + p + " failed: " + ex.GetType().Name + " - " + ex.Message); }
            }

            // Last resort: check already-loaded AppDomain assemblies (Clarion preloads most of these).
            var name = Path.GetFileNameWithoutExtension(fileName);
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    r.Log.Add("resolved " + fileName + " from already-loaded AppDomain");
                    return loaded;
                }
            }
            return null;
        }

        static object ToClaString(Type claStringType, string value)
        {
            if (claStringType == null) return value; // let the runtime fall back to string ctor coercion
            // Try constructors: (string), (byte[]) etc.
            var ctorStr = claStringType.GetConstructor(new[] { typeof(string) });
            if (ctorStr != null) return ctorStr.Invoke(new object[] { value ?? "" });
            var ctorDefault = claStringType.GetConstructor(Type.EmptyTypes);
            if (ctorDefault != null)
            {
                var inst = ctorDefault.Invoke(null);
                // Try common setters.
                TrySet(inst, "Value", value ?? "", null);
                TrySet(inst, "Text",  value ?? "", null);
                return inst;
            }
            return value;
        }

        static void TrySet(object target, string name, object value, ReadResult r)
        {
            if (target == null) return;
            try
            {
                var p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && p.CanWrite) { p.SetValue(target, value, null); if (r != null) r.Log.Add("set " + name + " ok"); return; }
                var f = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) { f.SetValue(target, value); if (r != null) r.Log.Add("set-field " + name + " ok"); return; }
            }
            catch (Exception ex) { if (r != null) r.Log.Add("set " + name + " failed: " + ex.Message); }
        }

        static MethodInfo FindMethod(Type t, string name, Type[] args)
        {
            if (t == null) return null;
            return t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, args, null);
        }

        static void Invoke(MethodInfo m, object target, string tag)
        {
            if (m == null) return;
            var ps = m.GetParameters();
            var args = new object[ps.Length];
            // Default every parameter — most Clarion Open overloads accept 0 or 1
            // modest argument (open mode / access flag); 0 will give read access.
            for (int i = 0; i < args.Length; i++)
                args[i] = ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
            m.Invoke(target, args);
        }
    }
}
