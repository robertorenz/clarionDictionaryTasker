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
        ComboBox     cbStyle, cbShow, cbOwner, cbKey;
        List<Row>    rows = new List<Row>();
        Dictionary<string, List<string>> otherOwners;   // ExternalName -> owners NOT in scope

        enum ShowFilter  { All, BlankOnly, DuplicatesOnly }
        enum OwnerSource { TableName, Prefix }
        enum KeySource   { LabelOnly, FullName }

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
            public string Table, Prefix, Name, KeyType, Components;
            public string Unique, Primary;
            public string OrigExternalName;
            public string ExternalName;
            public string Issues;
            public bool   IsBlank;        // ExternalName empty after edits
            public bool   IsDuplicated;   // collides with another key's ExternalName

            public bool DirtyExternalName
            {
                get { return !string.Equals(ExternalName ?? "", OrigExternalName ?? "", StringComparison.Ordinal); }
            }
            public bool Dirty { get { return DirtyExternalName; } }
            public string Owner { get { return (Table ?? "") + "." + (Name ?? ""); } }
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
            cbStyle = new ComboBox { Left = 100, Top = 4, Width = 240, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            cbStyle.Items.Add("UPPER_SNAKE");
            cbStyle.Items.Add("lower_snake");
            cbStyle.Items.Add("Camel_Snake");
            cbStyle.Items.Add("Pascal");
            cbStyle.Items.Add("camel");
            cbStyle.Items.Add("idx_snake");
            cbStyle.Items.Add("Key only (no owner)");
            cbStyle.Items.Add("Owner prefix + key as-is");
            cbStyle.SelectedIndex = ClampIndex(Settings.FixKeysStyle, cbStyle.Items.Count);
            cbStyle.SelectedIndexChanged += delegate { Settings.FixKeysStyle = cbStyle.SelectedIndex; };
            var lblOwner = new Label { Text = "Owner:", Left = 352, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            cbOwner = new ComboBox { Left = 400, Top = 4, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            cbOwner.Items.Add("Table name");
            cbOwner.Items.Add("Prefix");
            cbOwner.SelectedIndex = ClampIndex(Settings.FixKeysOwner, cbOwner.Items.Count);
            cbOwner.SelectedIndexChanged += delegate { Settings.FixKeysOwner = cbOwner.SelectedIndex; };
            var lblKey = new Label { Text = "Key:", Left = 532, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            cbKey = new ComboBox { Left = 564, Top = 4, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            cbKey.Items.Add("Label only  (after the ':')");
            cbKey.Items.Add("Full key name");
            cbKey.SelectedIndex = ClampIndex(Settings.FixKeysKey, cbKey.Items.Count);
            cbKey.SelectedIndexChanged += delegate { Settings.FixKeysKey = cbKey.SelectedIndex; };
            btnAutoBlank = new Button { Text = "Fill blanks", Left = 736, Top = 2, Width = 100, Height = 30, FlatStyle = FlatStyle.System };
            btnAutoBlank.Click += delegate { AutoFill(onlyBlanks: true); };
            btnAutoAll   = new Button { Text = "Fill all (overwrite)", Left = 842, Top = 2, Width = 150, Height = 30, FlatStyle = FlatStyle.System };
            btnAutoAll.Click += delegate { AutoFill(onlyBlanks: false); };
            autoBar.Controls.Add(lblStyle);
            autoBar.Controls.Add(cbStyle);
            autoBar.Controls.Add(lblOwner);
            autoBar.Controls.Add(cbOwner);
            autoBar.Controls.Add(lblKey);
            autoBar.Controls.Add(cbKey);
            autoBar.Controls.Add(btnAutoBlank);
            autoBar.Controls.Add(btnAutoAll);

            var filterBar = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = BgColor, Padding = new Padding(16, 6, 16, 4) };
            var lblShow = new Label { Text = "Show:", Left = 0, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            cbShow = new ComboBox { Left = 50, Top = 4, Width = 240, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            cbShow.Items.Add("All issues");
            cbShow.Items.Add("Blank ExternalName only");
            cbShow.Items.Add("Duplicated ExternalName only");
            cbShow.SelectedIndex = ClampIndex(Settings.FixKeysShow, cbShow.Items.Count);
            cbShow.SelectedIndexChanged += delegate
            {
                Settings.FixKeysShow = cbShow.SelectedIndex;
                RenderGrid();
            };
            filterBar.Controls.Add(lblShow);
            filterBar.Controls.Add(cbShow);

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
            Controls.Add(filterBar);
            Controls.Add(autoBar);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        void AutoFill(bool onlyBlanks)
        {
            var style       = (NamingStyle)cbStyle.SelectedIndex;
            var ownerSource = (OwnerSource)cbOwner.SelectedIndex;
            var keySource   = (KeySource)cbKey.SelectedIndex;
            int touched = 0;
            // Iterate the full rows list so offscreen rows (filtered out of the
            // grid right now) still get auto-filled.
            foreach (var r in rows)
            {
                if (onlyBlanks && !string.IsNullOrWhiteSpace(r.ExternalName)) continue;
                var ownerSegment = OwnerSegment(r, ownerSource);
                var keySegment   = KeySegmentOf(r, keySource);
                var suggested = MakeName(style, ownerSegment, keySegment);
                if (string.Equals(suggested, r.ExternalName, StringComparison.Ordinal)) continue;
                r.ExternalName = suggested;
                touched++;
            }
            RecomputeIssuesForAllRows();
            RenderGrid();
            if (touched == 0)
                MessageBox.Show(this, onlyBlanks
                    ? "No blank ExternalNames to fill."
                    : "No rows changed (values already match the chosen style).",
                    "Fix keys", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Clamp a persisted index to the current combo's item range — guards
        // against a persisted value from an older version that had more options.
        static int ClampIndex(int value, int itemCount)
        {
            if (itemCount <= 0) return 0;
            if (value < 0) return 0;
            if (value >= itemCount) return 0;
            return value;
        }

        // Resolve the "owner" text based on the dropdown — prefix when the user
        // asked for it (falling back to table name if the table has no prefix),
        // otherwise the table name.
        static string OwnerSegment(Row r, OwnerSource src)
        {
            if (src == OwnerSource.Prefix)
            {
                // Clarion prefixes sometimes carry a trailing ':' (used in field refs
                // like CLI:NAME). Strip it so it doesn't show up in the index name.
                var p = (r.Prefix ?? "").TrimEnd(':');
                if (!string.IsNullOrWhiteSpace(p)) return p;
            }
            return r.Table ?? "";
        }

        // "Label only" strips the PREFIX:  part of a Clarion key name — the chunk
        // up to and including the last ':'. So BIT:guidkey -> guidkey,
        // CLI:NAME:UK -> UK (whatever follows the last colon). SQL index names
        // can't contain ':' anyway, so keeping it would always be wrong.
        static string KeySegmentOf(Row r, KeySource src)
        {
            var name = r.Name ?? "";
            if (src == KeySource.FullName) return name;
            var idx = name.LastIndexOf(':');
            return idx >= 0 && idx + 1 < name.Length ? name.Substring(idx + 1) : name;
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
            RenderGrid();
        }

        void RenderGrid()
        {
            RecomputeIssuesForAllRows();
            var filter = (ShowFilter)cbShow.SelectedIndex;
            grid.Rows.Clear();
            foreach (var r in rows)
            {
                if (filter == ShowFilter.BlankOnly      && !r.IsBlank)      continue;
                if (filter == ShowFilter.DuplicatesOnly && !r.IsDuplicated) continue;
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
            if (colName == "ExternalName") r.ExternalName = v;

            // Editing one ExternalName can change duplicate status for other rows
            // too (e.g. fixing a collision clears flags on both sides), so recompute
            // the whole grid rather than just this row.
            RecomputeIssuesForAllRows();
            for (int i = 0; i < grid.Rows.Count; i++)
            {
                var gr = grid.Rows[i];
                var row = gr.Tag as Row;
                if (row == null) continue;
                gr.Cells["Issues"].Value = row.Issues;
                PaintRow(gr, row);
            }
            UpdateSummary();
        }

        static void PaintRow(DataGridViewRow gRow, Row r)
        {
            gRow.Cells["ExternalName"].Style.BackColor = r.DirtyExternalName ? DirtyBg : CleanBg;
        }

        void UpdateSummary()
        {
            int dirty = rows.Count(r => r.Dirty);
            int blank = rows.Count(r => r.IsBlank);
            int dup   = rows.Count(r => r.IsDuplicated);
            int outstanding = rows.Count(r => !string.IsNullOrEmpty(r.Issues));
            lblSummary.Text = rows.Count + " key(s) flagged"
                + "   ·   blank: " + blank
                + "   ·   duplicated: " + dup
                + "   ·   " + dirty + " edited"
                + "   ·   " + outstanding + " still have outstanding issues";
            btnApply.Enabled = dirty > 0;
        }

        List<Row> Collect()
        {
            var list = new List<Row>();

            // Which tables are in-scope (just the user-selected one, or all of them
            // when the dialog was launched dict-wide). We need this so we know which
            // keys get editable rows vs. which are "external" context for duplicate
            // detection only.
            var scoped = new HashSet<object>();
            if (singleTable != null) scoped.Add(singleTable);
            else foreach (var t in DictModel.GetTables(dict)) scoped.Add(t);

            // Snapshot every OUT-OF-SCOPE key's ExternalName so we can diff the
            // in-scope keys against them. Stays constant while the dialog is open
            // (we don't edit them) and spares us a full dict walk on every cell edit.
            otherOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in DictModel.GetTables(dict))
            {
                if (scoped.Contains(t)) continue;
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                var keys = DictModel.GetProp(t, "Keys") as IEnumerable;
                if (keys == null) continue;
                foreach (var k in keys)
                {
                    if (k == null) continue;
                    var ext = DictModel.AsString(DictModel.GetProp(k, "ExternalName")) ?? "";
                    if (string.IsNullOrWhiteSpace(ext)) continue;
                    var kName = DictModel.AsString(DictModel.GetProp(k, "Name")) ?? "";
                    if (string.IsNullOrEmpty(kName)) continue;
                    List<string> bucket;
                    if (!otherOwners.TryGetValue(ext, out bucket))
                        otherOwners[ext] = bucket = new List<string>();
                    bucket.Add(tName + "." + kName);
                }
            }

            // Per-table component-signature dedup + candidate rows for in-scope tables.
            foreach (var t in scoped)
            {
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                var keys = DictModel.GetProp(t, "Keys") as IEnumerable;
                if (keys == null) continue;

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
                        Prefix = DictModel.AsString(DictModel.GetProp(t, "Prefix")) ?? "",
                        Name = kName,
                        KeyType = DictModel.AsString(DictModel.GetProp(k, "KeyType")) ?? "Key",
                        Components = string.IsNullOrEmpty(sig) ? "" : sig.Replace(",", " + "),
                        Unique    = Yn(DictModel.AsString(DictModel.GetProp(k, "AttributeUnique"))),
                        Primary   = Yn(DictModel.AsString(DictModel.GetProp(k, "AttributePrimary"))),
                        OrigExternalName = DictModel.AsString(DictModel.GetProp(k, "ExternalName")) ?? "",
                    };
                    row.ExternalName = row.OrigExternalName;
                    // Remember this row's in-table duplicate-sig prior (for recomputes).
                    if (!string.IsNullOrEmpty(sig) && sigs.TryGetValue(sig, out string prior)
                        && !string.Equals(prior, kName, StringComparison.OrdinalIgnoreCase))
                        row.KeyType = row.KeyType + "*"; // sentinel (unused; just keeps context)

                    list.Add(row);
                }
            }

            // Filter to rows that actually have issues after the full cross-dict pass.
            RecomputeIssuesFor(list);
            return list.Where(r => !string.IsNullOrEmpty(r.Issues))
                       .OrderBy(r => r.Table, StringComparer.OrdinalIgnoreCase)
                       .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                       .ToList();
        }

        void RecomputeIssuesForAllRows()
        {
            RecomputeIssuesFor(rows);
        }

        // Reapply the three lint rules across the full row set using current
        // (possibly edited) ExternalNames. IN-scope collisions are computed from
        // the rows list; OUT-of-scope collisions come from the otherOwners snapshot.
        void RecomputeIssuesFor(IList<Row> all)
        {
            if (all == null) return;
            if (otherOwners == null) otherOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            // Build the "in-scope name -> owners" map from current edits.
            var inScope = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in all)
            {
                if (string.IsNullOrWhiteSpace(r.ExternalName)) continue;
                List<string> bucket;
                if (!inScope.TryGetValue(r.ExternalName, out bucket))
                    inScope[r.ExternalName] = bucket = new List<string>();
                bucket.Add(r.Owner);
            }

            foreach (var r in all)
            {
                var issues = new List<string>();
                r.IsBlank = string.IsNullOrWhiteSpace(r.ExternalName);
                r.IsDuplicated = false;

                if (r.IsBlank) issues.Add("no ExternalName");
                if (string.IsNullOrEmpty(r.Components)) issues.Add("no components");

                if (!r.IsBlank)
                {
                    var ext = r.ExternalName;
                    var others = new List<string>();
                    List<string> inScopeOwners;
                    if (inScope.TryGetValue(ext, out inScopeOwners) && inScopeOwners.Count > 1)
                    {
                        foreach (var o in inScopeOwners)
                            if (!string.Equals(o, r.Owner, StringComparison.OrdinalIgnoreCase)) others.Add(o);
                    }
                    List<string> extOwners;
                    if (otherOwners.TryGetValue(ext, out extOwners))
                        foreach (var o in extOwners) others.Add(o);

                    if (others.Count > 0)
                    {
                        r.IsDuplicated = true;
                        issues.Add("duplicates: " + string.Join(", ", others.ToArray()));
                    }
                }

                r.Issues = string.Join("; ", issues.ToArray());
            }
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

            RecomputeIssuesForAllRows();
            RenderGrid();
        }
    }
}
