using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    internal class LintReportDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color ErrorFg     = Color.FromArgb(160, 30, 30);
        static readonly Color WarnFg      = Color.FromArgb(160, 95, 10);
        static readonly Color InfoFg      = Color.FromArgb(80, 95, 115);

        readonly object dict;
        readonly object singleTable; // null for full dict scan
        ListView lv;
        Label lblSummary;
        TextBox txtFilter;
        List<LintEngine.Finding> allFindings;

        public LintReportDialog(object dict, object singleTable)
        {
            this.dict = dict;
            this.singleTable = singleTable;
            BuildUi();
            RunScan();
        }

        void BuildUi()
        {
            var dictName = DictModel.GetDictionaryName(dict);
            Text = singleTable == null
                ? "Lint report - " + dictName
                : "Lint report - " + (DictModel.AsString(DictModel.GetProp(singleTable, "Name")) ?? "?");
            Width = 1100;
            Height = 680;
            MinimumSize = new Size(780, 420);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;
            ShowIcon = false;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
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
                Text = singleTable == null
                    ? "Lint report - " + dictName + "     " + DictModel.GetDictionaryFileName(dict)
                    : "Lint report - table " + (DictModel.AsString(DictModel.GetProp(singleTable, "Name")) ?? "?")
            };

            var filterBar = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = BgColor, Padding = new Padding(16, 8, 16, 4) };
            var lblFilter = new Label { Text = "Filter:", AutoSize = true, Top = 8, Left = 0, Font = new Font("Segoe UI", 9F) };
            txtFilter = new TextBox { Top = 4, Left = 46, Width = 320, Font = new Font("Segoe UI", 9.5F) };
            txtFilter.TextChanged += delegate { ApplyFilter(); };
            lblSummary = new Label { AutoSize = true, Top = 8, Left = 380, Width = 600, Font = new Font("Segoe UI", 9F), ForeColor = Color.FromArgb(100, 115, 135) };
            filterBar.Controls.Add(lblFilter);
            filterBar.Controls.Add(txtFilter);
            filterBar.Controls.Add(lblSummary);

            lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.None
            };
            lv.Columns.Add("Severity", 80);
            lv.Columns.Add("Target", 260);
            lv.Columns.Add("Rule", 170);
            lv.Columns.Add("Message", 500);

            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            var btnRescan = new Button { Text = "Rescan", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnRescan.Click += delegate { RunScan(); };
            var btnFix = new Button { Text = "Fix fields...", Width = 140, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnFix.Click += delegate
            {
                using (var dlg = new LintFixItDialog(dict, singleTable)) dlg.ShowDialog(this);
                RunScan();
            };
            var btnFixKeys = new Button { Text = "Fix keys...", Width = 140, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnFixKeys.Click += delegate
            {
                using (var dlg = new LintFixKeysDialog(dict, singleTable)) dlg.ShowDialog(this);
                RunScan();
            };
            btnPanel.Controls.Add(btnClose);
            btnPanel.Controls.Add(btnRescan);
            btnPanel.Controls.Add(btnFix);
            btnPanel.Controls.Add(btnFixKeys);

            Controls.Add(lv);
            Controls.Add(btnPanel);
            Controls.Add(filterBar);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        void RunScan()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                allFindings = singleTable == null
                    ? LintEngine.RunFullScan(dict)
                    : LintEngine.RunTableScan(singleTable);
            }
            finally { Cursor = Cursors.Default; }
            ApplyFilter();
        }

        void ApplyFilter()
        {
            if (allFindings == null) return;
            var q = (txtFilter.Text ?? "").Trim();
            var shown = string.IsNullOrEmpty(q)
                ? allFindings
                : allFindings.Where(f =>
                    Contains(f.Target, q) || Contains(f.Rule, q) || Contains(f.Message, q) ||
                    Contains(f.Severity.ToString(), q)).ToList();

            lv.BeginUpdate();
            try
            {
                lv.Items.Clear();
                foreach (var f in shown)
                {
                    var item = new ListViewItem(new[] { f.Severity.ToString(), f.Target, f.Rule, f.Message });
                    switch (f.Severity)
                    {
                        case LintEngine.Severity.Error:   item.ForeColor = ErrorFg; break;
                        case LintEngine.Severity.Warning: item.ForeColor = WarnFg;  break;
                        default:                          item.ForeColor = InfoFg;  break;
                    }
                    lv.Items.Add(item);
                }
            }
            finally { lv.EndUpdate(); }

            int errors  = shown.Count(f => f.Severity == LintEngine.Severity.Error);
            int warns   = shown.Count(f => f.Severity == LintEngine.Severity.Warning);
            int infos   = shown.Count(f => f.Severity == LintEngine.Severity.Info);
            lblSummary.Text = string.Format("{0} findings    {1} errors    {2} warnings    {3} info",
                shown.Count, errors, warns, infos);
        }

        static bool Contains(string hay, string needle)
        {
            if (string.IsNullOrEmpty(hay)) return false;
            return hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
