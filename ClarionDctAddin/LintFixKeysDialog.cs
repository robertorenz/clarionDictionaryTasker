using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Editable grid of keys that have repairable issues — primarily empty
    // ExternalName (which SQL drivers need as the index name). Works the same
    // way as LintFixItDialog but for DDKey instead of DDField. FieldMutator
    // discovers the owning collection generically (parent.Keys instead of
    // parent.Fields) so the change-tracker registration still works.
    internal class LintFixKeysDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);
        static readonly Color DirtyBg     = Color.FromArgb(255, 246, 214);
        static readonly Color CleanBg     = Color.White;

        readonly object dict;
        readonly object singleTable;

        DataGridView grid;
        Label        lblSummary;
        Button       btnApply, btnRefresh, btnAutoBlank, btnAutoAll;
        ComboBox     cbStyle;
        List<Row>    rows = new List<Row>();

        enum NamingStyle
        {
            UpperSnake,        // CLIENTES_GUIDKEY
            LowerSnake,        // clientes_guidkey
            CamelSnake,        // Clientes_guidkey  (table PascalCase, key as-is)
            Pascal,            // ClientesGuidkey
            Camel,             // clientesGuidkey
            IdxSnake,          // idx_clientes_guidkey
            KeyAsIs,           // guidkey (just the key name; useful when the DB indexes are auto-named)
            TablePrefix        // CLIENTES_<keyName-as-is>
        }

        sealed class Row
        {
            public object Key;
            public string Table, Name, KeyType, Components;
            public string Unique, Primary;
            public string OrigExternalName;
            public string ExternalName;
            public string Issues;

            public bool DirtyExternalName
            {
                get { return !string.Equals(ExternalName ?? "", OrigExternalName ?? "", StringComparison.Ordinal); }
            }
            public bool Dirty { get { return DirtyExternalName; } }
        }

        public LintFixKeysDialog(object dict) : this(dict, null) { }
        public LintFixKeysDialog(object dict, object singleTable)
        {
            this.dict = dict;
            this.singleTable = singleTable;
            BuildUi();
            Populate();
        }

        void BuildUi()
        {
            var scope = singleTable == null
                ? "dictionary"
                : (DictModel.AsString(DictModel.GetProp(singleTable, "Name")) ?? "?");
            Text = "Fix keys - " + scope;
            Width = 1200; Height = 720;
            MinimumSize = new Size(920, 460);
            StartPosition = FormStartPosition.CenterParent;
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
                Text = singleTable == null
                    ? "Fix keys   dictionary: " + DictModel.GetDictionaryName(dict)
                    : "Fix keys   table: " + scope
            };

            var autoBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = BgColor, Padding = new Padding(16, 8, 16, 4) };
            var lblStyle = new Label { Text = "Auto-fill style:", Left = 0, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            cbStyle = new ComboBox { Left = 100, Top = 4, Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            cbStyle.Items.Add("UPPER_SNAKE    — CLIENTES_GUIDKEY");
            cbStyle.Items.Add("lower_snake    — clientes_guidkey");
            cbStyle.Items.Add("Camel_Snake    — Clientes_guidkey");
            cbStyle.Items.Add("Pascal         — ClientesGuidkey");
            cbStyle.Items.Add("camel          — clientesGuidkey");
            cbStyle.Items.Add("idx_snake      — idx_clientes_guidkey");
            cbStyle.Items.Add("Key only       — guidkey");
            cbStyle.Items.Add("Table prefix   — CLIENTES_<key as-is>");
            cbStyle.SelectedIndex = 0;
            btnAutoBlank = new Button { Text = "Fill blanks", Left = 372, Top = 2, Width = 110, Height = 30, FlatStyle = FlatStyle.System };
            btnAutoBlank.Click += delegate { AutoFill(onlyBlanks: true); };
            btnAutoAll   = new Button { Text = "Fill all (overwrite)", Left = 488, Top = 2, Width = 160, Height = 30, FlatStyle = FlatStyle.System };
            btnAutoAll.Click += delegate { AutoFill(onlyBlanks: false); };
            autoBar.Controls.Add(lblStyle);
            autoBar.Controls.Add(cbStyle);
            autoBar.Controls.Add(btnAutoBlank);
            autoBar.Controls.Add(btnAutoAll);

            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 26,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 6, 0, 0),
                Text = "Scanning..."
            };

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                EditMode = DataGridViewEditMode.EditOnEnter,
                Font = new Font("Segoe UI", 9F),
                ColumnHeadersDefaultCellStyle = { BackColor = Color.FromArgb(230, 235, 242), Font = new Font("Segoe UI Semibold", 9F) },
                EnableHeadersVisualStyles = false
            };
            AddCol("Table",         150, true);
            AddCol("Key",           180, true);
            AddCol("Type",          80,  true);
            AddCol("Components",    260, true);
            AddCol("U",             40,  true);
            AddCol("P",             40,  true);
            AddCol("ExternalName",  240, false);
            AddCol("Issues",        200, true);
            grid.CellEndEdit += delegate(object s, DataGridViewCellEventArgs e) { CommitCell(e.RowIndex, e.ColumnIndex); };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            btnApply = new Button { Text = "Apply changes...", Width = 160, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System, Enabled = false };
            btnApply.Click += delegate { Apply(); };
            btnRefresh = new Button { Text = "Rescan", Width = 100, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnRefresh.Click += delegate { Populate(); };
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(btnApply);
            bottom.Controls.Add(btnRefresh);

            Controls.Add(grid);
            Controls.Add(bottom);
            Controls.Add(lblSummary);
            Controls.Add(autoBar);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        void AutoFill(bool onlyBlanks)
        {
            var style = (NamingStyle)cbStyle.SelectedIndex;
            int touched = 0;
            for (int i = 0; i < grid.Rows.Count; i++)
            {
                var gRow = grid.Rows[i];
                var r = gRow.Tag as Row;
                if (r == null) continue;
                if (onlyBlanks && !string.IsNullOrWhiteSpace(r.ExternalName)) continue;
                var suggested = MakeName(style, r.Table, r.Name);
                if (string.Equals(suggested, r.ExternalName, StringComparison.Ordinal)) continue;
                r.ExternalName = suggested;
                gRow.Cells["ExternalName"].Value = suggested;
                r.Issues = BuildIssues(r);
                gRow.Cells["Issues"].Value = r.Issues;
                PaintRow(gRow, r);
                touched++;
            }
            UpdateSummary();
            if (touched == 0)
                MessageBox.Show(this, onlyBlanks
                    ? "No blank ExternalNames to fill."
                    : "No rows changed (values already match the chosen style).",
                    "Fix keys", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static string MakeName(NamingStyle style, string table, string key)
        {
            table = table ?? "";
            key   = key   ?? "";
            switch (style)
            {
                case NamingStyle.UpperSnake:
                    return (table + "_" + key).ToUpperInvariant();
                case NamingStyle.LowerSnake:
                    return (table + "_" + key).ToLowerInvariant();
                case NamingStyle.CamelSnake:
                    return Pascal(table) + "_" + key;
                case NamingStyle.Pascal:
                    return Pascal(table) + Pascal(key);
                case NamingStyle.Camel:
                    return Camel(table) + Pascal(key);
                case NamingStyle.IdxSnake:
                    return ("idx_" + table + "_" + key).ToLowerInvariant();
                case NamingStyle.KeyAsIs:
                    return key;
                case NamingStyle.TablePrefix:
                    return table + "_" + key;
                default:
                    return table + "_" + key;
            }
        }

        static string Pascal(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var parts = s.Split(new[] { '_', '-', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts)
            {
                if (p.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(p[0]));
                if (p.Length > 1) sb.Append(p.Substring(1).ToLowerInvariant());
            }
            return sb.Length == 0 ? s : sb.ToString();
        }

        static string Camel(string s)
        {
            var p = Pascal(s);
            if (string.IsNullOrEmpty(p)) return p;
            return char.ToLowerInvariant(p[0]) + p.Substring(1);
        }

        void AddCol(string name, int width, bool readOnly)
        {
            var col = new DataGridViewTextBoxColumn
            {
                HeaderText = name,
                Name = name,
                Width = width,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.Automatic,
                DefaultCellStyle = { WrapMode = DataGridViewTriState.True, Padding = new Padding(4, 2, 4, 2) }
            };
            grid.Columns.Add(col);
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        }

        void Populate()
        {
            rows = Collect();
            grid.Rows.Clear();
            foreach (var r in rows)
            {
                var idx = grid.Rows.Add(r.Table, r.Name, r.KeyType, r.Components, r.Unique, r.Primary, r.ExternalName, r.Issues);
                grid.Rows[idx].Tag = r;
                PaintRow(grid.Rows[idx], r);
            }
            UpdateSummary();
        }

        void CommitCell(int rowIdx, int colIdx)
        {
            if (rowIdx < 0 || rowIdx >= grid.Rows.Count) return;
            var gRow = grid.Rows[rowIdx];
            var r = gRow.Tag as Row;
            if (r == null) return;
            var colName = grid.Columns[colIdx].Name;
            var v = gRow.Cells[colIdx].Value as string ?? "";
            if (colName == "ExternalName")
            {
                r.ExternalName = v;
            }
            r.Issues = BuildIssues(r);
            gRow.Cells["Issues"].Value = r.Issues;
            PaintRow(gRow, r);
            UpdateSummary();
        }

        static void PaintRow(DataGridViewRow gRow, Row r)
        {
            gRow.Cells["ExternalName"].Style.BackColor = r.DirtyExternalName ? DirtyBg : CleanBg;
        }

        void UpdateSummary()
        {
            int dirty = rows.Count(r => r.Dirty);
            int outstanding = rows.Count(r => !string.IsNullOrEmpty(r.Issues));
            lblSummary.Text = rows.Count + " key(s) flagged"
                + "   ·   " + dirty + " edited"
                + "   ·   " + outstanding + " still have outstanding issues";
            btnApply.Enabled = dirty > 0;
        }

        List<Row> Collect()
        {
            var list = new List<Row>();
            IEnumerable<object> tables;
            if (singleTable != null) tables = new[] { singleTable };
            else tables = DictModel.GetTables(dict);

            foreach (var t in tables)
            {
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                var keys = DictModel.GetProp(t, "Keys") as IEnumerable;
                if (keys == null) continue;

                // Detect duplicate-component signatures within this table so we can flag them.
                var sigs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var snapshot = new List<Tuple<object, string, string>>();
                foreach (var k in keys)
                {
                    if (k == null) continue;
                    var kName = DictModel.AsString(DictModel.GetProp(k, "Name")) ?? "";
                    if (string.IsNullOrEmpty(kName)) continue;
                    var comps = KeyComponentList(k);
                    var sig   = string.Join(",", comps.ToArray());
                    snapshot.Add(Tuple.Create(k, kName, sig));
                    if (!string.IsNullOrEmpty(sig) && !sigs.ContainsKey(sig)) sigs[sig] = kName;
                }

                foreach (var tup in snapshot)
                {
                    var k = tup.Item1;
                    var kName = tup.Item2;
                    var sig = tup.Item3;
                    var row = new Row
                    {
                        Key = k,
                        Table = tName,
                        Name = kName,
                        KeyType = DictModel.AsString(DictModel.GetProp(k, "KeyType")) ?? "Key",
                        Components = string.IsNullOrEmpty(sig) ? "" : sig.Replace(",", " + "),
                        Unique    = Yn(DictModel.AsString(DictModel.GetProp(k, "AttributeUnique"))),
                        Primary   = Yn(DictModel.AsString(DictModel.GetProp(k, "AttributePrimary"))),
                        OrigExternalName = DictModel.AsString(DictModel.GetProp(k, "ExternalName")) ?? "",
                    };
                    row.ExternalName = row.OrigExternalName;
                    row.Issues = BuildIssuesFromSig(row, sigs, sig);
                    if (string.IsNullOrEmpty(row.Issues)) continue;
                    list.Add(row);
                }
            }
            return list.OrderBy(r => r.Table, StringComparer.OrdinalIgnoreCase)
                       .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                       .ToList();
        }

        static string BuildIssues(Row r)
        {
            var issues = new List<string>();
            if (string.IsNullOrWhiteSpace(r.ExternalName)) issues.Add("no ExternalName");
            if (string.IsNullOrEmpty(r.Components))        issues.Add("no components");
            return string.Join("; ", issues.ToArray());
        }

        static string BuildIssuesFromSig(Row r, Dictionary<string, string> sigs, string sig)
        {
            var issues = new List<string>();
            if (string.IsNullOrWhiteSpace(r.ExternalName)) issues.Add("no ExternalName");
            if (string.IsNullOrEmpty(sig))                  issues.Add("no components");
            string prior;
            if (!string.IsNullOrEmpty(sig) && sigs.TryGetValue(sig, out prior)
                && !string.Equals(prior, r.Name, StringComparison.OrdinalIgnoreCase))
                issues.Add("duplicates " + prior);
            return string.Join("; ", issues.ToArray());
        }

        static string Yn(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return string.Equals(v, "True", StringComparison.OrdinalIgnoreCase) ? "Y" : "";
        }

        static List<string> KeyComponentList(object key)
        {
            var list = new List<string>();
            string[] candidates = { "Components", "KeyComponents", "Fields", "KeyFields", "Segments" };
            IEnumerable en = null;
            foreach (var c in candidates)
            {
                en = DictModel.GetProp(key, c) as IEnumerable;
                if (en != null && !(en is string)) break;
                en = null;
            }
            if (en == null) return list;
            foreach (var comp in en)
            {
                if (comp == null) continue;
                var fld = DictModel.GetProp(comp, "Field") ?? DictModel.GetProp(comp, "DDField");
                var n = fld != null
                    ? DictModel.AsString(DictModel.GetProp(fld, "Label"))
                    : DictModel.AsString(DictModel.GetProp(comp, "Label")) ?? DictModel.AsString(DictModel.GetProp(comp, "Name"));
                if (!string.IsNullOrEmpty(n)) list.Add(n);
            }
            return list;
        }

        void Apply()
        {
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();

            var dirty = rows.Where(r => r.Dirty).ToList();
            if (dirty.Count == 0) return;

            var confirm = MessageBox.Show(this,
                dirty.Count + " key(s) will be updated.\r\n"
                + "  - ExternalName:  " + dirty.Count(r => r.DirtyExternalName) + "\r\n\r\n"
                + "A .tasker-bak-<timestamp> backup of the .DCT is written first.\r\n\r\nProceed?",
                "Fix keys", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK) return;

            var mr = new FieldMutator.Result();
            FieldMutator.Backup(DictModel.GetDictionaryFileName(dict), mr);
            if (mr.BackupFailed)
            {
                MessageBox.Show(this, "Backup failed — aborting.\r\n"
                    + string.Join("\r\n", mr.Messages.ToArray()), "Fix keys",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            foreach (var row in dirty)
            {
                var tag = row.Table + "." + row.Name;
                if (row.DirtyExternalName)
                {
                    if (FieldMutator.SetStringProp(row.Key, "ExternalName", row.ExternalName ?? "", mr, tag + ".ExternalName"))
                    {
                        row.OrigExternalName = row.ExternalName;
                        mr.Changed++;
                    }
                    else mr.Failed++;
                }
            }
            FieldMutator.ForceMarkDirty(dict, DictModel.GetActiveDictionaryView(), mr);

            MessageBox.Show(this,
                "Applied:   " + mr.Changed + "\r\n"
                + "Failed:   " + mr.Failed + "\r\n"
                + (string.IsNullOrEmpty(mr.BackupPath) ? "" : "Backup:  " + mr.BackupPath + "\r\n")
                + "\r\nThe dictionary is now DIRTY. Press Ctrl+S in Clarion to save.",
                "Fix keys",
                MessageBoxButtons.OK, mr.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            for (int i = 0; i < grid.Rows.Count; i++)
            {
                var gRow = grid.Rows[i];
                var refreshed = gRow.Tag as Row;
                if (refreshed == null) continue;
                refreshed.Issues = BuildIssues(refreshed);
                gRow.Cells["Issues"].Value = refreshed.Issues;
                PaintRow(gRow, refreshed);
            }
            UpdateSummary();
        }
    }
}
