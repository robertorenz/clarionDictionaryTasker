using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Flip a batch of tables from TPS (or anything else) to a SQL driver in one
    // pass. Covers the usual real-world migration inputs:
    //   - Target driver       (MSSQL / ODBC / ADO / SQLite / Oracle / ...)
    //   - Driver Options      (often a macro like !glo:driveroptions)
    //   - Owner Name          (often !glo:owner)
    //   - Full Name           (copy Label with an optional "dbo." schema prefix)
    //   - Create / Threaded / Encrypt / Bindable attributes
    //     three-state: leave alone / turn ON / turn OFF
    // Preview the plan, then apply through FieldMutator with a .DCT backup.
    internal class SqlMigrationDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color WarningBg   = Color.FromArgb(255, 247, 225);
        static readonly Color WarningFg   = Color.FromArgb(120, 80, 10);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        // Three-state for each attribute toggle. "Leave" means the dialog will
        // not touch that property on any selected table — critical so users
        // don't accidentally wipe attributes they never intended to change.
        enum Tri { Leave = 0, On = 1, Off = 2 }

        readonly object dict;
        readonly List<object> tables;

        TextBox  txtFilter;
        ComboBox cboDriverFilter;
        ListView lvTables;
        ListView lvPreview;
        Label    lblTablesSummary;
        Label    lblPreviewSummary;
        Button   btnApply;
        CheckBox chkExcludeAliases;

        ComboBox cboNewDriver;
        TextBox  txtDriverOptions;
        TextBox  txtOwner;
        CheckBox chkCopyLabelToFullName;
        TextBox  txtSchemaPrefix;
        ComboBox cboCreate, cboThreaded, cboEncrypt, cboBindable;

        // Does this build of DDFile actually expose a writable Bindable bool?
        // Probed once against the first table in the dictionary — if it's not
        // there, the combo stays disabled with a note.
        bool bindableAvailable;

        // Snapshot of the plan produced by the last Preview.
        readonly List<PlanItem> currentPlan = new List<PlanItem>();

        sealed class PlanItem
        {
            public object Table;
            public string TableLabel;

            public string BeforeDriver, AfterDriver;
            public string BeforeOptions, AfterOptions;
            public string BeforeOwner, AfterOwner;
            public string BeforeFullName, AfterFullName;
            public bool?  BeforeCreate, AfterCreate;
            public bool?  BeforeThreaded, AfterThreaded;
            public bool?  BeforeEncrypt, AfterEncrypt;
            public bool?  BeforeBindable, AfterBindable;
        }

        public SqlMigrationDialog(object dict)
        {
            this.dict = dict;
            this.tables = DictModel.GetTables(dict)
                .OrderBy(t => DictModel.AsString(DictModel.GetProp(t, "Name")), StringComparer.OrdinalIgnoreCase)
                .ToList();
            bindableAvailable = tables.Count > 0 && FieldMutator.HasWritableBoolProp(tables[0], "Bindable");
            BuildUi();
            PopulateTables();
        }

        void BuildUi()
        {
            Text = "SQL Migration - " + DictModel.GetDictionaryName(dict);
            Width = 1280;
            Height = 800;
            MinimumSize = new Size(1020, 600);
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
                Text = "SQL Migration   " + DictModel.GetDictionaryFileName(dict)
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
                Text = "Changes driver + related attributes on selected tables. A .DCT backup is written first; press Ctrl+S in Clarion to save."
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
            body.Controls.Add(BuildTablesPane(),  0, 0);
            body.Controls.Add(BuildOptionsPane(), 1, 0);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            btnApply = new Button { Text = "Apply plan...", Width = 150, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System, Enabled = false };
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
            var bAll      = MakeSmall("Select all",     0);   bAll.Click      += delegate { SetAllChecked(lvTables, true);  UpdateTablesSummary(); };
            var bNone     = MakeSmall("Clear all",      92);  bNone.Click     += delegate { SetAllChecked(lvTables, false); UpdateTablesSummary(); };
            var bFiltered = MakeSmall("Check filtered", 184); bFiltered.Width = 108; bFiltered.Click += delegate { CheckVisible(); UpdateTablesSummary(); };
            var bTps      = MakeSmall("Check TPS",      296); bTps.Width      = 108; bTps.Click      += delegate { CheckByDriver("TOPSPEED"); UpdateTablesSummary(); };
            tools.Controls.Add(bAll);
            tools.Controls.Add(bNone);
            tools.Controls.Add(bFiltered);
            tools.Controls.Add(bTps);

            var filter = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = BgColor, Padding = new Padding(0, 4, 0, 4) };
            var lf = new Label { Text = "Filter:", Left = 0, Top = 6, Width = 40, Font = new Font("Segoe UI", 9F) };
            txtFilter = new TextBox { Left = 44, Top = 2, Width = 180, Font = new Font("Segoe UI", 9.5F) };
            txtFilter.TextChanged += delegate { ApplyFilter(); };
            var lfd = new Label { Text = "Driver:", Left = 234, Top = 6, Width = 46, Font = new Font("Segoe UI", 9F) };
            cboDriverFilter = new ComboBox
            {
                Left = 282, Top = 2, Width = 130,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5F)
            };
            PopulateDriverFilter();
            cboDriverFilter.SelectedIndexChanged += delegate { ApplyFilter(); };
            chkExcludeAliases = new CheckBox
            {
                Text = "Exclude aliases",
                Left = 422, Top = 4,
                AutoSize = true,
                Checked = Settings.BatchExcludeAliases,
                Font = new Font("Segoe UI", 9F)
            };
            chkExcludeAliases.CheckedChanged += delegate
            {
                Settings.BatchExcludeAliases = chkExcludeAliases.Checked;
                ApplyFilter();
            };
            filter.Controls.Add(lf);
            filter.Controls.Add(txtFilter);
            filter.Controls.Add(lfd);
            filter.Controls.Add(cboDriverFilter);
            filter.Controls.Add(chkExcludeAliases);

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
            lvTables.Columns.Add("Name",       180);
            lvTables.Columns.Add("Prefix",      60);
            lvTables.Columns.Add("Driver",      90);
            lvTables.Columns.Add("Owner",      160);
            lvTables.Columns.Add("Full name",  220);
            lvTables.ItemChecked += delegate { UpdateTablesSummary(); };

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

        Panel BuildOptionsPane()
        {
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0), BackColor = BgColor };

            var grp = new GroupBox
            {
                Dock = DockStyle.Top,
                Height = 290,
                Text = "Migration settings",
                Font = new Font("Segoe UI Semibold", 9F),
                BackColor = BgColor
            };

            // Row layout starts well below the GroupBox title strip (~16px)
            // so the "Migration settings" header isn't hidden under the first control.
            int row = 0;
            int gap = 28;
            int topBase = 22;

            var lblDrv = new Label { Text = "New driver:", Left = 10, Top = topBase + row * gap + 2, Width = 110, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9F) };
            cboNewDriver = new ComboBox
            {
                Left = 124, Top = topBase + row * gap, Width = 260,
                DropDownStyle = ComboBoxStyle.DropDown,
                Font = new Font("Segoe UI", 9.5F)
            };
            PopulateNewDriverCombo();
            grp.Controls.Add(lblDrv);
            grp.Controls.Add(cboNewDriver);
            row++;

            var lblOpts = new Label { Text = "Driver options:", Left = 10, Top = topBase + row * gap + 2, Width = 110, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9F) };
            txtDriverOptions = new TextBox { Left = 124, Top = topBase + row * gap, Width = 420, Font = new Font("Consolas", 9.5F) };
            txtDriverOptions.Text = "!glo:driveroptions";
            grp.Controls.Add(lblOpts);
            grp.Controls.Add(txtDriverOptions);
            row++;

            var lblOwn = new Label { Text = "Owner name:", Left = 10, Top = topBase + row * gap + 2, Width = 110, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9F) };
            txtOwner = new TextBox { Left = 124, Top = topBase + row * gap, Width = 420, Font = new Font("Consolas", 9.5F) };
            txtOwner.Text = "!glo:owner";
            grp.Controls.Add(lblOwn);
            grp.Controls.Add(txtOwner);
            row++;

            var lblFn = new Label { Text = "Full name:", Left = 10, Top = topBase + row * gap + 2, Width = 110, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9F) };
            chkCopyLabelToFullName = new CheckBox
            {
                Text = "Copy table Label with schema prefix:",
                Left = 124, Top = topBase + row * gap + 2,
                AutoSize = true, Checked = true,
                Font = new Font("Segoe UI", 9F)
            };
            txtSchemaPrefix = new TextBox
            {
                Left = 390, Top = topBase + row * gap,
                Width = 80,
                Text = "dbo.",
                Font = new Font("Consolas", 9.5F)
            };
            grp.Controls.Add(lblFn);
            grp.Controls.Add(chkCopyLabelToFullName);
            grp.Controls.Add(txtSchemaPrefix);
            row++;

            var note = new Label
            {
                Text = "Leave Driver Options / Owner Name blank to keep the current value on each table.",
                Left = 10, Top = topBase + row * gap + 2,
                Width = 540, AutoSize = false, Height = 18,
                ForeColor = MutedColor,
                Font = new Font("Segoe UI", 8.5F)
            };
            grp.Controls.Add(note);
            row++;

            // Attributes block
            var attrHeader = new Label
            {
                Text = "Attributes (each is three-state)",
                Left = 10, Top = topBase + row * gap + 6,
                Width = 400, Height = 20, AutoSize = false,
                Font = new Font("Segoe UI Semibold", 9F)
            };
            grp.Controls.Add(attrHeader);

            int attrRow = 0;
            int attrGap = 30;
            int attrTop = topBase + row * gap + 28;

            cboCreate   = BuildTri("Create:",   10,  attrTop + attrRow * attrGap);  grp.Controls.Add(cboCreate.Parent);  attrRow++;
            cboThreaded = BuildTri("Threaded:", 10,  attrTop + attrRow * attrGap);  grp.Controls.Add(cboThreaded.Parent); attrRow++;
            cboEncrypt  = BuildTri("Encrypt:",  280, attrTop + 0        * attrGap); grp.Controls.Add(cboEncrypt.Parent);
            cboBindable = BuildTri("Bindable:", 280, attrTop + 1        * attrGap); grp.Controls.Add(cboBindable.Parent);
            if (!bindableAvailable)
            {
                cboBindable.Enabled = false;
                cboBindable.SelectedIndex = 0;
                var warn = new Label
                {
                    Text = "(Bindable not writable in this build)",
                    Left = cboBindable.Parent.Left + 440,
                    Top  = cboBindable.Parent.Top + 4,
                    AutoSize = true,
                    ForeColor = MutedColor,
                    Font = new Font("Segoe UI", 8.5F)
                };
                grp.Controls.Add(warn);
            }

            // Preview pane below options
            var lblPrev = new Label
            {
                Text = "Preview (what will change)",
                Dock = DockStyle.Top,
                Height = 22,
                Padding = new Padding(0, 4, 0, 2),
                Font = new Font("Segoe UI Semibold", 9F)
            };
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
            lvPreview.Columns.Add("Table",       150);
            lvPreview.Columns.Add("Property",    100);
            lvPreview.Columns.Add("Before",      180);
            lvPreview.Columns.Add("After",       180);

            lblPreviewSummary = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(2, 4, 0, 0),
                Text = "Select tables, set options, then click Preview."
            };

            host.Controls.Add(lvPreview);
            host.Controls.Add(lblPreviewSummary);
            host.Controls.Add(lblPrev);
            host.Controls.Add(grp);
            return host;
        }

        // Build a three-state combo inside a host panel that also hosts its
        // label. Returns the combo; host is combo.Parent.
        ComboBox BuildTri(string label, int left, int top)
        {
            var host = new Panel { Left = left, Top = top, Width = 260, Height = 26, BackColor = BgColor };
            var lbl = new Label { Text = label, Left = 0, Top = 4, Width = 72, AutoSize = false, Font = new Font("Segoe UI", 9F) };
            var cb  = new ComboBox
            {
                Left = 76, Top = 0, Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5F)
            };
            cb.Items.AddRange(new object[] { "Leave alone", "Turn ON", "Turn OFF" });
            cb.SelectedIndex = 0;
            host.Controls.Add(lbl);
            host.Controls.Add(cb);
            return cb;
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

        void CheckByDriver(string driver)
        {
            foreach (ListViewItem i in lvTables.Items)
            {
                var d = (i.SubItems.Count > 2 ? i.SubItems[2].Text : "") ?? "";
                if (string.Equals(d, driver, StringComparison.OrdinalIgnoreCase)) i.Checked = true;
            }
        }

        const string AllDriversLabel = "(all drivers)";

        // Canonical driver DLL -> driver token names as used in Clarion's DRIVER()
        // statements and the .DCT driverString field. Only DLLs that are present
        // on disk are surfaced; unknown Cla*.dll files on the system just aren't
        // listed (the combo is editable so custom drivers still work by typing).
        static readonly Dictionary<string, string> KnownDriverDlls =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ClaADO",   "ADO" },
            { "ClaASC",   "ASCII" },
            { "ClaBAS",   "BASIC" },
            { "ClaBTR",   "BTRIEVE" },
            { "ClaCLA",   "CLARION" },
            { "ClaCLP",   "CLIPPER" },
            { "ClaDB3",   "DBASE3" },
            { "ClaDB4",   "DBASE4" },
            { "ClaDOS",   "DOS" },
            { "ClaFOX",   "FOXPRO" },
            { "ClaIBC",   "INTERBASE" },
            { "ClaLIT",   "SQLITE" },
            { "ClaMEM",   "MEMORY" },
            { "ClaMSS",   "MSSQL" },
            { "ClaODB",   "ODBC" },
            { "ClaORA",   "ORACLE" },
            { "ClaSCA",   "SCALEABLE" },
            { "ClaSQA",   "SQLANY" },
            { "ClaTPS",   "TOPSPEED" },
            { "CLADOS2",  "DOS2" },
            { "CLALIT2",  "SQLITE2" },
            { "CLAMEM2",  "MEMORY2" },
            { "CLAMSS2",  "MSSQL2" },
        };

        // Build the dropdown: union of drivers installed on disk (Cla*.dll
        // matches in C:\clarion12\bin) + drivers already used by any table in
        // the dict. Both sources are guaranteed to work — installed DLLs mean
        // Clarion can resolve the driver on save/load; in-use drivers mean
        // sibling-borrow will succeed immediately.
        void PopulateNewDriverCombo()
        {
            var seen = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var d in EnumerateInstalledDrivers()) seen.Add(d);
            foreach (var t in tables)
            {
                var drv = DictModel.AsString(DictModel.GetProp(t, "FileDriverName"));
                if (!string.IsNullOrEmpty(drv)) seen.Add(drv);
            }

            cboNewDriver.Items.Clear();
            foreach (var d in seen) cboNewDriver.Items.Add(d);
            if (cboNewDriver.Items.Count == 0)
            {
                // Shouldn't happen in practice — but keep the combo usable.
                cboNewDriver.Items.Add("MSSQL");
            }
            var mssqlIndex = cboNewDriver.Items.IndexOf("MSSQL");
            cboNewDriver.SelectedIndex = mssqlIndex >= 0 ? mssqlIndex : 0;
        }

        static IEnumerable<string> EnumerateInstalledDrivers()
        {
            string[] binDirs =
            {
                @"C:\clarion12\bin",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? "", "")
            };
            foreach (var dir in binDirs)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                DirectoryInfo di;
                try { di = new DirectoryInfo(dir); }
                catch { continue; }
                if (!di.Exists) continue;
                FileInfo[] files;
                try { files = di.GetFiles("Cla*.dll"); }
                catch { continue; }
                foreach (var f in files)
                {
                    var stem = Path.GetFileNameWithoutExtension(f.Name);
                    string name;
                    if (KnownDriverDlls.TryGetValue(stem, out name)) yield return name;
                }
            }
        }

        void PopulateDriverFilter()
        {
            var distinct = tables
                .Select(t => DictModel.AsString(DictModel.GetProp(t, "FileDriverName")) ?? "")
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
            cboDriverFilter.Items.Clear();
            cboDriverFilter.Items.Add(AllDriversLabel);
            foreach (var d in distinct) cboDriverFilter.Items.Add(d);
            cboDriverFilter.SelectedIndex = 0;
        }

        void PopulateTables() { RebuildTableList(""); }

        void ApplyFilter() { RebuildTableList((txtFilter.Text ?? "").Trim()); }

        void RebuildTableList(string filter)
        {
            bool excludeAliases = chkExcludeAliases == null || chkExcludeAliases.Checked;
            string driverFilter = null;
            if (cboDriverFilter != null
                && cboDriverFilter.SelectedItem != null
                && cboDriverFilter.SelectedIndex > 0)
            {
                driverFilter = cboDriverFilter.SelectedItem.ToString();
            }

            lvTables.BeginUpdate();
            lvTables.Items.Clear();
            foreach (var t in tables)
            {
                if (excludeAliases && DictModel.IsAlias(t)) continue;
                var name = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                if (filter.Length > 0 && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var prefix   = DictModel.AsString(DictModel.GetProp(t, "Prefix")) ?? "";
                var drv      = DictModel.AsString(DictModel.GetProp(t, "FileDriverName")) ?? "";
                if (driverFilter != null && !string.Equals(drv, driverFilter, StringComparison.OrdinalIgnoreCase)) continue;
                var owner    = DictModel.AsString(DictModel.GetProp(t, "OwnerName")) ?? "";
                var fullName = DictModel.AsString(DictModel.GetProp(t, "FullPathName")) ?? "";
                var display  = DictModel.IsAlias(t) ? name + "  (alias)" : name;
                var item = new ListViewItem(new[] { display, prefix, drv, owner, fullName });
                item.Tag = t;
                lvTables.Items.Add(item);
            }
            lvTables.EndUpdate();
            UpdateTablesSummary();
        }

        void UpdateTablesSummary()
        {
            int shown  = lvTables.Items.Count;
            int checkd = lvTables.CheckedItems.Count;
            lblTablesSummary.Text = shown + " shown   ·   " + checkd + " checked   ·   " + tables.Count + " total in dictionary";
        }

        static Tri TriOf(ComboBox cb) { return cb == null ? Tri.Leave : (Tri)cb.SelectedIndex; }

        static bool? TriToBool(Tri t)
        {
            switch (t)
            {
                case Tri.On:  return true;
                case Tri.Off: return false;
                default:      return null;
            }
        }

        void RefreshPreview()
        {
            currentPlan.Clear();
            lvPreview.BeginUpdate();
            lvPreview.Items.Clear();

            var checkedItems = lvTables.CheckedItems.Cast<ListViewItem>().ToList();
            if (checkedItems.Count == 0)
            {
                lvPreview.EndUpdate();
                lblPreviewSummary.Text = "Check at least one table.";
                btnApply.Enabled = false;
                return;
            }

            var newDriver   = (cboNewDriver.Text ?? "").Trim();
            var newOptions  = (txtDriverOptions.Text ?? "");
            var newOwner    = (txtOwner.Text ?? "");
            bool copyLabel  = chkCopyLabelToFullName.Checked;
            var schema      = (txtSchemaPrefix.Text ?? "");
            var triCreate   = TriOf(cboCreate);
            var triThreaded = TriOf(cboThreaded);
            var triEncrypt  = TriOf(cboEncrypt);
            var triBindable = bindableAvailable ? TriOf(cboBindable) : Tri.Leave;

            int totalEdits = 0;
            foreach (ListViewItem i in checkedItems)
            {
                var t = i.Tag;
                if (t == null) continue;
                var tLabel = DictModel.AsString(DictModel.GetProp(t, "Label")) ?? "?";
                var tName  = DictModel.AsString(DictModel.GetProp(t, "Name"))  ?? tLabel;

                var plan = new PlanItem { Table = t, TableLabel = tName };
                plan.BeforeDriver   = DictModel.AsString(DictModel.GetProp(t, "FileDriverName")) ?? "";
                plan.BeforeOptions  = DictModel.AsString(DictModel.GetProp(t, "DriverOptions")) ?? "";
                plan.BeforeOwner    = DictModel.AsString(DictModel.GetProp(t, "OwnerName")) ?? "";
                plan.BeforeFullName = DictModel.AsString(DictModel.GetProp(t, "FullPathName")) ?? "";
                plan.BeforeCreate   = ReadBool(t, "Create");
                plan.BeforeThreaded = ReadBool(t, "Threaded");
                plan.BeforeEncrypt  = ReadBool(t, "Encrypt");
                plan.BeforeBindable = bindableAvailable ? ReadBool(t, "Bindable") : (bool?)null;

                plan.AfterDriver   = string.IsNullOrEmpty(newDriver) ? plan.BeforeDriver : newDriver;
                plan.AfterOptions  = newOptions == "" ? plan.BeforeOptions : newOptions;
                plan.AfterOwner    = newOwner   == "" ? plan.BeforeOwner   : newOwner;
                plan.AfterFullName = copyLabel ? (schema + tLabel) : plan.BeforeFullName;

                plan.AfterCreate   = MergeTri(plan.BeforeCreate,   triCreate);
                plan.AfterThreaded = MergeTri(plan.BeforeThreaded, triThreaded);
                plan.AfterEncrypt  = MergeTri(plan.BeforeEncrypt,  triEncrypt);
                plan.AfterBindable = bindableAvailable ? MergeTri(plan.BeforeBindable, triBindable) : (bool?)null;

                int edits = 0;
                edits += AddPreviewRow(tName, "Driver",        plan.BeforeDriver,           plan.AfterDriver);
                edits += AddPreviewRow(tName, "DriverOptions", plan.BeforeOptions,          plan.AfterOptions);
                edits += AddPreviewRow(tName, "OwnerName",     plan.BeforeOwner,            plan.AfterOwner);
                edits += AddPreviewRow(tName, "FullPathName",  plan.BeforeFullName,         plan.AfterFullName);
                edits += AddPreviewRowBool(tName, "Create",    plan.BeforeCreate,           plan.AfterCreate);
                edits += AddPreviewRowBool(tName, "Threaded",  plan.BeforeThreaded,         plan.AfterThreaded);
                edits += AddPreviewRowBool(tName, "Encrypt",   plan.BeforeEncrypt,          plan.AfterEncrypt);
                if (bindableAvailable)
                    edits += AddPreviewRowBool(tName, "Bindable", plan.BeforeBindable, plan.AfterBindable);

                totalEdits += edits;
                if (edits > 0) currentPlan.Add(plan);
            }
            lvPreview.EndUpdate();

            lblPreviewSummary.Text = totalEdits == 0
                ? "Nothing to change in the selected tables."
                : totalEdits + " edit(s) across " + currentPlan.Count + " table(s).";
            btnApply.Enabled = totalEdits > 0;
        }

        static bool? ReadBool(object table, string name)
        {
            var v = DictModel.GetProp(table, name);
            if (v is bool b) return b;
            if (v == null) return null;
            bool p;
            return bool.TryParse(v.ToString(), out p) ? p : (bool?)null;
        }

        static bool? MergeTri(bool? before, Tri tri)
        {
            switch (tri)
            {
                case Tri.On:  return true;
                case Tri.Off: return false;
                default:      return before;
            }
        }

        int AddPreviewRow(string tableName, string prop, string before, string after)
        {
            before = before ?? "";
            after  = after  ?? "";
            if (string.Equals(before, after, StringComparison.Ordinal)) return 0;
            var lvi = new ListViewItem(new[] { tableName, prop, before, after });
            lvPreview.Items.Add(lvi);
            return 1;
        }

        int AddPreviewRowBool(string tableName, string prop, bool? before, bool? after)
        {
            if (before == after) return 0;
            var b = before.HasValue ? (before.Value ? "ON" : "OFF") : "(unset)";
            var a = after.HasValue  ? (after.Value  ? "ON" : "OFF") : "(unset)";
            var lvi = new ListViewItem(new[] { tableName, prop, b, a });
            lvPreview.Items.Add(lvi);
            return 1;
        }

        void Apply()
        {
            if (currentPlan.Count == 0) return;

            bool currentSelectionIsTarget = IsAnyTargetCurrentlySelected();
            var warning = new StringBuilder();
            warning.AppendLine(currentPlan.Count + " table(s) will be modified.");
            warning.AppendLine("A .tasker-bak-<timestamp> backup of the .DCT is written first.");
            warning.AppendLine();
            if (currentSelectionIsTarget)
            {
                warning.AppendLine("IMPORTANT: the table you currently have selected in the DCT tree");
                warning.AppendLine("is in this migration. Clarion caches the selected table's values in");
                warning.AppendLine("a hidden buffer that overwrites the mutation on save. The tool will");
                warning.AppendLine("try to deselect+reselect automatically to work around this, but if");
                warning.AppendLine("the selected table still reverts, click 'Globals' or another non-");
                warning.AppendLine("target item in the tree BEFORE pressing Ctrl+S.");
                warning.AppendLine();
            }
            warning.AppendLine("Proceed?");

            var confirm = MessageBox.Show(this, warning.ToString(), "SQL Migration",
                MessageBoxButtons.OKCancel,
                currentSelectionIsTarget ? MessageBoxIcon.Warning : MessageBoxIcon.Question);
            if (confirm != DialogResult.OK) return;

            var r = new FieldMutator.Result();
            FieldMutator.Backup(DictModel.GetDictionaryFileName(dict), r);
            if (r.BackupFailed)
            {
                ShowTextModal("Backup failed",
                    "Backup failed — aborting.\r\n\r\n" + string.Join("\r\n", r.Messages.ToArray()));
                return;
            }

            // CRITICAL: close any open TableEditor panes for the tables we're
            // about to mutate. Otherwise the editor's UI widgets (driver
            // combobox, FullPathName textbox, etc.) still hold the pre-mutation
            // values — at save time EntityEditor.AcceptChanges flushes them
            // back over our DDFile changes and the table reverts.
            CloseOpenTableEditors(r);

            // If any target is the currently-selected tree item, deselect it —
            // the selection-change handler is what commits Clarion's hidden
            // "last selected" buffer to the model (this is the transition the
            // user discovered by clicking another table before saving).
            object capturedSelection = null;
            object capturedDCTContent = null;
            DeselectCurrentSelectionIfTarget(r, out capturedSelection, out capturedDCTContent);

            // Belt-and-braces: flush any pending UI state back to the model
            // BEFORE we mutate, so user-typed but uncommitted widget values
            // don't get lost. This also gives us a consistent baseline.
            FlushLiveEditorsToModel(r);

            int totalOk = 0, totalFail = 0;
            foreach (var p in currentPlan)
            {
                int okLocal = 0, failLocal = 0;
                ApplyDriver(p.Table, p.BeforeDriver,   p.AfterDriver,   r, p.TableLabel, ref okLocal, ref failLocal);
                ApplyString(p.Table, "DriverOptions",  p.BeforeOptions,  p.AfterOptions,  r, p.TableLabel, ref okLocal, ref failLocal);
                ApplyString(p.Table, "OwnerName",      p.BeforeOwner,    p.AfterOwner,    r, p.TableLabel, ref okLocal, ref failLocal);
                ApplyString(p.Table, "FullPathName",   p.BeforeFullName, p.AfterFullName, r, p.TableLabel, ref okLocal, ref failLocal);
                ApplyBool  (p.Table, "Create",         p.BeforeCreate,   p.AfterCreate,   r, p.TableLabel, ref okLocal, ref failLocal);
                ApplyBool  (p.Table, "Threaded",       p.BeforeThreaded, p.AfterThreaded, r, p.TableLabel, ref okLocal, ref failLocal);
                ApplyBool  (p.Table, "Encrypt",        p.BeforeEncrypt,  p.AfterEncrypt,  r, p.TableLabel, ref okLocal, ref failLocal);
                if (bindableAvailable)
                    ApplyBool(p.Table, "Bindable",     p.BeforeBindable, p.AfterBindable, r, p.TableLabel, ref okLocal, ref failLocal);
                totalOk   += okLocal;
                totalFail += failLocal;
                if (okLocal > 0) r.Changed++;
                if (okLocal == 0 && failLocal > 0) r.Failed++;
            }
            // After mutation: force every live editor bound to a target table
            // to refresh its widgets from the (now-updated) model. On save,
            // AcceptChanges will then flush the NEW values back instead of the
            // stale pre-mutation snapshot the editor loaded when the user
            // first clicked the table.
            RebindLiveEditorsToModel(r);

            // Restore the selection (if we cleared it) — this re-fires the
            // "entering the table" path which loads the UI from the now-
            // mutated model. Result: when the user hits Ctrl+S, save writes
            // the NEW driver instead of the stale buffer.
            RestoreSelection(r, capturedSelection, capturedDCTContent);

            FieldMutator.ForceMarkDirty(dict, DictModel.GetActiveDictionaryView(), r);

            var summary = new StringBuilder();
            summary.AppendLine("Tables touched:  " + r.Changed);
            summary.AppendLine("Tables failed:   " + r.Failed);
            summary.AppendLine("Edits applied:   " + totalOk);
            summary.AppendLine("Edits failed:    " + totalFail);
            if (!string.IsNullOrEmpty(r.BackupPath)) summary.AppendLine("Backup:          " + r.BackupPath);
            summary.AppendLine();
            summary.AppendLine("The dictionary is now DIRTY. Press Ctrl+S in Clarion to save.");

            ShowTextModal(
                totalFail > 0 ? "SQL Migration - finished with errors" : "SQL Migration - done",
                summary.ToString() + "\r\n--- details ---\r\n" + string.Join("\r\n", r.Messages.ToArray()));

            // Refresh list + preview so counts reflect the new state.
            RebuildTableList((txtFilter.Text ?? "").Trim());
            RefreshPreview();
        }

        static void ApplyString(object table, string prop, string before, string after,
                                FieldMutator.Result r, string tableLabel,
                                ref int okLocal, ref int failLocal)
        {
            if (string.Equals(before ?? "", after ?? "", StringComparison.Ordinal)) return;
            var tag = tableLabel + "." + prop;
            if (FieldMutator.SetStringProp(table, prop, after ?? "", r, tag)) okLocal++;
            else failLocal++;
        }

        // Driver change takes its own path because SoftVelocity's DDFile usually
        // exposes FileDriverName as a read-only wrapper over a DDDriver reference.
        // Try in order:
        //   1. FileDriverName, DriverName, Driver — as string setters
        //   2. Driver, FileDriver — as ref setters, resolved against the dict's
        //      Drivers collection (Drivers / FileDrivers / DriverCollection)
        // On failure, dump the shape of all driver-related properties to r.Messages
        // so we can see what's actually writable on this Clarion build.
        void ApplyDriver(object table, string before, string after,
                         FieldMutator.Result r, string tableLabel,
                         ref int okLocal, ref int failLocal)
        {
            if (string.Equals(before ?? "", after ?? "", StringComparison.Ordinal)) return;
            if (TrySetDriver(table, after ?? "", r, tableLabel)) okLocal++;
            else failLocal++;
        }

        bool TrySetDriver(object table, string newDriver, FieldMutator.Result r, string tableTag)
        {
            if (table == null || string.IsNullOrEmpty(newDriver)) return false;

            // Path 1: one of the string-typed writable properties.
            foreach (var propName in new[] { "FileDriverName", "DriverName", "Driver" })
            {
                var p = table.GetType().GetProperty(propName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p == null || !p.CanWrite || p.PropertyType != typeof(string)) continue;
                if (FieldMutator.SetStringProp(table, propName, newDriver, r, tableTag + ".driver@" + propName))
                    return true;
            }

            // Path 2: get a valid FileDriver ref and assign it to the table.
            // Two sources, in order:
            //   a) borrow from a sibling table already using the target driver
            //   b) call SoftVelocity.DataDictionary.FileDriver.Instance(name, true)
            //      — the static factory Clarion itself uses to resolve driver
            //      names to FileDriver objects. FileDriver's public ctors are
            //      all private, so this is the only supported way to make one.
            foreach (var propName in new[] { "FileDriver", "Driver" })
            {
                var p = table.GetType().GetProperty(propName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p == null || !p.CanWrite) continue;
                if (p.PropertyType == typeof(string)) continue;

                var driverObj = BorrowFileDriverFromSiblingTable(newDriver);
                string source = "borrowed ref";
                if (driverObj == null)
                {
                    driverObj = ResolveFileDriverViaInstance(p.PropertyType, newDriver, r, tableTag);
                    source = "FileDriver.Instance factory";
                }

                if (driverObj != null && p.PropertyType.IsAssignableFrom(driverObj.GetType()))
                {
                    try
                    {
                        p.SetValue(table, driverObj, null);
                        r.Messages.Add(tableTag + ".driver via " + propName + " (" + source + ") -> " + newDriver);
                        KickTableDirty(table, r, tableTag);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                        r.Messages.Add(tableTag + ".driver: " + propName + " ref setter threw " + inner.GetType().Name + " " + inner.Message);
                    }
                }
            }

            // Path 3: write the driverString backing field directly + null out
            // the driver ref so Clarion re-resolves it. This is the "set it by
            // token" path: Clarion serializes driverString to the .DCT and
            // reconstructs the FileDriver ref on next load.
            if (TrySetDriverByStringField(table, newDriver, r, tableTag))
            {
                KickTableDirty(table, r, tableTag);
                return true;
            }

            DumpDriverPropertyShape(table, r, tableTag);
            return false;
        }

        // Invoke SoftVelocity's own static factory to resolve a driver name to
        // a usable FileDriver object. The method lives on the FileDriver type
        // itself (SoftVelocity.DataDictionary.FileDriver::Instance(string, bool)).
        // Both parameterless and (IASLFileDriver) ctors on FileDriver are
        // private, so this is the only supported construction path.
        static object ResolveFileDriverViaInstance(Type fileDriverType, string driverName,
                                                   FieldMutator.Result r, string tableTag)
        {
            if (fileDriverType == null || string.IsNullOrEmpty(driverName)) return null;
            var m = fileDriverType.GetMethod("Instance",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(string), typeof(bool) }, null);
            if (m == null)
            {
                r.Messages.Add(tableTag + ".driver: FileDriver.Instance(string,bool) not found on " + fileDriverType.FullName);
                return null;
            }
            foreach (var createFlag in new[] { true, false })
            {
                try
                {
                    var result = m.Invoke(null, new object[] { driverName, createFlag });
                    if (result != null) return result;
                }
                catch (Exception ex)
                {
                    var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                    r.Messages.Add(tableTag + ".driver: FileDriver.Instance(\"" + driverName + "\", " + createFlag + ") threw " + inner.GetType().Name + " " + inner.Message);
                }
            }
            return null;
        }

        // Look for a table in the dict that's already on the target driver and
        // grab its FileDriver reference so we can reuse it. Returns null if no
        // such sibling exists.
        object BorrowFileDriverFromSiblingTable(string newDriver)
        {
            foreach (var t in tables)
            {
                var drv = DictModel.AsString(DictModel.GetProp(t, "FileDriverName"));
                if (!string.Equals(drv, newDriver, StringComparison.OrdinalIgnoreCase)) continue;
                var fd = DictModel.GetProp(t, "FileDriver");
                if (fd != null) return fd;
            }
            return null;
        }

        // Write the driverString backing field on the table to the new driver
        // name, and null out the driver ref so Clarion re-resolves it. Returns
        // true if at least one of those writes landed. This is the fallback
        // when no sibling FileDriver ref can be borrowed.
        bool TrySetDriverByStringField(object table, string newDriver, FieldMutator.Result r, string tableTag)
        {
            bool stringOk = false, refCleared = false;
            var t = table.GetType();
            while (t != null && t != typeof(object))
            {
                foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!stringOk && f.FieldType == typeof(string)
                        && string.Equals(f.Name, "driverString", StringComparison.OrdinalIgnoreCase))
                    {
                        try { f.SetValue(table, newDriver); stringOk = true; }
                        catch (Exception ex)
                        {
                            var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                            r.Messages.Add(tableTag + ".driver: driverString write threw " + inner.GetType().Name + " " + inner.Message);
                        }
                    }
                    if (!refCleared
                        && string.Equals(f.Name, "driver", StringComparison.OrdinalIgnoreCase)
                        && !f.FieldType.IsValueType)
                    {
                        try { f.SetValue(table, null); refCleared = true; }
                        catch { /* non-fatal — stringOk alone is enough */ }
                    }
                }
                t = t.BaseType;
            }
            if (stringOk)
            {
                r.Messages.Add(tableTag + ".driver via driverString backing field -> " + newDriver
                              + (refCleared ? " (driver ref cleared)" : " (driver ref not cleared)"));
            }
            return stringOk;
        }

        // After a direct ref/field write the standard notify pipeline hasn't run,
        // so poke OwnerName back into itself to reuse SetStringProp's dirty path.
        void KickTableDirty(object table, FieldMutator.Result r, string tableTag)
        {
            var owner = DictModel.AsString(DictModel.GetProp(table, "OwnerName")) ?? "";
            FieldMutator.SetStringProp(table, "OwnerName", owner, r, tableTag + ".driver.kick");
        }

        // Close every UI surface that holds stale buffered values for our target
        // tables, BEFORE we mutate the DDFile. Without this, the editor's UI
        // widgets (driver combobox, FullPathName textbox, etc.) loaded at open
        // time flush back over our changes when the user saves.
        //
        // Three surfaces to handle:
        //   1. DCTContent.CloseTable / CloseAllTables — table tab panes
        //   2. Application.OpenForms containing a TableEditor / TableForm for a
        //      target table — these are the MODAL windows the user gets when
        //      they click a table in the tree. CloseTable doesn't touch these.
        //   3. DCTExplorer.RefreshIfSelected — hints the tree to refresh the
        //      node if the user has it selected.
        void CloseOpenTableEditors(FieldMutator.Result r)
        {
            var view = DictModel.GetActiveDictionaryView();
            var control = view == null ? null : DictModel.GetProp(view, "Control");
            if (control == null) { r.Messages.Add("editor close: no Control on view"); }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var tableSet = new HashSet<object>(currentPlan.Select(p => p.Table).Where(x => x != null));

            // 1. CloseTable per target, or CloseAllTables as a fallback.
            if (control != null)
            {
                var mCloseOne = control.GetType().GetMethod("CloseTable", flags);
                if (mCloseOne != null)
                {
                    int closed = 0, failed = 0;
                    // Try both bool values — we don't know whether the second arg
                    // is "save first" (want false) or "force close" (want true).
                    // Running true-then-false is defensive and cheap.
                    foreach (var p in currentPlan)
                    {
                        bool ok = false;
                        foreach (var flag in new[] { true, false })
                        {
                            try { mCloseOne.Invoke(control, new object[] { p.Table, flag }); ok = true; break; }
                            catch { /* try next flag */ }
                        }
                        if (ok) closed++; else failed++;
                    }
                    r.Messages.Add("DCTContent.CloseTable() called for " + closed + " table(s) (" + failed + " failed).");
                }
                else
                {
                    var mCloseAll = control.GetType().GetMethod("CloseAllTables", flags, null, Type.EmptyTypes, null);
                    if (mCloseAll != null)
                    {
                        try { mCloseAll.Invoke(control, null); r.Messages.Add("DCTContent.CloseAllTables() invoked."); }
                        catch (Exception ex)
                        {
                            var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                            r.Messages.Add("CloseAllTables threw " + inner.GetType().Name + " " + inner.Message);
                        }
                    }
                }
            }

            // 2. Walk Application.OpenForms — snapshot first since closing mutates it.
            var snapshot = new List<Form>();
            foreach (Form f in Application.OpenForms) snapshot.Add(f);

            int formsClosed = 0;
            foreach (var f in snapshot)
            {
                if (f == null || f.IsDisposed || f == this) continue;
                var tfn = f.GetType().FullName ?? "";
                // Match TableForm (modal), EntityForm, or anything with TableEditor embedded.
                bool candidate = tfn.Contains("TableForm") || tfn.EndsWith("EntityForm")
                                 || FindEmbeddedByTypeName(f, "TableEditor") != null
                                 || FindEmbeddedByTypeName(f, "TableForm") != null;
                if (!candidate) continue;

                // Read .File directly or from an embedded TableEditor.
                var file = DictModel.GetProp(f, "File");
                if (file == null)
                {
                    var embedded = FindEmbeddedByTypeName(f, "TableEditor");
                    if (embedded != null) file = DictModel.GetProp(embedded, "File");
                }
                if (file == null) continue;
                if (!tableSet.Contains(file)) continue;

                try
                {
                    // DialogResult.Cancel = abandon; this avoids the "save changes?" prompt.
                    f.DialogResult = DialogResult.Cancel;
                    f.Close();
                    formsClosed++;
                }
                catch (Exception ex)
                {
                    r.Messages.Add("close form " + tfn + " threw " + ex.GetType().Name + " " + ex.Message);
                }
            }
            r.Messages.Add("Application.OpenForms walk: closed " + formsClosed + " form(s). Total forms seen: " + snapshot.Count + ".");

            // 3. DCTExplorer.RefreshIfSelected — nudge the tree to re-read each
            // target's display if the user has it selected in the sidebar.
            if (control != null)
            {
                var explorer = DictModel.GetProp(control, "DictionaryExplorer");
                if (explorer != null)
                {
                    var mRefresh = explorer.GetType().GetMethod("RefreshIfSelected", flags);
                    if (mRefresh != null)
                    {
                        int refreshed = 0;
                        foreach (var p in currentPlan)
                        {
                            try { mRefresh.Invoke(explorer, new object[] { p.Table }); refreshed++; }
                            catch { /* non-fatal */ }
                        }
                        r.Messages.Add("DCTExplorer.RefreshIfSelected ran " + refreshed + "/" + currentPlan.Count + " time(s).");
                    }
                }
            }
        }

        static Control FindEmbeddedByTypeName(Control root, string typeNameSuffix)
        {
            if (root == null) return null;
            var t = root.GetType().FullName ?? "";
            if (t.EndsWith(typeNameSuffix)) return root;
            foreach (Control c in root.Controls)
            {
                var hit = FindEmbeddedByTypeName(c, typeNameSuffix);
                if (hit != null) return hit;
            }
            return null;
        }

        // Check at preview/confirm time whether the user has a target table
        // currently selected in the DCT tree. Used to decide whether to show
        // the "selected item will revert" warning in the confirmation dialog.
        bool IsAnyTargetCurrentlySelected()
        {
            var view = DictModel.GetActiveDictionaryView();
            if (view == null) return false;
            var control = DictModel.GetProp(view, "Control");
            if (control == null) return false;
            var sel = DictModel.GetProp(control, "SelectedItem");
            if (sel == null) return false;
            var file = ResolveDDFileFromDataDictionaryItem(sel);
            if (file == null) return false;
            return currentPlan.Any(p => ReferenceEquals(p.Table, file));
        }

        // Clarion's DCTContent.SelectedItem (writable on the control, read-only
        // on the view wrapper) drives a hidden "last selected item" buffer.
        // When the user clicks a different tree node, the "leaving" handler
        // commits that buffer back to the model. If we want our mutation to
        // stick for the currently-selected table, we need to fire the same
        // transition: clear the selection now, mutate, then restore it.
        void DeselectCurrentSelectionIfTarget(FieldMutator.Result r,
            out object capturedSelection, out object capturedDCTContent)
        {
            capturedSelection = null;
            capturedDCTContent = null;

            var view = DictModel.GetActiveDictionaryView();
            if (view == null) { r.Messages.Add("selection toggle: no active view"); return; }
            var control = DictModel.GetProp(view, "Control");
            if (control == null) { r.Messages.Add("selection toggle: no Control on view"); return; }

            var selProp = control.GetType().GetProperty("SelectedItem",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (selProp == null)
            {
                r.Messages.Add("selection toggle: SelectedItem property not found on " + control.GetType().FullName);
                return;
            }
            if (!selProp.CanWrite)
            {
                r.Messages.Add("selection toggle: SelectedItem is read-only on " + control.GetType().FullName);
                return;
            }

            object sel;
            try { sel = selProp.GetValue(control, null); }
            catch (Exception ex) { r.Messages.Add("selection toggle: read SelectedItem threw " + ex.GetType().Name); return; }
            if (sel == null) { r.Messages.Add("selection toggle: nothing currently selected"); return; }

            var selFile = ResolveDDFileFromDataDictionaryItem(sel);
            bool isTarget = selFile != null && currentPlan.Any(p => ReferenceEquals(p.Table, selFile));
            r.Messages.Add("selection toggle: current=" + sel.GetType().Name
                          + " file=" + (selFile == null ? "null" : selFile.GetType().Name)
                          + " isTarget=" + isTarget);
            if (!isTarget) return;

            capturedSelection = sel;
            capturedDCTContent = control;
            try
            {
                selProp.SetValue(control, null, null);
                r.Messages.Add("selection toggle: cleared (null).");
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                r.Messages.Add("selection toggle: set null threw " + inner.GetType().Name + " " + inner.Message);
                capturedSelection = null;
                capturedDCTContent = null;
            }
        }

        void RestoreSelection(FieldMutator.Result r, object capturedSelection, object capturedDCTContent)
        {
            if (capturedSelection == null || capturedDCTContent == null) return;
            var selProp = capturedDCTContent.GetType().GetProperty("SelectedItem",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (selProp == null || !selProp.CanWrite) return;
            try
            {
                selProp.SetValue(capturedDCTContent, capturedSelection, null);
                r.Messages.Add("selection toggle: restored to " + capturedSelection.GetType().Name + ".");
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                r.Messages.Add("selection toggle: restore threw " + inner.GetType().Name + " " + inner.Message);
            }
        }

        // A DataDictionaryItem is a tree-display wrapper. For a table node it
        // holds a reference to the DDFile on some property whose name varies
        // by Clarion build — probe the usual candidates.
        static object ResolveDDFileFromDataDictionaryItem(object ddItem)
        {
            if (ddItem == null) return null;
            // If the item IS a DDFile already (some builds use the DDFile
            // directly as the selected item), pass it through.
            var typeName = ddItem.GetType().FullName ?? "";
            if (typeName.EndsWith(".DDFile") || typeName.EndsWith(".DDBaseFile")) return ddItem;
            foreach (var propName in new[] { "File", "DDFile", "BaseFile", "Item", "Entity", "Table", "WrappedItem", "Value" })
            {
                var v = DictModel.GetProp(ddItem, propName);
                if (v == null) continue;
                var tn = v.GetType().FullName ?? "";
                if (tn.EndsWith(".DDFile") || tn.EndsWith(".DDBaseFile") || tn.Contains("DDFile")) return v;
            }
            return null;
        }

        // Walk every accessible control — DCTContent's Control tree plus every
        // Application.OpenForms — and collect any TableEditor / BaseFileEditor
        // / EntityEditor whose File is one of our target DDFile refs.
        List<Control> FindLiveTargetEditors()
        {
            var list = new List<Control>();
            var tableSet = new HashSet<object>(currentPlan.Select(p => p.Table).Where(x => x != null));

            void Walk(Control c)
            {
                if (c == null) return;
                var tn = c.GetType().FullName ?? "";
                if (tn.EndsWith("TableEditor") || tn.EndsWith("BaseFileEditor") || tn.EndsWith("FileEditor"))
                {
                    var file = DictModel.GetProp(c, "File");
                    if (file != null && tableSet.Contains(file)) list.Add(c);
                }
                foreach (Control ch in c.Controls) Walk(ch);
            }

            var view = DictModel.GetActiveDictionaryView();
            var rootCtrl = view == null ? null : DictModel.GetProp(view, "Control") as Control;
            if (rootCtrl != null) Walk(rootCtrl);
            foreach (Form f in Application.OpenForms)
            {
                if (f == null || f.IsDisposed || f == this) continue;
                Walk(f);
            }
            return list;
        }

        // Pre-mutation: flush any pending widget values back into the model so
        // uncommitted edits don't get clobbered by our mutation.
        // EntityEditor has `internal bool AcceptChanges()` and
        // `protected void UpdateRecord()` — we try both via reflection.
        void FlushLiveEditorsToModel(FieldMutator.Result r)
        {
            var editors = FindLiveTargetEditors();
            r.Messages.Add("pre-flush: found " + editors.Count + " live editor(s) bound to target tables.");
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            int flushed = 0;
            foreach (var e in editors)
            {
                bool any = false;
                foreach (var name in new[] { "AcceptChanges", "UpdateRecord" })
                {
                    var m = e.GetType().GetMethod(name, flags, null, Type.EmptyTypes, null);
                    if (m == null) continue;
                    try { m.Invoke(e, null); any = true; break; }
                    catch { /* try next */ }
                }
                if (any) flushed++;
            }
            if (editors.Count > 0) r.Messages.Add("pre-flush: flushed " + flushed + "/" + editors.Count + " via AcceptChanges/UpdateRecord.");
        }

        // Post-mutation: rebind every live editor to its DDItem so the widgets
        // reload from the (now-updated) model. EntityEditor.Init(DataDictionaryItem,
        // bool) is the standard entry point; if that's not found, fall back to
        // writing the File property to itself (some editor hierarchies re-bind
        // on property set).
        void RebindLiveEditorsToModel(FieldMutator.Result r)
        {
            var editors = FindLiveTargetEditors();
            r.Messages.Add("post-rebind: found " + editors.Count + " live editor(s) bound to target tables.");
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            int rebound = 0;
            foreach (var e in editors)
            {
                var ddItem = DictModel.GetProp(e, "DDItem");
                bool ok = false;

                if (ddItem != null)
                {
                    var initMethods = e.GetType().GetMethods(flags)
                        .Where(m => m.Name == "Init")
                        .Where(m => m.GetParameters().Length == 2
                                 && m.GetParameters()[1].ParameterType == typeof(bool))
                        .ToList();
                    foreach (var init in initMethods)
                    {
                        foreach (var flag in new[] { false, true })
                        {
                            try { init.Invoke(e, new object[] { ddItem, flag }); ok = true; break; }
                            catch { /* try next */ }
                        }
                        if (ok) break;
                    }
                }

                // Fallback: clear + set File prop to itself.
                if (!ok)
                {
                    var fileProp = e.GetType().GetProperty("File", flags);
                    if (fileProp != null && fileProp.CanWrite)
                    {
                        var file = fileProp.GetValue(e, null);
                        try { fileProp.SetValue(e, file, null); ok = true; } catch { }
                    }
                }

                // Clear dirty if possible — otherwise save may think we have
                // pending changes for the editor that are actually committed.
                var mClear = e.GetType().GetMethod("ClearDirty", flags, null, Type.EmptyTypes, null);
                if (mClear != null) { try { mClear.Invoke(e, null); } catch { } }

                if (ok) rebound++;
            }
            if (editors.Count > 0) r.Messages.Add("post-rebind: rebound " + rebound + "/" + editors.Count + ".");
        }

        void DumpDriverPropertyShape(object table, FieldMutator.Result r, string tag)
        {
            var sb = new StringBuilder(tag + ".driver: no writable path found. Table props matching 'driver':");
            var t = table.GetType();
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (p.Name.IndexOf("driver", StringComparison.OrdinalIgnoreCase) < 0) continue;
                sb.Append(" [").Append(p.Name).Append(":").Append(p.PropertyType.Name);
                if (p.CanRead)  sb.Append(" R");
                if (p.CanWrite) sb.Append("W");
                sb.Append("]");
            }
            // Walk base types for private fields with 'driver' in the name.
            sb.Append("  Backing fields:");
            var walker = t;
            while (walker != null && walker != typeof(object))
            {
                foreach (var f in walker.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (f.Name.IndexOf("driver", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    sb.Append(" ").Append(f.Name).Append(":").Append(f.FieldType.Name);
                }
                walker = walker.BaseType;
            }
            // Dict-level driver collections.
            if (dict != null)
            {
                sb.Append("  dict props matching 'driver':");
                foreach (var p in dict.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (p.Name.IndexOf("driver", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    sb.Append(" ").Append(p.Name).Append(":").Append(p.PropertyType.Name);
                }
            }
            r.Messages.Add(sb.ToString());
        }

        static void ApplyBool(object table, string prop, bool? before, bool? after,
                              FieldMutator.Result r, string tableLabel,
                              ref int okLocal, ref int failLocal)
        {
            if (before == after) return;
            if (!after.HasValue) return;   // "Leave alone" never reaches here
            var tag = tableLabel + "." + prop;
            if (FieldMutator.SetBoolProp(table, prop, after.Value, r, tag)) okLocal++;
            else failLocal++;
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
