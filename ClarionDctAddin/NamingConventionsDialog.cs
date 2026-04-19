using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Configurable rules over table names, prefixes, field labels, and key names.
    // Checkboxes toggle rules; violations flow into a ListView with suggested fix.
    internal class NamingConventionsDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color ErrorFg     = Color.FromArgb(160, 30, 30);
        static readonly Color WarnFg      = Color.FromArgb(170, 95, 10);
        static readonly Color InfoFg      = Color.FromArgb(80, 95, 115);

        readonly object dict;
        CheckBox chkTblUpper, chkPrefix, chkLblNoSpace, chkLblNoDigitStart, chkKeyConvention;
        ListView lv;
        Label    lblSummary;

        public NamingConventionsDialog(object dict) { this.dict = dict; BuildUi(); Run(); }

        void BuildUi()
        {
            Text = "Naming conventions - " + DictModel.GetDictionaryName(dict);
            Width = 1100; Height = 700;
            MinimumSize = new Size(860, 460);
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
                Text = "Naming conventions   " + DictModel.GetDictionaryName(dict)
            };

            var rules = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = BgColor, Padding = new Padding(16, 10, 16, 6) };
            chkTblUpper        = MakeCheck("Tables UPPERCASE",                 0,   6, Settings.NamingTblUpper);
            chkPrefix          = MakeCheck("Prefixes 2-4 uppercase chars",     200, 6, Settings.NamingPrefix);
            chkLblNoSpace      = MakeCheck("Field labels have no whitespace",  440, 6, Settings.NamingLblNoSpace);
            chkLblNoDigitStart = MakeCheck("Field labels don't start w/ digit",680, 6, Settings.NamingLblNoDigitStart);
            chkKeyConvention   = MakeCheck("Key names include :PK / :BY / KEY",0,  32, Settings.NamingKeyConvention);
            chkTblUpper.CheckedChanged        += delegate { Settings.NamingTblUpper        = chkTblUpper.Checked; };
            chkPrefix.CheckedChanged          += delegate { Settings.NamingPrefix          = chkPrefix.Checked; };
            chkLblNoSpace.CheckedChanged      += delegate { Settings.NamingLblNoSpace      = chkLblNoSpace.Checked; };
            chkLblNoDigitStart.CheckedChanged += delegate { Settings.NamingLblNoDigitStart = chkLblNoDigitStart.Checked; };
            chkKeyConvention.CheckedChanged   += delegate { Settings.NamingKeyConvention   = chkKeyConvention.Checked; };
            rules.Controls.Add(chkTblUpper);
            rules.Controls.Add(chkPrefix);
            rules.Controls.Add(chkLblNoSpace);
            rules.Controls.Add(chkLblNoDigitStart);
            rules.Controls.Add(chkKeyConvention);

            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 28,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(100, 115, 135),
                Padding = new Padding(18, 6, 0, 0),
                Text = ""
            };

            lv = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true, BackColor = Color.White,
                Font = new Font("Segoe UI", 9F), BorderStyle = BorderStyle.FixedSingle
            };
            lv.Columns.Add("Severity",  80);
            lv.Columns.Add("Kind",      80);
            lv.Columns.Add("Target",   280);
            lv.Columns.Add("Rule",     180);
            lv.Columns.Add("Current",  180);
            lv.Columns.Add("Suggested fix", 280);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(btnClose);

            Controls.Add(lv);
            Controls.Add(bottom);
            Controls.Add(lblSummary);
            Controls.Add(rules);
            Controls.Add(header);
            CancelButton = btnClose;

            EventHandler refire = delegate { Run(); };
            chkTblUpper.CheckedChanged        += refire;
            chkPrefix.CheckedChanged          += refire;
            chkLblNoSpace.CheckedChanged      += refire;
            chkLblNoDigitStart.CheckedChanged += refire;
            chkKeyConvention.CheckedChanged   += refire;
        }

        CheckBox MakeCheck(string text, int left, int top, bool on)
        {
            return new CheckBox
            {
                Text = text, Left = left, Top = top,
                AutoSize = true, Checked = on,
                Font = new Font("Segoe UI", 9F)
            };
        }

        enum Sev { Info, Warning, Error }

        sealed class Finding
        {
            public Sev Sev;
            public string Kind, Target, Rule, Current, Fix;
        }

        void Run()
        {
            var findings = new List<Finding>();
            var prefixRe = new Regex(@"^[A-Z]{2,4}$");
            var wsRe = new Regex(@"\s");

            foreach (var t in DictModel.GetTables(dict))
            {
                var tName  = DictModel.AsString(DictModel.GetProp(t, "Name"))   ?? "";
                var prefix = DictModel.AsString(DictModel.GetProp(t, "Prefix")) ?? "";

                if (chkTblUpper.Checked && !string.IsNullOrEmpty(tName) && tName != tName.ToUpperInvariant())
                    findings.Add(new Finding
                    {
                        Sev = Sev.Warning, Kind = "Table", Target = tName,
                        Rule = "tables-uppercase", Current = tName, Fix = tName.ToUpperInvariant()
                    });

                if (chkPrefix.Checked && !string.IsNullOrEmpty(prefix) && !prefixRe.IsMatch(prefix))
                    findings.Add(new Finding
                    {
                        Sev = Sev.Warning, Kind = "Prefix", Target = tName,
                        Rule = "prefix-2-4-upper", Current = prefix, Fix = SuggestPrefix(prefix)
                    });

                var fields = DictModel.GetProp(t, "Fields") as IEnumerable;
                if (fields != null) foreach (var f in fields)
                {
                    if (f == null) continue;
                    var label = DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "";
                    if (string.IsNullOrEmpty(label)) continue;

                    if (chkLblNoSpace.Checked && wsRe.IsMatch(label))
                        findings.Add(new Finding
                        {
                            Sev = Sev.Error, Kind = "Field", Target = tName + "." + label,
                            Rule = "label-no-whitespace", Current = label, Fix = wsRe.Replace(label, "_")
                        });

                    if (chkLblNoDigitStart.Checked && label.Length > 0 && char.IsDigit(label[0]))
                        findings.Add(new Finding
                        {
                            Sev = Sev.Warning, Kind = "Field", Target = tName + "." + label,
                            Rule = "label-no-digit-start", Current = label, Fix = "_" + label
                        });
                }

                var keys = DictModel.GetProp(t, "Keys") as IEnumerable;
                if (keys != null) foreach (var k in keys)
                {
                    if (k == null) continue;
                    var kName = DictModel.AsString(DictModel.GetProp(k, "Name")) ?? "";
                    if (string.IsNullOrEmpty(kName)) continue;

                    if (chkKeyConvention.Checked)
                    {
                        var lu = kName.ToUpperInvariant();
                        bool ok = lu.Contains(":PK") || lu.Contains(":BY") || lu.Contains("KEY") || lu.Contains(":PRIMARY");
                        if (!ok)
                            findings.Add(new Finding
                            {
                                Sev = Sev.Info, Kind = "Key", Target = tName + "." + kName,
                                Rule = "key-naming", Current = kName,
                                Fix = kName + ":BY<Components>   or   " + prefix + ":PK"
                            });
                    }
                }
            }

            findings = findings
                .OrderByDescending(x => (int)x.Sev)
                .ThenBy(x => x.Target, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int e = findings.Count(x => x.Sev == Sev.Error);
            int w = findings.Count(x => x.Sev == Sev.Warning);
            int i = findings.Count(x => x.Sev == Sev.Info);

            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var f in findings)
            {
                var item = new ListViewItem(new[] {
                    f.Sev.ToString(), f.Kind, f.Target, f.Rule, f.Current, f.Fix
                });
                item.ForeColor = f.Sev == Sev.Error ? ErrorFg : f.Sev == Sev.Warning ? WarnFg : InfoFg;
                lv.Items.Add(item);
            }
            lv.EndUpdate();
            lblSummary.Text = string.Format("{0} error{1}, {2} warning{3}, {4} info.",
                e, e == 1 ? "" : "s", w, w == 1 ? "" : "s", i);
        }

        static string SuggestPrefix(string current)
        {
            var upper = (current ?? "").ToUpperInvariant();
            var letters = new string(upper.Where(char.IsLetter).ToArray());
            if (letters.Length < 2) return letters.PadRight(2, 'X').Substring(0, 2);
            if (letters.Length > 4) return letters.Substring(0, 4);
            return letters;
        }
    }
}
