using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Editable grid of fields whose Description or ScreenPicture needs fixing.
    // Populated from the same rules LintEngine uses for the "no-description",
    // "no-picture", and "picture-*-shape" findings. Description and Picture
    // cells are editable; everything else is read-only. Apply writes each
    // changed row back through FieldMutator, with a .tasker-bak-<ts> backup
    // of the .DCT taken first. Available from:
    //   - the Lint report dialog ("Fix fields..." button)
    //   - right-click → Fix fields... on any table list
    internal class LintFixItDialog : Form
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
        ComboBox     cbDescStyle;
        List<Row>    rows = new List<Row>();

        enum DescStyle
        {
            Humanized,   // first_name   -> First name
            Heading,     // copy DDField.ColumnHeading
            Prompt,      // copy DDField.PromptText
            BestAvailable, // heading | prompt | humanized — first non-blank
            Verbatim     // label as-is (e.g. CREATED_BY -> CREATED_BY)
        }

        sealed class Row
        {
            public object Field;
            public string Table, Label, DataType;
            public string OrigDescription, OrigPicture;
            public string Description, Picture;   // current editable values
            public string Issues;

            public bool DirtyDescription { get { return !string.Equals(Description ?? "", OrigDescription ?? "", StringComparison.Ordinal); } }
            public bool DirtyPicture     { get { return !string.Equals(Picture     ?? "", OrigPicture     ?? "", StringComparison.Ordinal); } }
            public bool Dirty            { get { return DirtyDescription || DirtyPicture; } }
        }

        public LintFixItDialog(object dict) : this(dict, null) { }
        public LintFixItDialog(object dict, object singleTable)
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
            Text = "Fix fields - " + scope;
            Width = 1180; Height = 720;
            MinimumSize = new Size(920, 480);
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
                    ? "Fix fields   dictionary: " + DictModel.GetDictionaryName(dict)
                    : "Fix fields   table: " + scope
            };

            var autoBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = BgColor, Padding = new Padding(16, 8, 16, 4) };
            var lblStyle = new Label { Text = "Auto-fill description:", Left = 0, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            cbDescStyle = new ComboBox { Left = 138, Top = 4, Width = 340, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            cbDescStyle.Items.Add("Best available  (heading | prompt | humanized label)");
            cbDescStyle.Items.Add("Humanized label   (first_name -> First name)");
            cbDescStyle.Items.Add("Column heading    (copy DDField.ColumnHeading)");
            cbDescStyle.Items.Add("Prompt text       (copy DDField.PromptText)");
            cbDescStyle.Items.Add("Label verbatim    (copy the label as-is)");
            var savedStyle = Settings.FixFieldsDescStyle;
            cbDescStyle.SelectedIndex = (savedStyle >= 0 && savedStyle < cbDescStyle.Items.Count) ? savedStyle : 0;
            cbDescStyle.SelectedIndexChanged += delegate { Settings.FixFieldsDescStyle = cbDescStyle.SelectedIndex; };
            btnAutoBlank = new Button { Text = "Fill blanks", Left = 490, Top = 2, Width = 110, Height = 30, FlatStyle = FlatStyle.System };
            btnAutoBlank.Click += delegate { AutoFillDescriptions(onlyBlanks: true); };
            btnAutoAll = new Button { Text = "Fill all (overwrite)", Left = 606, Top = 2, Width = 160, Height = 30, FlatStyle = FlatStyle.System };
            btnAutoAll.Click += delegate { AutoFillDescriptions(onlyBlanks: false); };
            autoBar.Controls.Add(lblStyle);
            autoBar.Controls.Add(cbDescStyle);
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
            AddCol("Table",       160, true);
            AddCol("Field",       200, true);
            AddCol("Type",        90,  true);
            AddCol("Description", 340, false);
            AddCol("Picture",     140, false);
            AddCol("Issues",      260, true);
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

        void AutoFillDescriptions(bool onlyBlanks)
        {
            var style = (DescStyle)cbDescStyle.SelectedIndex;
            int touched = 0;
            for (int i = 0; i < grid.Rows.Count; i++)
            {
                var gRow = grid.Rows[i];
                var r = gRow.Tag as Row;
                if (r == null) continue;
                if (onlyBlanks && !string.IsNullOrWhiteSpace(r.Description)) continue;
                var suggested = MakeDesc(style, r);
                if (string.IsNullOrEmpty(suggested)) continue;
                if (string.Equals(suggested, r.Description, StringComparison.Ordinal)) continue;
                r.Description = suggested;
                gRow.Cells["Description"].Value = suggested;
                r.Issues = BuildIssues(r.DataType, r.Label, r.Description, r.Picture);
                gRow.Cells["Issues"].Value = r.Issues;
                PaintRow(gRow, r);
                touched++;
            }
            UpdateSummary();
            if (touched == 0)
                MessageBox.Show(this, onlyBlanks
                    ? "No blank descriptions to fill (or the chosen source was empty for every blank row)."
                    : "No rows changed — all rows already match the chosen source, or the source is blank everywhere.",
                    "Fix fields", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static string MakeDesc(DescStyle style, Row r)
        {
            var heading = DictModel.AsString(DictModel.GetProp(r.Field, "ColumnHeading")) ?? "";
            var prompt  = DictModel.AsString(DictModel.GetProp(r.Field, "PromptText")) ?? "";
            switch (style)
            {
                case DescStyle.Humanized:     return Humanize(r.Label);
                case DescStyle.Heading:       return heading;
                case DescStyle.Prompt:        return prompt;
                case DescStyle.Verbatim:      return r.Label ?? "";
                case DescStyle.BestAvailable:
                    if (!string.IsNullOrWhiteSpace(heading)) return heading;
                    if (!string.IsNullOrWhiteSpace(prompt))  return prompt;
                    return Humanize(r.Label);
                default: return "";
            }
        }

        // first_name -> First name
        // CreatedOn  -> Created on
        // NO_DE_RECUPS -> No de recups
        // IDField    -> Id field
        // shortname  -> Shortname
        static string Humanize(string label)
        {
            if (string.IsNullOrEmpty(label)) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < label.Length; i++)
            {
                var c = label[i];
                if (c == '_' || c == '-' || c == '.') { sb.Append(' '); continue; }
                bool splitBefore = false;
                if (i > 0 && char.IsUpper(c))
                {
                    var prev = label[i - 1];
                    var next = i + 1 < label.Length ? label[i + 1] : (char)0;
                    // "camelCase" -> "camel Case"  or  "IDField" -> "ID Field"
                    if (char.IsLower(prev)) splitBefore = true;
                    else if (char.IsUpper(prev) && next != 0 && char.IsLower(next)) splitBefore = true;
                }
                if (splitBefore) sb.Append(' ');
                sb.Append(char.ToLowerInvariant(c));
            }
            var s = sb.ToString().Trim();
            while (s.IndexOf("  ", StringComparison.Ordinal) >= 0) s = s.Replace("  ", " ");
            if (s.Length == 0) return "";
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
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
                var idx = grid.Rows.Add(r.Table, r.Label, r.DataType, r.Description, r.Picture, r.Issues);
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
            if (colName == "Description")
            {
                r.Description = v;
            }
            else if (colName == "Picture")
            {
                r.Picture = v;
            }
            // Re-evaluate issues with the edited values so the user sees progress live.
            r.Issues = BuildIssues(r.DataType, r.Label, r.Description, r.Picture);
            gRow.Cells["Issues"].Value = r.Issues;
            PaintRow(gRow, r);
            UpdateSummary();
        }

        static void PaintRow(DataGridViewRow gRow, Row r)
        {
            // Highlight the dirty cells specifically; clean rows are white.
            gRow.Cells["Description"].Style.BackColor = r.DirtyDescription ? DirtyBg : CleanBg;
            gRow.Cells["Picture"].Style.BackColor     = r.DirtyPicture     ? DirtyBg : CleanBg;
        }

        void UpdateSummary()
        {
            int dirty = rows.Count(r => r.Dirty);
            int outstanding = rows.Count(r => !string.IsNullOrEmpty(r.Issues));
            lblSummary.Text = rows.Count + " field(s) flagged"
                + "   ·   " + dirty + " edited"
                + "   ·   " + outstanding + " still have outstanding issues"
                + "   ·   edits are applied only after you click \"Apply changes...\"";
            btnApply.Enabled = dirty > 0;
        }

        List<Row> Collect()
        {
            var list = new List<Row>();
            IEnumerable<object> tables;
            if (singleTable != null) tables = new[] { singleTable };
            else tables = DictModel.GetTables(dict).Where(t => !Settings.BatchExcludeAliases || !DictModel.IsAlias(t));

            foreach (var t in tables)
            {
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                var fields = DictModel.GetProp(t, "Fields") as IEnumerable;
                if (fields == null) continue;
                foreach (var f in fields)
                {
                    if (f == null) continue;
                    var label = DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "";
                    if (string.IsNullOrEmpty(label)) continue;
                    var dt   = DictModel.AsString(DictModel.GetProp(f, "DataType")) ?? "";
                    var desc = DictModel.AsString(DictModel.GetProp(f, "Description")) ?? "";
                    var pic  = DictModel.AsString(DictModel.GetProp(f, "ScreenPicture")) ?? "";

                    var issues = BuildIssues(dt, label, desc, pic);
                    if (string.IsNullOrEmpty(issues)) continue;

                    list.Add(new Row
                    {
                        Field = f, Table = tName, Label = label, DataType = dt,
                        OrigDescription = desc, OrigPicture = pic,
                        Description = desc, Picture = pic,
                        Issues = issues
                    });
                }
            }
            return list.OrderBy(r => r.Table, StringComparer.OrdinalIgnoreCase)
                       .ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
                       .ToList();
        }

        // Same shape-rule set LintEngine ships; inlined here so the grid can re-run it
        // on the edited values as the user types and the "Issues" column updates live.
        static string BuildIssues(string dataType, string label, string description, string picture)
        {
            var out1 = new List<string>();
            var dt = (dataType ?? "").ToUpperInvariant();
            var p  = (picture ?? "").ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(description)) out1.Add("no description");

            // picture category
            if (string.IsNullOrEmpty(picture)
                && dt != "MEMO" && dt != "BLOB")
                out1.Add("no picture");
            else if (!string.IsNullOrEmpty(picture))
            {
                if (dt == "DATE" && !p.StartsWith("@d"))           out1.Add("DATE needs @d*");
                else if (dt == "TIME" && !p.StartsWith("@t"))      out1.Add("TIME needs @t*");
                else if ((dt == "DECIMAL" || dt == "PDECIMAL" || dt == "REAL" || dt == "SREAL")
                      && !p.StartsWith("@n"))                      out1.Add("numeric needs @n*");
                else if ((dt == "LONG" || dt == "ULONG")
                      && !p.StartsWith("@n") && !p.StartsWith("@d") && !p.StartsWith("@t"))
                    out1.Add("LONG needs @n*, @d*, or @t*");
                else if ((dt == "BYTE" || dt == "SHORT" || dt == "USHORT")
                      && !p.StartsWith("@n"))                      out1.Add("integer needs @n*");
                else if ((dt == "STRING" || dt == "CSTRING" || dt == "PSTRING")
                      && (p.StartsWith("@d") || p.StartsWith("@t") || p.StartsWith("@n")))
                    out1.Add("STRING with non-string picture");
                else if ((dt == "DECIMAL" || dt == "PDECIMAL" || dt == "REAL" || dt == "SREAL")
                      && LooksLikeMoney(label) && p.IndexOf('$') < 0)
                    out1.Add("money-ish: consider @n$*.*");
            }
            return string.Join("; ", out1.ToArray());
        }

        static bool LooksLikeMoney(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;
            var l = label.ToLowerInvariant();
            string[] hints = { "amount", "amt", "price", "cost", "total", "balance",
                               "money", "salary", "fee", "charge", "payment",
                               "importe", "monto", "precio", "costo" };
            for (int i = 0; i < hints.Length; i++)
                if (l.IndexOf(hints[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        void Apply()
        {
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();

            var dirty = rows.Where(r => r.Dirty).ToList();
            if (dirty.Count == 0) return;

            int descChanges = dirty.Count(r => r.DirtyDescription);
            int picChanges  = dirty.Count(r => r.DirtyPicture);

            var confirm = MessageBox.Show(this,
                dirty.Count + " field(s) will be updated.\r\n"
                + "  - Description: " + descChanges + "\r\n"
                + "  - Picture:     " + picChanges + "\r\n\r\n"
                + "A .tasker-bak-<timestamp> backup of the .DCT is written first.\r\n\r\nProceed?",
                "Fix fields", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK) return;

            var mr = new FieldMutator.Result();
            FieldMutator.Backup(DictModel.GetDictionaryFileName(dict), mr);
            if (mr.BackupFailed)
            {
                MessageBox.Show(this, "Backup failed — aborting.\r\n"
                    + string.Join("\r\n", mr.Messages.ToArray()), "Fix fields",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            foreach (var row in dirty)
            {
                var tag = row.Table + "." + row.Label;
                if (row.DirtyDescription)
                {
                    if (FieldMutator.SetStringProp(row.Field, "Description", row.Description ?? "", mr, tag + ".desc"))
                    {
                        row.OrigDescription = row.Description;
                        mr.Changed++;
                    }
                    else mr.Failed++;
                }
                if (row.DirtyPicture)
                {
                    if (FieldMutator.SetStringProp(row.Field, "ScreenPicture", row.Picture ?? "", mr, tag + ".picture"))
                    {
                        row.OrigPicture = row.Picture;
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
                "Fix fields",
                MessageBoxButtons.OK, mr.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            // Refresh the rows' dirty highlighting + re-evaluate issues against the now-committed values.
            for (int i = 0; i < grid.Rows.Count; i++)
            {
                var gRow = grid.Rows[i];
                var refreshed = gRow.Tag as Row;
                if (refreshed == null) continue;
                refreshed.Issues = BuildIssues(refreshed.DataType, refreshed.Label, refreshed.Description, refreshed.Picture);
                gRow.Cells["Issues"].Value = refreshed.Issues;
                PaintRow(gRow, refreshed);
            }
            UpdateSummary();
        }
    }
}
