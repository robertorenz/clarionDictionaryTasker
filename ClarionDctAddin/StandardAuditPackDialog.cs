using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Copy a curated set of audit fields (Guid + CreatedOn/By + ModifiedOn/By
    // + DeletedOn) from a template table onto every selected target table.
    // The mutation path reuses FieldCopier, which is the same proven code
    // Batch copy fields runs through. The template table is how the tool
    // discovers the actual DDField definitions — without it we'd have to
    // construct DDField instances from scratch, which isn't portable across
    // Clarion 12 point releases.
    //
    // Workflow:
    //   1. Create or designate a template table with the audit fields.
    //   2. This dialog auto-matches the preset names against the template's
    //      fields and pre-checks everything it finds.
    //   3. Tick target tables, preview the plan, apply.
    //   4. Optionally export the plan as a Markdown recipe for the PR.
    internal class StandardAuditPackDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        // Canonical preset labels — case-insensitive match against the template.
        static readonly string[] PresetLabels = { "Guid", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "DeletedOn" };

        readonly object dict;
        ComboBox cbTemplate;
        CheckedListBox lstPackFields;  // fields on the template that match the preset
        CheckedListBox lstTargets;
        RadioButton rbSkip, rbAbort;
        Button btnApply, btnSaveRecipe;
        Label lblSummary;

        List<object> tables;
        List<object> matchedFields = new List<object>();  // parallel to lstPackFields items

        public StandardAuditPackDialog(object dict) { this.dict = dict; BuildUi(); PopulateTables(); SuggestTemplate(); }

        void BuildUi()
        {
            Text = "Standard audit pack - " + DictModel.GetDictionaryName(dict);
            Width = 1200; Height = 760;
            MinimumSize = new Size(940, 520);
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
                Text = "Standard audit pack   " + DictModel.GetDictionaryName(dict)
            };

            var row1 = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = BgColor, Padding = new Padding(16, 10, 16, 0) };
            var lblTpl = new Label { Text = "Template table (source of audit field definitions):", Left = 4, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            cbTemplate = new ComboBox { Left = 320, Top = 4, Width = 360, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            cbTemplate.SelectedIndexChanged += delegate { MatchPresets(); UpdateSummary(); };
            row1.Controls.Add(lblTpl); row1.Controls.Add(cbTemplate);

            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 24,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 4, 0, 0),
                Text = ""
            };

            var mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
                BackColor = BgColor, Panel1MinSize = 260, Panel2MinSize = 320
            };

            var leftStack = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                BackColor = BgColor, Panel1MinSize = 120, Panel2MinSize = 100
            };
            lstPackFields = new CheckedListBox
            {
                Dock = DockStyle.Fill, CheckOnClick = true,
                Font = new Font("Segoe UI", 9F), BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            lstPackFields.ItemCheck += delegate { BeginInvoke((Action)UpdateSummary); };
            leftStack.Panel1.Controls.Add(WrapSection("Audit fields (found on template)", lstPackFields));

            var targetPanel = new Panel { Dock = DockStyle.Fill, BackColor = BgColor, Padding = new Padding(6) };
            var targetHdr = new Label
            {
                Dock = DockStyle.Top, Height = 22,
                Font = new Font("Segoe UI Semibold", 9.5F),
                ForeColor = HeaderColor, Text = "Target tables"
            };
            var targetToolbar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = BgColor };
            var btnAll = new Button { Text = "All",   Width = 60, Height = 24, Left = 0,  Top = 2, FlatStyle = FlatStyle.System };
            btnAll.Click += delegate { SetAllTargets(true);  UpdateSummary(); };
            var btnNone = new Button { Text = "None", Width = 60, Height = 24, Left = 66, Top = 2, FlatStyle = FlatStyle.System };
            btnNone.Click += delegate { SetAllTargets(false); UpdateSummary(); };
            targetToolbar.Controls.Add(btnAll);
            targetToolbar.Controls.Add(btnNone);
            lstTargets = new CheckedListBox
            {
                Dock = DockStyle.Fill, CheckOnClick = true,
                Font = new Font("Segoe UI", 9F), BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            lstTargets.ItemCheck += delegate { BeginInvoke((Action)UpdateSummary); };
            targetPanel.Controls.Add(lstTargets);
            targetPanel.Controls.Add(targetToolbar);
            targetPanel.Controls.Add(targetHdr);
            leftStack.Panel2.Controls.Add(targetPanel);
            mainSplit.Panel1.Controls.Add(leftStack);

            var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = BgColor, Padding = new Padding(6) };
            var rightHdr = new Label
            {
                Dock = DockStyle.Top, Height = 22,
                Font = new Font("Segoe UI Semibold", 9.5F),
                ForeColor = HeaderColor, Text = "Plan options"
            };
            var opt = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            var lblMode = new Label { Text = "Conflict mode (target already has a field with this label):",
                Left = 12, Top = 12, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            rbSkip  = new RadioButton { Text = "Skip existing",       Left = 24, Top = 38, AutoSize = true, Checked = true, Font = new Font("Segoe UI", 9F) };
            rbAbort = new RadioButton { Text = "Abort batch",         Left = 24, Top = 62, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            var help = new Label
            {
                Left = 12, Top = 100, Width = 360, Height = 300,
                Font = new Font("Segoe UI", 9F), ForeColor = MutedColor,
                Text =
                    "How this works\r\n" +
                    "-----------------------------\r\n\r\n" +
                    "1. Pick a TEMPLATE TABLE that already has the audit fields " +
                    "defined the way you want (types / pictures / descriptions).\r\n\r\n" +
                    "2. The list on the left auto-matches the preset labels " +
                    "(Guid / CreatedOn / CreatedBy / ModifiedOn / ModifiedBy / " +
                    "DeletedOn) against that template. Tweak the check boxes " +
                    "if you want a subset.\r\n\r\n" +
                    "3. Tick every target table that should receive the pack. " +
                    "The mutation path is the same proven Batch-copy-fields code " +
                    "— it writes a .tasker-bak-<timestamp> backup of the .DCT " +
                    "first.\r\n\r\n" +
                    "4. If no template table exists yet, create one (e.g. " +
                    "\"_TEMPLATE_AUDIT\") and add the fields to it once; " +
                    "reuse it forever after."
            };
            opt.Controls.Add(lblMode);
            opt.Controls.Add(rbSkip);
            opt.Controls.Add(rbAbort);
            opt.Controls.Add(help);
            rightPanel.Controls.Add(opt);
            rightPanel.Controls.Add(rightHdr);
            mainSplit.Panel2.Controls.Add(rightPanel);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            btnApply = new Button { Text = "Preview && apply...", Width = 170, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnApply.Click += delegate { PreviewAndApply(); };
            btnSaveRecipe = new Button { Text = "Save recipe (.md)", Width = 160, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnSaveRecipe.Click += delegate { SaveRecipe(); };
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(btnApply);
            bottom.Controls.Add(btnSaveRecipe);

            Controls.Add(mainSplit);
            Controls.Add(bottom);
            Controls.Add(lblSummary);
            Controls.Add(row1);
            Controls.Add(header);
            CancelButton = btnClose;

            Load += delegate
            {
                mainSplit.SplitterDistance = (int)(mainSplit.Width * 0.55);
                leftStack.SplitterDistance = (int)(leftStack.Height * 0.42);
            };
        }

        static Control WrapSection(string title, Control content)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = BgColor, Padding = new Padding(6) };
            var lbl = new Label
            {
                Dock = DockStyle.Top, Height = 22,
                Font = new Font("Segoe UI Semibold", 9.5F),
                ForeColor = HeaderColor, Text = title
            };
            content.Dock = DockStyle.Fill;
            panel.Controls.Add(content);
            panel.Controls.Add(lbl);
            return panel;
        }

        void PopulateTables()
        {
            tables = DictModel.GetTables(dict)
                .OrderBy(t => DictModel.AsString(DictModel.GetProp(t, "Name")), StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var t in tables)
            {
                var n = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                cbTemplate.Items.Add(n);
                lstTargets.Items.Add(n, false);
            }
        }

        // Look for a table that contains the most preset labels; use it as the default template.
        void SuggestTemplate()
        {
            int bestScore = 0;
            int bestIdx = -1;
            for (int i = 0; i < tables.Count; i++)
            {
                int score = CountPresetMatches(tables[i]);
                if (score > bestScore) { bestScore = score; bestIdx = i; }
            }
            if (bestIdx >= 0) cbTemplate.SelectedIndex = bestIdx;
            else if (cbTemplate.Items.Count > 0) cbTemplate.SelectedIndex = 0;
        }

        int CountPresetMatches(object table)
        {
            int n = 0;
            foreach (var label in PresetLabels)
                if (FindField(table, label) != null) n++;
            return n;
        }

        static object FindField(object table, string label)
        {
            foreach (var f in FieldMutator.EnumerateFields(table))
            {
                var l = DictModel.AsString(DictModel.GetProp(f, "Label"));
                if (string.Equals(l, label, StringComparison.OrdinalIgnoreCase)) return f;
            }
            return null;
        }

        void MatchPresets()
        {
            lstPackFields.Items.Clear();
            matchedFields.Clear();
            if (cbTemplate.SelectedIndex < 0) return;
            var template = tables[cbTemplate.SelectedIndex];
            foreach (var label in PresetLabels)
            {
                var f = FindField(template, label);
                if (f == null)
                {
                    lstPackFields.Items.Add(label + "   (not found on template)", false);
                    matchedFields.Add(null);
                }
                else
                {
                    var dt   = DictModel.AsString(DictModel.GetProp(f, "DataType"))  ?? "";
                    var size = DictModel.AsString(DictModel.GetProp(f, "FieldSize")) ?? "";
                    var pic  = DictModel.AsString(DictModel.GetProp(f, "ScreenPicture")) ?? "";
                    lstPackFields.Items.Add(label + "   " + dt
                        + (string.IsNullOrEmpty(size) ? "" : " " + size)
                        + (string.IsNullOrEmpty(pic) ? "" : "   " + pic), true);
                    matchedFields.Add(f);
                }
            }
        }

        void SetAllTargets(bool on)
        {
            for (int i = 0; i < lstTargets.Items.Count; i++) lstTargets.SetItemChecked(i, on);
        }

        void UpdateSummary()
        {
            int fields = 0;
            for (int i = 0; i < lstPackFields.Items.Count; i++)
                if (lstPackFields.GetItemChecked(i) && matchedFields[i] != null) fields++;
            int targets = 0;
            for (int i = 0; i < lstTargets.Items.Count; i++)
                if (lstTargets.GetItemChecked(i)) targets++;
            lblSummary.Text = fields + " field(s) × " + targets + " target(s) = "
                + (fields * targets) + " insertion(s) planned.";
        }

        List<object> CheckedFields()
        {
            var list = new List<object>();
            for (int i = 0; i < lstPackFields.Items.Count; i++)
                if (lstPackFields.GetItemChecked(i) && matchedFields[i] != null)
                    list.Add(matchedFields[i]);
            return list;
        }

        List<object> CheckedTargets()
        {
            var list = new List<object>();
            for (int i = 0; i < lstTargets.Items.Count; i++)
                if (lstTargets.GetItemChecked(i)) list.Add(tables[i]);
            return list;
        }

        void PreviewAndApply()
        {
            if (cbTemplate.SelectedIndex < 0)
            { MessageBox.Show(this, "Pick a template table first.", "Audit pack", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var template = tables[cbTemplate.SelectedIndex];
            var fields   = CheckedFields();
            var targets  = CheckedTargets();
            if (fields.Count == 0)
            { MessageBox.Show(this, "No audit fields found on the template — add them to the template table first, or pick a different template.", "Audit pack", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (targets.Count == 0)
            { MessageBox.Show(this, "Tick at least one target table.", "Audit pack", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            // Don't let the user stamp the template onto itself.
            targets = targets.Where(t => !ReferenceEquals(t, template)).ToList();
            if (targets.Count == 0)
            { MessageBox.Show(this, "Template table is the only selected target — nothing to do.", "Audit pack", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            var mode = rbAbort.Checked ? FieldCopier.ConflictMode.Abort : FieldCopier.ConflictMode.Skip;
            var plan = FieldCopier.BuildPlan(template, fields, targets, mode);

            using (var dlg = new BatchCopyPreviewDialog(plan, DictModel.GetDictionaryFileName(dict)))
            {
                var dr = dlg.ShowDialog(this);
                if (dr != DialogResult.OK) return;

                var view    = DictModel.GetActiveDictionaryView();
                var dctPath = DictModel.GetDictionaryFileName(dict);

                var progress = new BatchProgressDialog(plan.Count);
                Enabled = false;
                progress.Show(this);
                progress.Report(0, "Starting...");
                FieldCopier.ApplyResult result;
                try
                {
                    result = FieldCopier.Apply(
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

        void ShowResult(FieldCopier.ApplyResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Added:   " + r.AddedCount);
            sb.AppendLine("Skipped: " + r.SkippedCount);
            sb.AppendLine("Failed:  " + r.FailedCount);
            sb.AppendLine();
            if (!string.IsNullOrEmpty(r.BackupPath))
                sb.AppendLine("Backup: " + r.BackupPath);
            sb.AppendLine();
            if (r.AddedCount > 0)
                sb.AppendLine("The dictionary is now DIRTY. Press Ctrl+S in Clarion to save.");
            MessageBox.Show(this, sb.ToString(), "Audit pack result",
                MessageBoxButtons.OK, r.FailedCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        void SaveRecipe()
        {
            if (cbTemplate.SelectedIndex < 0) return;
            var template = tables[cbTemplate.SelectedIndex];
            var templateName = DictModel.AsString(DictModel.GetProp(template, "Name")) ?? "?";
            var fields   = CheckedFields();
            var targets  = CheckedTargets();

            var sb = new StringBuilder();
            sb.AppendLine("# Standard audit pack — recipe");
            sb.AppendLine();
            sb.AppendLine("- **Dictionary:** `" + DictModel.GetDictionaryName(dict) + "`");
            sb.AppendLine("- **Template table:** `" + templateName + "`");
            sb.AppendLine("- **Generated:** " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine();
            sb.AppendLine("## Fields to copy from template");
            sb.AppendLine();
            sb.AppendLine("| Label | Type | Size | Picture |");
            sb.AppendLine("|-------|------|------|---------|");
            foreach (var f in fields)
            {
                sb.AppendLine("| `" + (DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "") + "` | "
                    + (DictModel.AsString(DictModel.GetProp(f, "DataType")) ?? "") + " | "
                    + (DictModel.AsString(DictModel.GetProp(f, "FieldSize")) ?? "") + " | `"
                    + (DictModel.AsString(DictModel.GetProp(f, "ScreenPicture")) ?? "") + "` |");
            }
            sb.AppendLine();
            sb.AppendLine("## Target tables");
            sb.AppendLine();
            if (targets.Count == 0) sb.AppendLine("_None selected._");
            else foreach (var t in targets)
                sb.AppendLine("- `" + (DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "") + "`");

            var suggested = "audit-pack-recipe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".md";
            using (var dlg = new SaveFileDialog
            {
                Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*",
                FileName = suggested
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, sb.ToString());
                    MessageBox.Show(this, "Saved: " + dlg.FileName, "Audit pack recipe",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Save failed: " + ex.Message, "Audit pack recipe",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
