using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ClarionDctAddin
{
    // Reflection-based batch copy of DDField instances between DDFile tables
    // in an open Clarion dictionary. Uses the Clarion-supplied
    //   DDField.Copy(DataDictionaryItem parent, bool quiet) -> DataDictionaryItem
    //   DDFile.AddField(DDField)
    // so validation and change-tracking run through Clarion's own paths.
    internal static class FieldCopier
    {
        public enum ConflictMode { Skip, Abort }
        public enum PlanAction   { Add, Skip, Conflict }

        public sealed class PlanItem
        {
            public object      SourceField;
            public object      TargetTable;
            public string      FieldLabel;
            public string      SourceTableName;
            public string      TargetTableName;
            public string      DataType;
            public string      FieldSize;
            public PlanAction  Action;
            public string      Reason;
        }

        public sealed class ApplyResult
        {
            public int          AddedCount;
            public int          SkippedCount;
            public int          FailedCount;
            public List<string> Messages  = new List<string>();
            public string       BackupPath;
            public bool         BackupFailed;
        }

        public static List<PlanItem> BuildPlan(
            object sourceTable,
            IList<object> selectedSourceFields,
            IList<object> targetTables,
            ConflictMode mode)
        {
            var plan = new List<PlanItem>();
            var sourceName = DictModel.AsString(DictModel.GetProp(sourceTable, "Name")) ?? "";

            foreach (var target in targetTables)
            {
                var targetName = DictModel.AsString(DictModel.GetProp(target, "Name")) ?? "";
                foreach (var field in selectedSourceFields)
                {
                    var item = new PlanItem
                    {
                        SourceField     = field,
                        TargetTable     = target,
                        SourceTableName = sourceName,
                        TargetTableName = targetName,
                        FieldLabel      = DictModel.AsString(DictModel.GetProp(field, "Label")) ?? "",
                        DataType        = DictModel.AsString(DictModel.GetProp(field, "DataType")) ?? "",
                        FieldSize       = DictModel.AsString(DictModel.GetProp(field, "FieldSize")) ?? ""
                    };

                    var existing = FindFieldByLabel(target, item.FieldLabel);
                    if (existing != null)
                    {
                        item.Action = mode == ConflictMode.Skip ? PlanAction.Skip : PlanAction.Conflict;
                        item.Reason = "target already has a field labeled '" + item.FieldLabel + "'";
                    }
                    else
                    {
                        item.Action = PlanAction.Add;
                    }
                    plan.Add(item);
                }
            }
            return plan;
        }

        public static string MakeBackupPath(string dctPath)
        {
            return dctPath + ".tasker-bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        }

        static bool CollectionInternalsDumped;

        public static ApplyResult Apply(List<PlanItem> plan, object dict, object viewContent, string dctPath)
        {
            CollectionInternalsDumped = false;
            var result = new ApplyResult();

            if (!string.IsNullOrEmpty(dctPath) && File.Exists(dctPath))
            {
                try
                {
                    result.BackupPath = MakeBackupPath(dctPath);
                    File.Copy(dctPath, result.BackupPath, false);
                }
                catch (Exception ex)
                {
                    result.BackupFailed = true;
                    result.Messages.Add("Backup failed (" + ex.GetType().Name + "): " + ex.Message + ". Aborting batch.");
                    result.FailedCount = plan.Count(p => p.Action == PlanAction.Add);
                    return result;
                }
            }

            // If any item is a hard conflict (Abort mode), don't mutate at all.
            if (plan.Any(p => p.Action == PlanAction.Conflict))
            {
                foreach (var p in plan)
                {
                    if (p.Action == PlanAction.Conflict)
                        result.Messages.Add(p.TargetTableName + "." + p.FieldLabel + ": CONFLICT - " + p.Reason);
                }
                result.FailedCount = plan.Count;
                return result;
            }

            foreach (var item in plan)
            {
                if (item.Action == PlanAction.Skip) { result.SkippedCount++; continue; }

                var tag = item.TargetTableName + "." + item.FieldLabel;
                try
                {
                    CopyAndAdd(item.SourceField, item.TargetTable, result, tag);
                    result.AddedCount++;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    var inner = ex.InnerException ?? ex;
                    result.Messages.Add(tag + ": " + inner.GetType().Name + " - " + inner.Message);
                }
            }

            if (result.AddedCount > 0)
            {
                ForceMarkDirty(dict, viewContent, result);
            }
            return result;
        }

        // The editor's Save button is driven by IViewContent.IsDirty (from SharpDevelop's
        // ICanBeDirty). Setting it straight through on the DataDictionaryViewContent is
        // the most reliable way to tell Clarion "yes, there are unsaved changes, enable Save."
        // We also try a handful of model-side mark-dirty paths so the dictionary's own
        // persistence layer treats the new fields as pending-save rather than phantoms.
        static void ForceMarkDirty(object dict, object viewContent, ApplyResult result)
        {
            var log = new List<string>();

            if (TrySetBool(dict, "IsDirty", true, log, "dict.IsDirty")
                || TrySetField(dict, "isDirty", true, log, "dict.isDirty"))
            { /* logged inside */ }
            TryInvokeNoArgs(dict, "DoIsDirtyChanged", true, log, "dict.DoIsDirtyChanged");
            TryInvokeNoArgs(dict, "ChildListTouched", true, log, "dict.ChildListTouched");

            if (viewContent != null)
            {
                if (!TrySetBool(viewContent, "IsDirty", true, log, "view.IsDirty"))
                    TrySetField(viewContent, "isDirty", true, log, "view.isDirty");
                // SharpDevelop AbstractViewContent has OnIsDirtyChanged() which fires
                // IsDirtyChanged so the workbench's Save menu updates.
                TryInvokeNoArgs(viewContent, "OnIsDirtyChanged", true, log, "view.OnIsDirtyChanged");
            }

            if (log.Count > 0)
            {
                result.Messages.Add("Mark-dirty paths tried: " + string.Join(", ", log.ToArray()));
            }
        }

        static bool TrySetBool(object target, string propName, bool value, List<string> log, string tag)
        {
            if (target == null) return false;
            var p = target.GetType().GetProperty(propName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p == null || !p.CanWrite || p.PropertyType != typeof(bool)) return false;
            try { p.SetValue(target, value, null); log.Add(tag + "=OK"); return true; }
            catch (Exception ex) { log.Add(tag + "=ERR(" + ex.GetType().Name + ")"); return false; }
        }

        static bool TrySetField(object target, string fieldName, bool value, List<string> log, string tag)
        {
            if (target == null) return false;
            var f = target.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null || f.FieldType != typeof(bool)) return false;
            try { f.SetValue(target, value); log.Add(tag + "=OK"); return true; }
            catch (Exception ex) { log.Add(tag + "=ERR(" + ex.GetType().Name + ")"); return false; }
        }

        static void TryInvokeNoArgs(object target, string methodName, bool includeNonPublic,
                                    List<string> log, string tag)
        {
            if (target == null) return;
            var flags = BindingFlags.Public | BindingFlags.Instance;
            if (includeNonPublic) flags |= BindingFlags.NonPublic;
            var m = target.GetType().GetMethod(methodName, flags, null, Type.EmptyTypes, null);
            if (m == null) { log.Add(tag + "=missing"); return; }
            try { m.Invoke(target, null); log.Add(tag + "=OK"); }
            catch (Exception ex) { log.Add(tag + "=ERR(" + (ex.InnerException ?? ex).GetType().Name + ")"); }
        }

        static void CopyAndAdd(object sourceField, object targetTable, ApplyResult result, string tag)
        {
            var steps = new List<string>();

            int countBefore = CountEnumerable(DictModel.GetProp(targetTable, "Fields"));
            steps.Add("n0=" + countBefore);

            // 1. Clone. quiet=false so ItemAdded events fire for subscribers like
            //    the persistence-tracker UniqueDataDictionaryItemList<T>.
            var copyMethod = sourceField.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m =>
                    m.Name == "Copy"
                    && m.GetParameters().Length == 2
                    && m.GetParameters()[1].ParameterType == typeof(bool));
            if (copyMethod == null)
                throw new InvalidOperationException("Cannot find Copy(parent, bool) on " + sourceField.GetType().FullName);

            object newField;
            try { newField = copyMethod.Invoke(sourceField, new object[] { targetTable, false }); }
            catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
            if (newField == null) throw new InvalidOperationException("Copy returned null.");
            steps.Add("copy");

            // 2. Did Copy already register the field in the target's Fields list?
            bool alreadyIn = false;
            var targetFields = DictModel.GetProp(targetTable, "Fields") as IEnumerable;
            if (targetFields != null)
                foreach (var f in targetFields)
                    if (ReferenceEquals(f, newField)) { alreadyIn = true; break; }

            if (alreadyIn)
            {
                steps.Add("preReg");
            }
            else
            {
                // 3. Call fieldsCollection.Add(newField) directly. This is the method
                //    Clarion's editor invokes internally — it updates both the items
                //    GuidKeyedCollection and the addedItems Dictionary<Guid,DDField>
                //    (the persistence tracker) in one shot. DDFile.InsertField and
                //    DDFile.AddField turned out to be silent no-ops in this build.
                var fieldsCollection = DictModel.GetProp(targetTable, "Fields");
                if (fieldsCollection == null)
                    throw new InvalidOperationException("Target table has no Fields collection.");

                var collAdd = fieldsCollection.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Add"
                                      && m.GetParameters().Length == 1
                                      && m.GetParameters()[0].ParameterType == newField.GetType());
                if (collAdd == null)
                    throw new InvalidOperationException("Fields collection has no Add(" + newField.GetType().Name + ").");

                try { collAdd.Invoke(fieldsCollection, new object[] { newField }); steps.Add("coll.Add"); }
                catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
            }

            // 4. Post-add verification: count, presence, and addedItems tracker.
            var targetFieldsCollection = DictModel.GetProp(targetTable, "Fields");
            int countAfter = CountEnumerable(targetFieldsCollection);
            bool nowIn = false;
            var fieldsAfter = targetFieldsCollection as IEnumerable;
            if (fieldsAfter != null)
                foreach (var f in fieldsAfter) if (ReferenceEquals(f, newField)) { nowIn = true; break; }
            steps.Add("n1=" + countAfter);
            steps.Add("inFields=" + nowIn);

            // The persistence tracker is the inherited addedItems Dictionary<Guid,DDField>.
            // Report whether our field made it in.
            var addedDictObj = GetNonPublicMember(targetFieldsCollection, "addedItems");
            var addedDict = addedDictObj as IDictionary;
            if (addedDict != null)
            {
                bool inTracker = false;
                foreach (var v in addedDict.Values) if (ReferenceEquals(v, newField)) { inTracker = true; break; }
                steps.Add("addedItems[" + addedDict.Count + "]" + (inTracker ? ":IN" : ":OUT"));
            }

            // 5. SetInFile flips IsInFile=true so the save code treats it as persistent.
            if (TryInvokeNoArgs(newField, "SetInFile", true)) steps.Add("SetInFile");

            // 6. ChildListTouched on file + dict so both know their child list changed.
            if (TryInvokeNoArgs(targetTable, "ChildListTouched", true)) steps.Add("file.ChildListTouched");
            var dict = DictModel.GetProp(targetTable, "DataDictionary");
            if (TryInvokeNoArgs(dict, "ChildListTouched", true)) steps.Add("dict.ChildListTouched");

            // 7. Diagnostic — dump UniqueDataDictionaryItemList<T> non-public state once
            //    so we can see the real name of the persistence tracker.
            if (!CollectionInternalsDumped && targetFieldsCollection != null)
            {
                CollectionInternalsDumped = true;
                DumpNonPublicState(targetFieldsCollection, result.Messages, "Fields-collection ");
            }

            // 8. Final state of the new field.
            var isInFile = DictModel.AsString(DictModel.GetProp(newField, "IsInFile")) ?? "?";
            var touched  = DictModel.AsString(DictModel.GetProp(newField, "Touched"))  ?? "?";
            steps.Add("IsInFile=" + isInFile);
            steps.Add("Touched=" + touched);

            result.Messages.Add(tag + " : " + string.Join(" > ", steps.ToArray()));
        }

        static int CountEnumerable(object maybeEnum)
        {
            var en = maybeEnum as IEnumerable;
            if (en == null) return -1;
            int n = 0;
            foreach (var _ in en) n++;
            return n;
        }

        static void DumpNonPublicState(object o, List<string> msgs, string prefix)
        {
            if (o == null) return;
            var t = o.GetType();
            while (t != null && t != typeof(object))
            {
                foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                   .Where(x => !x.Name.EndsWith("__BackingField", StringComparison.Ordinal)))
                {
                    object v; string vs;
                    try { v = f.GetValue(o); }
                    catch (Exception ex) { vs = "<ex:" + ex.GetType().Name + ">"; msgs.Add(prefix + t.Name + "." + f.Name + " = " + vs); continue; }
                    if (v == null) vs = "null";
                    else if (v is string) vs = "\"" + v + "\"";
                    else if (v is IEnumerable && !(v is string))
                    {
                        int c = 0; try { foreach (var _ in (IEnumerable)v) c++; } catch { }
                        vs = "IEnumerable[" + c + "]  (" + v.GetType().Name + ")";
                    }
                    else vs = v.ToString();
                    if (vs.Length > 90) vs = vs.Substring(0, 90) + "...";
                    msgs.Add(prefix + t.Name + "." + f.Name + " : " + FormatType(f.FieldType) + " = " + vs);
                }
                t = t.BaseType;
            }
        }

        static string FormatType(Type t)
        {
            if (t == null) return "?";
            if (!t.IsGenericType) return t.Name;
            var root = t.Name; var i = root.IndexOf('`'); if (i > 0) root = root.Substring(0, i);
            return root + "<" + string.Join(",", t.GetGenericArguments().Select(x => x.Name).ToArray()) + ">";
        }

