using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ClarionDctAddin
{
    // Shared helpers for in-place field mutations (rename, retype, edit
    // description/heading/prompt). The pattern mirrors FieldCopier's add path
    // but is tailored to property changes rather than inserts:
    //   1. back up the .DCT first
    //   2. set the property via public setter or non-public backing field
    //   3. flip itemHasChanged / stored / Touched on the field
    //   4. ChildListTouched on table + dict
    //   5. mark view dirty so Clarion's Save button activates
    internal static class FieldMutator
    {
        public sealed class Result
        {
            public int Changed;
            public int Failed;
            public List<string> Messages = new List<string>();
            public string BackupPath;
            public bool   BackupFailed;
        }

        public static string Backup(string dctPath, Result r)
        {
            if (string.IsNullOrEmpty(dctPath) || !File.Exists(dctPath)) return null;
            try
            {
                var path = dctPath + ".tasker-bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(dctPath, path, false);
                r.BackupPath = path;
                return path;
            }
            catch (Exception ex)
            {
                r.BackupFailed = true;
                r.Messages.Add("Backup failed: " + ex.GetType().Name + " - " + ex.Message);
                return null;
            }
        }

        public static bool SetStringProp(object target, string propName, string value, Result r, string tag)
        {
            if (target == null) return false;
            var t = target.GetType();
            var p = t.GetProperty(propName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                string how;
                if (p != null && p.CanWrite)
                {
                    p.SetValue(target, value, null);
                    how = "setter";
                }
                else if (SetBackingStringField(target, propName, value))
                {
                    how = "backing-field";
                }
                else
                {
                    r.Messages.Add(tag + ": no writable path for " + propName);
                    return false;
                }
                NotifyFieldUpdated(target, r, tag, how);
                return true;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                r.Messages.Add(tag + ": set " + propName + " failed - " + inner.GetType().Name + " " + inner.Message);
                return false;
            }
        }

        static bool SetBackingStringField(object target, string propName, string value)
        {
            // Typical SoftVelocity convention: PublicProperty <-> privateField (camelCase)
            var candidates = new[]
            {
                char.ToLowerInvariant(propName[0]) + (propName.Length > 1 ? propName.Substring(1) : ""),
                "_" + propName,
                "_" + char.ToLowerInvariant(propName[0]) + (propName.Length > 1 ? propName.Substring(1) : ""),
                "m_" + propName,
                propName
            };
            var t = target.GetType();
            while (t != null && t != typeof(object))
            {
                foreach (var name in candidates)
                {
                    var f = t.GetField(name,
                        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (f != null && f.FieldType == typeof(string))
                    {
                        try { f.SetValue(target, value); return true; } catch { }
                    }
                }
                t = t.BaseType;
            }
            return false;
        }

        public static void TouchField(object field)
        {
            // Per-field state that the native TPS save path looks at.
            TrySetBoolField(field, "itemHasChanged", true);
            TrySetBoolField(field, "stored", true);
            TrySetBoolProp(field,  "Touched", true);
            TryInvokeNoArgs(field, "SetInFile");
        }

        // Fire every notification + tracker-dict entry we know about so the
        // save pipeline treats this field as a modified record, not a phantom.
        // Writes a diagnostic line to r.Messages showing which of the probes
        // hit — so if a given Clarion build wires things differently, we can
        // see what succeeded in the Fix-fields result output.
        static void NotifyFieldUpdated(object field, Result r, string tag, string setVia)
        {
            var steps = new List<string> { "set=" + setVia };

            // Per-field flags + notify methods.
            if (TrySetBoolField(field, "itemHasChanged", true)) steps.Add("item=chg");
            if (TrySetBoolField(field, "stored", true))         steps.Add("stored");
            if (TrySetBoolProp(field,  "Touched", true))        steps.Add("Touched");
            if (TryInvokeNoArgs(field, "SetInFile"))            steps.Add("SetInFile");
            if (TryInvokeNoArgs(field, "Touch"))                steps.Add("Touch");
            if (TryInvokeNoArgs(field, "DoUpdate"))             steps.Add("DoUpdate");
            if (TryInvokeNoArgs(field, "NotifyUpdate"))         steps.Add("NotifyUpdate");
            if (TryInvokeNoArgs(field, "NotifyChanged"))        steps.Add("NotifyChanged");
            if (TryInvokeNoArgs(field, "MarkChanged"))          steps.Add("MarkChanged");

            // Parent file + dict notifications.
            var parent = FindFirstNonNullProp(field, "File", "ParentFile", "Parent", "DDFile");
            if (parent != null)
            {
                if (TryInvokeNoArgs(parent, "ChildListTouched")) steps.Add("file.ChildTouched");
                if (TryInvokeOneArg(parent, "FieldChanged", field)) steps.Add("file.FieldChanged");
                if (TryInvokeOneArg(parent, "ChildTouched", field)) steps.Add("file.ChildTouched(item)");

                // Find whichever collection on the parent actually contains this item
                // — Fields for a DDField, Keys for a DDKey, Relations for a DDRelation,
                // and so on. The change-tracker dictionary lives on that collection.
                string ownerKind;
                var owningColl = FindOwningCollection(parent, field, out ownerKind);
                if (owningColl != null)
                {
                    steps.Add("owner=" + ownerKind);
                    if (RegisterInChangeTracker(owningColl, field, out var trackerName))
                        steps.Add("coll." + trackerName);
                }
            }

            var dict = DictModel.GetProp(parent, "DataDictionary") ?? DictModel.GetProp(field, "DataDictionary");
            if (dict != null && TryInvokeNoArgs(dict, "ChildListTouched")) steps.Add("dict.ChildTouched");

            r.Messages.Add(tag + " -> " + string.Join(", ", steps.ToArray()));
        }

        // Which collection on `parent` actually contains `item`? Tries the common
        // collection names in order and checks each for a reference-equal match.
        static object FindOwningCollection(object parent, object item, out string name)
        {
            name = null;
            if (parent == null || item == null) return null;
            string[] candidates = { "Fields", "Keys", "Relations", "Triggers", "Aliases", "Components" };
            foreach (var n in candidates)
            {
                var coll = DictModel.GetProp(parent, n);
                if (coll == null) continue;
                var en = coll as IEnumerable;
                if (en == null) continue;
                try
                {
                    foreach (var x in en)
                        if (ReferenceEquals(x, item)) { name = n; return coll; }
                }
                catch { }
            }
            return null;
        }

        static object FindFirstNonNullProp(object src, params string[] names)
        {
            if (src == null) return null;
            foreach (var n in names)
            {
                var v = DictModel.GetProp(src, n);
                if (v != null) return v;
            }
            return null;
        }

        // Look for a change-tracker dictionary on the Fields collection (non-public
        // field whose name matches known patterns) and register the field there so
        // the save pipeline sees a modified record. Clarion new-field inserts route
        // through "addedItems"; modifications are expected to land in a similar
        // collection.
        static bool RegisterInChangeTracker(object collection, object field, out string foundName)
        {
            foundName = null;
            string[] candidates = { "modifiedItems", "changedItems", "updatedItems", "dirtyItems",
                                    "modified",      "changed",      "updated",      "dirty",
                                    "addedItems" };
            var t = collection.GetType();
            while (t != null && t != typeof(object))
            {
                foreach (var name in candidates)
                {
                    var f = t.GetField(name,
                        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (f == null) continue;
                    object tracker;
                    try { tracker = f.GetValue(collection); } catch { continue; }
                    if (tracker == null) continue;
                    if (AddToTracker(tracker, field))
                    {
                        foundName = name;
                        return true;
                    }
                }
                t = t.BaseType;
            }
            return false;
        }

        static bool AddToTracker(object tracker, object field)
        {
            var dict = tracker as IDictionary;
            if (dict != null)
            {
                var id = DictModel.GetProp(field, "Id");
                if (id != null && !dict.Contains(id))
                {
                    try { dict.Add(id, field); return true; } catch { }
                }
                return false;
            }
            // Fallback: List<T>.Add(field)
            var add = tracker.GetType().GetMethod("Add",
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { field.GetType() }, null);
            if (add != null)
            {
                try { add.Invoke(tracker, new[] { field }); return true; } catch { }
            }
            return false;
        }

        static bool TryInvokeOneArg(object target, string methodName, object arg)
        {
            if (target == null || arg == null) return false;
            var m = target.GetType().GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { arg.GetType() }, null);
            if (m == null) return false;
            try { m.Invoke(target, new[] { arg }); return true; } catch { return false; }
        }

        public static void ForceMarkDirty(object dict, object viewContent, Result r)
        {
            TrySetBoolProp(dict,  "IsDirty", true);
            TrySetBoolField(dict, "isDirty", true);
            TryInvokeNoArgs(dict, "ChildListTouched");
            TryInvokeNoArgs(dict, "DoIsDirtyChanged");
            if (viewContent != null)
            {
                if (!TrySetBoolProp(viewContent, "IsDirty", true))
                    TrySetBoolField(viewContent, "isDirty", true);
                TryInvokeNoArgs(viewContent, "OnIsDirtyChanged");
            }
            r.Messages.Add("Dict + view marked dirty.");
        }

        static bool TrySetBoolProp(object target, string name, bool value)
        {
            if (target == null) return false;
            var p = target.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p == null || !p.CanWrite || p.PropertyType != typeof(bool)) return false;
            try { p.SetValue(target, value, null); return true; } catch { return false; }
        }

        static bool TrySetBoolField(object target, string name, bool value)
        {
            if (target == null) return false;
            var t = target.GetType();
            while (t != null && t != typeof(object))
            {
                var f = t.GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (f != null && f.FieldType == typeof(bool))
                {
                    try { f.SetValue(target, value); return true; } catch { return false; }
                }
                t = t.BaseType;
            }
            return false;
        }

        static bool TryInvokeNoArgs(object target, string methodName)
        {
            if (target == null) return false;
            var m = target.GetType().GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (m == null) return false;
            try { m.Invoke(target, null); return true; } catch { return false; }
        }

        public static IEnumerable<object> EnumerateFields(object table)
        {
            var en = DictModel.GetProp(table, "Fields") as IEnumerable;
            if (en == null) yield break;
            foreach (var f in en) if (f != null) yield return f;
        }

        public static IEnumerable<object> EnumerateTriggers(object table)
        {
            var en = DictModel.GetProp(table, "Triggers") as IEnumerable;
            if (en == null) yield break;
            foreach (var t in en) if (t != null) yield return t;
        }

        public static string GetTriggerBody(object trigger)
        {
            return DictModel.AsString(DictModel.GetProp(trigger, "Body"))
                ?? DictModel.AsString(DictModel.GetProp(trigger, "Code"))
                ?? DictModel.AsString(DictModel.GetProp(trigger, "Source"))
                ?? DictModel.AsString(DictModel.GetProp(trigger, "TriggerCode"))
                ?? "";
        }
    }
}
