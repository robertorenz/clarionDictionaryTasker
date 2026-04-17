using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Hierarchical explorer:
    //   Dictionary
    //     Tables (N)
    //       <TableName>               [lazy]
    //         <scalar table props>
    //         Fields (N)              [lazy]
    //           <FieldName>
    //             <scalar field props>
    //         Keys (N)                [lazy]
    //           <KeyName>
    //             <scalar key props>
    //             Fields              (key components, best-effort)
    //         Relations (N)           [lazy]
    //           <RelationName>
    //             <scalar relation props>
    //         Triggers (N)            [lazy]
    //           <TriggerName>
    //             <scalar trigger props>
    //
    // Every group node gets a "..." dummy child so the expander chevron shows;
    // BeforeExpand swaps the dummy for real content. Leaf-level scalar properties
    // are added eagerly since they're small and cheap.
    internal class DictTreeViewPanel : Panel
    {
        const string DummyText = "...loading";

        static readonly Color BgColor   = Color.White;
        static readonly Color TextColor = Color.FromArgb(30, 40, 55);

        // Known useful DDField property names, in display order.
        static readonly KeyValuePair<string, string>[] FieldProps = new[]
        {
            New("Name",           "full name"),
            New("DataType",       "type"),
            New("FieldSize",      "size"),
            New("Places",         "places"),
            New("Dimensions",     "dimensions"),
            New("ScreenPicture",  "picture"),
            New("RowPicture",     "row picture"),
            New("ColumnHeading",  "heading"),
            New("PromptText",     "prompt"),
            New("InitialValue",   "initial value"),
            New("Description",    "description"),
            New("Message",        "message"),
            New("ToolTip",        "tooltip"),
            New("ExternalName",   "external name"),
            New("Scope",          "scope"),
            New("Justification",  "justification"),
            New("CaseAttribute",  "case"),
            New("Location",       "location"),
            New("IsAutoNumber",   "auto-number"),
            New("IsString",       "is string"),
            New("IsNumeric",      "is numeric"),
            New("IsBLOBorMEMO",   "is BLOB/MEMO"),
            New("IsTimeStamp",    "is timestamp"),
            New("IsInFile",       "in file"),
            New("FlagReadOnly",   "read only"),
            New("FlagImmediate",  "immediate"),
            New("FlagPassword",   "password"),
            New("Binary",         "binary"),
            New("External",       "external"),
            New("Reference",      "reference"),
            New("Threaded",       "threaded"),
            New("Static",         "static"),
            New("CreatedDate",    "created"),
            New("ModifiedDate",   "modified"),
            New("Id",             "id"),
        };

        // Known table-level scalar props (shown directly under the table node).
        static readonly KeyValuePair<string, string>[] TableProps = new[]
        {
            New("Prefix",          "prefix"),
            New("FieldPrefix",     "field prefix"),
            New("FileDriverName",  "driver"),
            New("DriverOptions",   "driver options"),
            New("FullPathName",    "full path"),
            New("DefaultFileName", "default filename"),
            New("OwnerName",       "owner"),
            New("FileStatement",   "file statement"),
            New("Encrypt",         "encrypt"),
            New("Create",          "create"),
            New("Reclaim",         "reclaim"),
            New("OEM",             "oem"),
            New("Threaded",        "threaded"),
            New("IsAlias",         "is alias"),
            New("CreatedDate",     "created"),
            New("ModifiedDate",    "modified"),
            New("Id",              "id"),
        };

        readonly object dict;
        TreeView tv;

        public DictTreeViewPanel(object dict)
        {
            this.dict = dict;
            BuildUi();
            BuildRoot();
        }

        void BuildUi()
        {
            BackColor = BgColor;
            tv = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = BgColor,
                ForeColor = TextColor,
                Font = new Font("Segoe UI", 9.5F),
                HideSelection = false,
                ShowLines = true,
                ShowPlusMinus = true,
                ShowRootLines = true,
                BorderStyle = BorderStyle.None
            };
            tv.BeforeExpand += delegate(object s, TreeViewCancelEventArgs e)
            {
                if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Text == DummyText)
                {
                    e.Node.Nodes.Clear();
                    Populate(e.Node);
                }
            };
            Controls.Add(tv);
        }

        void BuildRoot()
        {
            var name = DictModel.GetDictionaryName(dict);
            var fileName = DictModel.GetDictionaryFileName(dict);
            var tables = DictModel.GetTables(dict);

            var root = new TreeNode(string.Format("{0}   ({1})", name, fileName));
            root.NodeFont = new Font("Segoe UI Semibold", 10F);

            var tablesGroup = new TreeNode(string.Format("Tables ({0})", tables.Count));
            tablesGroup.NodeFont = new Font("Segoe UI Semibold", 9.5F);
            foreach (var t in tables.OrderBy(x => DictModel.AsString(DictModel.GetProp(x, "Name")), StringComparer.OrdinalIgnoreCase))
            {
                var tn = MakeLazyNode(
                    DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?",
                    new TableToken(t));
                tablesGroup.Nodes.Add(tn);
            }
            root.Nodes.Add(tablesGroup);

            tv.Nodes.Add(root);
            root.Expand();
            tablesGroup.Expand();
        }

        // --- lazy expansion dispatcher ---
        void Populate(TreeNode n)
        {
            var tok = n.Tag;
            var tt = tok as TableToken;     if (tt != null) { ExpandTable(n, tt.Table); return; }
            var fg = tok as FieldsToken;    if (fg != null) { ExpandFields(n, fg.Table); return; }
            var kg = tok as KeysToken;      if (kg != null) { ExpandKeys(n, kg.Table); return; }
            var rg = tok as RelationsToken; if (rg != null) { ExpandRelations(n, rg.Table); return; }
            var tg = tok as TriggersToken;  if (tg != null) { ExpandTriggers(n, tg.Table); return; }
        }

        void ExpandTable(TreeNode tableNode, object table)
        {
            AddScalars(tableNode, table, TableProps);

            AddGroupIfAny(tableNode, table, "Fields",    "Fields",    new FieldsToken(table));
            AddGroupIfAny(tableNode, table, "Keys",      "Keys",      new KeysToken(table));
            AddGroupIfAny(tableNode, table, "Relations", "Relations", new RelationsToken(table));
            AddGroupIfAny(tableNode, table, "Triggers",  "Triggers",  new TriggersToken(table));
        }

        void AddGroupIfAny(TreeNode parent, object source, string propName, string label, object token)
        {
            int count = DictModel.CountEnumerable(source, propName);
            var n = new TreeNode(string.Format("{0} ({1})", label, count));
            n.NodeFont = new Font("Segoe UI Semibold", 9.5F);
            n.Tag = token;
            if (count > 0) n.Nodes.Add(new TreeNode(DummyText));
            parent.Nodes.Add(n);
        }

        void ExpandFields(TreeNode groupNode, object table)
        {
            var en = DictModel.GetProp(table, "Fields") as IEnumerable;
            if (en == null) return;
            foreach (var f in en)
            {
                if (f == null) continue;
                var label = DictModel.AsString(DictModel.GetProp(f, "Label"))
                         ?? DictModel.AsString(DictModel.GetProp(f, "Name"))
                         ?? "field";
                var dt    = DictModel.AsString(DictModel.GetProp(f, "DataType")) ?? "";
                var sz    = DictModel.AsString(DictModel.GetProp(f, "FieldSize")) ?? "";
                string suffix = "";
                if (!string.IsNullOrEmpty(dt))
                {
                    suffix = "  [" + dt;
                    if (!string.IsNullOrEmpty(sz) && sz != "0") suffix += ", " + sz;
                    suffix += "]";
                }
                var fn = new TreeNode(label + suffix);
                AddScalars(fn, f, FieldProps);
                groupNode.Nodes.Add(fn);
            }
        }

        void ExpandKeys(TreeNode groupNode, object table)
        {
            var en = DictModel.GetProp(table, "Keys") as IEnumerable;
            if (en == null) return;
            foreach (var k in en)
            {
                if (k == null) continue;
                var name = DictModel.AsString(DictModel.GetProp(k, "Name"))
                        ?? DictModel.AsString(DictModel.GetProp(k, "Label"))
                        ?? "key";
                var kn = new TreeNode(name);

                // Every readable scalar — we don't have a whitelist yet.
                AddAllScalarProperties(kn, k);

                // Try to surface the key's component fields.
                var components = FindCollection(k, new[] { "Fields", "Components", "Segments", "KeyFields" });
                if (components != null)
                {
                    var fg = new TreeNode("Key fields");
                    fg.NodeFont = new Font("Segoe UI Semibold", 9.5F);
                    foreach (var kf in components)
                    {
                        if (kf == null) continue;
                        var nm = DictModel.AsString(DictModel.GetProp(kf, "Name"))
                              ?? DictModel.AsString(DictModel.GetProp(kf, "Label"))
                              ?? kf.ToString();
                        fg.Nodes.Add(new TreeNode(nm));
                    }
                    if (fg.Nodes.Count > 0) kn.Nodes.Add(fg);
                }

                groupNode.Nodes.Add(kn);
            }
        }

        void ExpandRelations(TreeNode groupNode, object table)
        {
            var en = DictModel.GetProp(table, "Relations") as IEnumerable;
            if (en == null) return;
            foreach (var r in en)
            {
                if (r == null) continue;
                var name = DictModel.AsString(DictModel.GetProp(r, "Name"))
                        ?? DictModel.AsString(DictModel.GetProp(r, "Label"))
                        ?? "relation";
                var rn = new TreeNode(name);
                AddAllScalarProperties(rn, r);
                groupNode.Nodes.Add(rn);
            }
        }

        void ExpandTriggers(TreeNode groupNode, object table)
        {
            var en = DictModel.GetProp(table, "Triggers") as IEnumerable;
            if (en == null) return;
            foreach (var t in en)
            {
                if (t == null) continue;
                var name = DictModel.AsString(DictModel.GetProp(t, "Name"))
                        ?? DictModel.AsString(DictModel.GetProp(t, "Label"))
                        ?? t.GetType().Name;
                var tn = new TreeNode(name);
                AddAllScalarProperties(tn, t);
                groupNode.Nodes.Add(tn);
            }
        }

        static IEnumerable FindCollection(object o, string[] candidateNames)
        {
            foreach (var cn in candidateNames)
            {
                var v = DictModel.GetProp(o, cn) as IEnumerable;
                if (v != null && !(v is string)) return v;
            }
            return null;
        }

        static void AddScalars(TreeNode parent, object obj, KeyValuePair<string, string>[] props)
        {
            // First, show Name/Label if present so every item self-identifies.
            foreach (var p in props)
            {
                var v = DictModel.GetProp(obj, p.Key);
                var s = AsDisplay(v);
                if (s == null) continue;
                parent.Nodes.Add(new TreeNode(p.Value + ": " + s));
            }
        }

        static void AddAllScalarProperties(TreeNode parent, object obj)
        {
            foreach (var pi in obj.GetType()
                                  .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                  .Where(pp => pp.CanRead && pp.GetIndexParameters().Length == 0 && pp.Name.IndexOf('.') < 0)
                                  .OrderBy(pp => pp.Name, StringComparer.OrdinalIgnoreCase))
            {
                object v;
                try { v = pi.GetValue(obj, null); }
                catch { continue; }
                if (v == null) continue;
                if (!IsScalar(v.GetType())) continue;
                var s = AsDisplay(v);
                if (s == null) continue;
                parent.Nodes.Add(new TreeNode(pi.Name + ": " + s));
            }
        }

        static bool IsScalar(Type t)
        {
            if (t.IsEnum) return true;
            if (t.IsPrimitive) return true;
            return t == typeof(string) || t == typeof(decimal) || t == typeof(Guid)
                || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan);
        }

        static string AsDisplay(object v)
        {
            if (v == null) return null;
            var s = v.ToString();
            if (string.IsNullOrEmpty(s)) return null;
            if (s.Length > 160) s = s.Substring(0, 160) + "...";
            // Strip newlines so tree stays single-line per leaf.
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s;
        }

        TreeNode MakeLazyNode(string text, object token)
        {
            var n = new TreeNode(text);
            n.Tag = token;
            n.Nodes.Add(new TreeNode(DummyText));
            return n;
        }

        static KeyValuePair<string, string> New(string key, string label)
        {
            return new KeyValuePair<string, string>(key, label);
        }

        // --- expansion context markers ---
        sealed class TableToken     { public readonly object Table; public TableToken(object t) { Table = t; } }
        sealed class FieldsToken    { public readonly object Table; public FieldsToken(object t) { Table = t; } }
        sealed class KeysToken      { public readonly object Table; public KeysToken(object t) { Table = t; } }
        sealed class RelationsToken { public readonly object Table; public RelationsToken(object t) { Table = t; } }
        sealed class TriggersToken  { public readonly object Table; public TriggersToken(object t) { Table = t; } }
    }
}
