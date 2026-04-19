using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Pick a field on a table — list every key, relation, and trigger body
    // across the dictionary that references it. Read-only.
    internal class WhereUsedDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        readonly object dict;
        ComboBox cbTable, cbField;
        ListView lv;
        Label    lblSummary;
        List<object> tables;

        public WhereUsedDialog(object dict) { this.dict = dict; BuildUi(); }

        void BuildUi()
        {
            Text = "Where used - " + DictModel.GetDictionaryName(dict);
            Width = 1100; Height = 700;
            MinimumSize = new Size(840, 460);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true; MinimizeBox = false;
            ShowIcon = false; ShowInTaskbar = false;
            SizeGripStyle = SizeGripStyle.Show;

            var header = new Label
            {
                Dock = DockStyle.Top, Height = 48,
                BackColor = HeaderColor, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Text = "Where used   " + DictModel.GetDictionaryName(dict)
            };

            var top = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = BgColor, Padding = new Padding(16, 14, 16, 8) };
            var lblT = new Label { Text = "Table:", Left = 4,   Top = 10, Width = 50, Font = new Font("Segoe UI", 9F) };
            cbTable = new ComboBox { Left = 56,  Top = 6, Width = 340, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            var lblF = new Label { Text = "Field:", Left = 416, Top = 10, Width = 46, Font = new Font("Segoe UI", 9F) };
            cbField = new ComboBox { Left = 466, Top = 6, Width = 340, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            top.Controls.Add(lblT); top.Controls.Add(cbTable); top.Controls.Add(lblF); top.Controls.Add(cbField);

            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 24,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 2, 0, 0),
                Text = "Pick a table and a field."
            };

            lv = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true, BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };
            lv.Columns.Add("Kind",    100);
            lv.Columns.Add("Table",   180);
            lv.Columns.Add("Item",    220);
            lv.Columns.Add("Context", 560);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(btnClose);

            Controls.Add(lv);
            Controls.Add(bottom);
            Controls.Add(lblSummary);
            Controls.Add(top);
            Controls.Add(header);
            CancelButton = btnClose;

            tables = DictModel.GetTables(dict)
                .OrderBy(t => DictModel.AsString(DictModel.GetProp(t, "Name")), StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var t in tables)
                cbTable.Items.Add(DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?");

            cbTable.SelectedIndexChanged += delegate { PopulateFields(); Recompute(); };
            cbField.SelectedIndexChanged += delegate { Recompute(); };
            if (cbTable.Items.Count > 0) cbTable.SelectedIndex = 0;
        }

        void PopulateFields()
        {
            cbField.Items.Clear();
            if (cbTable.SelectedIndex < 0) return;
            var t = tables[cbTable.SelectedIndex];
            var fields = DictModel.GetProp(t, "Fields") as IEnumerable;
            if (fields == null) return;
            var list = new List<string>();
            foreach (var f in fields)
            {
                if (f == null) continue;
                var lbl = DictModel.AsString(DictModel.GetProp(f, "Label"));
                if (!string.IsNullOrEmpty(lbl)) list.Add(lbl);
            }
            foreach (var l in list.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                cbField.Items.Add(l);
            if (cbField.Items.Count > 0) cbField.SelectedIndex = 0;
        }

        void Recompute()
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            if (cbTable.SelectedIndex < 0 || cbField.SelectedIndex < 0)
            {
                lv.EndUpdate();
                lblSummary.Text = "Pick a table and a field.";
                return;
            }
            var homeTable = tables[cbTable.SelectedIndex];
            var homeName  = DictModel.AsString(DictModel.GetProp(homeTable, "Name")) ?? "";
            var prefix    = DictModel.AsString(DictModel.GetProp(homeTable, "Prefix")) ?? "";
            var fieldLabel = cbField.SelectedItem as string ?? "";
            int hits = 0;

            foreach (var t in tables)
            {
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "";
                bool sameTable = string.Equals(tName, homeName, StringComparison.OrdinalIgnoreCase);

                if (sameTable)
                {
                    var keys = DictModel.GetProp(t, "Keys") as IEnumerable;
                    if (keys != null) foreach (var k in keys)
                    {
                        if (k == null) continue;
                        if (KeyReferencesField(k, fieldLabel))
                        {
                            Add("Key", tName, DictModel.AsString(DictModel.GetProp(k, "Name")) ?? "?",
                                "components: " + ComponentSummary(k));
                            hits++;
                        }
                    }
                }

                var rels = DictModel.GetProp(t, "Relations") as IEnumerable;
                if (rels != null) foreach (var r in rels)
                {
                    if (r == null) continue;
                    if (RelationReferencesField(r, fieldLabel))
                    {
                        Add("Relation", tName, DictModel.AsString(DictModel.GetProp(r, "Name")) ?? "?",
                            RelationContext(r));
                        hits++;
                    }
                }

                var trigs = DictModel.GetProp(t, "Triggers") as IEnumerable;
                if (trigs != null) foreach (var tr in trigs)
                {
                    if (tr == null) continue;
                    var body = DictModel.AsString(DictModel.GetProp(tr, "Body"))
                            ?? DictModel.AsString(DictModel.GetProp(tr, "Code"))
                            ?? DictModel.AsString(DictModel.GetProp(tr, "Source"))
                            ?? DictModel.AsString(DictModel.GetProp(tr, "TriggerCode"))
                            ?? "";
                    if (BodyMentionsField(body, fieldLabel, prefix))
                    {
                        Add("Trigger", tName, DictModel.AsString(DictModel.GetProp(tr, "Name")) ?? "?",
                            Clip(body));
                        hits++;
                    }
                }
            }

            lv.EndUpdate();
            lblSummary.Text = fieldLabel + " in " + homeName + ": " + hits
                + " reference" + (hits == 1 ? "" : "s") + ".";
        }

        void Add(string kind, string table, string item, string context)
        {
            lv.Items.Add(new ListViewItem(new[] { kind, table, item, context }));
        }

        static bool KeyReferencesField(object key, string fieldLabel)
        {
            foreach (var c in KeyComponents(key))
                if (string.Equals(c, fieldLabel, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static IEnumerable<string> KeyComponents(object key)
        {
            string[] candidates = { "Components", "KeyComponents", "Fields", "KeyFields", "Segments" };
            IEnumerable en = null;
            foreach (var c in candidates)
            {
                en = DictModel.GetProp(key, c) as IEnumerable;
                if (en != null && !(en is string)) break;
                en = null;
            }
            if (en == null) yield break;
            foreach (var comp in en)
            {
                if (comp == null) continue;
                var fld = DictModel.GetProp(comp, "Field") ?? DictModel.GetProp(comp, "DDField");
                var n = fld != null
                    ? DictModel.AsString(DictModel.GetProp(fld, "Label"))
                    : DictModel.AsString(DictModel.GetProp(comp, "Label")) ?? DictModel.AsString(DictModel.GetProp(comp, "Name"));
                if (!string.IsNullOrEmpty(n)) yield return n;
            }
        }

        static string ComponentSummary(object key)
        {
            return string.Join(" + ", KeyComponents(key).ToArray());
        }

        static bool RelationReferencesField(object relation, string fieldLabel)
        {
            string[] compProps = { "Components", "Pairs", "Links", "Fields", "KeyFields", "RelationPairs" };
            foreach (var p in compProps)
            {
                var en = DictModel.GetProp(relation, p) as IEnumerable;
                if (en == null || en is string) continue;
                foreach (var c in en)
                {
                    if (c == null) continue;
                    string[] fieldProps = { "ParentField", "ChildField", "FromField", "ToField", "Field", "DDField", "PrimaryField", "ForeignField" };
                    foreach (var fp in fieldProps)
                    {
                        var fld = DictModel.GetProp(c, fp);
                        if (fld == null) continue;
                        var n = DictModel.AsString(DictModel.GetProp(fld, "Label"));
                        if (!string.IsNullOrEmpty(n) && string.Equals(n, fieldLabel, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            return false;
        }

        static string RelationContext(object r)
        {
            string related = "";
            string[] child = { "ChildFile", "RelatedFile", "Child", "ToFile", "To", "File", "DetailFile", "ForeignFile" };
            foreach (var p in child)
            {
                var v = DictModel.GetProp(r, p);
                if (v != null) { related = DictModel.AsString(DictModel.GetProp(v, "Name")) ?? ""; break; }
            }
            return "-> " + related;
        }

        static bool BodyMentionsField(string body, string fieldLabel, string prefix)
        {
            if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(fieldLabel)) return false;
            var needles = new[]
            {
                (prefix + ":" + fieldLabel).ToLowerInvariant(),
                fieldLabel.ToLowerInvariant()
            };
            var bodyL = body.ToLowerInvariant();
            foreach (var h in needles)
            {
                if (string.IsNullOrEmpty(h)) continue;
                int from = 0;
                while (from < bodyL.Length)
                {
                    var idx = bodyL.IndexOf(h, from, StringComparison.Ordinal);
                    if (idx < 0) break;
                    var before = idx == 0 ? ' ' : bodyL[idx - 1];
                    var after  = idx + h.Length >= bodyL.Length ? ' ' : bodyL[idx + h.Length];
                    bool wordLeft  = !char.IsLetterOrDigit(before) && before != '_';
                    bool wordRight = !char.IsLetterOrDigit(after)  && after  != '_';
                    if (wordLeft && wordRight) return true;
                    from = idx + h.Length;
                }
            }
            return false;
        }

        static string Clip(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            return s.Length <= 240 ? s : s.Substring(0, 240) + "...";
        }
    }
}
