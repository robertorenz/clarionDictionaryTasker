using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Regex find/replace across field Label / Description / Heading / Prompt,
    // optionally scoped to tables matching a name pattern. Preview every
    // planned edit, then apply through FieldMutator.
    internal class BatchRenameDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        readonly object dict;
        TextBox  txtPattern, txtReplacement, txtTableFilter;
        CheckBox chkLabel, chkDesc, chkHeading, chkPrompt, chkIgnoreCase, chkExcludeAliases;
        ListView lv;
        Label    lblSummary;
        Button   btnApply;

        sealed class Edit
        {
            public object Field;
            public string TableName;
            public string FieldLabel;
            public string Property;
            public string Before;
            public string After;
        }

        List<Edit> lastPlan = new List<Edit>();

        public BatchRenameDialog(object dict) { this.dict = dict; BuildUi(); }

        void BuildUi()
        {
            Text = "Batch rename (regex) - " + DictModel.GetDictionaryName(dict);
            Width = 1140; Height = 740;
            MinimumSize = new Size(880, 500);
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
                Text = "Batch rename (regex)   " + DictModel.GetDictionaryName(dict)
            };

            var row1 = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = BgColor, Padding = new Padding(16, 8, 16, 0) };
            var lblP = new Label { Text = "Find (regex):",   Left = 4,  Top = 8, Width = 90, Font = new Font("Segoe UI", 9F) };
            txtPattern = new TextBox { Left = 96, Top = 4, Width = 360, Font = new Font("Consolas", 10F) };
            var lblR = new Label { Text = "Replace with:",   Left = 474, Top = 8, Width = 90, Font = new Font("Segoe UI", 9F) };
            txtReplacement = new TextBox { Left = 568, Top = 4, Width = 360, Font = new Font("Consolas", 10F) };
            row1.Controls.Add(lblP); row1.Controls.Add(txtPattern);
            row1.Controls.Add(lblR); row1.Controls.Add(txtReplacement);

            var row2 = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = BgColor, Padding = new Padding(16, 6, 16, 0) };
            var lblTF = new Label { Text = "Table filter (regex, blank = all):", Left = 4, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            txtTableFilter = new TextBox { Left = 206, Top = 4, Width = 220, Font = new Font("Consolas", 10F) };
            chkIgnoreCase  = new CheckBox { Text = "Case-insensitive", Left = 444, Top = 6, AutoSize = true, Checked = true, Font = new Font("Segoe UI", 9F) };
            chkExcludeAliases = new CheckBox { Text = "Exclude aliases", Left = 576, Top = 6, AutoSize = true, Checked = Settings.BatchExcludeAliases, Font = new Font("Segoe UI", 9F) };
            chkExcludeAliases.CheckedChanged += delegate { Settings.BatchExcludeAliases = chkExcludeAliases.Checked; };
            row2.Controls.Add(lblTF); row2.Controls.Add(txtTableFilter); row2.Controls.Add(chkIgnoreCase); row2.Controls.Add(chkExcludeAliases);

            var row3 = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = BgColor, Padding = new Padding(16, 6, 16, 0) };
            chkLabel   = new CheckBox { Text = "Label",       Left = 4,   Top = 6, AutoSize = true, Checked = true,  Font = new Font("Segoe UI", 9F) };
            chkDesc    = new CheckBox { Text = "Description", Left = 90,  Top = 6, AutoSize = true, Checked = false, Font = new Font("Segoe UI", 9F) };
            chkHeading = new CheckBox { Text = "Heading",     Left = 216, Top = 6, AutoSize = true, Checked = false, Font = new Font("Segoe UI", 9F) };
            chkPrompt  = new CheckBox { Text = "Prompt",      Left = 314, Top = 6, AutoSize = true, Checked = false, Font = new Font("Segoe UI", 9F) };
            var btnPrev = new Button  { Text = "Preview",     Left = 420, Top = 2, Width = 100, Height = 30, FlatStyle = FlatStyle.System };
            btnPrev.Click += delegate { Preview(); };
            row3.Controls.Add(chkLabel);
            row3.Controls.Add(chkDesc);
            row3.Controls.Add(chkHeading);
            row3.Controls.Add(chkPrompt);
            row3.Controls.Add(btnPrev);

            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 24,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 4, 0, 0),
                Text = ""
            };

            lv = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true, BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };
            lv.Columns.Add("Table",     160);
            lv.Columns.Add("Field",     180);
            lv.Columns.Add("Property",  100);
            lv.Columns.Add("Before",    300);
            lv.Columns.Add("After",     300);

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
            Controls.Add(row3);
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

            var pattern = txtPattern.Text ?? "";
            var replacement = txtReplacement.Text ?? "";
            var tableFilter = (txtTableFilter.Text ?? "").Trim();
            if (string.IsNullOrEmpty(pattern)) { lv.EndUpdate(); lblSummary.Text = "Enter a regex pattern."; return; }

            Regex re;
            Regex tableRe = null;
            try
            {
                var opts = chkIgnoreCase.Checked ? RegexOptions.IgnoreCase : RegexOptions.None;
                re = new Regex(pattern, opts);
                if (!string.IsNullOrEmpty(tableFilter)) tableRe = new Regex(tableFilter, opts);
            }
            catch (Exception ex) { lv.EndUpdate(); lblSummary.Text = "Invalid regex: " + ex.Message; return; }

            var props = new List<string>();
            if (chkLabel.Checked)   props.Add("Label");
            if (chkDesc.Checked)    props.Add("Description");
            if (chkHeading.Checked) props.Add("Heading");
            if (chkPrompt.Checked)  props.Add("Prompt");
            if (props.Count == 0)   { lv.EndUpdate(); lblSummary.Text = "Pick at least one property."; return; }

            bool excludeAliases = chkExcludeAliases != null && chkExcludeAliases.Checked;
            foreach (var t in DictModel.GetTables(dict))
            {
                if (excludeAliases && DictModel.IsAlias(t)) continue;
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "";
                if (tableRe != null && !tableRe.IsMatch(tName)) continue;
                foreach (var f in FieldMutator.EnumerateFields(t))
                {
                    var label = DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "";
                    foreach (var p in props)
                    {
                        var before = DictModel.AsString(DictModel.GetProp(f, p)) ?? "";
                        if (string.IsNullOrEmpty(before)) continue;
                        if (!re.IsMatch(before)) continue;
                        var after = re.Replace(before, replacement);
                        if (after == before) continue;
                        lastPlan.Add(new Edit { Field = f, TableName = tName, FieldLabel = label, Property = p, Before = before, After = after });
                        lv.Items.Add(new ListViewItem(new[] { tName, label, p, Clip(before), Clip(after) }));
                    }
                }
            }
            lv.EndUpdate();
            lblSummary.Text = lastPlan.Count + " edit(s) planned.";
            btnApply.Enabled = lastPlan.Count > 0;
        }

        void Apply()
        {
            if (lastPlan.Count == 0) return;
            var confirm = MessageBox.Show(this,
                lastPlan.Count + " field edits will be applied to the dictionary.\r\n"
                + "A .tasker-bak-<timestamp> backup of the .DCT is written first.\r\n\r\nProceed?",
                "Batch rename", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK) return;

            var r = new FieldMutator.Result();
            FieldMutator.Backup(DictModel.GetDictionaryFileName(dict), r);
            if (r.BackupFailed)
            { MessageBox.Show(this, "Backup failed — aborting.\r\n" + string.Join("\r\n", r.Messages.ToArray()),
                "Batch rename", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            foreach (var e in lastPlan)
            {
                var tag = e.TableName + "." + e.FieldLabel + "." + e.Property;
                if (FieldMutator.SetStringProp(e.Field, e.Property, e.After, r, tag)) r.Changed++;
                else r.Failed++;
            }
            FieldMutator.ForceMarkDirty(dict, DictModel.GetActiveDictionaryView(), r);

            MessageBox.Show(this,
                "Changed: " + r.Changed + "\r\nFailed: " + r.Failed
                + (string.IsNullOrEmpty(r.BackupPath) ? "" : "\r\nBackup: " + r.BackupPath)
                + "\r\n\r\nThe dictionary is now DIRTY. Press Ctrl+S in Clarion to save.",
                "Batch rename",
                MessageBoxButtons.OK, r.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            Preview();
        }

        static string Clip(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            return s.Length <= 120 ? s : s.Substring(0, 120) + "...";
        }
    }
}
