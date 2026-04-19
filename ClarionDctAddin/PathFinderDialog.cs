using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // BFS the relation graph to find the shortest path between two tables.
    // Treats relations as undirected edges. Shows the path as
    //     FROM --(relation name)--> MID --(relation name)--> TO
    internal class PathFinderDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        readonly object dict;
        ComboBox cbFrom, cbTo;
        ListView lv;
        Label    lblSummary;
        List<object> tables;

        public PathFinderDialog(object dict) { this.dict = dict; BuildUi(); }

        void BuildUi()
        {
            Text = "Path finder - " + DictModel.GetDictionaryName(dict);
            Width = 1040; Height = 620;
            MinimumSize = new Size(820, 420);
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
                Text = "Path finder   " + DictModel.GetDictionaryName(dict)
            };

            var top = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = BgColor, Padding = new Padding(16, 14, 16, 8) };
            var lblF = new Label { Text = "From:", Left = 4,   Top = 10, Width = 48, Font = new Font("Segoe UI", 9F) };
            cbFrom = new ComboBox { Left = 56, Top = 6, Width = 340, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            var lblT = new Label { Text = "To:",   Left = 416, Top = 10, Width = 32, Font = new Font("Segoe UI", 9F) };
            cbTo   = new ComboBox { Left = 452, Top = 6, Width = 340, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            top.Controls.Add(lblF); top.Controls.Add(cbFrom); top.Controls.Add(lblT); top.Controls.Add(cbTo);

            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 26,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 4, 0, 0),
                Text = ""
            };

            lv = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true, BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };
            lv.Columns.Add("Step",     60,  HorizontalAlignment.Right);
            lv.Columns.Add("Table",   280);
            lv.Columns.Add("Via relation", 240);
            lv.Columns.Add("Next table",   280);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(btnClose);

            Controls.Add(lv);
            Controls.Add(bottom);
            Controls.Add(lblSummary);
            Controls.Add(top);
            Controls.Add(header);
            CancelButton = btnClose;

            tables = DictModel.GetTables(dict)
                .OrderBy(t => DictModel.AsString(DictModel.GetProp(t, "Name")), StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var t in tables)
            {
                var n = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                cbFrom.Items.Add(n);
                cbTo.Items.Add(n);
            }
            if (cbFrom.Items.Count > 0) cbFrom.SelectedIndex = 0;
            if (cbTo.Items.Count   > 1) cbTo.SelectedIndex   = 1;
            else if (cbTo.Items.Count > 0) cbTo.SelectedIndex = 0;

            cbFrom.SelectedIndexChanged += delegate { Recompute(); };
            cbTo.SelectedIndexChanged   += delegate { Recompute(); };
            Recompute();
        }

        sealed class Edge
        {
            public string From, To, RelName;
        }

        void Recompute()
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            if (cbFrom.SelectedIndex < 0 || cbTo.SelectedIndex < 0)
            {
                lv.EndUpdate();
                lblSummary.Text = "";
                return;
            }
            var fromName = cbFrom.SelectedItem as string ?? "";
            var toName   = cbTo.SelectedItem   as string ?? "";

            if (string.Equals(fromName, toName, StringComparison.OrdinalIgnoreCase))
            {
                lblSummary.Text = "Source and destination are the same.";
                lv.EndUpdate();
                return;
            }

            var edges = BuildEdges();
            var path  = BfsShortest(fromName, toName, edges);

            if (path == null)
            {
                lblSummary.Text = "No relation path from " + fromName + " to " + toName + ".";
                lv.EndUpdate();
                return;
            }

            for (int i = 0; i < path.Count; i++)
            {
                var step = path[i];
                var via  = i < path.Count - 1 ? path[i + 1].ViaRelation : "";
                var next = i < path.Count - 1 ? path[i + 1].Table       : "";
                lv.Items.Add(new ListViewItem(new[] { (i + 1).ToString(), step.Table, via, next }));
            }
            lblSummary.Text = "Shortest path: " + (path.Count - 1) + " hop" + (path.Count == 2 ? "" : "s") + ".";
            lv.EndUpdate();
        }

        sealed class Step { public string Table; public string ViaRelation; }

        List<Edge> BuildEdges()
        {
            var edges = new List<Edge>();
            foreach (var t in tables)
            {
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "";
                var rels = DictModel.GetProp(t, "Relations") as IEnumerable;
                if (rels == null) continue;
                foreach (var r in rels)
                {
                    if (r == null) continue;
                    string related = "";
                    string[] child = { "ChildFile", "RelatedFile", "Child", "ToFile", "To", "File", "DetailFile", "ForeignFile" };
                    foreach (var p in child)
                    {
                        var v = DictModel.GetProp(r, p);
                        if (v != null) { related = DictModel.AsString(DictModel.GetProp(v, "Name")) ?? ""; break; }
                    }
                    if (string.IsNullOrEmpty(related) || string.Equals(related, tName, StringComparison.OrdinalIgnoreCase)) continue;
                    var rName = DictModel.AsString(DictModel.GetProp(r, "Name")) ?? "(rel)";
                    edges.Add(new Edge { From = tName, To = related, RelName = rName });
                }
            }
            return edges;
        }

        static List<Step> BfsShortest(string from, string to, List<Edge> edges)
        {
            var adj = new Dictionary<string, List<Edge>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in edges)
            {
                if (!adj.ContainsKey(e.From)) adj[e.From] = new List<Edge>();
                adj[e.From].Add(e);
                if (!adj.ContainsKey(e.To))   adj[e.To] = new List<Edge>();
                adj[e.To].Add(new Edge { From = e.To, To = e.From, RelName = e.RelName });
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { from };
            var pred = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);
            var q = new Queue<string>();
            q.Enqueue(from);
            bool found = false;
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (string.Equals(cur, to, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                List<Edge> ns;
                if (!adj.TryGetValue(cur, out ns)) continue;
                foreach (var e in ns)
                {
                    if (visited.Contains(e.To)) continue;
                    visited.Add(e.To);
                    pred[e.To] = new KeyValuePair<string, string>(cur, e.RelName);
                    q.Enqueue(e.To);
                }
            }
            if (!found) return null;

            var rev = new List<Step>();
            var node = to;
            rev.Add(new Step { Table = node, ViaRelation = "" });
            while (pred.ContainsKey(node))
            {
                var p = pred[node];
                rev[rev.Count - 1].ViaRelation = p.Value;
                rev.Add(new Step { Table = p.Key, ViaRelation = "" });
                node = p.Key;
            }
            rev.Reverse();
            return rev;
        }
    }
}
