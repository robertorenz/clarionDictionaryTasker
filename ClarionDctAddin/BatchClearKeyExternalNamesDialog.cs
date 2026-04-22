using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Select tables, clear ExternalName on every key in those tables.
    // For projects that don't target a SQL backend the per-key external
    // name is pure noise — this tool bulk-wipes it. Uses the same
    // FieldMutator path as LintFixKeysDialog so the native save pipeline
    // sees the edits.
    internal class BatchClearKeyExternalNamesDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color WarningBg   = Color.FromArgb(255, 247, 225);
        static readonly Color WarningFg   = Color.FromArgb(120, 80, 10);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        readonly object dict;
        readonly List<object> tables;

        TextBox  txtFilter;
        ListView lvTables;
        ListView lvPreview;
        Label    lblTablesSummary;
        Label    lblPreviewSummary;
        Button   btnApply;

        List<PlanItem> currentPlan = new List<PlanItem>();

        sealed class PlanItem
        {
            public object Key;
            public string TableName;
            public string KeyName;
            public string CurrentExternal;
        }

        public BatchClearKeyExternalNamesDialog(object dict)
        {
            this.dict = dict;
            this.tables = DictModel.GetTables(dict)
                .OrderBy(t => DictModel.AsString(DictModel.GetProp(t, "Name")), StringComparer.OrdinalIgnoreCase)
                .ToList();
            BuildUi();
            PopulateTables();
        }

        void BuildUi()
        {
            Text = "Batch clear key external names - " + DictModel.GetDictionaryName(dict);
            Width = 1100;
            Height = 720;
            MinimumSize = new Size(900, 520);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            SizeGripStyle = SizeGripStyle.Show;

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = HeaderColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Text = "Batch clear key external names   " + DictModel.GetDictionaryFileName(dict)
            };

            var warning = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = WarningBg,
                ForeColor = WarningFg,
                Font = new Font("Segoe UI", 9F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Text = "Clears the EXTERNAL NAME on every key in the selected tables. A .DCT backup is written first; press Ctrl+S in Clarion to save afterwards."
            };

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = BgColor,
                Padding = new Padding(12)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(BuildTablesPane(),  0, 0);
            body.Controls.Add(BuildPreviewPane(), 1, 0);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            btnApply = new Button { Text = "Clear external names...", Width = 180, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System, Enabled = false };
            btnApply.Click += delegate { Apply(); };
            var btnPreview = new Button { Text = "Preview", Width = 110, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnPreview.Click += delegate { RefreshPreview(); };
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(btnApply);
            bottom.Controls.Add(btnPreview);

            Controls.Add(body);
            Controls.Add(bottom);
            Controls.Add(warning);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        Panel BuildTablesPane()
        {
            var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 8, 0), BackColor = BgColor };
            var lbl = new Label { Text = "Tables", Dock = DockStyle.Top, Height = 20, Font = new Font("Segoe UI Semibold", 9F) };

            var tools = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = BgColor };
            var bAll  = MakeSmall("Select all", 0);   bAll.Click  += delegate { SetAllChecked(lvTables, true);  RefreshPreview(); };
            var bNone = MakeSmall("Clear all",  92);  bNone.Click += delegate { SetAllChecked(lvTables, false); RefreshPreview(); };
            var bFiltered = MakeSmall("Check filtered", 184); bFiltered.Width = 108; bFiltered.Click += delegate { CheckVisible(); RefreshPreview(); };
            tools.Controls.Add(bAll);
            tools.Controls.Add(bNone);
            tools.Controls.Add(bFiltered);

            var filter = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = BgColor, Padding = new Padding(0, 4, 0, 4) };
            var lf = new Label { Text = "Filter:", Left = 0, Top = 6, Width = 40, Font = new Font("Segoe UI", 9F) };
            txtFilter = new TextBox { Left = 44, Top = 2, Width = 260, Font = new Font("Segoe UI", 9.5F) };
            txtFilter.TextChanged += delegate { ApplyFilter(); };
            filter.Controls.Add(lf);
            filter.Controls.Add(txtFilter);

            lvTables = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };
            lvTables.Columns.Add("Name",   220);
            lvTables.Columns.Add("Prefix", 80);
            lvTables.Columns.Add("Driver", 90);
            lvTables.Columns.Add("Keys",   55, HorizontalAlignment.Right);
            lvTables.Columns.Add("Named",  65, HorizontalAlignment.Right);
            lvTables.ItemChecked += delegate { RefreshPreview(); };

            lblTablesSummary = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(2, 4, 0, 0),
                Text = ""
            };

            p.Controls.Add(lvTables);
            p.Controls.Add(lblTablesSummary);
            p.Controls.Add(tools);
            p.Controls.Add(filter);
            p.Controls.Add(lbl);
            return p;
        }

        Panel BuildPreviewPane()
        {
            var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0), BackColor = BgColor };
            var lbl = new Label { Text = "Keys whose ExternalName will be cleared", Dock = DockStyle.Top, Height = 20, Font = new Font("Segoe UI Semibold", 9F) };

            lvPreview = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };
            lvPreview.Columns.Add("Table",         180);
            lvPreview.Columns.Add("Key",           200);
            lvPreview.Columns.Add("Current external name", 220);

            lblPreviewSummary = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(2, 4, 0, 0),
                Text = "Check tables on the left to preview."
            };

            p.Controls.Add(lvPreview);
            p.Controls.Add(lblPreviewSummary);
            p.Controls.Add(lbl);
            return p;
        }

        static Button MakeSmall(string text, int left)
        {
            return new Button { Text = text, Left = left, Top = 0, Width = 86, Height = 26, FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 9F) };
        }

        static void SetAllChecked(ListView lv, bool on)
        {
            foreach (ListViewItem i in lv.Items) i.Checked = on;
        }

        void CheckVisible()
        {
            foreach (ListViewItem i in lvTables.Items) i.Checked = true;
        }

        void PopulateTables()
        {
            lvTables.BeginUpdate();
            lvTables.Items.Clear();
            foreach (var t in tables)
            {
                var name   = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                var prefix = DictModel.AsString(DictModel.GetProp(t, "Prefix")) ?? "";
                var drv    = DictModel.AsString(DictModel.GetProp(t, "FileDriverName")) ?? "";
                int keyCount, namedCount;
                CountKeys(t, out keyCount, out namedCount);
                var item = new ListViewItem(new[] { name, prefix, drv, keyCount.ToString(), namedCount.ToString() });
                item.Tag = t;
                lvTables.Items.Add(item);
            }
            lvTables.EndUpdate();
            UpdateTablesSummary();
        }

        static void CountKeys(object table, out int keys, out int named)
        {
            keys = 0; named = 0;
            var en = DictModel.GetProp(table, "Keys") as IEnumerable;
            if (en == null) return;
            foreach (var k in en)
            {
                if (k == null) continue;
                keys++;
                var ext = DictModel.AsString(DictModel.GetProp(k, "ExternalName")) ?? "";
                if (!string.IsNullOrEmpty(ext)) named++;
            }
        }

        void ApplyFilter()
        {
            var q = (txtFilter.Text ?? "").Trim();
            lvTables.BeginUpdate();
            lvTables.Items.Clear();
            foreach (var t in tables)
            {
                var name = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                if (q.Length > 0 && name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var prefix = DictModel.AsString(DictModel.GetProp(t, "Prefix")) ?? "";
                var drv    = DictModel.AsString(DictModel.GetProp(t, "FileDriverName")) ?? "";
                int keyCount, namedCount;
                CountKeys(t, out keyCount, out namedCount);
                var item = new ListViewItem(new[] { name, prefix, drv, keyCount.ToString(), namedCount.ToString() });
                item.Tag = t;
                lvTables.Items.Add(item);
            }
            lvTables.EndUpdate();
            UpdateTablesSummary();
        }

        void UpdateTablesSummary()
        {
            int shown   = lvTables.Items.Count;
            int checkd  = lvTables.CheckedItems.Count;
            lblTablesSummary.Text = shown + " shown   ·   " + checkd + " checked   ·   " + tables.Count + " total in dictionary";
        }

        void RefreshPreview()
        {
            UpdateTablesSummary();
            currentPlan.Clear();
            lvPreview.BeginUpdate();
            lvPreview.Items.Clear();

            foreach (ListViewItem i in lvTables.CheckedItems)
            {
                var t = i.Tag;
                if (t == null) continue;
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                var keys = DictModel.GetProp(t, "Keys") as IEnumerable;
                if (keys == null) continue;
                foreach (var k in keys)
                {
                    if (k == null) continue;
                    var ext = DictModel.AsString(DictModel.GetProp(k, "ExternalName")) ?? "";
                    if (string.IsNullOrEmpty(ext)) continue;
                    var kName = DictModel.AsString(DictModel.GetProp(k, "Name")) ?? "?";
                    var plan = new PlanItem
                    {
                        Key = k,
                        TableName = tName,
                        KeyName = kName,
                        CurrentExternal = ext
                    };
                    currentPlan.Add(plan);
                    var lvi = new ListViewItem(new[] { tName, kName, ext });
                    lvi.Tag = plan;
                    lvPreview.Items.Add(lvi);
                }
            }
            lvPreview.EndUpdate();

            if (lvTables.CheckedItems.Count == 0)
                lblPreviewSummary.Text = "Check tables on the left to preview.";
            else if (currentPlan.Count == 0)
                lblPreviewSummary.Text = "No keys with a non-blank ExternalName in the selected tables.";
            else
                lblPreviewSummary.Text = currentPlan.Count + " key(s) will have ExternalName cleared across "
                    + lvTables.CheckedItems.Count + " table(s).";

            btnApply.Enabled = currentPlan.Count > 0;
        }

        void Apply()
        {
            if (currentPlan.Count == 0) return;

            var confirm = MessageBox.Show(this,
                currentPlan.Count + " key(s) will have their ExternalName cleared across "
                + lvTables.CheckedItems.Count + " table(s).\r\n"
                + "A .tasker-bak-<timestamp> backup of the .DCT is written first.\r\n\r\nProceed?",
                "Batch clear key external names", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK) return;

            var mr = new FieldMutator.Result();
            FieldMutator.Backup(DictModel.GetDictionaryFileName(dict), mr);
            if (mr.BackupFailed)
            {
                ShowTextModal("Backup failed",
                    "Backup failed — aborting.\r\n\r\n" + string.Join("\r\n", mr.Messages.ToArray()));
                return;
            }

            foreach (var p in currentPlan)
            {
                var tag = p.TableName + "." + p.KeyName + ".ExternalName";
                if (FieldMutator.SetStringProp(p.Key, "ExternalName", "", mr, tag))
                    mr.Changed++;
                else
                    mr.Failed++;
            }
            FieldMutator.ForceMarkDirty(dict, DictModel.GetActiveDictionaryView(), mr);

            var summary =
                "Cleared:  " + mr.Changed + "\r\n" +
                "Failed:   " + mr.Failed + "\r\n" +
                (string.IsNullOrEmpty(mr.BackupPath) ? "" : "Backup:  " + mr.BackupPath + "\r\n") +
                "\r\nThe dictionary is now DIRTY. Press Ctrl+S in Clarion to save.";

            ShowTextModal(
                mr.Failed > 0 ? "Batch clear - finished with errors" : "Batch clear - done",
                summary + "\r\n\r\n--- details ---\r\n" + string.Join("\r\n", mr.Messages.ToArray()));

            // Refresh so the counts + preview reflect the new empty state.
            PopulateTables();
            RefreshPreview();
        }

        void ShowTextModal(string title, string text)
        {
            using (var f = new Form
            {
                Text = title,
                Width = 900, Height = 520,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = BgColor, ShowIcon = false, ShowInTaskbar = false
            })
            {
                var tb = new TextBox
                {
                    Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                    Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5F),
                    Text = text, WordWrap = false
                };
                var bp = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = PanelColor, Padding = new Padding(12, 8, 12, 8) };
                var btn = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
                btn.Click += delegate { f.Close(); };
                bp.Controls.Add(btn);
                f.Controls.Add(tb); f.Controls.Add(bp);
                f.CancelButton = btn;
                f.ShowDialog(this);
            }
        }
    }
}
