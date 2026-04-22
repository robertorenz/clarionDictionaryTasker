using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    internal class BatchCopyKeysDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color WarningBg   = Color.FromArgb(255, 247, 225);
        static readonly Color WarningFg   = Color.FromArgb(120, 80, 10);

        readonly object dict;
        readonly List<object> tables;

        ComboBox cboSource;
        ListView lvSourceKeys;
        ListView lvTargets;
        RadioButton rbSkip;
        RadioButton rbAbort;
        CheckBox chkExcludeAliases;
        readonly List<object> visibleTables = new List<object>();

        public BatchCopyKeysDialog(object dict)
        {
            this.dict = dict;
            this.tables = DictModel.GetTables(dict)
                .OrderBy(t => DictModel.AsString(DictModel.GetProp(t, "Name")), StringComparer.OrdinalIgnoreCase)
                .ToList();
            BuildUi();
            PopulateSourceCombo();
        }

        void BuildUi()
        {
            var name = DictModel.GetDictionaryName(dict);
            Text = "Batch copy keys - " + name;
            Width = 1100;
            Height = 700;
            MinimumSize = new Size(880, 560);
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
                Text = "Batch copy keys   " + DictModel.GetDictionaryFileName(dict)
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
                Text = "Target tables must already contain every field a key references (match by Label). A backup is written before any change, and you still need Ctrl+S in Clarion to save."
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

            var btnPreview = new Button { Text = "Preview and apply...", Width = 170, Height = 32, Top = 36, FlatStyle = FlatStyle.System, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnPreview.Left = bottom.Width - btnPreview.Width - 150;
            btnPreview.Click += delegate { OnPreview(); };

            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Top = 36, FlatStyle = FlatStyle.System, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnClose.Left = bottom.Width - btnClose.Width - 16;
            btnClose.Click += delegate { Close(); };

            bottom.Controls.Add(btnPreview);
            bottom.Controls.Add(btnClose);

            Controls.Add(body);
            Controls.Add(bottom);
            Controls.Add(warning);
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
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                Font = new Font("Segoe UI", 9.5F)
            };
            cboSource.SelectedIndexChanged += delegate { PopulateKeysAndTargets(); };

            var lblKeys = new Label { Text = "Keys to copy", Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI Semibold", 9F), Padding = new Padding(0, 8, 0, 2) };
            var tools = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = BgColor };
            var bAll  = MakeSmall("Select all", 0);   bAll.Click  += delegate { SetAllChecked(lvSourceKeys, true); };
            var bNone = MakeSmall("Clear all",  92);  bNone.Click += delegate { SetAllChecked(lvSourceKeys, false); };
            tools.Controls.Add(bAll);
            tools.Controls.Add(bNone);

            lvSourceKeys = MakeListView();
            lvSourceKeys.Columns.Add("Name",       220);
            lvSourceKeys.Columns.Add("Components", 280);
            lvSourceKeys.Columns.Add("Type",        80);
            lvSourceKeys.Columns.Add("Unique",      60);

            p.Controls.Add(lvSourceKeys);
            p.Controls.Add(tools);
            p.Controls.Add(lblKeys);
            p.Controls.Add(cboSource);
            p.Controls.Add(lblTable);
            return p;
        }

        Panel BuildTargetsPane()
        {
            var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0), BackColor = BgColor };
            var lbl = new Label { Text = "Target tables", Dock = DockStyle.Top, Height = 20, Font = new Font("Segoe UI Semibold", 9F) };
            var tools = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = BgColor };
            var bAll  = MakeSmall("Select all", 0);   bAll.Click  += delegate { SetAllChecked(lvTargets, true); };
            var bNone = MakeSmall("Clear all",  92);  bNone.Click += delegate { SetAllChecked(lvTargets, false); };
            tools.Controls.Add(bAll);
            tools.Controls.Add(bNone);

            lvTargets = MakeListView();
            lvTargets.Columns.Add("Name",   220);
            lvTargets.Columns.Add("Prefix", 80);
            lvTargets.Columns.Add("Driver", 90);
            lvTargets.Columns.Add("Fields", 60, HorizontalAlignment.Right);
            lvTargets.Columns.Add("Keys",   50, HorizontalAlignment.Right);

            p.Controls.Add(lvTargets);
            p.Controls.Add(tools);
            p.Controls.Add(lbl);
            return p;
        }

        void BuildConflictRadios(Panel host)
        {
            var lbl = new Label { Text = "If a key with the same name already exists, or the target is missing required fields:", Left = 0, Top = 4, Width = 600, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            rbSkip  = new RadioButton { Text = "Skip that target (recommended)", Left = 0,   Top = 24, Width = 260, Checked = true, Font = new Font("Segoe UI", 9F) };
            rbAbort = new RadioButton { Text = "Abort the whole batch",           Left = 280, Top = 24, Width = 220, Font = new Font("Segoe UI", 9F) };
            chkExcludeAliases = new CheckBox
            {
                Text = "Exclude aliases",
                Left = 520, Top = 24,
                AutoSize = true,
                Checked = Settings.BatchExcludeAliases,
                Font = new Font("Segoe UI", 9F)
            };
            chkExcludeAliases.CheckedChanged += delegate
            {
                Settings.BatchExcludeAliases = chkExcludeAliases.Checked;
                PopulateSourceCombo();
                PopulateKeysAndTargets();
            };
            host.Controls.Add(lbl);
            host.Controls.Add(rbSkip);
            host.Controls.Add(rbAbort);
            host.Controls.Add(chkExcludeAliases);
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

        static Button MakeSmall(string text, int left)
        {
            return new Button { Text = text, Left = left, Top = 0, Width = 86, Height = 26, FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 9F) };
        }

        static void SetAllChecked(ListView lv, bool on)
        {
            foreach (ListViewItem i in lv.Items) i.Checked = on;
        }

        void PopulateSourceCombo()
        {
            bool excludeAliases = chkExcludeAliases == null || chkExcludeAliases.Checked;
            visibleTables.Clear();
            cboSource.Items.Clear();
            foreach (var t in tables)
            {
                if (excludeAliases && DictModel.IsAlias(t)) continue;
                visibleTables.Add(t);
                var display = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                if (DictModel.IsAlias(t)) display += "  (alias)";
                cboSource.Items.Add(display);
            }
            if (cboSource.Items.Count > 0) cboSource.SelectedIndex = 0;
        }

        object CurrentSourceTable
        {
            get { return cboSource.SelectedIndex < 0 || cboSource.SelectedIndex >= visibleTables.Count ? null : visibleTables[cboSource.SelectedIndex]; }
        }

        void PopulateKeysAndTargets()
        {
            PopulateKeys();
            PopulateTargets();
        }

        void PopulateKeys()
        {
            lvSourceKeys.BeginUpdate();
            lvSourceKeys.Items.Clear();
            var t = CurrentSourceTable;
            if (t != null)
            {
                var keys = DictModel.GetProp(t, "Keys") as System.Collections.IEnumerable;
                if (keys != null)
                {
                    foreach (var k in keys)
                    {
                        if (k == null) continue;
                        var name = DictModel.AsString(DictModel.GetProp(k, "Name")) ?? "";
                        var kind = DictModel.AsString(DictModel.GetProp(k, "KeyType")) ?? "Key";
                        var uniq = DictModel.AsString(DictModel.GetProp(k, "AttributeUnique")) ?? "";
                        var comps = ComponentsSummary(k);
                        var item = new ListViewItem(new[] { name, comps, kind, uniq });
                        item.Tag = k;
                        lvSourceKeys.Items.Add(item);
                    }
                }
            }
            lvSourceKeys.EndUpdate();
        }

        static string ComponentsSummary(object key)
        {
            // Use the same component-name discovery logic shape as KeyCopier but
            // we only need a display string here — labels joined with commas.
            string[] candidates = { "Components", "KeyComponents", "Fields", "KeyFields",
                                     "Segments", "Parts", "Children", "Items", "FieldList" };
            System.Collections.IEnumerable en = null;
            foreach (var n in candidates)
            {
                en = DictModel.GetProp(key, n) as System.Collections.IEnumerable;
                if (en != null && !(en is string)) break;
                en = null;
            }
            if (en == null) return "(components unknown)";
            var parts = new List<string>();
            foreach (var c in en)
            {
                if (c == null) continue;
                string label = null;
                var fld = DictModel.GetProp(c, "Field") ?? DictModel.GetProp(c, "DDField");
                if (fld != null) label = DictModel.AsString(DictModel.GetProp(fld, "Label"));
                if (string.IsNullOrEmpty(label))
                    label = DictModel.AsString(DictModel.GetProp(c, "Label")) ?? DictModel.AsString(DictModel.GetProp(c, "Name"));
                if (!string.IsNullOrEmpty(label)) parts.Add(label);
            }
            return parts.Count == 0 ? "(empty)" : string.Join(", ", parts.ToArray());
        }

        void PopulateTargets()
        {
            bool excludeAliases = chkExcludeAliases == null || chkExcludeAliases.Checked;
            lvTargets.BeginUpdate();
            lvTargets.Items.Clear();
            var source = CurrentSourceTable;
            foreach (var t in tables)
            {
                if (excludeAliases && DictModel.IsAlias(t)) continue;
                if (ReferenceEquals(t, source)) continue;
                var name   = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "";
                var prefix = DictModel.AsString(DictModel.GetProp(t, "Prefix")) ?? "";
                var drv    = DictModel.AsString(DictModel.GetProp(t, "FileDriverName")) ?? "";
                var fCount = DictModel.CountEnumerable(t, "Fields");
                var kCount = DictModel.CountEnumerable(t, "Keys");
                var display = DictModel.IsAlias(t) ? name + "  (alias)" : name;
                var item   = new ListViewItem(new[] { display, prefix, drv, fCount.ToString(), kCount.ToString() });
                item.Tag = t;
                lvTargets.Items.Add(item);
            }
            lvTargets.EndUpdate();
        }

        void OnPreview()
        {
            var source = CurrentSourceTable;
            if (source == null) { Info("Pick a source table."); return; }

            var selectedKeys = lvSourceKeys.CheckedItems.Cast<ListViewItem>().Select(i => i.Tag).ToList();
            if (selectedKeys.Count == 0) { Info("Check at least one source key."); return; }

            var selectedTargets = lvTargets.CheckedItems.Cast<ListViewItem>().Select(i => i.Tag).ToList();
            if (selectedTargets.Count == 0) { Info("Check at least one target table."); return; }

            var mode = rbAbort.Checked ? KeyCopier.ConflictMode.Abort : KeyCopier.ConflictMode.Skip;
            var plan = KeyCopier.BuildPlan(source, selectedKeys, selectedTargets, mode);

            using (var pdlg = new BatchCopyKeysPreviewDialog(plan, DictModel.GetDictionaryFileName(dict)))
            {
                var dr = pdlg.ShowDialog(this);
                if (dr != DialogResult.OK) return;

                var view    = DictModel.GetActiveDictionaryView();
                var dctPath = DictModel.GetDictionaryFileName(dict);

                var progress = new BatchProgressDialog(plan.Count);
                Enabled = false;
                progress.Show(this);
                progress.Report(0, "Starting...");
                KeyCopier.ApplyResult result;
                try
                {
                    result = KeyCopier.Apply(
                        plan, dict, view, dctPath,
                        delegate(int done, string label) { progress.Report(done, label); },
                        delegate { return progress.CancelRequested; });
                }
                finally
                {
                    progress.Close();
                    progress.Dispose();
                    Enabled = true;
                    Activate();
                }
                ShowResult(result);
            }
        }

        void ShowResult(KeyCopier.ApplyResult r)
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
            ShowTextModal("Batch copy keys - result", sb.ToString());
        }

        void ShowTextModal(string title, string text)
        {
            using (var f = new Form
            {
                Text = title,
                Width = 900, Height = 560,
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
            MessageBox.Show(this, msg, "Batch copy keys", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    internal class BatchCopyKeysPreviewDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color ConflictFg  = Color.FromArgb(150, 30, 30);
        static readonly Color SkipFg      = Color.FromArgb(120, 120, 120);

        public BatchCopyKeysPreviewDialog(List<KeyCopier.PlanItem> plan, string dctPath)
        {
            Text = "Preview batch copy keys";
            Width = 1000; Height = 620;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = BgColor; ShowIcon = false; ShowInTaskbar = false;
            MinimumSize = new Size(780, 420);

            int add   = plan.Count(p => p.Action == KeyCopier.PlanAction.Add);
            int skip  = plan.Count(p => p.Action == KeyCopier.PlanAction.Skip);
            int conf  = plan.Count(p => p.Action == KeyCopier.PlanAction.NameConflict || p.Action == KeyCopier.PlanAction.MissingFields);

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = HeaderColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Text = string.Format("{0} to add   ·   {1} to skip   ·   {2} blocking conflicts", add, skip, conf)
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
            lv.Columns.Add("Action", 90);
            lv.Columns.Add("Source table", 140);
            lv.Columns.Add("Key", 180);
            lv.Columns.Add("Requires", 260);
            lv.Columns.Add("Target table", 160);
            lv.Columns.Add("Note", 260);

            foreach (var p in plan)
            {
                var item = new ListViewItem(new[]
                {
                    p.Action.ToString(),
                    p.SourceTableName,
                    p.KeyName,
                    string.Join(", ", p.ComponentLabels.ToArray()),
                    p.TargetTableName,
                    p.Reason ?? ""
                });
                if (p.Action == KeyCopier.PlanAction.NameConflict || p.Action == KeyCopier.PlanAction.MissingFields)
                    item.ForeColor = ConflictFg;
                else if (p.Action == KeyCopier.PlanAction.Skip) item.ForeColor = SkipFg;
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
                    : "Backup will be written to: " + KeyCopier.MakeBackupPath(dctPath)
            };

            var bp = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnApply = new Button
            {
                Text = conf > 0 ? "Abort (conflicts)" : "Apply changes",
                Width = 170, Height = 32, Dock = DockStyle.Right,
                FlatStyle = FlatStyle.System,
                DialogResult = conf > 0 ? DialogResult.Cancel : DialogResult.OK,
                Enabled = conf == 0 && add > 0
            };
            var btnCancel = new Button { Text = "Cancel", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System, DialogResult = DialogResult.Cancel };
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
