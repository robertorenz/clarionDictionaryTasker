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

        public static ApplyResult Apply(List<PlanItem> plan, object dict, object viewContent, string dctPath)
        {
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

                try
                {
                    CopyAndAdd(item.SourceField, item.TargetTable);
                    result.AddedCount++;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    var inner = ex.InnerException ?? ex;
                    result.Messages.Add(item.TargetTableName + "." + item.FieldLabel + ": "
                        + inner.GetType().Name + " - " + inner.Message);
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

        static void CopyAndAdd(object sourceField, object targetTable)
        {
            // DDField inherits Copy(DataDictionaryItem parent, bool quiet) : DataDictionaryItem
            var copyMethod = sourceField.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m =>
                    m.Name == "Copy"
                    && m.GetParameters().Length == 2
                    && m.GetParameters()[1].ParameterType == typeof(bool));
            if (copyMethod == null)
                throw new InvalidOperationException("Cannot find Copy(parent, bool) on " + sourceField.GetType().FullName);

            // quiet = false: the UniqueDataDictionaryItemList<T> relies on the ItemAdded
            // event to register a field in its "added items" list. That list is what
            // Clarion's save code iterates over. Quiet mode suppresses that event, so
            // the field never reaches disk even though it's visible in the editor.
            object newField;
            try { newField = copyMethod.Invoke(sourceField, new object[] { targetTable, false }); }
            catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
            if (newField == null) throw new InvalidOperationException("Copy returned null.");

            // Copy may already have registered the field on the parent — check before re-adding.
            bool alreadyIn = false;
            var targetFields = DictModel.GetProp(targetTable, "Fields") as IEnumerable;
            if (targetFields != null)
            {
                foreach (var f in targetFields)
                    if (ReferenceEquals(f, newField)) { alreadyIn = true; break; }
            }

            if (!alreadyIn)
            {
                var addMethod = targetTable.GetType()
                    .GetMethod("AddField", BindingFlags.Public | BindingFlags.Instance);
                if (addMethod == null)
                    throw new InvalidOperationException("Cannot find AddField on " + targetTable.GetType().FullName);
                try { addMethod.Invoke(targetTable, new object[] { newField }); }
                catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
            }

            // Post-insert housekeeping so the field actually persists:
            //   SetInFile()        on DDField — flips IsInFile=true
            //   ChildListTouched() on the DDFile and DDDataDictionary — marks dirty for save
            // All internal methods — best-effort and non-fatal.
            TryInvokeNoArgs(newField,    "SetInFile",         true);
            TryInvokeNoArgs(targetTable, "ChildListTouched",  true);
            var dict = DictModel.GetProp(targetTable, "DataDictionary");
            TryInvokeNoArgs(dict,        "ChildListTouched",  true);
        }

        static void TryInvokeNoArgs(object target, string methodName, bool includeNonPublic)
        {
            if (target == null) return;
            var flags = BindingFlags.Public | BindingFlags.Instance;
            if (includeNonPublic) flags |= BindingFlags.NonPublic;
            var m = target.GetType().GetMethod(methodName, flags, null, Type.EmptyTypes, null);
            if (m == null) return;
            try { m.Invoke(target, null); } catch { /* diagnostic-only */ }
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
