using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    internal class BatchCopyDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color WarningBg   = Color.FromArgb(255, 247, 225);
        static readonly Color WarningFg   = Color.FromArgb(120, 80, 10);

        readonly object dict;
        readonly List<object> tables;

        ComboBox cboSource;
        ListView lvSourceFields;
        ListView lvTargets;
        RadioButton rbSkip;
        RadioButton rbAbort;
        Label lblWarning;

        public BatchCopyDialog(object dict)
        {
            this.dict = dict;
            this.tables = DictModel.GetTables(dict).OrderBy(t =>
                DictModel.AsString(DictModel.GetProp(t, "Name")), StringComparer.OrdinalIgnoreCase).ToList();
            BuildUi();
            PopulateSourceCombo();
        }

        void BuildUi()
        {
            var name = DictModel.GetDictionaryName(dict);
            Text = "Batch copy fields - " + name;
            Width = 1000;
            Height = 700;
            MinimumSize = new Size(820, 560);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;
            ShowIcon = false;
            ShowInTaskbar = false;

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = HeaderColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Text = "Batch copy fields   " + DictModel.GetDictionaryFileName(dict)
            };

            lblWarning = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = WarningBg,
                ForeColor = WarningFg,
                Font = new Font("Segoe UI", 9F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Text = "This will modify the open dictionary. A backup of the .DCT will be written before any change is applied. You still have to press Ctrl+S in Clarion to save."
            };

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = BgColor,
                Padding = new Padding(12)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            body.Controls.Add(BuildSourcePane(),  0, 0);
            body.Controls.Add(BuildTargetsPane(), 1, 0);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 78, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            BuildConflictRadios(bottom);

            var btnPreview = new Button { Text = "Preview and apply...", Width = 170, Height = 32, Top = 36, FlatStyle = FlatStyle.System };
            btnPreview.Left = bottom.Width - btnPreview.Width - 150;
            btnPreview.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnPreview.Click += delegate { OnPreview(); };

            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Top = 36, FlatStyle = FlatStyle.System };
            btnClose.Left = bottom.Width - btnClose.Width - 16;
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Click += delegate { Close(); };

            bottom.Controls.Add(btnPreview);
            bottom.Controls.Add(btnClose);

            Controls.Add(body);
            Controls.Add(bottom);
            Controls.Add(lblWarning);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        Panel BuildSourcePane()
        {
            var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 8, 0), BackColor = BgColor };

            var lblTable = new Label { Text = "Source table", Dock = DockStyle.Top, Height = 20, Font = new Font("Segoe UI Semibold", 9F) };
            cboSource = new ComboBox
            {
                Dock = DockStyle.Top,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5F)
            };
            cboSource.SelectedIndexChanged += delegate { PopulateSourceFieldsAndTargets(); };

            var lblFields = new Label { Text = "Fields to copy", Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI Semibold", 9F), Padding = new Padding(0, 8, 0, 2) };

            var fieldsTools = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = BgColor };
            var btnAllF  = MakeSmallBtn("Select all", 0);   btnAllF.Click  += delegate { SetAllChecked(lvSourceFields, true); };
            var btnNoneF = MakeSmallBtn("Clear all",  92);  btnNoneF.Click += delegate { SetAllChecked(lvSourceFields, false); };
            fieldsTools.Controls.Add(btnAllF);
            fieldsTools.Controls.Add(btnNoneF);

            lvSourceFields = MakeListView();
            lvSourceFields.Columns.Add("Name",    180);
            lvSourceFields.Columns.Add("Type",     80);
            lvSourceFields.Columns.Add("Size",     60, HorizontalAlignment.Right);
            lvSourceFields.Columns.Add("Picture", 100);
            lvSourceFields.Columns.Add("Description", 220);

            p.Controls.Add(lvSourceFields);
            p.Controls.Add(fieldsTools);
            p.Controls.Add(lblFields);
            p.Controls.Add(cboSource);
            p.Controls.Add(lblTable);
            return p;
        }

        Panel BuildTargetsPane()
        {
            var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0), BackColor = BgColor };

            var lbl = new Label { Text = "Target tables", Dock = DockStyle.Top, Height = 20, Font = new Font("Segoe UI Semibold", 9F) };

            var tools = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = BgColor };
            var btnAll  = MakeSmallBtn("Select all", 0);   btnAll.Click  += delegate { SetAllChecked(lvTargets, true); };
            var btnNone = MakeSmallBtn("Clear all",  92);  btnNone.Click += delegate { SetAllChecked(lvTargets, false); };
            tools.Controls.Add(btnAll);
            tools.Controls.Add(btnNone);

            lvTargets = MakeListView();
            lvTargets.Columns.Add("Name",   220);
            lvTargets.Columns.Add("Prefix", 80);
            lvTargets.Columns.Add("Driver", 90);
            lvTargets.Columns.Add("Fields", 60, HorizontalAlignment.Right);

            p.Controls.Add(lvTargets);
            p.Controls.Add(tools);
            p.Controls.Add(lbl);
            return p;
        }

        void BuildConflictRadios(Panel host)
        {
            var lbl = new Label { Text = "If a field with the same name already exists in a target table:", Left = 0, Top = 4, Width = 420, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            rbSkip  = new RadioButton { Text = "Skip that field (recommended)", Left = 0,   Top = 24, Width = 240, Checked = true,  Font = new Font("Segoe UI", 9F) };
            rbAbort = new RadioButton { Text = "Abort the whole batch",         Left = 260, Top = 24, Width = 220, Font = new Font("Segoe UI", 9F) };
            host.Controls.Add(lbl);
            host.Controls.Add(rbSkip);
            host.Controls.Add(rbAbort);
        }

        static ListView MakeListView()
        {
            return new ListView
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
        }

        static Button MakeSmallBtn(string text, int left)
        {
            return new Button { Text = text, Left = left, Top = 0, Width = 86, Height = 26, FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 9F) };
        }

        static void SetAllChecked(ListView lv, bool on)
        {
            foreach (ListViewItem i in lv.Items) i.Checked = on;
        }

        // --- data ---
        void PopulateSourceCombo()
        {
            cboSource.Items.Clear();
            foreach (var t in tables)
                cboSource.Items.Add(DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?");
            if (cboSource.Items.Count > 0) cboSource.SelectedIndex = 0;
        }

        object CurrentSourceTable
        {
            get { return cboSource.SelectedIndex < 0 ? null : tables[cboSource.SelectedIndex]; }
        }

        void PopulateSourceFieldsAndTargets()
        {
            PopulateSourceFields();
            PopulateTargets();
        }

        void PopulateSourceFields()
        {
            lvSourceFields.BeginUpdate();
            lvSourceFields.Items.Clear();
            var t = CurrentSourceTable;
            if (t != null)
            {
                var fields = DictModel.GetProp(t, "Fields") as System.Collections.IEnumerable;
                if (fields != null)
                {
                    foreach (var f in fields)
                    {
                        if (f == null) continue;
                        var label = DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "";
                        var type  = DictModel.AsString(DictModel.GetProp(f, "DataType")) ?? "";
                        var size  = DictModel.AsString(DictModel.GetProp(f, "FieldSize")) ?? "";
                        var pic   = DictModel.AsString(DictModel.GetProp(f, "ScreenPicture")) ?? "";
                        var desc  = DictModel.AsString(DictModel.GetProp(f, "Description")) ?? "";
                        var item  = new ListViewItem(new[] { label, type, size, pic, desc });
                        item.Tag = f;
                        lvSourceFields.Items.Add(item);
                    }
                }
            }
            lvSourceFields.EndUpdate();
        }

        void PopulateTargets()
        {
            lvTargets.BeginUpdate();
            lvTargets.Items.Clear();
            var source = CurrentSourceTable;
            foreach (var t in tables)
            {
                if (ReferenceEquals(t, source)) continue;
                var name    = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "";
                var prefix  = DictModel.AsString(DictModel.GetProp(t, "Prefix")) ?? "";
                var drv     = DictModel.AsString(DictModel.GetProp(t, "FileDriverName")) ?? "";
                var fCount  = DictModel.CountEnumerable(t, "Fields");
                var item    = new ListViewItem(new[] { name, prefix, drv, fCount.ToString() });
                item.Tag = t;
                lvTargets.Items.Add(item);
            }
            lvTargets.EndUpdate();
        }

        // --- preview / apply ---
        void OnPreview()
        {
            var source = CurrentSourceTable;
            if (source == null) { Info("Pick a source table."); return; }

            var selectedFields = lvSourceFields.CheckedItems.Cast<ListViewItem>().Select(i => i.Tag).ToList();
            if (selectedFields.Count == 0) { Info("Check at least one source field."); return; }

            var selectedTargets = lvTargets.CheckedItems.Cast<ListViewItem>().Select(i => i.Tag).ToList();
            if (selectedTargets.Count == 0) { Info("Check at least one target table."); return; }

            var mode = rbAbort.Checked ? FieldCopier.ConflictMode.Abort : FieldCopier.ConflictMode.Skip;
            var plan = FieldCopier.BuildPlan(source, selectedFields, selectedTargets, mode);

            using (var dlg = new BatchCopyPreviewDialog(plan, DictModel.GetDictionaryFileName(dict)))
            {
                var dr = dlg.ShowDialog(this);
                if (dr != DialogResult.OK) return;

                var result = FieldCopier.Apply(plan, DictModel.GetDictionaryFileName(dict));
                ShowResult(result);
            }
        }

        void ShowResult(FieldCopier.ApplyResult r)
        {
            var sb = new StringBuilder();
            sb.AppendFormat("Added:   {0}\r\n", r.AddedCount);
            sb.AppendFormat("Skipped: {0}\r\n", r.SkippedCount);
            sb.AppendFormat("Failed:  {0}\r\n", r.FailedCount);
            sb.AppendLine();
            if (!string.IsNullOrEmpty(r.BackupPath))
            {
                sb.AppendLine("Backup: " + r.BackupPath);
                sb.AppendLine();
            }
            if (r.Messages.Count > 0)
            {
                sb.AppendLine("Details:");
                foreach (var m in r.Messages) sb.AppendLine("  - " + m);
                sb.AppendLine();
            }
            if (r.AddedCount > 0)
            {
                sb.AppendLine("The dictionary is now DIRTY. Press Ctrl+S in Clarion to save,");
                sb.AppendLine("or close the dictionary tab and reopen to refresh the editor view.");
            }
            ShowTextModal("Batch copy result", sb.ToString());
        }

        void ShowTextModal(string title, string text)
        {
            using (var f = new Form
            {
                Text = title,
                Width = 820, Height = 520,
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

        void Info(string msg)
        {
            MessageBox.Show(this, msg, "Batch copy", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // Small standalone preview dialog — own file would be overkill.
    internal class BatchCopyPreviewDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color ConflictFg  = Color.FromArgb(150, 30, 30);
        static readonly Color SkipFg      = Color.FromArgb(120, 120, 120);

        public BatchCopyPreviewDialog(List<FieldCopier.PlanItem> plan, string dctPath)
        {
            Text = "Preview batch copy";
            Width = 900; Height = 620;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = BgColor; ShowIcon = false; ShowInTaskbar = false;
            MinimumSize = new Size(760, 420);

            int add   = plan.Count(p => p.Action == FieldCopier.PlanAction.Add);
            int skip  = plan.Count(p => p.Action == FieldCopier.PlanAction.Skip);
            int conf  = plan.Count(p => p.Action == FieldCopier.PlanAction.Conflict);

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = HeaderColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Text = string.Format("{0} to add   ·   {1} to skip   ·   {2} conflicts", add, skip, conf)
            };

            var lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.None
            };
            lv.Columns.Add("Action", 80);
            lv.Columns.Add("Source table", 140);
            lv.Columns.Add("Field",  180);
            lv.Columns.Add("Type",   80);
            lv.Columns.Add("Size",   60);
            lv.Columns.Add("Target table", 160);
            lv.Columns.Add("Note",   220);

            foreach (var p in plan)
            {
                var action = p.Action.ToString();
                var item = new ListViewItem(new[] {
                    action, p.SourceTableName, p.FieldLabel,
                    p.DataType, p.FieldSize, p.TargetTableName, p.Reason ?? ""
                });
                if (p.Action == FieldCopier.PlanAction.Conflict) item.ForeColor = ConflictFg;
                else if (p.Action == FieldCopier.PlanAction.Skip) item.ForeColor = SkipFg;
                lv.Items.Add(item);
            }

            var backupLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = BgColor,
                Font = new Font("Segoe UI", 9F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 16, 0),
                Text = string.IsNullOrEmpty(dctPath)
                    ? "No file on disk - skipping backup."
                    : "Backup will be written to: " + FieldCopier.MakeBackupPath(dctPath)
            };

            var bp = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnApply = new Button
            {
                Text = conf > 0 ? "Abort (conflicts)" : "Apply changes",
                Width = 170, Height = 32,
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.System,
                DialogResult = conf > 0 ? DialogResult.Cancel : DialogResult.OK,
                Enabled = conf == 0 && add > 0
            };
            var btnCancel = new Button
            {
                Text = "Cancel", Width = 120, Height = 32, Dock = DockStyle.Right,
                FlatStyle = FlatStyle.System, DialogResult = DialogResult.Cancel
            };
            bp.Controls.Add(btnCancel);
            bp.Controls.Add(btnApply);

            Controls.Add(lv);
            Controls.Add(backupLbl);
            Controls.Add(bp);
            Controls.Add(header);

            AcceptButton = btnApply;
            CancelButton = btnCancel;
        }
    }
}
