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
        static readonly Color NodeFillSel = Color.FromArgb(220, 232, 245);
        static readonly Color NodeBorder  = Color.FromArgb(45,  90, 135);
        static readonly Color NodeText    = Color.FromArgb(30,  40,  55);
        static readonly Color NodeSub     = Color.FromArgb(100, 115, 135);
        static readonly Color EdgeColor   = Color.FromArgb(90,  110, 135);
        static readonly Color EdgeLabel   = Color.FromArgb(70,  95,  125);
        static readonly Color ToolbarBg   = Color.FromArgb(225, 230, 235);

        const int NodeWidth    = 170;
        const int NodeHeight   = 56;
        const int ColSpacing   = 240;
        const int RowSpacing   = 80;
        const int GridGap      = 40;
        const int CanvasMargin = 24;

        const float MinZoom = 0.25f;
        const float MaxZoom = 3.0f;

        enum LayoutMode { Layered, Alphabetical, ByFieldCount, ByConnections, ByDriver, Circle }

        readonly object dict;
        readonly List<TableNode> nodes = new List<TableNode>();
        readonly List<RelEdge>   edges = new List<RelEdge>();

        Panel    canvas;
        Label    emptyLabel;
        ComboBox cboLayout;
        Label    lblZoom;

        Size  contentSize = new Size(800, 600);
        float zoom        = 1.0f;

        TableNode draggedNode;
        Point     dragOffset;

        public RelationsDiagramPanel(object dict)
        {
            this.dict = dict;
            BuildUi();
            BuildGraph();
            DoLayout(LayoutMode.Layered);
        }

        // --- UI ---
        void BuildUi()
        {
            BackColor = CanvasBg;

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = ToolbarBg, Padding = new Padding(10, 6, 10, 6) };

            var lblLayout = new Label { Text = "Layout:", AutoSize = true, Top = 8, Left = 8, Font = new Font("Segoe UI", 9F), ForeColor = NodeText };
            cboLayout = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 180, Top = 5, Left = 65,
                Font = new Font("Segoe UI", 9F)
            };
            cboLayout.Items.AddRange(new object[]
            {
                "Layered (by dependency)",
                "Alphabetical",
                "By field count",
                "By connections",
                "By driver",
                "Circle"
            });
            cboLayout.SelectedIndex = 0;
            cboLayout.SelectedIndexChanged += delegate { DoLayout((LayoutMode)cboLayout.SelectedIndex); };

            var btnRelayout = MakeToolbarButton("Re-layout", 255);
            btnRelayout.Click += delegate { DoLayout((LayoutMode)cboLayout.SelectedIndex); };

            var btnZoomOut = MakeToolbarButton("-", 360, 40);
            btnZoomOut.Click += delegate { SetZoom(zoom / 1.15f, ViewportCenter()); };

            lblZoom = new Label { Top = 10, Left = 405, Width = 55, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9F), ForeColor = NodeText, Text = "100%" };

            var btnZoomIn = MakeToolbarButton("+", 465, 40);
            btnZoomIn.Click += delegate { SetZoom(zoom * 1.15f, ViewportCenter()); };

            var btn100 = MakeToolbarButton("100%", 510, 60);
            btn100.Click += delegate { SetZoom(1.0f, ViewportCenter()); };

            var btnFit = MakeToolbarButton("Fit", 575, 50);
            btnFit.Click += delegate { FitToView(); };

            var rightSide = new Panel { Dock = DockStyle.Right, Width = 220, BackColor = ToolbarBg };
            var btnInspect = MakeToolbarButton("Inspect first relation...", 10, 200);
            btnInspect.Click += delegate { InspectFirstRelation(); };
            rightSide.Controls.Add(btnInspect);

            toolbar.Controls.AddRange(new Control[] { lblLayout, cboLayout, btnRelayout, btnZoomOut, lblZoom, btnZoomIn, btn100, btnFit, rightSide });

            var scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = CanvasBg };

            canvas = new Panel { BackColor = CanvasBg };
            canvas.Paint      += OnCanvasPaint;
            canvas.MouseDown  += OnCanvasMouseDown;
            canvas.MouseMove  += OnCanvasMouseMove;
            canvas.MouseUp    += OnCanvasMouseUp;
            canvas.MouseLeave += delegate { canvas.Cursor = Cursors.Default; };

            scrollHost.Controls.Add(canvas);
            scrollHost.MouseWheel += OnWheel;

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

        Button MakeToolbarButton(string text, int left, int width = 80)
        {
            return new Button
            {
                Text = text, Left = left, Top = 5, Width = width, Height = 28,
                FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 9F)
            };
        }

        // --- graph build ---
        void BuildGraph()
        {
            var byName = new Dictionary<string, TableNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in DictModel.GetTables(dict))
            {
                var name = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                var node = new TableNode(
                    name,
                    DictModel.CountEnumerable(t, "Fields"),
                    DictModel.AsString(DictModel.GetProp(t, "FileDriverName")) ?? "");
                nodes.Add(node);
                byName[name] = node;
            }

            var relSet = new List<object>();
            var seen = new HashSet<object>();
            AddAll(relSet, seen, DictModel.GetProp(dict, "RelationsPool"));
            foreach (var t in DictModel.GetTables(dict))
                AddAll(relSet, seen, DictModel.GetProp(t, "Relations"));

            foreach (var r in relSet)
            {
                var parent = GetRelatedTable(r, true);
                var child  = GetRelatedTable(r, false);
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

            if (nodes.Count == 0)
            {
                emptyLabel.Text = "No tables in this dictionary.";
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
                if (v is string || v.GetType().IsPrimitive) continue;
                if (DictModel.GetProp(v, "Name") != null) return v;
            }
            return null;
        }

        // --- layout ---
        void DoLayout(LayoutMode mode)
        {
            if (nodes.Count == 0) return;
            switch (mode)
            {
                case LayoutMode.Layered:       LayoutLayered();     break;
                case LayoutMode.Alphabetical:  LayoutGrid(nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)); break;
                case LayoutMode.ByFieldCount:  LayoutGrid(nodes.OrderByDescending(n => n.FieldCount).ThenBy(n => n.Name)); break;
                case LayoutMode.ByConnections: LayoutGrid(nodes.OrderByDescending(CountConnections).ThenBy(n => n.Name)); break;
                case LayoutMode.ByDriver:      LayoutGroupedGrid(nodes.OrderBy(n => n.Driver).ThenBy(n => n.Name)); break;
                case LayoutMode.Circle:        LayoutCircle();      break;
            }
            UpdateCanvasSize();
            canvas.Invalidate();
        }

        int CountConnections(TableNode n)
        {
            return edges.Count(e => e.From == n || e.To == n);
        }

        void LayoutLayered()
        {
            var incoming = nodes.ToDictionary(n => n, n => 0);
            foreach (var e in edges) if (incoming.ContainsKey(e.To)) incoming[e.To]++;

            var layer = new Dictionary<TableNode, int>();
            var q = new Queue<TableNode>();
            foreach (var n in nodes.Where(x => incoming[x] == 0)) { layer[n] = 0; q.Enqueue(n); }
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

            var isolated = nodes.Where(n => !edges.Any(e => e.From == n || e.To == n)).ToList();
            int maxLayer = layer.Values.DefaultIfEmpty(0).Max();
            if (isolated.Count > 0)
            {
                int isoLayer = maxLayer + 1;
                foreach (var n in isolated) layer[n] = isoLayer;
            }

            int colIndex = 0;
            foreach (var grp in nodes.GroupBy(n => layer[n]).OrderBy(g => g.Key))
            {
                int x = CanvasMargin + colIndex * ColSpacing;
                int y = CanvasMargin;
                foreach (var n in grp.OrderBy(nn => nn.Name, StringComparer.OrdinalIgnoreCase))
                {
                    n.Bounds = new Rectangle(x, y, NodeWidth, NodeHeight);
                    y += NodeHeight + (RowSpacing - NodeHeight);
                }
                colIndex++;
            }
        }

        void LayoutGrid(IEnumerable<TableNode> ordered)
        {
            var list = ordered.ToList();
            int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(list.Count) * 1.2));
            int col = 0, row = 0;
            foreach (var n in list)
            {
                int x = CanvasMargin + col * (NodeWidth + GridGap);
                int y = CanvasMargin + row * (NodeHeight + GridGap);
                n.Bounds = new Rectangle(x, y, NodeWidth, NodeHeight);
                col++;
                if (col >= cols) { col = 0; row++; }
            }
        }

        void LayoutGroupedGrid(IEnumerable<TableNode> ordered)
        {
            // Group by driver: one column per driver, nodes stacked vertically.
            var groups = ordered.GroupBy(n => string.IsNullOrEmpty(n.Driver) ? "(no driver)" : n.Driver).ToList();
            int x = CanvasMargin;
            foreach (var g in groups)
            {
                int y = CanvasMargin;
                foreach (var n in g.OrderBy(nn => nn.Name, StringComparer.OrdinalIgnoreCase))
                {
                    n.Bounds = new Rectangle(x, y, NodeWidth, NodeHeight);
                    y += NodeHeight + 20;
                }
                x += NodeWidth + GridGap + 20;
            }
        }

        void LayoutCircle()
        {
            int count = nodes.Count;
            double radius = Math.Max(180, count * 26);
            double centerX = CanvasMargin + radius + NodeWidth / 2.0;
            double centerY = CanvasMargin + radius + NodeHeight / 2.0;
            int i = 0;
            foreach (var n in nodes.OrderBy(nn => nn.Name, StringComparer.OrdinalIgnoreCase))
            {
                double angle = 2 * Math.PI * i / count - Math.PI / 2;
                int x = (int)(centerX + Math.Cos(angle) * radius - NodeWidth / 2.0);
                int y = (int)(centerY + Math.Sin(angle) * radius - NodeHeight / 2.0);
                n.Bounds = new Rectangle(x, y, NodeWidth, NodeHeight);
                i++;
            }
        }

        void UpdateCanvasSize()
        {
            int maxR = 0, maxB = 0;
            foreach (var n in nodes)
            {
                if (n.Bounds.Right  > maxR) maxR = n.Bounds.Right;
                if (n.Bounds.Bottom > maxB) maxB = n.Bounds.Bottom;
            }
            contentSize = new Size(maxR + CanvasMargin, maxB + CanvasMargin);
            canvas.Size = new Size(
                Math.Max(10, (int)(contentSize.Width * zoom)),
                Math.Max(10, (int)(contentSize.Height * zoom)));
        }

        // --- zoom ---
        Point ViewportCenter()
        {
            var host = canvas.Parent;
            return host == null
                ? new Point(canvas.Width / 2, canvas.Height / 2)
                : new Point(host.ClientSize.Width / 2, host.ClientSize.Height / 2);
        }

        void SetZoom(float newZoom, Point anchor)
        {
            newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, newZoom));
            if (Math.Abs(newZoom - zoom) < 0.001f) return;

            var host = canvas.Parent as Panel;
            // Logical point under the anchor before zoom.
            float lx = (anchor.X - canvas.Left) / zoom;
            float ly = (anchor.Y - canvas.Top)  / zoom;

            zoom = newZoom;
            UpdateCanvasSize();

            // Adjust scroll so same logical point stays near the anchor.
            if (host != null)
            {
                int desiredLeft = anchor.X - (int)(lx * zoom);
                int desiredTop  = anchor.Y - (int)(ly * zoom);
                host.AutoScrollPosition = new Point(-desiredLeft, -desiredTop);
            }
            UpdateZoomLabel();
            canvas.Invalidate();
        }

        void FitToView()
        {
            var host = canvas.Parent;
            if (host == null || contentSize.Width == 0 || contentSize.Height == 0) return;
            float zx = (float)host.ClientSize.Width  / contentSize.Width;
            float zy = (float)host.ClientSize.Height / contentSize.Height;
            SetZoom(Math.Min(zx, zy) * 0.95f, ViewportCenter());
        }

        void UpdateZoomLabel()
        {
            if (lblZoom != null) lblZoom.Text = ((int)Math.Round(zoom * 100)) + "%";
        }

        void OnWheel(object sender, MouseEventArgs e)
        {
            if ((Control.ModifierKeys & Keys.Control) != Keys.Control) return;
            SetZoom(zoom * (e.Delta > 0 ? 1.1f : 1f / 1.1f),
                canvas.Parent.PointToClient(Cursor.Position));
        }

        // --- drag ---
        Point ScreenToLogical(Point screenFromCanvas)
        {
            return new Point((int)(screenFromCanvas.X / zoom), (int)(screenFromCanvas.Y / zoom));
        }

        void OnCanvasMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var p = ScreenToLogical(e.Location);
            // Topmost hit wins — iterate back to front.
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                if (nodes[i].Bounds.Contains(p))
                {
                    draggedNode = nodes[i];
                    dragOffset  = new Point(p.X - draggedNode.Bounds.X, p.Y - draggedNode.Bounds.Y);
                    // Bring to front so it paints on top of overlapping nodes.
                    nodes.RemoveAt(i);
                    nodes.Add(draggedNode);
                    canvas.Cursor = Cursors.SizeAll;
                    canvas.Invalidate();
                    return;
                }
            }
        }

        void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (draggedNode != null)
            {
                var p = ScreenToLogical(e.Location);
                draggedNode.Bounds = new Rectangle(
                    p.X - dragOffset.X,
                    p.Y - dragOffset.Y,
                    draggedNode.Bounds.Width,
                    draggedNode.Bounds.Height);
                canvas.Invalidate();
                return;
            }
            var lp = ScreenToLogical(e.Location);
            bool over = nodes.Any(n => n.Bounds.Contains(lp));
            canvas.Cursor = over ? Cursors.SizeAll : Cursors.Default;
        }

        void OnCanvasMouseUp(object sender, MouseEventArgs e)
        {
            if (draggedNode != null)
            {
                draggedNode = null;
                UpdateCanvasSize();
                canvas.Invalidate();
            }
            canvas.Cursor = Cursors.Default;
        }

        // --- paint ---
        void OnCanvasPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.ScaleTransform(zoom, zoom);

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

            using (var nodeFill    = new SolidBrush(NodeFill))
            using (var nodeFillSel = new SolidBrush(NodeFillSel))
            using (var nodePen     = new Pen(NodeBorder, 1.5f))
            using (var nodePenSel  = new Pen(NodeBorder, 2.2f))
            using (var titleFont   = new Font("Segoe UI Semibold", 9F))
            using (var subFont     = new Font("Segoe UI", 8F))
            using (var titleBrush  = new SolidBrush(NodeText))
            using (var subBrush    = new SolidBrush(NodeSub))
            {
                var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                foreach (var n in nodes)
                {
                    var r = n.Bounds;
                    bool isDragged = n == draggedNode;
                    using (var path = RoundedRect(r, 6))
                    {
                        g.FillPath(isDragged ? nodeFillSel : nodeFill, path);
                        g.DrawPath(isDragged ? nodePenSel  : nodePen,  path);
                    }
                    var title = new Rectangle(r.X, r.Y + 6, r.Width, 20);
                    var sub   = new Rectangle(r.X, r.Y + 28, r.Width, 20);
                    g.DrawString(n.Name, titleFont, titleBrush, title, fmt);
                    var subText = n.FieldCount + " fields";
                    if (!string.IsNullOrEmpty(n.Driver)) subText += "  ·  " + n.Driver;
                    g.DrawString(subText, subFont, subBrush, sub, fmt);
                }
            }
        }

        static void ComputeEdgePoints(Rectangle a, Rectangle b, out Point p1, out Point p2)
        {
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

        // --- diagnostic ---
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
                Text = "Inspect relation", Width = 900, Height = 640,
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
            public readonly int    FieldCount;
            public readonly string Driver;
            public Rectangle Bounds;
            public TableNode(string name, int fc, string drv) { Name = name; FieldCount = fc; Driver = drv; }
        }

        sealed class RelEdge
        {
            public readonly TableNode From;
            public readonly TableNode To;
            public readonly string    Name;
            public readonly object    Relation;
            public RelEdge(TableNode f, TableNode t, string n, object r)
            { From = f; To = t; Name = n; Relation = r; }
        }
    }
}
