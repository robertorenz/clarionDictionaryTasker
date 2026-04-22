using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Select every field whose label matches a regex, change type / size /
    // picture in one shot. Any of the new values can be blank = leave it alone.
    // Preview the plan, then apply through FieldMutator.
    internal class BatchRetypeDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        readonly object dict;
        TextBox  txtLabelPattern, txtTableFilter;
        ComboBox cbNewType;
        TextBox  txtNewSize, txtNewPicture;
        CheckBox chkIgnoreCase, chkExcludeAliases;
        ListView lv;
        Label    lblSummary;
        Button   btnApply;

        sealed class Edit
        {
            public object Field;
            public string TableName, FieldLabel;
            public string BeforeType, BeforeSize, BeforePicture;
            public string AfterType,  AfterSize,  AfterPicture;
        }

        List<Edit> lastPlan = new List<Edit>();

        public BatchRetypeDialog(object dict) { this.dict = dict; BuildUi(); }

        void BuildUi()
        {
            Text = "Batch retype fields - " + DictModel.GetDictionaryName(dict);
            Width = 1160; Height = 740;
            MinimumSize = new Size(900, 500);
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
                Text = "Batch retype fields   " + DictModel.GetDictionaryName(dict)
            };

            var row1 = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = BgColor, Padding = new Padding(16, 8, 16, 0) };
            var lblL = new Label { Text = "Label pattern (regex):", Left = 4, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            txtLabelPattern = new TextBox { Left = 140, Top = 4, Width = 260, Font = new Font("Consolas", 10F) };
            var lblT = new Label { Text = "Table filter (blank = all):", Left = 420, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            txtTableFilter = new TextBox { Left = 574, Top = 4, Width = 220, Font = new Font("Consolas", 10F) };
            chkIgnoreCase = new CheckBox { Text = "Case-insensitive", Left = 810, Top = 6, AutoSize = true, Checked = true, Font = new Font("Segoe UI", 9F) };
            chkExcludeAliases = new CheckBox { Text = "Exclude aliases", Left = 940, Top = 6, AutoSize = true, Checked = Settings.BatchExcludeAliases, Font = new Font("Segoe UI", 9F) };
            chkExcludeAliases.CheckedChanged += delegate { Settings.BatchExcludeAliases = chkExcludeAliases.Checked; };
            row1.Controls.Add(lblL); row1.Controls.Add(txtLabelPattern);
            row1.Controls.Add(lblT); row1.Controls.Add(txtTableFilter);
            row1.Controls.Add(chkIgnoreCase);
            row1.Controls.Add(chkExcludeAliases);

            var row2 = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = BgColor, Padding = new Padding(16, 6, 16, 0) };
            var lblNT = new Label { Text = "New type:",    Left = 4,   Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            cbNewType = new ComboBox { Left = 66, Top = 4, Width = 140, Font = new Font("Segoe UI", 9F) };
            cbNewType.Items.AddRange(new object[] { "", "STRING", "CSTRING", "PSTRING", "BYTE", "SHORT", "USHORT", "LONG", "ULONG", "DATE", "TIME", "REAL", "SREAL", "DECIMAL", "PDECIMAL", "MEMO" });
            cbNewType.SelectedIndex = 0;
            var lblNS = new Label { Text = "Size:",        Left = 216, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            txtNewSize    = new TextBox { Left = 250, Top = 4, Width = 60, Font = new Font("Segoe UI", 10F) };
            var lblNP = new Label { Text = "Picture:",     Left = 326, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            txtNewPicture = new TextBox { Left = 374, Top = 4, Width = 160, Font = new Font("Consolas", 10F) };
            var btnPrev = new Button { Text = "Preview",   Left = 554, Top = 2, Width = 100, Height = 30, FlatStyle = FlatStyle.System };
            btnPrev.Click += delegate { Preview(); };
            row2.Controls.Add(lblNT); row2.Controls.Add(cbNewType);
            row2.Controls.Add(lblNS); row2.Controls.Add(txtNewSize);
            row2.Controls.Add(lblNP); row2.Controls.Add(txtNewPicture);
            row2.Controls.Add(btnPrev);

            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 24,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 4, 0, 0),
                Text = "Leave any of type/size/picture blank to preserve the current value."
            };

            lv = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true, BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };
            lv.Columns.Add("Table",          160);
            lv.Columns.Add("Field",          180);
            lv.Columns.Add("Type: before",    90);
            lv.Columns.Add("Type: after",     90);
            lv.Columns.Add("Size: before",    80, HorizontalAlignment.Right);
            lv.Columns.Add("Size: after",     80, HorizontalAlignment.Right);
            lv.Columns.Add("Picture: before", 140);
            lv.Columns.Add("Picture: after",  140);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            btnApply = new Button { Text = "Apply plan...", Width = 150, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System, Enabled = false };
            btnApply.Click += delegate { Apply(); };
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(btnApply);

            Controls.Add(lv);
            Controls.Add(bottom);
            Controls.Add(lblSummary);
            Controls.Add(row2);
            Controls.Add(row1);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        void Preview()
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            lastPlan.Clear();
            btnApply.Enabled = false;

            var labelPat = txtLabelPattern.Text ?? "";
            if (string.IsNullOrEmpty(labelPat)) { lv.EndUpdate(); lblSummary.Text = "Enter a label pattern."; return; }
            var opts = chkIgnoreCase.Checked ? RegexOptions.IgnoreCase : RegexOptions.None;
            Regex labelRe, tableRe = null;
            try
            {
                labelRe = new Regex(labelPat, opts);
                var tf = (txtTableFilter.Text ?? "").Trim();
                if (!string.IsNullOrEmpty(tf)) tableRe = new Regex(tf, opts);
            }
            catch (Exception ex) { lv.EndUpdate(); lblSummary.Text = "Invalid regex: " + ex.Message; return; }

            var newType = (cbNewType.SelectedItem as string ?? "").Trim();
            var newSize = (txtNewSize.Text ?? "").Trim();
            var newPic  = (txtNewPicture.Text ?? "");
            if (newType == "" && newSize == "" && newPic == "")
            { lv.EndUpdate(); lblSummary.Text = "Specify at least one of type / size / picture."; return; }

            bool excludeAliases = chkExcludeAliases != null && chkExcludeAliases.Checked;
            foreach (var t in DictModel.GetTables(dict))
            {
                if (excludeAliases && DictModel.IsAlias(t)) continue;
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "";
                if (tableRe != null && !tableRe.IsMatch(tName)) continue;
                foreach (var f in FieldMutator.EnumerateFields(t))
                {
                    var lbl = DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "";
                    if (string.IsNullOrEmpty(lbl)) continue;
                    if (!labelRe.IsMatch(lbl)) continue;

                    var curType = DictModel.AsString(DictModel.GetProp(f, "DataType"))      ?? "";
                    var curSize = DictModel.AsString(DictModel.GetProp(f, "FieldSize"))     ?? "";
                    var curPic  = DictModel.AsString(DictModel.GetProp(f, "ScreenPicture")) ?? "";
                    var finalType = newType == "" ? curType : newType;
                    var finalSize = newSize == "" ? curSize : newSize;
                    var finalPic  = txtNewPicture.Text == "" ? curPic : newPic;
                    if (finalType == curType && finalSize == curSize && finalPic == curPic) continue;
                    lastPlan.Add(new Edit
                    {
                        Field = f, TableName = tName, FieldLabel = lbl,
                        BeforeType = curType, AfterType = finalType,
                        BeforeSize = curSize, AfterSize = finalSize,
                        BeforePicture = curPic, AfterPicture = finalPic
                    });
                    lv.Items.Add(new ListViewItem(new[] {
                        tName, lbl,
                        curType, finalType,
                        curSize, finalSize,
                        curPic, finalPic
                    }));
                }
            }
            lv.EndUpdate();
            lblSummary.Text = lastPlan.Count + " field(s) will be retyped.";
            btnApply.Enabled = lastPlan.Count > 0;
        }

        void Apply()
        {
            if (lastPlan.Count == 0) return;
            var confirm = MessageBox.Show(this,
                lastPlan.Count + " fields will be retyped.\r\n"
                + "A .tasker-bak-<timestamp> backup of the .DCT is written first.\r\n\r\nProceed?",
                "Batch retype", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK) return;

            var r = new FieldMutator.Result();
            FieldMutator.Backup(DictModel.GetDictionaryFileName(dict), r);
            if (r.BackupFailed)
            { MessageBox.Show(this, "Backup failed — aborting.", "Batch retype",
                MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            foreach (var e in lastPlan)
            {
                var tag = e.TableName + "." + e.FieldLabel;
                bool anyOk = false;
                if (e.AfterType != e.BeforeType &&
                    FieldMutator.SetStringProp(e.Field, "DataType", e.AfterType, r, tag + ".type")) anyOk = true;
                if (e.AfterSize != e.BeforeSize &&
                    FieldMutator.SetStringProp(e.Field, "FieldSize", e.AfterSize, r, tag + ".size")) anyOk = true;
                if (e.AfterPicture != e.BeforePicture &&
                    FieldMutator.SetStringProp(e.Field, "ScreenPicture", e.AfterPicture, r, tag + ".picture")) anyOk = true;
                if (anyOk) r.Changed++; else r.Failed++;
            }
            FieldMutator.ForceMarkDirty(dict, DictModel.GetActiveDictionaryView(), r);

            MessageBox.Show(this,
                "Retyped: " + r.Changed + "\r\nFailed: " + r.Failed
                + (string.IsNullOrEmpty(r.BackupPath) ? "" : "\r\nBackup: " + r.BackupPath)
                + "\r\n\r\nThe dictionary is now DIRTY. Press Ctrl+S in Clarion to save.",
                "Batch retype",
                MessageBoxButtons.OK, r.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            Preview();
        }
    }
}
