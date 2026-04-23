using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Flag fields whose picture doesn't match the category of their data type:
    //   DATE   without @d*  -> warning
    //   TIME   without @t*  -> warning
    //   DECIMAL / REAL without @n -> warning; suggest @n$*.* for money-like labels
    //   BYTE / SHORT / USHORT without @n -> info
    //   LONG / ULONG: also accept @d* and @t* (Clarion uses LONG as the
    //     underlying storage for dates and times)
    //   STRING / CSTRING / PSTRING with @n or @d picture -> error
    //   Same label on many tables with divergent pictures -> warning
    internal class PictureConsistencyDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color ErrorFg     = Color.FromArgb(160, 30, 30);
        static readonly Color WarnFg      = Color.FromArgb(170, 95, 10);
        static readonly Color InfoFg      = Color.FromArgb(80, 95, 115);

        readonly object dict;
        ListView lv;
        Label    lblSummary;

        public PictureConsistencyDialog(object dict) { this.dict = dict; BuildUi(); Run(); }

        void BuildUi()
        {
            Text = "Picture consistency - " + DictModel.GetDictionaryName(dict);
            Width = 1100; Height = 680;
            MinimumSize = new Size(840, 440);
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
                Text = "Picture consistency   " + DictModel.GetDictionaryName(dict)
            };

            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 28,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(100, 115, 135),
                Padding = new Padding(18, 6, 0, 0),
                Text = "Scanning..."
            };

            lv = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true, BackColor = Color.White,
                Font = new Font("Segoe UI", 9F), BorderStyle = BorderStyle.FixedSingle
            };
            lv.Columns.Add("Severity", 80);
            lv.Columns.Add("Table",    160);
            lv.Columns.Add("Field",    180);
            lv.Columns.Add("Type",     80);
            lv.Columns.Add("Picture",  140);
            lv.Columns.Add("Issue",    500);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(btnClose);

            Controls.Add(lv);
            Controls.Add(bottom);
            Controls.Add(lblSummary);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        enum Sev { Info, Warning, Error }

        sealed class Finding
        {
            public Sev Sev;
            public string Table, Field, Type, Picture, Issue;
        }

        void Run()
        {
            var findings = new List<Finding>();
            // Track pictures per field label across tables for consistency pass
            var picsByLabel = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in DictModel.GetTables(dict))
            {
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                var fields = DictModel.GetProp(t, "Fields") as IEnumerable;
                if (fields == null) continue;
                foreach (var f in fields)
                {
                    if (f == null) continue;
                    var label = DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "";
                    if (string.IsNullOrEmpty(label)) continue;
                    var dt   = (DictModel.AsString(DictModel.GetProp(f, "DataType")) ?? "").ToUpperInvariant();
                    var pic  = DictModel.AsString(DictModel.GetProp(f, "ScreenPicture")) ?? "";
                    var picL = pic.ToLowerInvariant();

                    // Per-type rules
                    if (dt == "DATE")
                    {
                        if (string.IsNullOrEmpty(pic))
                            findings.Add(N(Sev.Warning, tName, label, dt, pic, "DATE field has no picture; use @d6, @d1, etc."));
                        else if (!picL.StartsWith("@d"))
                            findings.Add(N(Sev.Error, tName, label, dt, pic, "DATE picture should start with @d."));
                    }
                    else if (dt == "TIME")
                    {
                        if (string.IsNullOrEmpty(pic))
                            findings.Add(N(Sev.Warning, tName, label, dt, pic, "TIME field has no picture; use @t*."));
                        else if (!picL.StartsWith("@t"))
                            findings.Add(N(Sev.Error, tName, label, dt, pic, "TIME picture should start with @t."));
                    }
                    else if (dt == "DECIMAL" || dt == "PDECIMAL" || dt == "REAL" || dt == "SREAL")
                    {
                        if (string.IsNullOrEmpty(pic))
                            findings.Add(N(Sev.Warning, tName, label, dt, pic, "Numeric field has no picture; use @n*.*."));
                        else if (!picL.StartsWith("@n"))
                            findings.Add(N(Sev.Error, tName, label, dt, pic, "Numeric picture should start with @n."));
                        else if (LooksLikeMoney(label) && picL.IndexOf('$') < 0)
                            findings.Add(N(Sev.Info, tName, label, dt, pic, "Money-ish label; consider @n$*.* with currency marker."));
                    }
                    else if (dt == "LONG" || dt == "ULONG")
                    {
                        // Clarion commonly stores dates / times as LONG with @d* or @t* pictures.
                        if (!string.IsNullOrEmpty(pic)
                            && !picL.StartsWith("@n") && !picL.StartsWith("@d") && !picL.StartsWith("@t") && !picL.StartsWith("@p"))
                            findings.Add(N(Sev.Warning, tName, label, dt, pic,
                                "LONG picture should be @n*, @d*, or @t*."));
                    }
                    else if (dt == "BYTE" || dt == "SHORT" || dt == "USHORT")
                    {
                        if (!string.IsNullOrEmpty(pic) && !picL.StartsWith("@n") && !picL.StartsWith("@p"))
                            findings.Add(N(Sev.Warning, tName, label, dt, pic, "Integer picture should start with @n."));
                    }
                    else if (dt == "STRING" || dt == "CSTRING" || dt == "PSTRING")
                    {
                        if (!string.IsNullOrEmpty(pic) && (picL.StartsWith("@d") || picL.StartsWith("@t") || picL.StartsWith("@n")))
                            findings.Add(N(Sev.Error, tName, label, dt, pic, "STRING field has a non-string picture."));
                    }

                    // Collect for cross-table consistency
                    Dictionary<string, List<string>> inner;
                    if (!picsByLabel.TryGetValue(label, out inner))
                        picsByLabel[label] = inner = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    var pk = dt + "|" + (pic ?? "");
                    List<string> tablesSeen;
                    if (!inner.TryGetValue(pk, out tablesSeen))
                        inner[pk] = tablesSeen = new List<string>();
                    tablesSeen.Add(tName);
                }
            }

            // Cross-table consistency: label has >1 distinct (type|picture) combinations
            foreach (var kv in picsByLabel)
            {
                if (kv.Value.Count < 2) continue;
                var variants = string.Join(" | ", kv.Value.Select(p => p.Key + " @" + p.Value.Count + "tbl").ToArray());
                findings.Add(new Finding
                {
                    Sev = Sev.Warning,
                    Table = "(cross-table)",
                    Field = kv.Key,
                    Type = "",
                    Picture = "",
                    Issue = "Label appears with divergent type/picture combos: " + variants
                });
            }

            findings = findings
                .OrderByDescending(x => (int)x.Sev)
                .ThenBy(x => x.Table, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Field, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int e = findings.Count(x => x.Sev == Sev.Error);
            int w = findings.Count(x => x.Sev == Sev.Warning);
            int i = findings.Count(x => x.Sev == Sev.Info);

            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var f in findings)
            {
                var item = new ListViewItem(new[] { f.Sev.ToString(), f.Table, f.Field, f.Type, f.Picture, f.Issue });
                item.ForeColor = f.Sev == Sev.Error ? ErrorFg : f.Sev == Sev.Warning ? WarnFg : InfoFg;
                lv.Items.Add(item);
            }
            lv.EndUpdate();
            lblSummary.Text = string.Format("{0} error{1}, {2} warning{3}, {4} info.",
                e, e == 1 ? "" : "s", w, w == 1 ? "" : "s", i);
        }

        static Finding N(Sev s, string table, string field, string type, string pic, string issue)
        {
            return new Finding { Sev = s, Table = table, Field = field, Type = type, Picture = pic, Issue = issue };
        }

        static bool LooksLikeMoney(string label)
        {
            var l = (label ?? "").ToLowerInvariant();
            string[] hints = { "amount", "amt", "price", "cost", "total", "balance", "money", "salary", "fee", "charge", "payment", "importe", "monto", "precio", "costo" };
            foreach (var h in hints) if (l.IndexOf(h, StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }
}
