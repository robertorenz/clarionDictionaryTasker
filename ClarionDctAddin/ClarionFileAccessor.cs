using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace ClarionDctAddin
{
    // Opens a Clarion data file (TPS only in this version) via Clarion's own
    // runtime — SoftVelocity.Clarion.FileIO.Clarion.CFile — using reflection
    // so we stay free of compile-time deps on the SoftVelocity assemblies.
    //
    // Pipeline (reverse-engineered from CFile.SetupFile / ToXML / FieldToXML
    // IL + CLegacy dispatcher ctor):
    //
    //   1. Emit a dynamic CFile subclass with abstract-method stubs plus a
    //      forwarding 4-arg ctor(Byte[], ClaString, ClaString, ClaString).
    //   2. Instantiate it via that ctor (buffer, name, owner, driver). That's
    //      the shape Clarion-generated code uses.
    //   3. Call SetupFile(XmlDocument, 0) with a schema XML built from the
    //      DCT field list, matching the dialect that CFile's own FieldToXML /
    //      MemoToXML / KeyToXML emit. SetupFile rebuilds m_pClaFile with a
    //      real record layout and rewires m_pBuffer to wrap rec_buf.
    //   4. Write the driver name ("TOPSPEED\0") into the native sbyte*
    //      m_sDrvName buffer (it's what CDriver.GetDriver hashes on).
    //   5. Open(0x42h) → the CLegacy dispatcher routes through IdeRtlCall
    //      into ClarionDrv.dll.
    //
    // *** Known ceiling ***
    //
    // Step 5 succeeds without exception but the native runtime then refuses
    // to populate rec_buf — Bytes() and Records() both return 0. This is
    // because CLegacy communicates with managed code via an m_pEval callback
    // (CLegacy.GetValue) that Clarion-generated subclasses fulfill via the
    // abstract __CLA_Retrieve / __CLA_Store / __CLA_GetRecord overrides. Our
    // stubbed no-op abstracts don't satisfy the contract, so native code
    // silently skips record marshaling. Getting past this would require
    // re-implementing the Clarion code generator's CGroup binding — out of
    // scope here. We detect the failure cleanly and route users to
    // "Embed TopScan" / "Open in TopScan" for TPS viewing.
    internal static class ClarionFileAccessor
    {
        public sealed class ReadResult
        {
            public bool         Ok;
            public string       Error;
            public List<object> Rows          = new List<object>();   // List<List<object>> aligned with ColumnLabels
            public List<string> ColumnLabels  = new List<string>();
            public List<string> ColumnTypes   = new List<string>();
            public List<string> Log           = new List<string>();
            public int          TotalScanned;
        }

        // Clarion open mode: ReadWrite + DenyNone. Generated ABC apps use
        // this by default; mode 0 is rejected by the native driver.
        const int CLARION_OPEN_MODE = 0x42;

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

            string path = ResolvePath(dict, table, r);
            r.Log.Add("path=" + path);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                r.Ok = false;
                r.Error = "File not found on disk: " + path;
                return;
            }

            // Column metadata for the grid (always populated so SQL / TPS
            // both render the same shape).
            var fields = new List<object>();
            var fieldsEnum = DictModel.GetProp(table, "Fields") as IEnumerable;
            if (fieldsEnum != null) foreach (var f in fieldsEnum) if (f != null) fields.Add(f);
            foreach (var f in fields)
            {
                r.ColumnLabels.Add(DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "");
                r.ColumnTypes .Add(DictModel.AsString(DictModel.GetProp(f, "DataType")) ?? "");
            }

            // Load the Clarion .NET runtime assemblies.
            var fileIo = LoadClarionAssembly("SoftVelocity.Clarion.FileIO.dll", r);
            if (fileIo == null) { r.Ok = false; r.Error = "Could not load SoftVelocity.Clarion.FileIO.dll."; return; }
            LoadClarionAssembly("SoftVelocity.Clarion.Runtime.Classes.dll",   r);
            LoadClarionAssembly("SoftVelocity.Clarion.Classes.dll",           r);
            LoadClarionAssembly("SoftVelocity.Clarion.Runtime.Procedures.dll", r);

            var cfileBaseType = fileIo.GetType("Clarion.CFile");
            if (cfileBaseType == null) { r.Ok = false; r.Error = "Clarion.CFile type not found."; return; }

            var claStringType = ResolveClaStringType(fileIo, r);
            if (claStringType == null) { r.Ok = false; r.Error = "Clarion.ClaString type not found."; return; }

            // Emit our concrete subclass with abstract stubs + forwarding ctor.
            var cfileType = EmitDynamicCFileSubclass(cfileBaseType, claStringType, r);
            if (cfileType == null) { r.Ok = false; r.Error = "Could not emit a dynamic CFile subclass."; return; }

            // Record-size placeholder (SetupFile discards this and builds a
            // real buffer from the XML schema).
            int recordSize;
            var offsets = BuildFieldOffsets(fields, out recordSize);
            byte[] seedBuffer = new byte[Math.Max(1, recordSize)];

            // Instantiate via 4-arg ctor(buffer, name, owner, driver).
            var nameCla  = WrapClaString(claStringType, path,   r);
            var ownerCla = WrapClaString(claStringType, "",     r);
            var drvCla   = WrapClaString(claStringType, driver, r);
            if (nameCla == null || ownerCla == null || drvCla == null)
            { r.Ok = false; r.Error = "Could not wrap ClaString for ctor arguments."; return; }

            var ctor4 = cfileType.GetConstructor(new[] { typeof(byte[]), claStringType, claStringType, claStringType });
            if (ctor4 == null) { r.Ok = false; r.Error = "4-arg CFile ctor not available on emitted subclass."; return; }

            object cfile;
            try
            {
                cfile = ctor4.Invoke(new object[] { seedBuffer, nameCla, ownerCla, drvCla });
                r.Log.Add("instantiated CFile via 4-arg ctor (buffer, name, owner, driver)");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                r.Ok = false; r.Error = "CFile ctor failed: " + inner.Message;
                return;
            }

            // Inject the schema via SetupFile. Without this step, the TPS
            // driver opens the file but m_pClaFile has no layout and rec_buf
            // stays empty.
            var schemaXml = BuildClarionSchemaXml(table, path, driver, r);
            if (!InvokeSetupFile(cfile, cfileBaseType, schemaXml, r))
            { r.Ok = false; r.Error = "SetupFile(xmlDoc, 0) failed — see log."; return; }

            // Populate the native sbyte* m_sDrvName buffer so CDriver.GetDriver's
            // `new String(cfile.DrvName).ToUpper()` hashes to a real driver.
            PopulateDrvNamePointer(cfile, cfileBaseType, driver, r);

            // Open / iterate / close.
            var openMethod  = FindMethod(cfileType, "Open",  Type.EmptyTypes) ?? FindMethod(cfileType, "Open", new[] { typeof(int) });
            var closeMethod = FindMethod(cfileType, "Close", Type.EmptyTypes);
            var setMethod   = FindMethod(cfileType, "Set",   Type.EmptyTypes);
            var nextMethod  = FindMethod(cfileType, "Next",  Type.EmptyTypes);
            if (openMethod == null || nextMethod == null)
            { r.Ok = false; r.Error = "CFile is missing Open() or Next() methods."; return; }

            try
            {
                InvokeOpen(openMethod, cfile, r);

                // After Open, the Clarion runtime routes through CLegacy →
                // native ClarionDrv.dll. If the native runtime is satisfied,
                // Bytes()/Records() return real values. If our callback
                // contract falls short, both silently return 0 — detect that
                // cleanly rather than iterating empty records.
                int drvBytes = SafeIntCall(cfileType, cfile, "Bytes");
                int drvRecords = SafeIntCall(cfileType, cfile, "Records");
                r.Log.Add("driver Bytes=" + drvBytes + "  Records=" + drvRecords);

                if (drvBytes <= 0 || drvRecords <= 0)
                {
                    r.Ok = false;
                    r.Error = "Inline TPS read: native runtime opened the file but won't populate records "
                        + "(the Clarion callback contract for emitted subclasses isn't fully satisfied). "
                        + "Use 'Open in TopScan' or 'Embed TopScan' to view this table.";
                    return;
                }

                if (setMethod != null) Invoke(setMethod, cfile);

                // Native rec_buf / rec_len live inside m_pClaFile's ClaFile*
                // struct (SetupFile just set them). The TPS driver refills
                // rec_buf on every Next().
                IntPtr recBufPtr; int recLen;
                if (!ResolveNativeRecordBuffer(cfile, cfileBaseType, out recBufPtr, out recLen, r))
                { r.Ok = false; r.Error = "Could not resolve rec_buf from m_pClaFile."; return; }

                byte[] nativeRecord = new byte[Math.Max(1, recLen)];
                for (int i = 0; i < maxRows; i++)
                {
                    var ret = nextMethod.Invoke(cfile, null);
                    int code = 0;
                    if (ret != null) try { code = Convert.ToInt32(ret); } catch { }
                    r.TotalScanned++;
                    if (code != 0) { r.Log.Add("Next() returned " + code + " at row " + (i + 1)); break; }

                    Marshal.Copy(recBufPtr, nativeRecord, 0, recLen);

                    var row = new List<object>(fields.Count);
                    for (int fi = 0; fi < fields.Count; fi++)
                    {
                        try { row.Add(UnpackField(nativeRecord, offsets[fi], fields[fi])); }
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
            return d == "TOPSPEED" || d == "TPS" || d == "TOPSCAN" || d == "TPSCAN";
        }

        static string ResolvePath(object dict, object table, ReadResult r)
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

        // Running byte offsets for each field in declaration order.
        static int[] BuildFieldOffsets(IList<object> fields, out int recordSize)
        {
            var offsets = new int[fields.Count];
            int cursor = 0;
            for (int i = 0; i < fields.Count; i++)
            {
                offsets[i] = cursor;
                cursor += LogicalFieldSize(fields[i]);
            }
            recordSize = cursor;
            return offsets;
        }

        static int LogicalFieldSize(object field)
        {
            var dt = (DictModel.AsString(DictModel.GetProp(field, "DataType")) ?? "").ToUpperInvariant();
            int declared;
            int.TryParse(DictModel.AsString(DictModel.GetProp(field, "FieldSize")) ?? "0", out declared);
            switch (dt)
            {
                case "BYTE":                    return 1;
                case "SHORT": case "USHORT":    return 2;
                case "LONG":  case "ULONG":     return 4;
                case "DATE":                    return 4;
                case "TIME":                    return 4;
                case "REAL":                    return 8;
                case "SREAL":                   return 4;
                case "DECIMAL": case "PDECIMAL":
                    return Math.Max(1, (declared + 1) / 2);
                case "STRING": case "CSTRING": case "PSTRING":
                    return Math.Max(0, declared);
                case "MEMO": case "BLOB":
                    return 0;
                default:
                    return Math.Max(0, declared);
            }
        }

        // ---- Value unpacking ------------------------------------------------

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
                case "DATE":   return ClarionDateToString(BitConverter.ToInt32(buf, offset));
                case "TIME":   return ClarionTimeToString(BitConverter.ToInt32(buf, offset));
                case "STRING": case "CSTRING":
                    return ReadAsciiField(buf, offset, size, dt);
                case "PSTRING":
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
                int firstNonZero = 0;
                while (firstNonZero < raw.Length - 1 && raw[firstNonZero] == '0' && raw[firstNonZero + 1] != '.') firstNonZero++;
                raw = raw.Substring(firstNonZero);
                return negative ? "-" + raw : raw;
            }
            catch { return "<decimal>"; }
        }

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
            var total = centiseconds - 1;
            if (total < 0) total = 0;
            int h  = total / 360000;      total -= h * 360000;
            int m  = total / 6000;        total -= m * 6000;
            int s  = total / 100;         total -= s * 100;
            return string.Format("{0:D2}:{1:D2}:{2:D2}.{3:D2}", h, m, s, total);
        }

        // ---- Assembly + type resolution ------------------------------------

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
                try { return Assembly.LoadFrom(p); }
                catch (Exception ex) { r.Log.Add("LoadFrom " + p + " failed: " + ex.Message); }
            }
            var name = Path.GetFileNameWithoutExtension(fileName);
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                    return loaded;
            return null;
        }

        static Type ResolveClaStringType(Assembly fileIo, ReadResult r)
        {
            var t = fileIo.GetType("Clarion.ClaString");
            if (t != null) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = a.GetType("Clarion.ClaString"); if (t != null) return t; }
                catch { }
            }
            return null;
        }

        // ---- Dynamic CFile subclass emission --------------------------------

        static Type EmitDynamicCFileSubclass(Type cfileBase, Type claStringType, ReadResult r)
        {
            try
            {
                var asmName = new AssemblyName("DynCFile_" + Guid.NewGuid().ToString("N"));
                var asmBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
                var modBuilder = asmBuilder.DefineDynamicModule(asmName.Name);
                var typeBuilder = modBuilder.DefineType(
                    "DynCFileImpl_" + Guid.NewGuid().ToString("N"),
                    TypeAttributes.Public | TypeAttributes.Class,
                    cfileBase);

                // Stub every abstract member (collapse inheritance chain).
                var handled = new HashSet<string>(StringComparer.Ordinal);
                var t = cfileBase;
                while (t != null && t != typeof(object))
                {
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (!m.IsAbstract) continue;
                        var sig = MethodSignature(m);
                        if (!handled.Add(sig)) continue;
                        try { EmitStub(typeBuilder, m); }
                        catch (Exception ex) { r.Log.Add("emit stub " + m.Name + " failed: " + ex.Message); }
                    }
                    t = t.BaseType;
                }

                // Forwarding 4-arg ctor — the shape generated code uses.
                var baseCtor4 = cfileBase.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(byte[]), claStringType, claStringType, claStringType },
                    null);
                if (baseCtor4 == null)
                {
                    r.Log.Add("no 4-arg CFile base ctor found — inline reader cannot proceed");
                    return null;
                }
                var ctorBuilder = typeBuilder.DefineConstructor(
                    MethodAttributes.Public | MethodAttributes.HideBySig
                        | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                    CallingConventions.Standard,
                    new[] { typeof(byte[]), claStringType, claStringType, claStringType });
                var cil = ctorBuilder.GetILGenerator();
                cil.Emit(OpCodes.Ldarg_0);
                cil.Emit(OpCodes.Ldarg_1);
                cil.Emit(OpCodes.Ldarg_2);
                cil.Emit(OpCodes.Ldarg_3);
                cil.Emit(OpCodes.Ldarg_S, (byte)4);
                cil.Emit(OpCodes.Call, baseCtor4);
                cil.Emit(OpCodes.Ret);

                return typeBuilder.CreateType();
            }
            catch (Exception ex)
            {
                r.Log.Add("EmitDynamicCFileSubclass failed: " + (ex.InnerException ?? ex).Message);
                return null;
            }
        }

        static string MethodSignature(MethodInfo m)
        {
            var parms = m.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name).ToArray();
            return m.Name + "(" + string.Join(",", parms) + ")";
        }

        static void EmitStub(TypeBuilder tb, MethodInfo abs)
        {
            var parms = abs.GetParameters();
            var parmTypes = parms.Select(p => p.ParameterType).ToArray();
            var attrs = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig;
            if (abs.IsFinal || abs.Attributes.HasFlag(MethodAttributes.NewSlot)) attrs |= MethodAttributes.NewSlot;
            else                                                                 attrs |= MethodAttributes.ReuseSlot;

            var mb = tb.DefineMethod(abs.Name, attrs, abs.CallingConvention, abs.ReturnType, parmTypes);
            for (int i = 0; i < parms.Length; i++)
                mb.DefineParameter(i + 1, parms[i].Attributes, parms[i].Name);

            var il = mb.GetILGenerator();
            if (abs.ReturnType == typeof(void))
            {
                il.Emit(OpCodes.Ret);
            }
            else if (abs.ReturnType.IsValueType)
            {
                var loc = il.DeclareLocal(abs.ReturnType);
                il.Emit(OpCodes.Ldloca_S, (byte)loc.LocalIndex);
                il.Emit(OpCodes.Initobj, abs.ReturnType);
                il.Emit(OpCodes.Ldloc, loc);
                il.Emit(OpCodes.Ret);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            }

            tb.DefineMethodOverride(mb, abs);
        }

        // ---- Clarion schema XML synthesis -----------------------------------

        // Matches the dialect CFile.ToXML / FieldToXML / MemoToXML / KeyToXML
        // emit (and therefore the inverse parser in NewClaFile(XmlDocument)
        // accepts). Keys are omitted — they don't affect record layout and
        // we read sequentially.
        static XmlDocument BuildClarionSchemaXml(object table, string path, string driver, ReadResult r)
        {
            var doc = new XmlDocument();
            var file = doc.CreateElement("File");
            var tableName = DictModel.AsString(DictModel.GetProp(table, "Name")) ?? "";
            file.SetAttribute("label",        ToValidClarionLabel(tableName, "File"));
            file.SetAttribute("version",      "1");
            file.SetAttribute("compatible",   "1");
            file.SetAttribute("driver",       driver ?? "");
            file.SetAttribute("driverString", driver ?? "");
            if (!string.IsNullOrEmpty(path)) file.SetAttribute("name", path);
            doc.AppendChild(file);

            var fields = new List<object>();
            var fieldsEnum = DictModel.GetProp(table, "Fields") as IEnumerable;
            if (fieldsEnum != null) foreach (var f in fieldsEnum) if (f != null) fields.Add(f);

            // Memos first (Clarion writes them before Fields).
            foreach (var f in fields)
            {
                var dt = (DictModel.AsString(DictModel.GetProp(f, "DataType")) ?? "").ToUpperInvariant();
                if (dt != "MEMO" && dt != "BLOB") continue;
                var label = DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "";
                var memo = doc.CreateElement("Memo");
                memo.SetAttribute("label",      ToValidClarionLabel(label, "Memo"));
                memo.SetAttribute("version",    "1");
                memo.SetAttribute("compatible", "1");
                if (dt == "BLOB")
                {
                    memo.SetAttribute("blob", "true");
                }
                else
                {
                    int sz; int.TryParse(DictModel.AsString(DictModel.GetProp(f, "FieldSize")) ?? "0", out sz);
                    memo.SetAttribute("size", (sz > 0 ? sz : 1024).ToString());
                }
                file.AppendChild(memo);
            }

            // Then scalar / string fields.
            foreach (var f in fields)
            {
                var dt = (DictModel.AsString(DictModel.GetProp(f, "DataType")) ?? "").ToUpperInvariant();
                if (dt == "MEMO" || dt == "BLOB") continue;
                var label = DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "";

                var field = doc.CreateElement("Field");
                field.SetAttribute("label",      ToValidClarionLabel(label, "Field"));
                field.SetAttribute("version",    "1");
                field.SetAttribute("compatible", "1");
                field.SetAttribute("type",       dt);

                int sizeN; int.TryParse(DictModel.AsString(DictModel.GetProp(f, "FieldSize")) ?? "0", out sizeN);
                if (dt == "STRING" || dt == "CSTRING" || dt == "PSTRING"
                    || dt == "DECIMAL" || dt == "PDECIMAL")
                {
                    if (sizeN > 0) field.SetAttribute("size", sizeN.ToString());
                    if (dt == "DECIMAL" || dt == "PDECIMAL")
                    {
                        var places = DictModel.AsString(DictModel.GetProp(f, "Places"));
                        if (!string.IsNullOrEmpty(places) && places != "0")
                            field.SetAttribute("places", places);
                    }
                }
                var picture = DictModel.AsString(DictModel.GetProp(f, "Picture"));
                if (!string.IsNullOrEmpty(picture)) field.SetAttribute("picture", picture);

                file.AppendChild(field);
            }

            r.Log.Add("built schema XML (" + doc.OuterXml.Length + " chars)");
            return doc;
        }

        // Clarion labels must be letters / digits / underscores, starting
        // with a letter. Non-alphanumerics → '_', leading digit → fallback prefix.
        static string ToValidClarionLabel(string s, string fallback)
        {
            if (string.IsNullOrEmpty(s)) s = fallback;
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            if (sb.Length == 0 || !char.IsLetter(sb[0])) sb.Insert(0, fallback + "_");
            return sb.ToString();
        }

        static bool InvokeSetupFile(object cfile, Type cfileBase, XmlDocument xml, ReadResult r)
        {
            var m = cfileBase.GetMethod("SetupFile", BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(XmlDocument), typeof(int) }, null);
            if (m == null) { r.Log.Add("SetupFile(XmlDocument, Int32) not found"); return false; }
            try { m.Invoke(cfile, new object[] { xml, 0 }); return true; }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                r.Log.Add("SetupFile threw: " + inner.GetType().Name + " - " + inner.Message);
                return false;
            }
        }

        // ---- Native pointer helpers -----------------------------------------

        // Clarion's 4-arg CFile ctor allocates a native sbyte* buffer for
        // m_sDrvName but leaves it as an empty string. CDriver.GetDriver's IL
        // does `new String(cfile.DrvName).ToUpper()` → null pointer gives
        // ""→Hashtable miss→null unbox→NRE, and even a non-null-but-empty
        // pointer misses the hashtable. We overwrite the existing buffer
        // (never swap the pointer — that desyncs sibling native state).
        static unsafe void PopulateDrvNamePointer(object cfile, Type cfileBase, string driver, ReadResult r)
        {
            var f = cfileBase.GetField("m_sDrvName", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) { r.Log.Add("m_sDrvName field not found"); return; }
            var v = f.GetValue(cfile);
            if (!(v is Pointer)) { r.Log.Add("m_sDrvName not a Pointer"); return; }
            IntPtr p = (IntPtr)Pointer.Unbox(v);
            if (p == IntPtr.Zero) { r.Log.Add("m_sDrvName pointer is null"); return; }

            byte[] bytes = Encoding.ASCII.GetBytes((driver ?? "") + "\0");
            try { Marshal.Copy(bytes, 0, p, bytes.Length); }
            catch (Exception ex) { r.Log.Add("Marshal.Copy m_sDrvName failed: " + ex.Message); }
        }

        // m_pClaFile -> ClaFile struct has rec_buf (SByte*) and rec_len (Int32)
        // fields with sequential managed layout matching native, so
        // Marshal.OffsetOf gives us the right addresses.
        static unsafe bool ResolveNativeRecordBuffer(object cfile, Type cfileBase, out IntPtr recBufPtr, out int recLen, ReadResult r)
        {
            recBufPtr = IntPtr.Zero; recLen = 0;
            var mPClaFile = cfileBase.GetField("m_pClaFile", BindingFlags.NonPublic | BindingFlags.Instance);
            if (mPClaFile == null) { r.Log.Add("m_pClaFile not found"); return false; }
            var val = mPClaFile.GetValue(cfile);
            if (!(val is Pointer)) { r.Log.Add("m_pClaFile not a Pointer"); return false; }
            IntPtr claFilePtr = (IntPtr)Pointer.Unbox(val);
            if (claFilePtr == IntPtr.Zero) { r.Log.Add("m_pClaFile is null"); return false; }

            var claFileType = mPClaFile.FieldType.GetElementType();
            if (claFileType == null) { r.Log.Add("ClaFile element type unresolvable"); return false; }

            int recBufOff, recLenOff;
            try
            {
                recBufOff = Marshal.OffsetOf(claFileType, "rec_buf").ToInt32();
                recLenOff = Marshal.OffsetOf(claFileType, "rec_len").ToInt32();
            }
            catch (Exception ex)
            {
                r.Log.Add("Marshal.OffsetOf ClaFile fields failed: " + ex.Message);
                return false;
            }

            recLen    = Marshal.ReadInt32(claFilePtr, recLenOff);
            recBufPtr = Marshal.ReadIntPtr(claFilePtr, recBufOff);
            return true;
        }

        // ---- ClaString wrapping ---------------------------------------------

        static object WrapClaString(Type claStringType, string value, ReadResult r)
        {
            if (claStringType == null) return value;
            value = value ?? "";

            var cs = claStringType.GetConstructor(new[] { typeof(string) });
            if (cs != null)
            {
                try { return cs.Invoke(new object[] { value }); }
                catch { }
            }
            var op = claStringType.GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (op != null)
            {
                try { return op.Invoke(null, new object[] { value }); }
                catch { }
            }
            var cb = claStringType.GetConstructor(new[] { typeof(byte[]) });
            if (cb != null)
            {
                try { return cb.Invoke(new object[] { Encoding.GetEncoding(1252).GetBytes(value) }); }
                catch { }
            }
            r.Log.Add("WrapClaString: all strategies exhausted for \"" + value + "\"");
            return null;
        }

        // ---- Misc method dispatch ------------------------------------------

        static MethodInfo FindMethod(Type t, string name, Type[] args)
        {
            if (t == null) return null;
            return t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, args, null);
        }

        static void Invoke(MethodInfo m, object target)
        {
            if (m == null) return;
            var ps = m.GetParameters();
            var args = new object[ps.Length];
            for (int i = 0; i < args.Length; i++)
                args[i] = ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
            m.Invoke(target, args);
        }

        static void InvokeOpen(MethodInfo m, object target, ReadResult r)
        {
            if (m == null) return;
            var ps = m.GetParameters();
            if (ps.Length == 0) { m.Invoke(target, null); return; }
            var args = new object[ps.Length];
            for (int i = 0; i < args.Length; i++)
            {
                var pt = ps[i].ParameterType;
                if (pt == typeof(int)   || pt == typeof(uint))  args[i] = CLARION_OPEN_MODE;
                else if (pt == typeof(short) || pt == typeof(ushort)) args[i] = (short)CLARION_OPEN_MODE;
                else if (pt == typeof(byte)) args[i] = (byte)CLARION_OPEN_MODE;
                else args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
            }
            m.Invoke(target, args);
            r.Log.Add("Open(" + CLARION_OPEN_MODE.ToString("X") + "h)");
        }

        // Call a no-arg instance method that returns an int; return 0 on
        // any reflection / runtime failure. Used for Bytes() / Records() /
        // Status() probes after Open.
        static int SafeIntCall(Type t, object target, string name)
        {
            try
            {
                var m = t.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (m == null) return 0;
                var ret = m.Invoke(target, null);
                if (ret == null) return 0;
                try { return Convert.ToInt32(ret); } catch { return 0; }
            }
            catch { return 0; }
        }
    }
}
