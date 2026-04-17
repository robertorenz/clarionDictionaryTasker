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

        public static ApplyResult Apply(List<PlanItem> plan, string dctPath)
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
            return result;
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

            object newField;
            try { newField = copyMethod.Invoke(sourceField, new object[] { targetTable, true }); }
            catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
            if (newField == null) throw new InvalidOperationException("Copy returned null.");

            // Copy may already have registered the field on the parent — check.
            var targetFields = DictModel.GetProp(targetTable, "Fields") as IEnumerable;
            if (targetFields != null)
            {
                foreach (var f in targetFields)
                    if (ReferenceEquals(f, newField)) return;
            }

            var addMethod = targetTable.GetType()
                .GetMethod("AddField", BindingFlags.Public | BindingFlags.Instance);
            if (addMethod == null)
                throw new InvalidOperationException("Cannot find AddField on " + targetTable.GetType().FullName);

            try { addMethod.Invoke(targetTable, new object[] { newField }); }
            catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
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
