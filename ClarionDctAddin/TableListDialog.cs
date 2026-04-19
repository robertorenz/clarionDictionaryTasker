using System;
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

            // Right-click menu — actions scoped to the selected table.
            var ctx = new ContextMenuStrip();
            ctx.Items.Add("Show fields...",         null, delegate { ShowFieldsForSelection(); });
            ctx.Items.Add("Export to JSON...",      null, delegate { ExportSelected(); });
            ctx.Items.Add("Export SQL DDL...",      null, delegate { ExportSqlForSelected(); });
            ctx.Items.Add("Copy table name",        null, delegate { CopySelectedName(); });
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add("Lint this table...",     null, delegate { LintSelectedTable(); });
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add("More dictionary tools...", null, delegate { OpenToolsDialog(); });
            lv.ContextMenuStrip = ctx;

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 0), BackColor = BgColor };
            host.Controls.Add(lv);

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
            lv.BeginUpdate();
            try
            {
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
                    lv.Items.Add(item);
                }
                if (lv.Items.Count > 0) lv.Items[0].Selected = true;
            }
            finally { lv.EndUpdate(); }
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