#pragma warning disable CS0219 // keep shim helpers even if temporarily unused
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
            var f = target.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null || f.FieldType != typeof(bool)) return false;
            try { f.SetValue(target, value); return true; } catch { return false; }
        }

        static object GetNonPublicMember(object target, string name)
        {
            if (target == null) return null;
            var t = target.GetType();
            var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (f != null) { try { return f.GetValue(target); } catch { } }
            var p = t.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanRead) { try { return p.GetValue(target, null); } catch { } }
            // Walk base types for inherited non-public members.
            var bt = t.BaseType;
            while (bt != null && bt != typeof(object))
            {
                f = bt.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (f != null) { try { return f.GetValue(target); } catch { } }
                p = bt.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.CanRead) { try { return p.GetValue(target, null); } catch { } }
                bt = bt.BaseType;
            }
            return null;
        }

        static bool TryInvokeNoArgs(object target, string methodName, bool includeNonPublic)
        {
            if (target == null) return false;
            var flags = BindingFlags.Public | BindingFlags.Instance;
            if (includeNonPublic) flags |= BindingFlags.NonPublic;
            var m = target.GetType().GetMethod(methodName, flags, null, Type.EmptyTypes, null);
            if (m == null) return false;
            try { m.Invoke(target, null); return true; } catch { return false; }
        }

        static object FindFieldByLabel(object table, string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            var fields = DictModel.GetProp(table, "Fields") as IEnumerable;
            if (fields == null) return null;
            foreach (var f in fields)
            {
                var l = DictModel.AsString(DictModel.GetProp(f, "Label"));
                if (string.Equals(l, label, StringComparison.OrdinalIgnoreCase)) return f;
            }
            return null;
        }
    }
}
