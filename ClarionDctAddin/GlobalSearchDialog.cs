using System;
using System.Collections;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Search a single term across tables, fields, keys, relations, and
    // trigger bodies in the open dictionary. Case-insensitive, optional
    // regex. Results list shows kind + owning table + item name + context.
    internal class GlobalSearchDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        readonly object dict;
        TextBox  txtSearch;
        CheckBox chkTables, chkFields, chkKeys, chkRelations, chkTriggers, chkDescriptions, chkRegex;
        ListView lv;
        Label    lblSummary;
        Timer    debounce;

        public GlobalSearchDialog(object dict) { this.dict = dict; BuildUi(); }

        void BuildUi()
        {
            Text = "Global search - " + DictModel.GetDictionaryName(dict);
            Width = 1120; Height = 740;
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
                Text = "Global search   " + DictModel.GetDictionaryName(dict)
            };

            var searchBar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = BgColor, Padding = new Padding(16, 10, 16, 4) };
            var lblS = new Label { Text = "Search:", Left = 4, Top = 10, Width = 50, Font = new Font("Segoe UI", 9F) };
            txtSearch = new TextBox { Left = 60, Top = 6, Width = 560, Font = new Font("Segoe UI", 10F) };
            chkRegex  = new CheckBox { Text = "Regex", Left = 630, Top = 8, AutoSize = true, Checked = Settings.GlobalSearchRegex, Font = new Font("Segoe UI", 9F) };
            chkRegex.CheckedChanged += delegate { Settings.GlobalSearchRegex = chkRegex.Checked; };
            searchBar.Controls.Add(lblS);
            searchBar.Controls.Add(txtSearch);
            searchBar.Controls.Add(chkRegex);

            var filterBar = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = BgColor, Padding = new Padding(60, 0, 16, 4) };
            chkTables       = MakeCheck("Tables",         0,  Settings.GlobalSearchTables);
            chkFields       = MakeCheck("Fields",         90, Settings.GlobalSearchFields);
            chkKeys         = MakeCheck("Keys",          180, Settings.GlobalSearchKeys);
            chkRelations    = MakeCheck("Relations",     260, Settings.GlobalSearchRelations);
            chkTriggers     = MakeCheck("Triggers",      360, Settings.GlobalSearchTriggers);
            chkDescriptions = MakeCheck("Descriptions",  450, Settings.GlobalSearchDescriptions);
            chkTables.CheckedChanged       += delegate { Settings.GlobalSearchTables       = chkTables.Checked; };
            chkFields.CheckedChanged       += delegate { Settings.GlobalSearchFields       = chkFields.Checked; };
            chkKeys.CheckedChanged         += delegate { Settings.GlobalSearchKeys         = chkKeys.Checked; };
            chkRelations.CheckedChanged    += delegate { Settings.GlobalSearchRelations    = chkRelations.Checked; };
            chkTriggers.CheckedChanged     += delegate { Settings.GlobalSearchTriggers     = chkTriggers.Checked; };
            chkDescriptions.CheckedChanged += delegate { Settings.GlobalSearchDescriptions = chkDescriptions.Checked; };
            filterBar.Controls.Add(chkTables);
            filterBar.Controls.Add(chkFields);
            filterBar.Controls.Add(chkKeys);
            filterBar.Controls.Add(chkRelations);
            filterBar.Controls.Add(chkTriggers);
            filterBar.Controls.Add(chkDescriptions);

            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 24,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 2, 0, 0),
                Text = "Type to search."
            };

            lv = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true, BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };
            lv.Columns.Add("Kind",     90);
            lv.Columns.Add("Table",   180);
            lv.Columns.Add("Item",    200);
            lv.Columns.Add("Context", 600);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(btnClose);

            Controls.Add(lv);
            Controls.Add(bottom);
            Controls.Add(lblSummary);
            Controls.Add(filterBar);
            Controls.Add(searchBar);
            Controls.Add(header);
            CancelButton = btnClose;

            debounce = new Timer { Interval = 220 };
            debounce.Tick += delegate { debounce.Stop(); RunSearch(); };
            txtSearch.TextChanged += delegate { debounce.Stop(); debounce.Start(); };
            EventHandler refire = delegate { RunSearch(); };
            chkRegex.CheckedChanged        += refire;
            chkTables.CheckedChanged       += refire;
            chkFields.CheckedChanged       += refire;
            chkKeys.CheckedChanged         += refire;
            chkRelations.CheckedChanged    += refire;
            chkTriggers.CheckedChanged     += refire;
            chkDescriptions.CheckedChanged += refire;

            Shown += delegate { txtSearch.Focus(); };
        }

        CheckBox MakeCheck(string text, int left, bool on)
        {
            return new CheckBox
            {
                Text = text, Left = left, Top = 6,
                AutoSize = true, Checked = on,
                Font = new Font("Segoe UI", 9F)
            };
        }

        void RunSearch()
        {
            var term = txtSearch.Text;
            lv.BeginUpdate();
            lv.Items.Clear();
            if (string.IsNullOrEmpty(term))
            {
                lv.EndUpdate();
                lblSummary.Text = "Type to search.";
                return;
            }

            Func<string, bool> match;
            if (chkRegex.Checked)
            {
                Regex re;
                try { re = new Regex(term, RegexOptions.IgnoreCase); }
                catch (Exception ex)
                {
                    lv.EndUpdate();
                    lblSummary.Text = "Invalid regex: " + ex.Message;
                    return;
                }
                match = s => !string.IsNullOrEmpty(s) && re.IsMatch(s);
            }
            else
            {
                var low = term.ToLowerInvariant();
                match = s => !string.IsNullOrEmpty(s) && s.ToLowerInvariant().IndexOf(low, StringComparison.Ordinal) >= 0;
            }

            int hits = 0;
            foreach (var t in DictModel.GetTables(dict))
            {
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "";
                var tDesc = DictModel.AsString(DictModel.GetProp(t, "Description")) ?? "";
                if (chkTables.Checked && match(tName)) { Add("Table", tName, tName, ""); hits++; }
                if (chkDescriptions.Checked && chkTables.Checked && match(tDesc))
                    { Add("TableDesc", tName, tName, Clip(tDesc)); hits++; }

                if (chkFields.Checked)
                {
                    var fields = DictModel.GetProp(t, "Fields") as IEnumerable;
                    if (fields != null) foreach (var f in fields)
                    {
                        if (f == null) continue;
                        var fLabel  = DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "";
                        var fDesc   = DictModel.AsString(DictModel.GetProp(f, "Description")) ?? "";
                        var fPrompt = DictModel.AsString(DictModel.GetProp(f, "Prompt")) ?? "";
                        var fHead   = DictModel.AsString(DictModel.GetProp(f, "Heading")) ?? "";
                        if (match(fLabel)) { Add("Field", tName, fLabel, "label match"); hits++; }
                        if (chkDescriptions.Checked)
                        {
                            if (match(fDesc))   { Add("FieldDesc",   tName, fLabel, Clip(fDesc));   hits++; }
                            if (match(fPrompt)) { Add("FieldPrompt", tName, fLabel, Clip(fPrompt)); hits++; }
                            if (match(fHead))   { Add("FieldHead",   tName, fLabel, Clip(fHead));   hits++; }
                        }
                    }
                }

                if (chkKeys.Checked)
                {
                    var keys = DictModel.GetProp(t, "Keys") as IEnumerable;
                    if (keys != null) foreach (var k in keys)
                    {
                        if (k == null) continue;
                        var kName = DictModel.AsString(DictModel.GetProp(k, "Name")) ?? "";
                        if (match(kName)) { Add("Key", tName, kName, ""); hits++; }
                    }
                }

                if (chkRelations.Checked)
                {
                    var rels = DictModel.GetProp(t, "Relations") as IEnumerable;
                    if (rels != null) foreach (var r in rels)
                    {
                        if (r == null) continue;
                        var rName = DictModel.AsString(DictModel.GetProp(r, "Name")) ?? "";
                        if (match(rName)) { Add("Relation", tName, rName, ""); hits++; }
                    }
                }

                if (chkTriggers.Checked)
                {
                    var trigs = DictModel.GetProp(t, "Triggers") as IEnumerable;
                    if (trigs != null) foreach (var tr in trigs)
                    {
                        if (tr == null) continue;
                        var trName = DictModel.AsString(DictModel.GetProp(tr, "Name")) ?? "";
                        if (match(trName)) { Add("Trigger", tName, trName, "name match"); hits++; }
                        if (chkDescriptions.Checked)
                        {
                            var body = DictModel.AsString(DictModel.GetProp(tr, "Body"))
                                    ?? DictModel.AsString(DictModel.GetProp(tr, "Code"))
                                    ?? DictModel.AsString(DictModel.GetProp(tr, "Source"))
                                    ?? DictModel.AsString(DictModel.GetProp(tr, "TriggerCode"))
                                    ?? "";
                            if (match(body)) { Add("TriggerBody", tName, trName, Clip(body)); hits++; }
                        }
                    }
                }
            }
            lv.EndUpdate();
            lblSummary.Text = hits + " hit" + (hits == 1 ? "" : "s") + ".";
        }

        void Add(string kind, string table, string item, string context)
        {
            lv.Items.Add(new ListViewItem(new[] { kind, table, item, context }));
        }

        static string Clip(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            return s.Length <= 240 ? s : s.Substring(0, 240) + "...";
        }
    }
}
