using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ICSharpCode.SharpDevelop.Gui;

namespace ClarionDctAddin
{
    // Reflection-based accessor for the running Clarion DataDictionary model.
    // No compile-time reference to SoftVelocity assemblies so the add-in stays
    // portable across Clarion 12.x builds that may rev the internal types.
    internal static class DictModel
    {
        public static object GetActiveDictionaryView()
        {
            try
            {
                var active = WorkbenchSingleton.Workbench.ActiveContent;
                if (active == null) return null;
                if (active.GetType().FullName != "SoftVelocity.DataDictionary.Editor.DataDictionaryViewContent") return null;
                return active;
            }
            catch { return null; }
        }

        public static bool TryGetOpenDictionary(out object dict, out string error)
        {
            dict = null;
            error = null;
            try
            {
                var active = WorkbenchSingleton.Workbench.ActiveContent;
                if (active == null)
                {
                    error = "No active window. Open a dictionary first.";
                    return false;
                }
                if (active.GetType().FullName != "SoftVelocity.DataDictionary.Editor.DataDictionaryViewContent")
                {
                    error = "The active window is not a dictionary.\r\nSwitch to an open .DCT tab and try again.";
                    return false;
                }

                var control = GetProp(active, "Control");
                if (control == null) { error = "Dictionary view has no Control."; return false; }
                dict = GetProp(control, "DCT");
                if (dict == null) { error = "Dictionary model not available."; return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed to access dictionary: " + ex.Message;
                return false;
            }
        }

        // Enumerates every Clarion dictionary currently open in the workbench (across
        // all tabs, not just the active one). Clarion 12 can hold multiple .DCTs open
        // simultaneously, which is what makes live-vs-live comparison possible.
        public static IList<object> GetAllOpenDictionaries()
        {
            var list = new List<object>();
            try
            {
                var wb = WorkbenchSingleton.Workbench;
                if (wb == null) return list;
                var vcs = wb.ViewContentCollection as IEnumerable;
                if (vcs == null) return list;
                foreach (var vc in vcs)
                {
                    if (vc == null) continue;
                    if (vc.GetType().FullName != "SoftVelocity.DataDictionary.Editor.DataDictionaryViewContent") continue;
                    var control = GetProp(vc, "Control");
                    if (control == null) continue;
                    var dict = GetProp(control, "DCT");
                    if (dict == null) continue;
                    list.Add(dict);
                }
            }
            catch { }
            return list;
        }

        // Returns every DDFile in the dictionary — base tables AND aliases,
        // flattened into one list. Clarion stores aliases under each base
        // table's .Aliases sub-collection (and sometimes also exposes a
        // top-level dict.Aliases); both paths are merged here, de-duped by
        // reference so an alias listed in both places doesn't appear twice.
        public static IList<object> GetTables(object dict)
        {
            var list = new List<object>();
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

            void Add(object t)
            {
                if (t == null) return;
                if (!seen.Add(t)) return;
                list.Add(t);
            }

            var coll = GetProp(dict, "Tables") as IEnumerable;
            if (coll != null)
            {
                foreach (var o in coll)
                {
                    Add(o);
                    // Each base table can carry its own .Aliases collection —
                    // that's where the alias DDFile instances actually live.
                    var aliases = GetProp(o, "Aliases") as IEnumerable;
                    if (aliases != null)
                        foreach (var a in aliases) Add(a);
                }
            }

            // Belt-and-braces: some Clarion builds also surface aliases at the
            // dictionary level. Harmless to merge since the hash-set de-dupes.
            var topAliases = GetProp(dict, "Aliases") as IEnumerable;
            if (topAliases != null)
                foreach (var a in topAliases) Add(a);

            return list;
        }

        sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object x, object y) { return ReferenceEquals(x, y); }
            public int GetHashCode(object obj) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj); }
        }

        public static string GetDictionaryName(object dict)
        {
            return AsString(GetProp(dict, "Name")) ?? AsString(GetProp(dict, "UniqueName")) ?? "dictionary";
        }

        public static string GetDictionaryFileName(object dict)
        {
            return AsString(GetProp(dict, "FileName")) ?? "";
        }

        // --- tiny reflection helpers used elsewhere too ---
        public static object GetProp(object o, string name)
        {
            if (o == null) return null;
            var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanRead || p.GetIndexParameters().Length > 0) return null;
            try { return p.GetValue(o, null); } catch { return null; }
        }

        public static string AsString(object v) { return v == null ? null : v.ToString(); }

        public static int CountEnumerable(object o, string prop)
        {
            var v = GetProp(o, prop) as IEnumerable;
            if (v == null) return 0;
            int n = 0;
            foreach (var _ in v) n++;
            return n;
        }
    }
}
