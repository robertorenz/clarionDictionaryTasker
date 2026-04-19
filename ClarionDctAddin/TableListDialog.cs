using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    internal class TableListDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);

        readonly object dict;
        readonly IList<object> tables;
        ListView lv;
        TextBox  txtFilter;
        ComboBox cbDriver;
        Label    lblCount;
        readonly List<ListViewItem> allItems = new List<ListViewItem>();
        TableSorter sorter;

        public TableListDialog(object dict)
        {
            this.dict = dict;
            this.tables = DictModel.GetTables(dict);
            BuildUi();
            PopulateList();
        }

        void BuildUi()
        {
            var name = DictModel.GetDictionaryName(dict);
            Text = "Dictionary - " + name;
            Width = 1000;
            Height = 660;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;
            MinimumSize = new Size(780, 440);
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
                Text = string.Format("{0}     {1}", name, DictModel.GetDictionaryFileName(dict))
            };

            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F)
            };

            var tabTables = new TabPage(string.Format("Tables ({0})", tables.Count));
            tabTables.BackColor = BgColor;
            tabTables.UseVisualStyleBackColor = true;
            BuildTablesTab(tabTables);

            var tabTree = new TabPage("Tree");
            tabTree.BackColor = Color.White;
            tabTree.UseVisualStyleBackColor = true;
            var tree = new DictTreeViewPanel(dict) { Dock = DockStyle.Fill };
            tabTree.Controls.Add(tree);

            var tabRelations = new TabPage("Relations");
            tabRelations.BackColor = Color.White;
            tabRelations.UseVisualStyleBackColor = true;
            var diagram = new RelationsDiagramPanel(dict) { Dock = DockStyle.Fill };
            tabRelations.Controls.Add(diagram);

            tabs.TabPages.Add(tabTables);
            tabs.TabPages.Add(tabTree);
            tabs.TabPages.Add(tabRelations);

            var formButtons = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = PanelColor, Padding = new Padding(12, 8, 12, 8) };
            var btnClose = new Button
            {
                Text = "Close",
                Width = 120, Height = 32,
                FlatStyle = FlatStyle.System,
                Dock = DockStyle.Right
            };
            btnClose.Click += delegate { Close(); };
            formButtons.Controls.Add(btnClose);

            Controls.Add(tabs);
            Controls.Add(formButtons);
            Controls.Add(header);

            CancelButton = btnClose;
        }

        void BuildTablesTab(TabPage page)
        {
            lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = true,
                HideSelection = false,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.None
            };
            lv.Columns.Add("Name", 200);
            lv.Columns.Add("Prefix", 80);
            lv.Columns.Add("Driver", 100);
            lv.Columns.Add("Fields", 60, HorizontalAlignment.Right);
            lv.Columns.Add("Keys", 55, HorizontalAlignment.Right);
            lv.Columns.Add("Description", 440);
            lv.DoubleClick += delegate { ShowFieldsForSelection(); };
            var savedCol = Settings.TableListSortColumn;
            if (savedCol < 0) savedCol = 0;
            sorter = new TableSorter { Column = savedCol, Ascending = Settings.TableListSortAsc };
            lv.ListViewItemSorter = sorter;
            lv.ColumnClick += OnColumnClick;

            // Right-click menu — actions scoped to the selected table.
            var ctx = new ContextMenuStrip();
            ctx.Items.Add("Show fields...",         null, delegate { ShowFieldsForSelection(); });
            ctx.Items.Add("View data...",           null, delegate { ViewDataForSelected(); });
            ctx.Items.Add("Export to JSON...",      null, delegate { ExportSelected(); });
            ctx.Items.Add("Export SQL DDL...",      null, delegate { ExportSqlForSelected(); });
            ctx.Items.Add("Copy table name",        null, delegate { CopySelectedName(); });
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add("Lint this table...",     null, delegate { LintSelectedTable(); });
            ctx.Items.Add("Fix fields...",          null, delegate { FixFieldsForSelected(); });
            ctx.Items.Add("Fix keys...",            null, delegate { FixKeysForSelected(); });
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add("More dictionary tools...", null, delegate { OpenToolsDialog(); });
            lv.ContextMenuStrip = ctx;

            // Filter bar above the list
            var filterBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = BgColor, Padding = new Padding(8, 8, 8, 4) };
            var lblFilter = new Label { Text = "Filter:", Left = 0,   Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            txtFilter     = new TextBox { Left = 50,  Top = 4, Width = 300, Font = new Font("Segoe UI", 9.5F) };
            txtFilter.TextChanged += delegate { ApplyFilter(); };
            var lblDriver = new Label { Text = "Driver:", Left = 366, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            cbDriver      = new ComboBox { Left = 416, Top = 4, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9.5F) };
            cbDriver.SelectedIndexChanged += delegate { ApplyFilter(); };
            lblCount      = new Label { Left = 592, Top = 8, AutoSize = true, Font = new Font("Segoe UI", 9F), ForeColor = Color.FromArgb(100, 115, 135) };
            filterBar.Controls.Add(lblFilter);
            filterBar.Controls.Add(txtFilter);
            filterBar.Controls.Add(lblDriver);
            filterBar.Controls.Add(cbDriver);
            filterBar.Controls.Add(lblCount);

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0), BackColor = BgColor };
            host.Controls.Add(lv);
            host.Controls.Add(filterBar);

            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = BgColor, Padding = new Padding(8, 8, 8, 8) };
            var btnFields = MakeButton("Show fields...",                 b => ShowFieldsForSelection());
            var btnSel    = MakeButton("Export selected to JSON...",     b => ExportSelected());
            var btnAll    = MakeButton("Export all tables to JSON...",   b => ExportAll());
            btnAll.Dock    = DockStyle.Right;
            btnSel.Dock    = DockStyle.Right;
            btnFields.Dock = DockStyle.Right;
            btnPanel.Controls.Add(btnAll);
            btnPanel.Controls.Add(btnSel);
            btnPanel.Controls.Add(btnFields);

            page.Controls.Add(host);
            page.Controls.Add(btnPanel);
        }

        Button MakeButton(string text, Action<Button> onClick)
        {
            var b = new Button
            {
                Text = text,
                Width = 200,
                Height = 32,
                FlatStyle = FlatStyle.System,
                Margin = new Padding(6, 0, 0, 0)
            };
            b.Click += delegate { onClick(b); };
            return b;
        }

        void PopulateList()
        {
            allItems.Clear();
            var distinctDrivers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tables)
            {
                var name   = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "";
                var prefix = DictModel.AsString(DictModel.GetProp(t, "Prefix")) ?? "";
                var drv    = DictModel.AsString(DictModel.GetProp(t, "FileDriverName")) ?? "";
                var desc   = DictModel.AsString(DictModel.GetProp(t, "Description")) ?? "";
                var fCount = DictModel.CountEnumerable(t, "Fields");
                var kCount = DictModel.CountEnumerable(t, "Keys");
                var item = new ListViewItem(new[]
                {
                    name, prefix, drv, fCount.ToString(), kCount.ToString(), desc
                });
                item.Tag = t;
                allItems.Add(item);
                if (!string.IsNullOrEmpty(drv)) distinctDrivers.Add(drv);
            }

            cbDriver.Items.Clear();
            cbDriver.Items.Add("(all)");
            foreach (var d in distinctDrivers) cbDriver.Items.Add(d);
            cbDriver.SelectedIndex = 0;

            ApplyFilter();
        }

        void ApplyFilter()
        {
            var needle = (txtFilter.Text ?? "").Trim().ToLowerInvariant();
            var driver = cbDriver.SelectedIndex > 0 ? (cbDriver.SelectedItem as string) : null;

            lv.BeginUpdate();
            try
            {
                lv.Items.Clear();
                int shown = 0;
                foreach (var item in allItems)
                {
                    if (driver != null
                        && !string.Equals(item.SubItems[2].Text, driver, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (needle.Length > 0 && !RowMatches(item, needle))
                        continue;
                    lv.Items.Add(item);
                    shown++;
                }
                lv.Sort();
                if (lv.Items.Count > 0) lv.Items[0].Selected = true;
                lblCount.Text = shown + " of " + allItems.Count + " tables";
                PaintSortHeader();
            }
            finally { lv.EndUpdate(); }
        }

        static bool RowMatches(ListViewItem item, string needleLower)
        {
            // Substring hit in any column is enough.
            for (int i = 0; i < item.SubItems.Count; i++)
            {
                var txt = item.SubItems[i].Text;
                if (!string.IsNullOrEmpty(txt)
                    && txt.ToLowerInvariant().IndexOf(needleLower, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (sorter.Column == e.Column) sorter.Ascending = !sorter.Ascending;
            else { sorter.Column = e.Column; sorter.Ascending = true; }
            Settings.TableListSortColumn = sorter.Column;
            Settings.TableListSortAsc    = sorter.Ascending;
            lv.Sort();
            PaintSortHeader();
        }

        void PaintSortHeader()
        {
            // Paint a small arrow on the sorted column header by rewriting its text.
            for (int i = 0; i < lv.Columns.Count; i++)
            {
                var original = BaseHeaderText(lv.Columns[i].Text);
                if (i == sorter.Column)
                    lv.Columns[i].Text = original + (sorter.Ascending ? "  ^" : "  v");
                else
                    lv.Columns[i].Text = original;
            }
        }

        static string BaseHeaderText(string t)
        {
            if (string.IsNullOrEmpty(t)) return t;
            var idx = t.IndexOf("  ^", StringComparison.Ordinal);
            if (idx < 0) idx = t.IndexOf("  v", StringComparison.Ordinal);
            return idx >= 0 ? t.Substring(0, idx) : t;
        }

        // ListView sorter that understands numeric columns (Fields / Keys).
        sealed class TableSorter : IComparer
        {
            public int  Column;
            public bool Ascending = true;
            public int Compare(object x, object y)
            {
                var a = x as ListViewItem;
                var b = y as ListViewItem;
                if (a == null || b == null) return 0;
                var sa = Column < a.SubItems.Count ? a.SubItems[Column].Text : "";
                var sb = Column < b.SubItems.Count ? b.SubItems[Column].Text : "";
                int cmp;
                if (Column == 3 || Column == 4) // Fields, Keys — integer columns
                {
                    int ia, ib;
                    int.TryParse(sa, out ia);
                    int.TryParse(sb, out ib);
                    cmp = ia.CompareTo(ib);
                }
                else
                {
                    cmp = string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
                }
                return Ascending ? cmp : -cmp;
            }
        }

        void CopySelectedName()
        {
            if (lv.SelectedItems.Count == 0) return;
            var name = lv.SelectedItems[0].Text;
            try
            {
                Clipboard.SetText(name);
            }
            catch { /* clipboard can fail under remote sessions */ }
        }

        void ExportSqlForSelected()
        {
            if (lv.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select a table first.", "Dict Tools",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var table = lv.SelectedItems[0].Tag;
            using (var dlg = new SqlDdlDialog(dict, table)) dlg.ShowDialog(this);
        }

        void FixFieldsForSelected()
        {
            if (lv.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select a table first.", "Fix fields",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var table = lv.SelectedItems[0].Tag;
            using (var dlg = new LintFixItDialog(dict, table)) dlg.ShowDialog(this);
        }

        void ViewDataForSelected()
        {
            if (lv.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select a table first.", "View data",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var table = lv.SelectedItems[0].Tag;
            using (var dlg = new ViewDataDialog(dict, table)) dlg.ShowDialog(this);
        }

        void FixKeysForSelected()
        {
            if (lv.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select a table first.", "Fix keys",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var table = lv.SelectedItems[0].Tag;
            using (var dlg = new LintFixKeysDialog(dict, table)) dlg.ShowDialog(this);
        }

        void LintSelectedTable()
        {
            if (lv.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select a table first.", "Dict Tools",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var table = lv.SelectedItems[0].Tag;
            using (var dlg = new LintReportDialog(dict, table)) dlg.ShowDialog(this);
        }

        void OpenToolsDialog()
        {
            using (var dlg = new ToolsDialog(dict)) dlg.ShowDialog(this);
        }

        void ShowFieldsForSelection()
        {
            if (lv.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select a table first.", "DCT Addin",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var table = lv.SelectedItems[0].Tag;
            using (var dlg = new FieldListDialog(table)) dlg.ShowDialog(this);
        }

        void ExportSelected()
        {
            if (lv.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select one or more tables first.", "DCT Addin",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SafeExport(delegate
            {
                if (lv.SelectedItems.Count == 1)
                {
                    var t = lv.SelectedItems[0].Tag;
                    var json = JsonExporter.TableJson(t);
                    var tableName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "table";
                    ShowJson("JSON export - table " + tableName, tableName + ".json", json);
                }
                else
                {
                    var selected = lv.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).ToList();
                    var json = JsonExporter.TablesJson(
                        DictModel.GetDictionaryName(dict),
                        DictModel.GetDictionaryFileName(dict),
                        selected);
                    ShowJson("JSON export - " + selected.Count + " selected tables",
                        DictModel.GetDictionaryName(dict) + "-selected.json", json);
                }
            });
        }

        void ExportAll()
        {
            SafeExport(delegate
            {
                var json = JsonExporter.TablesJson(
                    DictModel.GetDictionaryName(dict),
                    DictModel.GetDictionaryFileName(dict),
                    tables);
                ShowJson("JSON export - " + DictModel.GetDictionaryName(dict),
                    DictModel.GetDictionaryName(dict) + ".json", json);
            });
        }

        // Wraps a JSON-export action so any failure during reflection / render is reported
        // as a modal error instead of propagating up and crashing the IDE.
        void SafeExport(Action action)
        {
            var prev = Cursor;
            Cursor = Cursors.WaitCursor;
            try { action(); }
            catch (OutOfMemoryException)
            {
                MessageBox.Show(this,
                    "The JSON export ran out of memory.\r\n\r\n"
                    + "Try exporting fewer tables at a time, or use SQL DDL / Markdown export instead.",
                    "JSON export", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "JSON export failed.\r\n\r\n" + ex.GetType().Name + ": " + ex.Message,
                    "JSON export", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = prev; }
        }

        void ShowJson(string title, string suggestedName, string json)
        {
            using (var dlg = new JsonPreviewDialog(title, json, suggestedName, GetInitialDir()))
                dlg.ShowDialog(this);
        }

        string GetInitialDir()
        {
            var f = DictModel.GetDictionaryFileName(dict);
            try
            {
                if (!string.IsNullOrEmpty(f) && File.Exists(f)) return Path.GetDirectoryName(f);
            }
            catch { }
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }
}
