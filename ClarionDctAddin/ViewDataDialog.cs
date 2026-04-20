using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // "View data" — opens the table's TPS file via Clarion's runtime file API and
    // renders a row-sample in a DataGridView. Currently TPS-only; other drivers
    // show a clear "not supported yet" message. Read-only — we never write back.
    internal class ViewDataDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        readonly object dict;
        readonly object table;

        DataGridView  grid;
        Label         lblStatus;
        NumericUpDown numRows;
        Button        btnLoad, btnShowLog;
        TextBox       txtLog;
        SplitContainer split;

        ClarionFileAccessor.ReadResult lastResult;
        SqlTableAccessor.ReadResult    lastSqlResult;

        public ViewDataDialog(object dict, object table)
        {
            this.dict = dict;
            this.table = table;
            BuildUi();
        }

        void BuildUi()
        {
            var tName = DictModel.AsString(DictModel.GetProp(table, "Name")) ?? "?";
            Text = "View data - " + tName;
            Width = 1160; Height = 720;
            MinimumSize = new Size(860, 460);
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
                Text = "View data   table: " + tName
                    + "   (" + (DictModel.AsString(DictModel.GetProp(table, "FileDriverName")) ?? "?") + ")"
            };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = BgColor, Padding = new Padding(16, 8, 16, 4) };
            var lblRows = new Label { Text = "Rows:", Left = 0, Top = 10, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            numRows = new NumericUpDown { Left = 42, Top = 6, Width = 80, Minimum = 1, Maximum = 10000, Value = 25, Font = new Font("Segoe UI", 9.5F) };
            btnLoad  = new Button { Text = "Load", Left = 134, Top = 4, Width = 90, Height = 30, FlatStyle = FlatStyle.System };
            btnLoad.Click += delegate { LoadRows(); };
            btnShowLog = new Button { Text = "Show log", Left = 234, Top = 4, Width = 110, Height = 30, FlatStyle = FlatStyle.System };
            btnShowLog.Click += delegate { ToggleLog(); };
            var btnConn = new Button { Text = "Change connection...", Left = 354, Top = 4, Width = 160, Height = 30, FlatStyle = FlatStyle.System };
            btnConn.Click += delegate { ChangeConnection(); };
            var btnTopScan = new Button { Text = "Open in TopScan", Left = 524, Top = 4, Width = 150, Height = 30, FlatStyle = FlatStyle.System };
            btnTopScan.Click += delegate { OpenInTopScan(); };
            var btnEmbed = new Button { Text = "Embed TopScan", Left = 684, Top = 4, Width = 150, Height = 30, FlatStyle = FlatStyle.System };
            btnEmbed.Click += delegate { EmbedTopScan(); };
            toolbar.Controls.Add(lblRows);
            toolbar.Controls.Add(numRows);
            toolbar.Controls.Add(btnLoad);
            toolbar.Controls.Add(btnShowLog);
            toolbar.Controls.Add(btnConn);
            toolbar.Controls.Add(btnTopScan);
            toolbar.Controls.Add(btnEmbed);

            lblStatus = new Label
            {
                Dock = DockStyle.Top, Height = 26,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 6, 0, 0),
                Text = "Pick a row count and click Load."
            };

            split = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                BackColor = BgColor, Panel1MinSize = 120, Panel2MinSize = 80
            };

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = true,
                ReadOnly = true,
                Font = new Font("Consolas", 9F),
                ColumnHeadersDefaultCellStyle = { BackColor = Color.FromArgb(230, 235, 242), Font = new Font("Segoe UI Semibold", 9F) },
                EnableHeadersVisualStyles = false
            };
            split.Panel1.Controls.Add(grid);

            txtLog = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new Font("Consolas", 9F), BackColor = Color.White
            };
            split.Panel2.Controls.Add(txtLog);
            split.Panel2Collapsed = true;

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(btnClose);

            Controls.Add(split);
            Controls.Add(bottom);
            Controls.Add(lblStatus);
            Controls.Add(toolbar);
            Controls.Add(header);
            CancelButton = btnClose;

            Load += delegate { LoadRows(); };
        }

        void ToggleLog()
        {
            split.Panel2Collapsed = !split.Panel2Collapsed;
            btnShowLog.Text = split.Panel2Collapsed ? "Show log" : "Hide log";
            if (!split.Panel2Collapsed) txtLog.Text = CurrentLog();
        }

        string CurrentLog()
        {
            if (lastSqlResult != null) return string.Join("\r\n", lastSqlResult.Log.ToArray());
            if (lastResult    != null) return string.Join("\r\n", lastResult   .Log.ToArray());
            return "";
        }

        void LoadRows()
        {
            Cursor = Cursors.WaitCursor;
            btnLoad.Enabled = false;
            try
            {
                int n = (int)numRows.Value;
                var driver = DictModel.AsString(DictModel.GetProp(table, "FileDriverName")) ?? "";
                lastResult = null;
                lastSqlResult = null;

                if (SqlTableAccessor.IsSqlServerDriver(driver))
                    LoadViaSql(n);
                else
                    LoadViaClarionFile(n);

                if (!split.Panel2Collapsed) txtLog.Text = CurrentLog();
            }
            finally
            {
                btnLoad.Enabled = true;
                Cursor = Cursors.Default;
            }
        }

        void LoadViaSql(int n)
        {
            var connStr = ResolveMssqlConnection(promptIfMissing: true);
            if (connStr == null)
            {
                lblStatus.Text = "Connection cancelled.";
                return;
            }

            var r = SqlTableAccessor.Read(dict, table, n, connStr);
            lastSqlResult = r;
            grid.Columns.Clear();
            grid.Rows.Clear();
            if (!r.Ok)
            {
                lblStatus.Text = "Could not read data: " + r.Error + "   ·   click Show log for details.";
                return;
            }
            for (int i = 0; i < r.ColumnNames.Count; i++)
            {
                var col = new DataGridViewTextBoxColumn
                {
                    HeaderText = r.ColumnNames[i] + "\n" + r.ColumnTypes[i],
                    Name = r.ColumnNames[i]
                };
                grid.Columns.Add(col);
            }
            foreach (var row in r.Rows)
                grid.Rows.Add(row.Select(v => v == null ? "" : v.ToString()).ToArray());
            lblStatus.Text = r.Rows.Count + " row(s) loaded via MSSQL   ·   "
                + grid.Columns.Count + " columns";
        }

        // Pulls the connection string from:
        //   1. the saved per-dict Settings entry (skips the prompt on reloads),
        //   2. the dict's Owner string if it's a literal 'server,database,...',
        //   3. an interactive prompt (saved on OK for next time).
        // Returns null when the user cancels the prompt.
        string ResolveMssqlConnection(bool promptIfMissing)
        {
            var dictName = DictModel.GetDictionaryName(dict) ?? "dict";
            var saved = Settings.MssqlConnectionFor(dictName);
            if (!string.IsNullOrEmpty(saved)) return saved;

            var owner = DictModel.AsString(DictModel.GetProp(table, "OwnerName")) ?? "";
            if (!string.IsNullOrWhiteSpace(owner)
                && !SqlTableAccessor.IsRuntimeOwnerRef(owner))
            {
                string ignored;
                var fromOwner = SqlTableAccessor.TryBuildConnectionStringFromOwner(owner, out ignored);
                if (!string.IsNullOrEmpty(fromOwner)) return fromOwner;
            }

            if (!promptIfMissing) return null;

            using (var dlg = new SqlConnectionPromptDialog(dictName, saved))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                Settings.SetMssqlConnectionFor(dictName, dlg.ResultConnectionString);
                return dlg.ResultConnectionString;
            }
        }

        void OpenInTopScan()
        {
            // Resolve the TPS path the same way the inline reader does — FullPathName,
            // falling back to the DefaultFileName alongside the .DCT.
            var tpsPath = ResolveTpsPath();
            if (string.IsNullOrEmpty(tpsPath))
            {
                MessageBox.Show(this, "Could not resolve a .TPS path for this table.",
                    "TopScan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string err;
            if (!TopScanLauncher.Launch(tpsPath, out err))
            {
                MessageBox.Show(this, err, "TopScan", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            lblStatus.Text = "Launched TopScan on " + tpsPath;
        }

        void EmbedTopScan()
        {
            var tpsPath = ResolveTpsPath();
            if (string.IsNullOrEmpty(tpsPath))
            {
                MessageBox.Show(this, "Could not resolve a .TPS path for this table.",
                    "TopScan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using (var dlg = new TopScanEmbedDialog(tpsPath))
                dlg.ShowDialog(this);
            lblStatus.Text = "Embedded TopScan closed.";
        }

        string ResolveTpsPath()
        {
            var full = DictModel.AsString(DictModel.GetProp(table, "FullPathName")) ?? "";
            if (!string.IsNullOrEmpty(full) && File.Exists(full)) return full;
            var defName = DictModel.AsString(DictModel.GetProp(table, "DefaultFileName")) ?? full;
            if (string.IsNullOrEmpty(defName))
                defName = (DictModel.AsString(DictModel.GetProp(table, "Name")) ?? "table") + ".tps";
            var dctPath = DictModel.GetDictionaryFileName(dict);
            if (!string.IsNullOrEmpty(dctPath))
            {
                var dir = Path.GetDirectoryName(dctPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    var cand = Path.Combine(dir, defName);
                    if (File.Exists(cand)) return cand;
                    var cand2 = Path.Combine(dir, Path.GetFileName(defName));
                    if (File.Exists(cand2)) return cand2;
                }
            }
            return full;
        }

        void ChangeConnection()
        {
            var dictName = DictModel.GetDictionaryName(dict) ?? "dict";
            var current = Settings.MssqlConnectionFor(dictName);
            using (var dlg = new SqlConnectionPromptDialog(dictName, current))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Settings.SetMssqlConnectionFor(dictName, dlg.ResultConnectionString);
                lblStatus.Text = "Connection updated — click Load to reload.";
            }
        }

        void LoadViaClarionFile(int n)
        {
            // TPS drivers: read the TPS bytes directly via our bundled
            // tps-parse-net — no native ClaTPS.dll / CLegacy involvement.
            // Other drivers fall back to the Clarion-runtime reader (which
            // is currently TPS-gated with a graceful error).
            var driver = (DictModel.AsString(DictModel.GetProp(table, "FileDriverName")) ?? "").ToUpperInvariant();
            ClarionFileAccessor.ReadResult r;
            if (driver == "TOPSPEED" || driver == "TPS" || driver == "TOPSCAN" || driver == "TPSCAN")
                r = TpsDirectReader.Read(dict, table, n);
            else
                r = ClarionFileAccessor.OpenForRead(dict, table, n);
            lastResult = r;
            grid.Columns.Clear();
            grid.Rows.Clear();
            if (!r.Ok)
            {
                lblStatus.Text = "Could not read data: " + r.Error + "   ·   click Show log for details.";
                return;
            }
            for (int i = 0; i < r.ColumnLabels.Count; i++)
            {
                var col = new DataGridViewTextBoxColumn
                {
                    HeaderText = r.ColumnLabels[i] + "\n" + r.ColumnTypes[i],
                    Name = r.ColumnLabels[i]
                };
                grid.Columns.Add(col);
            }
            foreach (var rowObj in r.Rows)
            {
                var values = rowObj as List<object>;
                if (values == null) continue;
                grid.Rows.Add(values.Select(v => v == null ? "" : v.ToString()).ToArray());
            }
            lblStatus.Text = r.Rows.Count + " row(s) loaded"
                + "   ·   scanned " + r.TotalScanned
                + (r.Rows.Count < n ? "   ·   end of file reached before " + n + " rows" : "");
        }
    }
}
