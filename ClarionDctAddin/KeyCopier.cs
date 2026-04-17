using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ClarionDctAddin
{
    // Batch copy of DDKeys between DDFile tables. Unlike fields, a key contains
    // references to specific fields in its parent table. When copying to a new
    // table, those component references have to be remapped to fields with the
    // same Label in the target, or the copy has to be skipped when any
    // referenced field is missing from the target.
    internal static class KeyCopier
    {
        public enum ConflictMode { Skip, Abort }
        public enum PlanAction   { Add, Skip, NameConflict, MissingFields }

        public sealed class PlanItem
        {
            public object       SourceKey;
            public object       TargetTable;
            public string       KeyName;
            public string       SourceTableName;
            public string       TargetTableName;
            public List<string> ComponentLabels    = new List<string>();
            public List<string> MissingFieldLabels = new List<string>();
            public PlanAction   Action;
            public string       Reason;
        }

        public sealed class ApplyResult
        {
            public int          AddedCount;
            public int          SkippedCount;
            public int          FailedCount;
            public List<string> Messages   = new List<string>();
            public string       BackupPath;
            public bool         BackupFailed;
        }

        static bool FirstKeyDumped;

        // ------------------------------------------------------------- plan --
        public static List<PlanItem> BuildPlan(
            object sourceTable,
            IList<object> selectedKeys,
            IList<object> targetTables,
            ConflictMode mode)
        {
            var plan = new List<PlanItem>();
            var sourceName = DictModel.AsString(DictModel.GetProp(sourceTable, "Name")) ?? "";

            foreach (var target in targetTables)
            {
                var targetName       = DictModel.AsString(DictModel.GetProp(target, "Name")) ?? "";
                var targetFieldLabels = GetFieldLabels(target);
                var targetKeyNames   = GetKeyNames(target);

                foreach (var key in selectedKeys)
                {
                    var keyName     = DictModel.AsString(DictModel.GetProp(key, "Name")) ?? "";
                    var compLabels  = GetComponentLabels(key);

                    var item = new PlanItem
                    {
                        SourceKey        = key,
                        TargetTable      = target,
                        KeyName          = keyName,
                        SourceTableName  = sourceName,
                        TargetTableName  = targetName,
                        ComponentLabels  = compLabels
                    };

                    if (targetKeyNames.Any(n => string.Equals(n, keyName, StringComparison.OrdinalIgnoreCase)))
                    {
                        item.Action = mode == ConflictMode.Skip ? PlanAction.Skip : PlanAction.NameConflict;
                        item.Reason = "target already has a key named '" + keyName + "'";
                    }
                    else
                    {
                        foreach (var label in compLabels)
                        {
                            if (!targetFieldLabels.Any(f => string.Equals(f, label, StringComparison.OrdinalIgnoreCase)))
                                item.MissingFieldLabels.Add(label);
                        }
                        if (item.MissingFieldLabels.Count > 0)
                        {
                            item.Action = mode == ConflictMode.Skip ? PlanAction.Skip : PlanAction.MissingFields;
                            item.Reason = "target is missing required fields: " + string.Join(", ", item.MissingFieldLabels.ToArray());
                        }
                        else
                        {
                            item.Action = PlanAction.Add;
                        }
                    }
                    plan.Add(item);
                }
            }
            return plan;
        }

        public static string MakeBackupPath(string dctPath)
        {
            return dctPath + ".tasker-keybak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        }

        // ------------------------------------------------------------ apply --
        public static ApplyResult Apply(
            List<PlanItem> plan,
            object dict,
            object viewContent,
            string dctPath,
            Action<int, string> onProgress,
            Func<bool> isCancelled)
        {
            FirstKeyDumped = false;
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

            if (plan.Any(p => p.Action == PlanAction.NameConflict || p.Action == PlanAction.MissingFields))
            {
                foreach (var p in plan)
                {
                    if (p.Action == PlanAction.NameConflict || p.Action == PlanAction.MissingFields)
                        result.Messages.Add(p.TargetTableName + "." + p.KeyName + ": ABORT - " + p.Reason);
                }
                result.FailedCount = plan.Count;
                return result;
            }

            int done = 0;
            foreach (var item in plan)
            {
                if (isCancelled != null && isCancelled())
                {
                    result.Messages.Add("Cancelled by user after " + done + " / " + plan.Count + " items.");
                    break;
                }

                var tag = item.TargetTableName + "." + item.KeyName;
                if (onProgress != null) onProgress(done, tag);

                if (item.Action == PlanAction.Skip) { result.SkippedCount++; done++; continue; }

                try
                {
                    CopyAndAdd(item, result, tag);
                    result.AddedCount++;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    var inner = ex.InnerException ?? ex;
                    result.Messages.Add(tag + ": " + inner.GetType().Name + " - " + inner.Message);
                }
                done++;
            }
            if (onProgress != null) onProgress(done, "Finalising...");

            if (result.AddedCount > 0)
                ForceMarkDirty(dict, viewContent, result);

            return result;
        }

        // -------------------------------------------------- copy + remap --
        static void CopyAndAdd(PlanItem item, ApplyResult result, string tag)
        {
            var sourceKey   = item.SourceKey;
            var targetTable = item.TargetTable;
            var steps       = new List<string>();

            int n0 = CountEnum(DictModel.GetProp(targetTable, "Keys"));
            steps.Add("n0=" + n0);

            // 1. Copy with quiet=false so ItemAdded events fire.
            var copyMethod = sourceKey.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Copy"
                                  && m.GetParameters().Length == 2
                                  && m.GetParameters()[1].ParameterType == typeof(bool));
            if (copyMethod == null)
                throw new InvalidOperationException("No Copy(parent, bool) on " + sourceKey.GetType().FullName);

            object newKey;
            try { newKey = copyMethod.Invoke(sourceKey, new object[] { targetTable, false }); }
            catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
            if (newKey == null) throw new InvalidOperationException("Copy returned null.");
            steps.Add("copy");

            // 2. One-time shape dump of the first copied key so we can see the real
            //    name of the components collection and the component's field ref.
            if (!FirstKeyDumped)
            {
                FirstKeyDumped = true;
                try { DumpKeyShape(newKey, result.Messages); }
                catch (Exception ex) { result.Messages.Add("Shape dump: " + ex.GetType().Name); }
            }

            // 3. Pre-reg state (same recipe fields needed).
            TrySetObjectField(newKey, "parentItem", targetTable);
            TrySetBoolField  (newKey, "stored",          true);
            TrySetBoolField  (newKey, "itemHasChanged",  true);

            // 4. Remap components BEFORE registration so the key looks consistent
            //    when InsertKey validates.
            RemapComponents(newKey, targetTable, steps);

            // 5. Register. Mirror the field path: try internal InsertKey first,
            //    then public AddKey, then the collection's own Add(DDKey).
            bool registered = IsInKeys(newKey, targetTable);
            if (registered) steps.Add("preReg");

            if (!registered)
                registered = TryInvokeOneArg(targetTable, "InsertKey", BindingFlags.NonPublic, newKey, steps, "InsertKey");

            if (!registered)
                registered = TryInvokeOneArg(targetTable, "AddKey", BindingFlags.Public, newKey, steps, "AddKey");

            if (!registered)
            {
                var keysColl = DictModel.GetProp(targetTable, "Keys");
                if (keysColl != null)
                {
                    var collAdd = keysColl.GetType()
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "Add"
                                          && m.GetParameters().Length == 1
                                          && m.GetParameters()[0].ParameterType == newKey.GetType());
                    if (collAdd != null)
                    {
                        try { collAdd.Invoke(keysColl, new object[] { newKey }); steps.Add("coll.Add(fb)"); }
                        catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
                    }
                }
                registered = IsInKeys(newKey, targetTable);
            }

            if (!registered)
                throw new InvalidOperationException("Could not register key via any known path.");

            int n1 = CountEnum(DictModel.GetProp(targetTable, "Keys"));
            steps.Add("n1=" + n1);

            // 6. Post-reg repair.
            TrySetObjectField(newKey, "parentItem", targetTable);
            TrySetBoolField  (newKey, "stored",          true);
            TrySetBoolField  (newKey, "itemHasChanged",  true);

            // 7. Final report.
            steps.Add("comps=" + GetComponentLabels(newKey).Count);
            result.Messages.Add(tag + " : " + string.Join(" > ", steps.ToArray()));
        }

        static void RemapComponents(object newKey, object targetTable, List<string> steps)
        {
            var comps = FindComponents(newKey);
            if (comps == null) { steps.Add("remap:no-list"); return; }

            var byLabel = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var targetFields = DictModel.GetProp(targetTable, "Fields") as IEnumerable;
            if (targetFields != null)
            {
                foreach (var f in targetFields)
                {
                    var l = DictModel.AsString(DictModel.GetProp(f, "Label"));
                    if (!string.IsNullOrEmpty(l) && !byLabel.ContainsKey(l)) byLabel[l] = f;
                }
            }

            int remapped = 0, failed = 0;
            foreach (var comp in comps)
            {
                if (comp == null) continue;

                // Figure out what label the component currently refers to.
                string label = null;

                var fieldRef = FirstNonNullProp(comp, new[] { "Field", "DDField" });
                if (fieldRef != null)
                    label = DictModel.AsString(DictModel.GetProp(fieldRef, "Label"));

                if (string.IsNullOrEmpty(label))
                    label = DictModel.AsString(FirstNonNullProp(comp, new[] { "Label", "Name" }));

                if (string.IsNullOrEmpty(label)) { failed++; continue; }

                object targetField;
                if (!byLabel.TryGetValue(label, out targetField)) { failed++; continue; }

                // Rewire: try Field property first, then FieldId, then setter method.
                bool ok = TrySetProp(comp, "Field", targetField)
                       || TrySetProp(comp, "DDField", targetField);
                if (!ok)
                {
                    var newId = DictModel.GetProp(targetField, "Id");
                    if (newId != null) ok = TrySetProp(comp, "FieldId", newId);
                }
                if (ok) remapped++; else failed++;
            }
            steps.Add("remap=" + remapped + (failed > 0 ? "/F" + failed : ""));
        }

        // ----------------------------------------------------- helpers --
        static List<string> GetComponentLabels(object key)
        {
            var labels = new List<string>();
            var comps = FindComponents(key);
            if (comps == null) return labels;
            foreach (var c in comps)
            {
                if (c == null) continue;
                var fieldRef = FirstNonNullProp(c, new[] { "Field", "DDField" });
                if (fieldRef != null)
                {
                    var l = DictModel.AsString(DictModel.GetProp(fieldRef, "Label"));
                    if (!string.IsNullOrEmpty(l)) { labels.Add(l); continue; }
                }
                var own = DictModel.AsString(FirstNonNullProp(c, new[] { "Label", "Name" }));
                if (!string.IsNullOrEmpty(own)) labels.Add(own);
            }
            return labels;
        }

        static IEnumerable FindComponents(object key)
        {
            string[] names = { "Components", "KeyComponents", "Fields", "KeyFields",
                               "Segments", "Parts", "Children", "Items", "FieldList" };
            foreach (var n in names)
            {
                var v = DictModel.GetProp(key, n) as IEnumerable;
                if (v != null && !(v is string))
                {
                    // Must contain objects that look like key components (not DDFields directly).
                    foreach (var _ in v) return v;
                    return v; // empty is fine
                }
            }
            // Last resort: scan for any IEnumerable property whose first element has a
            // Field or DDField reference.
            foreach (var p in key.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length != 0) continue;
                if (!typeof(IEnumerable).IsAssignableFrom(p.PropertyType) || p.PropertyType == typeof(string)) continue;
                object v; try { v = p.GetValue(key, null); } catch { continue; }
                var en = v as IEnumerable; if (en == null) continue;
                foreach (var item in en)
                {
                    if (item == null) continue;
                    if (FirstNonNullProp(item, new[] { "Field", "DDField" }) != null) return en;
                    break;
                }
            }
            return null;
        }

        static object FirstNonNullProp(object o, string[] names)
        {
            foreach (var n in names)
            {
                var v = DictModel.GetProp(o, n);
                if (v != null) return v;
            }
            return null;
        }

        static IList<string> GetFieldLabels(object table)
        {
            var list = new List<string>();
            var fields = DictModel.GetProp(table, "Fields") as IEnumerable;
            if (fields != null)
            {
                foreach (var f in fields)
                {
                    var l = DictModel.AsString(DictModel.GetProp(f, "Label"));
                    if (!string.IsNullOrEmpty(l)) list.Add(l);
                }
            }
            return list;
        }

        static IList<string> GetKeyNames(object table)
        {
            var list = new List<string>();
            var keys = DictModel.GetProp(table, "Keys") as IEnumerable;
            if (keys != null)
            {
                foreach (var k in keys)
                {
                    var n = DictModel.AsString(DictModel.GetProp(k, "Name"));
                    if (!string.IsNullOrEmpty(n)) list.Add(n);
                }
            }
            return list;
        }

        static bool IsInKeys(object key, object targetTable)
        {
            var keys = DictModel.GetProp(targetTable, "Keys") as IEnumerable;
            if (keys == null) return false;
            foreach (var k in keys) if (ReferenceEquals(k, key)) return true;
            return false;
        }

        static int CountEnum(object maybeEnum)
        {
            var en = maybeEnum as IEnumerable;
            if (en == null) return -1;
            int n = 0; foreach (var _ in en) n++;
            return n;
        }

        static bool TryInvokeOneArg(object target, string methodName, BindingFlags visibility, object arg,
            List<string> steps, string tag)
        {
            var m = target.GetType().GetMethod(methodName,
                visibility | BindingFlags.Instance,
                null, new[] { arg.GetType() }, null);
            if (m == null) return false;
            try
            {
                m.Invoke(target, new[] { arg });
                steps.Add(tag);
                return IsInKeys(arg, target);
            }
            catch (TargetInvocationException tie)
            {
                steps.Add(tag + ":EX(" + (tie.InnerException ?? tie).GetType().Name + ")");
                return false;
            }
        }

        // --- recycled from FieldCopier (kept local to avoid coupling) ---
        static bool TrySetProp(object target, string name, object value)
        {
            if (target == null) return false;
            var p = target.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p == null || !p.CanWrite) return false;
            try { p.SetValue(target, value, null); return true; } catch { return false; }
        }

        static bool TrySetObjectField(object target, string fieldName, object value)
        {
            if (target == null) return false;
            var t = target.GetType();
            while (t != null && t != typeof(object))
            {
                var f = t.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (f != null)
                {
                    try { f.SetValue(target, value); return true; } catch { return false; }
                }
                t = t.BaseType;
            }
            return false;
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

        static void DumpKeyShape(object key, List<string> msgs)
        {
            msgs.Add("Key shape:");
            foreach (var p in key.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                 .Where(pp => pp.CanRead && pp.GetIndexParameters().Length == 0 && pp.Name.IndexOf('.') < 0)
                                 .OrderBy(pp => pp.Name))
            {
                object v; try { v = p.GetValue(key, null); } catch { continue; }
                if (v == null) continue;
                var vt = v.GetType();
                var isEnumerable = typeof(IEnumerable).IsAssignableFrom(vt) && vt != typeof(string);
                if (!isEnumerable) continue;
                int c = 0; try { foreach (var _ in (IEnumerable)v) c++; } catch { }
                msgs.Add("  " + p.Name + " : " + vt.Name + " [" + c + "]");
            }
        }

        static void ForceMarkDirty(object dict, object viewContent, ApplyResult result)
        {
            var log = new List<string>();
            var dictType = dict == null ? null : dict.GetType();
            if (dictType != null)
            {
                var pDirty = dictType.GetProperty("IsDirty", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pDirty != null && pDirty.CanWrite) { try { pDirty.SetValue(dict, true, null); log.Add("dict.IsDirty=OK"); } catch { } }
                var mChanged = dictType.GetMethod("DoIsDirtyChanged",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, Type.EmptyTypes, null);
                if (mChanged != null) { try { mChanged.Invoke(dict, null); log.Add("dict.DoIsDirtyChanged=OK"); } catch { } }
                var mTouched = dictType.GetMethod("ChildListTouched",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, Type.EmptyTypes, null);
                if (mTouched != null) { try { mTouched.Invoke(dict, null); log.Add("dict.ChildListTouched=OK"); } catch { } }
            }
            if (viewContent != null)
            {
                var pDirty = viewContent.GetType().GetProperty("IsDirty",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pDirty != null && pDirty.CanWrite) { try { pDirty.SetValue(viewContent, true, null); log.Add("view.IsDirty=OK"); } catch { } }
            }
            if (log.Count > 0) result.Messages.Add("Mark-dirty paths tried: " + string.Join(", ", log.ToArray()));
        }
    }
}
