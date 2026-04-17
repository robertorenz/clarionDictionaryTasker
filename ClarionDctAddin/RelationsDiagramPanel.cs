using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    internal class RelationsDiagramPanel : Panel
    {
        static readonly Color CanvasBg    = Color.White;
        static readonly Color NodeFill    = Color.FromArgb(250, 252, 255);
        static readonly Color NodeBorder  = Color.FromArgb(45,  90, 135);
        static readonly Color NodeText    = Color.FromArgb(30,  40,  55);
        static readonly Color NodeSub     = Color.FromArgb(100, 115, 135);
        static readonly Color EdgeColor   = Color.FromArgb(90,  110, 135);
        static readonly Color EdgeLabel   = Color.FromArgb(70,  95,  125);
        static readonly Color ToolbarBg   = Color.FromArgb(225, 230, 235);

        const int NodeWidth   = 170;
        const int NodeHeight  = 56;
        const int ColSpacing  = 240;
        const int RowSpacing  = 80;
        const int CanvasMargin = 24;

        readonly object dict;
        readonly List<TableNode> nodes = new List<TableNode>();
        readonly List<RelEdge>   edges = new List<RelEdge>();

        Panel canvas;
        Label emptyLabel;

        public RelationsDiagramPanel(object dict)
        {
            this.dict = dict;
            BuildUi();
            BuildGraph();
            LayoutNodes();
        }

        void BuildUi()
        {
            BackColor = CanvasBg;

            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = ToolbarBg,
                Padding = new Padding(12, 6, 12, 6)
            };
            var btnInspect = new Button
            {
                Text = "Inspect first relation...",
                Width = 200,
                Height = 28,
                FlatStyle = FlatStyle.System,
                Dock = DockStyle.Right
            };
            btnInspect.Click += delegate { InspectFirstRelation(); };
            toolbar.Controls.Add(btnInspect);

            var scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = CanvasBg
            };
            canvas = new Panel { BackColor = CanvasBg };
            canvas.Paint += OnCanvasPaint;
            scrollHost.Controls.Add(canvas);

            emptyLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10F),
                ForeColor = NodeSub,
                Visible = false,
                Text = "No relations defined in this dictionary."
            };
            scrollHost.Controls.Add(emptyLabel);

            Controls.Add(scrollHost);
            Controls.Add(toolbar);
        }

        void BuildGraph()
        {
            var byName = new Dictionary<string, TableNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in DictModel.GetTables(dict))
            {
                var name = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                var node = new TableNode(name, DictModel.CountEnumerable(t, "Fields"));
                nodes.Add(node);
                byName[name] = node;
            }

            // Collect DDRelation objects from both the dictionary-level pool
            // and per-table Relations lists (dedupe by reference).
            var relSet = new List<object>();
            var seen = new HashSet<object>();
            AddAll(relSet, seen, DictModel.GetProp(dict, "RelationsPool"));
            foreach (var t in DictModel.GetTables(dict))
                AddAll(relSet, seen, DictModel.GetProp(t, "Relations"));

            foreach (var r in relSet)
            {
                var parent = GetRelatedTable(r, parentSide: true);
                var child  = GetRelatedTable(r, parentSide: false);
                if (parent == null || child == null) continue;

                var pName = DictModel.AsString(DictModel.GetProp(parent, "Name"));
                var cName = DictModel.AsString(DictModel.GetProp(child,  "Name"));
                if (string.IsNullOrEmpty(pName) || string.IsNullOrEmpty(cName)) continue;

                TableNode pn, cn;
                byName.TryGetValue(pName, out pn);
                byName.TryGetValue(cName, out cn);
                if (pn == null || cn == null) continue;

                edges.Add(new RelEdge(pn, cn,
                    DictModel.AsString(DictModel.GetProp(r, "Name")) ?? "", r));
            }

            // If there are no relations at all, hide everything and show the empty message.
            if (edges.Count == 0)
            {
                emptyLabel.Visible = true;
                canvas.Visible = false;
            }
        }

        static void AddAll(List<object> target, HashSet<object> seen, object maybeEnumerable)
        {
            var en = maybeEnumerable as IEnumerable;
            if (en == null) return;
            foreach (var item in en)
                if (item != null && seen.Add(item)) target.Add(item);
        }

        static object GetRelatedTable(object relation, bool parentSide)
        {
            string[] parentCandidates = { "ParentFile", "PrimaryFile", "Parent", "FromFile", "From", "LookupFile", "MasterFile" };
            string[] childCandidates  = { "ChildFile",  "RelatedFile", "Child",  "ToFile",   "To",   "File",       "DetailFile", "ForeignFile" };
            var names = parentSide ? parentCandidates : childCandidates;
            foreach (var n in names)
            {
                var v = DictModel.GetProp(relation, n);
                if (v == null) continue;
                // Ensure it looks like a table — has a Name property and isn't a primitive.
                if (v is string || v.GetType().IsPrimitive) continue;
                if (DictModel.GetProp(v, "Name") != null) return v;
            }
            return null;
        }

        void LayoutNodes()
        {
            if (nodes.Count == 0) return;

            // Assign each node a layer via BFS from roots (no incoming edges).
            var incoming = nodes.ToDictionary(n => n, n => 0);
            foreach (var e in edges) if (incoming.ContainsKey(e.To)) incoming[e.To]++;

            var layer = new Dictionary<TableNode, int>();
            var q = new Queue<TableNode>();
            foreach (var n in nodes.Where(x => incoming[x] == 0))
            {
                layer[n] = 0;
                q.Enqueue(n);
            }
            if (q.Count == 0 && nodes.Count > 0) { layer[nodes[0]] = 0; q.Enqueue(nodes[0]); }

            while (q.Count > 0)
            {
                var u = q.Dequeue();
                int lv = layer[u];
                foreach (var e in edges.Where(x => x.From == u))
                {
                    int proposed = lv + 1;
                    int existing;
                    if (!layer.TryGetValue(e.To, out existing) || existing < proposed)
                    {
                        layer[e.To] = proposed;
                        q.Enqueue(e.To);
                    }
                }
            }
            foreach (var n in nodes) if (!layer.ContainsKey(n)) layer[n] = 0;

            // Isolated tables (no edges at all) go into a trailing column so they
            // don't dilute the relationship columns.
            var isolated = nodes.Where(n => !edges.Any(e => e.From == n || e.To == n)).ToList();
            int maxLayer = layer.Values.DefaultIfEmpty(0).Max();
            if (isolated.Count > 0)
            {
                int isoLayer = maxLayer + 1;
                foreach (var n in isolated) layer[n] = isoLayer;
            }

            var byLayer = nodes.GroupBy(n => layer[n]).OrderBy(g => g.Key).ToList();
            int maxBottom = 0;
            int maxRight = 0;
            int colIndex = 0;
            foreach (var grp in byLayer)
            {
                int x = CanvasMargin + colIndex * ColSpacing;
                int y = CanvasMargin;
                foreach (var n in grp.OrderBy(nn => nn.Name, StringComparer.OrdinalIgnoreCase))
                {
                    n.Bounds = new Rectangle(x, y, NodeWidth, NodeHeight);
                    y += NodeHeight + (RowSpacing - NodeHeight);
                    maxBottom = Math.Max(maxBottom, y);
                    maxRight  = Math.Max(maxRight, x + NodeWidth);
                }
                colIndex++;
            }

            canvas.Size = new Size(maxRight + CanvasMargin, maxBottom + CanvasMargin);
            canvas.Invalidate();
        }

        void OnCanvasPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var edgePen = new Pen(EdgeColor, 1.3f))
            using (var labelFont = new Font("Segoe UI", 7.5F))
            using (var labelBrush = new SolidBrush(EdgeLabel))
            {
                edgePen.CustomEndCap = new AdjustableArrowCap(5, 6, true);
                foreach (var edge in edges)
                {
                    Point p1, p2;
                    ComputeEdgePoints(edge.From.Bounds, edge.To.Bounds, out p1, out p2);
                    g.DrawLine(edgePen, p1, p2);

                    if (!string.IsNullOrEmpty(edge.Name))
                    {
                        float mx = (p1.X + p2.X) / 2F;
                        float my = (p1.Y + p2.Y) / 2F - 10;
                        var sz = g.MeasureString(edge.Name, labelFont);
                        using (var bg = new SolidBrush(Color.FromArgb(230, Color.White)))
                            g.FillRectangle(bg, mx - sz.Width / 2f - 2, my - 1, sz.Width + 4, sz.Height + 2);
                        g.DrawString(edge.Name, labelFont, labelBrush, mx - sz.Width / 2f, my);
                    }
                }
            }

            using (var nodeFill = new SolidBrush(NodeFill))
            using (var nodePen  = new Pen(NodeBorder, 1.5f))
            using (var titleFont = new Font("Segoe UI Semibold", 9F))
            using (var subFont   = new Font("Segoe UI", 8F))
            using (var titleBrush = new SolidBrush(NodeText))
            using (var subBrush   = new SolidBrush(NodeSub))
            {
                var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                foreach (var n in nodes)
                {
                    var r = n.Bounds;
                    using (var path = RoundedRect(r, 6))
                    {
                        g.FillPath(nodeFill, path);
                        g.DrawPath(nodePen,  path);
                    }
                    var title = new Rectangle(r.X, r.Y + 6, r.Width, 20);
                    var sub   = new Rectangle(r.X, r.Y + 28, r.Width, 20);
                    g.DrawString(n.Name, titleFont, titleBrush, title, fmt);
                    g.DrawString(n.FieldCount + " fields", subFont, subBrush, sub, fmt);
                }
            }
        }

        static void ComputeEdgePoints(Rectangle a, Rectangle b, out Point p1, out Point p2)
        {
            // Exit on the right side of parent, enter on the left side of child,
            // unless child is to the left — in which case flip.
            if (b.X >= a.Right)
            {
                p1 = new Point(a.Right, a.Y + a.Height / 2);
                p2 = new Point(b.Left,  b.Y + b.Height / 2);
            }
            else if (a.X >= b.Right)
            {
                p1 = new Point(a.Left,  a.Y + a.Height / 2);
                p2 = new Point(b.Right, b.Y + b.Height / 2);
            }
            else
            {
                // Overlapping X ranges — use top/bottom.
                if (b.Y >= a.Bottom)
                {
                    p1 = new Point(a.X + a.Width / 2, a.Bottom);
                    p2 = new Point(b.X + b.Width / 2, b.Top);
                }
                else
                {
                    p1 = new Point(a.X + a.Width / 2, a.Top);
                    p2 = new Point(b.X + b.Width / 2, b.Bottom);
                }
            }
        }

        static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void InspectFirstRelation()
        {
            object rel = null;
            var pool = DictModel.GetProp(dict, "RelationsPool") as IEnumerable;
            if (pool != null) foreach (var r in pool) { rel = r; break; }
            if (rel == null)
            {
                foreach (var t in DictModel.GetTables(dict))
                {
                    var en = DictModel.GetProp(t, "Relations") as IEnumerable;
                    if (en == null) continue;
                    foreach (var r in en) { rel = r; break; }
                    if (rel != null) break;
                }
            }
            if (rel == null)
            {
                MessageBox.Show(this, "This dictionary has no DDRelation objects to inspect.",
                    "DCT Addin", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Type: " + rel.GetType().FullName);
            sb.AppendLine("Assembly: " + rel.GetType().Assembly.GetName().Name);
            sb.AppendLine();
            sb.AppendLine("Public properties:");
            foreach (var p in rel.GetType()
                                 .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                 .Where(pp => pp.CanRead && pp.GetIndexParameters().Length == 0 && pp.Name.IndexOf('.') < 0)
                                 .OrderBy(pp => pp.Name))
            {
                object v; string vs;
                try { v = p.GetValue(rel, null); vs = v == null ? "<null>" : v.ToString(); }
                catch (Exception ex) { vs = "<ex: " + ex.GetType().Name + ">"; }
                if (vs != null && vs.Length > 140) vs = vs.Substring(0, 140) + "...";
                sb.AppendFormat("  {0,-30} = {1}\r\n", p.Name, vs);
            }

            using (var f = new Form
            {
                Text = "Inspect relation",
                Width = 900, Height = 640,
                StartPosition = FormStartPosition.CenterParent,
                ShowIcon = false, ShowInTaskbar = false,
                BackColor = Color.FromArgb(245, 247, 250)
            })
            {
                var tb = new TextBox
                {
                    Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                    Dock = DockStyle.Fill, Font = new Font("Consolas", 9F),
                    Text = sb.ToString(), WordWrap = false
                };
                var bp = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = ToolbarBg, Padding = new Padding(12, 8, 12, 8) };
                var bc = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
                bc.Click += delegate { f.Close(); };
                bp.Controls.Add(bc);
                f.Controls.Add(tb); f.Controls.Add(bp);
                f.CancelButton = bc;
                f.ShowDialog(this);
            }
        }

        sealed class TableNode
        {
            public readonly string Name;
            public readonly int FieldCount;
            public Rectangle Bounds;
            public TableNode(string name, int fc) { Name = name; FieldCount = fc; }
        }

        sealed class RelEdge
        {
            public readonly TableNode From;
            public readonly TableNode To;
            public readonly string   Name;
            public readonly object   Relation;
            public RelEdge(TableNode f, TableNode t, string n, object r)
            { From = f; To = t; Name = n; Relation = r; }
        }
    }
}
