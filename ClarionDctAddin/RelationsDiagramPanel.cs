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

        enum LayoutMode
        {
            Layered, Alphabetical, ByFieldCount, ByConnections, ByDriver, Circle,
            ForceDirected, HubSpoke, Clusters, Tree,
            RadialTree, Bipartite, Matrix, Sugiyama, Orthogonal, Arc, Chord
        }

        enum EdgeStyle { Default, Orthogonal, Arc, Chord }

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

        bool      relatedOnly;
        CheckBox  chkRelatedOnly;

        EdgeStyle       currentEdgeStyle = EdgeStyle.Default;
        bool            matrixMode;
        List<TableNode> matrixNodes;
        const int       MatrixCellSize   = 18;
        const int       MatrixLabelWidth = 160;
        const int       MatrixLabelHeight = 140;

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
                "Circle",
                "Force-directed (spring)",
                "Hub and spoke",
                "Clusters (islands)",
                "Tree",
                "Radial tree",
                "Bipartite (parent | both | child)",
                "Matrix view",
                "Sugiyama (min crossings)",
                "Orthogonal",
                "Arc diagram",
                "Chord diagram"
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

            chkRelatedOnly = new CheckBox
            {
                Text = "Only Tables with Relations",
                Appearance = Appearance.Button,
                Left = 635, Top = 5, Width = 190, Height = 28,
                TextAlign = ContentAlignment.MiddleCenter,
                FlatStyle = FlatStyle.System,
                Font = new Font("Segoe UI", 9F)
            };
            chkRelatedOnly.CheckedChanged += delegate
            {
                relatedOnly = chkRelatedOnly.Checked;
                DoLayout((LayoutMode)cboLayout.SelectedIndex);
            };

            var rightSide = new Panel { Dock = DockStyle.Right, Width = 220, BackColor = ToolbarBg };
            var btnInspect = MakeToolbarButton("Inspect first relation...", 10, 200);
            btnInspect.Click += delegate { InspectFirstRelation(); };
            rightSide.Controls.Add(btnInspect);

            toolbar.Controls.AddRange(new Control[] { lblLayout, cboLayout, btnRelayout, btnZoomOut, lblZoom, btnZoomIn, btn100, btnFit, chkRelatedOnly, rightSide });

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
                    DictModel.AsString(DictModel.GetProp(t, "FileDriverName")) ?? "",
                    t);
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
        bool IsConnected(TableNode n)
        {
            return edges.Any(e => e.From == n || e.To == n);
        }

        void DoLayout(LayoutMode mode)
        {
            if (nodes.Count == 0) return;

            // Reset mode-specific state — each layout opts into what it needs.
            matrixMode       = false;
            matrixNodes      = null;
            currentEdgeStyle = EdgeStyle.Default;

            // Recompute visibility based on the current filter.
            foreach (var n in nodes) n.Visible = !relatedOnly || IsConnected(n);

            var visible = nodes.Where(n => n.Visible).ToList();
            if (visible.Count == 0)
            {
                contentSize = new Size(100, 100);
                canvas.Size = new Size(10, 10);
                canvas.Invalidate();
                return;
            }

            switch (mode)
            {
                case LayoutMode.Layered:       LayoutLayered(visible);     break;
                case LayoutMode.Alphabetical:  LayoutGrid(visible.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)); break;
                case LayoutMode.ByFieldCount:  LayoutGrid(visible.OrderByDescending(n => n.FieldCount).ThenBy(n => n.Name)); break;
                case LayoutMode.ByConnections: LayoutGrid(visible.OrderByDescending(CountConnections).ThenBy(n => n.Name)); break;
                case LayoutMode.ByDriver:      LayoutGroupedGrid(visible.OrderBy(n => n.Driver).ThenBy(n => n.Name)); break;
                case LayoutMode.Circle:        LayoutCircle(visible);      break;
                case LayoutMode.ForceDirected: LayoutForceDirected(visible); break;
                case LayoutMode.HubSpoke:      LayoutHubSpoke(visible);    break;
                case LayoutMode.Clusters:      LayoutClusters(visible);    break;
                case LayoutMode.Tree:          LayoutTree(visible);        break;
                case LayoutMode.RadialTree:    LayoutRadialTree(visible);  break;
                case LayoutMode.Bipartite:     LayoutBipartite(visible);   break;
                case LayoutMode.Matrix:        LayoutMatrix(visible);      break;
                case LayoutMode.Sugiyama:      LayoutSugiyama(visible);    break;
                case LayoutMode.Orthogonal:    LayoutGrid(visible.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)); currentEdgeStyle = EdgeStyle.Orthogonal; break;
                case LayoutMode.Arc:           LayoutArcDiagram(visible);  break;
                case LayoutMode.Chord:         LayoutCircle(visible); currentEdgeStyle = EdgeStyle.Chord; break;
            }
            UpdateCanvasSize();
            canvas.Invalidate();
        }

        // --- Force-directed (Fruchterman-Reingold, simplified) -------------
        void LayoutForceDirected(IList<TableNode> visible)
        {
            int n = visible.Count;
            if (n == 0) return;

            var idx = new Dictionary<TableNode, int>();
            for (int i = 0; i < n; i++) idx[visible[i]] = i;

            double area = Math.Max(1600.0 * 1000.0, n * 45000.0);
            double k = Math.Sqrt(area / n);
            int W = (int)Math.Sqrt(area * 1.4);
            int H = (int)Math.Sqrt(area / 1.4);

            var pos = new PointF[n];
            var disp = new PointF[n];
            var rnd = new Random(42);
            for (int i = 0; i < n; i++)
                pos[i] = new PointF(rnd.Next(W), rnd.Next(H));

            double t = Math.Max(W, H) / 10.0;
            const int iterations = 160;
            for (int iter = 0; iter < iterations; iter++)
            {
                for (int i = 0; i < n; i++) disp[i] = PointF.Empty;

                // repulsion
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        if (i == j) continue;
                        double dx = pos[i].X - pos[j].X;
                        double dy = pos[i].Y - pos[j].Y;
                        double d = Math.Sqrt(dx * dx + dy * dy);
                        if (d < 0.1) { d = 0.1; dx = 0.1; dy = 0; }
                        double f = (k * k) / d;
                        disp[i] = new PointF((float)(disp[i].X + dx / d * f), (float)(disp[i].Y + dy / d * f));
                    }

                // attraction via edges
                foreach (var e in edges)
                {
                    int a, b;
                    if (!idx.TryGetValue(e.From, out a)) continue;
                    if (!idx.TryGetValue(e.To, out b))   continue;
                    double dx = pos[a].X - pos[b].X;
                    double dy = pos[a].Y - pos[b].Y;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < 0.1) d = 0.1;
                    double f = (d * d) / k;
                    disp[a] = new PointF((float)(disp[a].X - dx / d * f), (float)(disp[a].Y - dy / d * f));
                    disp[b] = new PointF((float)(disp[b].X + dx / d * f), (float)(disp[b].Y + dy / d * f));
                }

                // apply, clamped by temperature
                for (int i = 0; i < n; i++)
                {
                    double d = Math.Sqrt(disp[i].X * disp[i].X + disp[i].Y * disp[i].Y);
                    if (d < 0.1) continue;
                    double step = Math.Min(d, t);
                    pos[i] = new PointF(
                        (float)(pos[i].X + disp[i].X / d * step),
                        (float)(pos[i].Y + disp[i].Y / d * step));
                    pos[i] = new PointF(
                        (float)Math.Max(0, Math.Min(W - NodeWidth, pos[i].X)),
                        (float)Math.Max(0, Math.Min(H - NodeHeight, pos[i].Y)));
                }
                t *= 0.96;
            }

            for (int i = 0; i < n; i++)
                visible[i].Bounds = new Rectangle(
                    CanvasMargin + (int)pos[i].X,
                    CanvasMargin + (int)pos[i].Y,
                    NodeWidth, NodeHeight);
        }

        // --- Hub and spoke: most-connected at center, two concentric rings -
        void LayoutHubSpoke(IList<TableNode> visible)
        {
            if (visible.Count == 0) return;

            var hub = visible[0];
            int hubC = CountConnections(hub);
            foreach (var n in visible)
            {
                int c = CountConnections(n);
                if (c > hubC) { hub = n; hubC = c; }
            }

            var level = new Dictionary<TableNode, int> { { hub, 0 } };
            foreach (var e in edges)
            {
                if (e.From == hub && !level.ContainsKey(e.To))   level[e.To]   = 1;
                if (e.To   == hub && !level.ContainsKey(e.From)) level[e.From] = 1;
            }
            foreach (var n in visible) if (!level.ContainsKey(n)) level[n] = 2;

            var ring1 = visible.Where(x => level[x] == 1).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var ring2 = visible.Where(x => level[x] == 2).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

            double r1 = 160 + Math.Max(0, ring1.Count) * 12;
            double r2 = r1 + 180 + ring2.Count * 8;
            double canvasSize = (r2 + NodeWidth) * 2 + CanvasMargin * 2;
            double cx = canvasSize / 2;
            double cy = canvasSize / 2;

            hub.Bounds = new Rectangle((int)(cx - NodeWidth / 2.0), (int)(cy - NodeHeight / 2.0), NodeWidth, NodeHeight);

            PlaceOnRing(ring1, cx, cy, r1);
            PlaceOnRing(ring2, cx, cy, r2);
        }

        void PlaceOnRing(IList<TableNode> items, double cx, double cy, double radius)
        {
            int n = items.Count;
            if (n == 0) return;
            for (int i = 0; i < n; i++)
            {
                double angle = 2 * Math.PI * i / n - Math.PI / 2;
                int x = (int)(cx + Math.Cos(angle) * radius - NodeWidth / 2.0);
                int y = (int)(cy + Math.Sin(angle) * radius - NodeHeight / 2.0);
                items[i].Bounds = new Rectangle(x, y, NodeWidth, NodeHeight);
            }
        }

        // --- Clusters: one tile per connected component, packed on a grid --
        void LayoutClusters(IList<TableNode> visible)
        {
            var set = new HashSet<TableNode>(visible);
            var visited = new HashSet<TableNode>();
            var comps = new List<List<TableNode>>();

            foreach (var start in visible)
            {
                if (!visited.Add(start)) continue;
                var comp = new List<TableNode> { start };
                var queue = new Queue<TableNode>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var u = queue.Dequeue();
                    foreach (var e in edges)
                    {
                        TableNode other = null;
                        if (e.From == u && set.Contains(e.To))   other = e.To;
                        else if (e.To == u && set.Contains(e.From)) other = e.From;
                        if (other != null && visited.Add(other))
                        {
                            comp.Add(other);
                            queue.Enqueue(other);
                        }
                    }
                }
                comps.Add(comp);
            }

            comps = comps.OrderByDescending(c => c.Count).ToList();

            const int InnerPad = 18;
            const int OuterGap = 30;
            const int MaxRowWidth = 1600;

            int xCursor = CanvasMargin;
            int yCursor = CanvasMargin;
            int rowHeight = 0;

            foreach (var comp in comps)
            {
                int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(comp.Count) * 1.1));
                int rows = (int)Math.Ceiling(comp.Count / (double)cols);
                int tileW = cols * NodeWidth  + (cols - 1) * 16 + InnerPad * 2;
                int tileH = rows * NodeHeight + (rows - 1) * 16 + InnerPad * 2;

                if (xCursor > CanvasMargin && xCursor + tileW > MaxRowWidth)
                {
                    xCursor = CanvasMargin;
                    yCursor += rowHeight + OuterGap;
                    rowHeight = 0;
                }

                int i = 0;
                foreach (var node in comp.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
                {
                    int col = i % cols;
                    int row = i / cols;
                    node.Bounds = new Rectangle(
                        xCursor + InnerPad + col * (NodeWidth  + 16),
                        yCursor + InnerPad + row * (NodeHeight + 16),
                        NodeWidth, NodeHeight);
                    i++;
                }
                xCursor   += tileW + OuterGap;
                if (tileH > rowHeight) rowHeight = tileH;
            }
        }

        // --- Tree: root per no-incoming node, centered parent over children -
        void LayoutTree(IList<TableNode> visible)
        {
            var set = new HashSet<TableNode>(visible);
            var childrenOf = new Dictionary<TableNode, List<TableNode>>();
            var indeg = new Dictionary<TableNode, int>();
            foreach (var n in visible) { childrenOf[n] = new List<TableNode>(); indeg[n] = 0; }
            foreach (var e in edges)
            {
                if (!set.Contains(e.From) || !set.Contains(e.To)) continue;
                childrenOf[e.From].Add(e.To);
                indeg[e.To]++;
            }

            var roots = visible.Where(n => indeg[n] == 0)
                               .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                               .ToList();
            if (roots.Count == 0 && visible.Count > 0) roots.Add(visible[0]);

            int unitW = NodeWidth + 24;
            int unitH = NodeHeight + 60;
            var placed = new HashSet<TableNode>();
            var width = new Dictionary<TableNode, int>();
            var widthComputing = new HashSet<TableNode>();

            Func<TableNode, int> computeWidth = null;
            computeWidth = delegate(TableNode n)
            {
                if (width.ContainsKey(n)) return width[n];
                if (!widthComputing.Add(n)) { width[n] = 1; return 1; } // cycle guard
                var kids = childrenOf[n].Where(k => !width.ContainsKey(k) || !placed.Contains(k)).ToList();
                int total = 0;
                foreach (var k in kids) total += computeWidth(k);
                width[n] = Math.Max(1, total);
                return width[n];
            };
            foreach (var r in roots) computeWidth(r);

            int xCol = 0;
            foreach (var root in roots)
            {
                int used = PlaceSubtree(root, xCol, 0, childrenOf, placed, width, unitW, unitH);
                xCol += used * unitW + 48;
            }

            // Orphans (cycles) — stack below the last tree.
            var orphans = visible.Where(n => !placed.Contains(n)).ToList();
            if (orphans.Count > 0)
            {
                int maxY = placed.Any() ? placed.Max(p => p.Bounds.Bottom) : CanvasMargin;
                int y = maxY + 30;
                int x = CanvasMargin;
                foreach (var o in orphans)
                {
                    o.Bounds = new Rectangle(x, y, NodeWidth, NodeHeight);
                    placed.Add(o);
                    x += unitW;
                }
            }

            // Shift so everything is in positive space with margin.
            if (placed.Count > 0)
            {
                int minX = placed.Min(p => p.Bounds.X);
                int minY = placed.Min(p => p.Bounds.Y);
                int dx = CanvasMargin - minX;
                int dy = CanvasMargin - minY;
                if (dx != 0 || dy != 0)
                    foreach (var n in placed)
                        n.Bounds = new Rectangle(n.Bounds.X + dx, n.Bounds.Y + dy, NodeWidth, NodeHeight);
            }
        }

        // --- Radial tree: concentric arcs, subtree slices by leaf count -----
        void LayoutRadialTree(IList<TableNode> visible)
        {
            var set = new HashSet<TableNode>(visible);
            var kids = new Dictionary<TableNode, List<TableNode>>();
            var indeg = new Dictionary<TableNode, int>();
            foreach (var n in visible) { kids[n] = new List<TableNode>(); indeg[n] = 0; }
            foreach (var e in edges)
            {
                if (!set.Contains(e.From) || !set.Contains(e.To)) continue;
                kids[e.From].Add(e.To);
                indeg[e.To]++;
            }
            var roots = visible.Where(n => indeg[n] == 0)
                               .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                               .ToList();
            if (roots.Count == 0 && visible.Count > 0) roots.Add(visible[0]);

            var leafCount = new Dictionary<TableNode, int>();
            var placed = new HashSet<TableNode>();
            ComputeLeafCount(roots, kids, leafCount, new HashSet<TableNode>());

            double cx = 800, cy = 600;
            const double radiusStep = 180;

            if (roots.Count == 1)
            {
                var r = roots[0];
                placed.Add(r);
                r.Bounds = new Rectangle((int)(cx - NodeWidth / 2.0), (int)(cy - NodeHeight / 2.0), NodeWidth, NodeHeight);
                PlaceRadialChildren(r, kids, leafCount, placed, cx, cy,
                    -Math.PI / 2, 2 * Math.PI, 1, radiusStep);
            }
            else
            {
                int totalLeaves = 0;
                foreach (var r in roots) totalLeaves += Lookup(leafCount, r, 1);
                double cur = -Math.PI / 2;
                foreach (var root in roots)
                {
                    double slice = 2 * Math.PI * Lookup(leafCount, root, 1) / (double)Math.Max(1, totalLeaves);
                    PlaceRadialAt(root, cx, cy, cur, cur + slice, 1, radiusStep, kids, leafCount, placed);
                    cur += slice;
                }
            }

            // Any orphans stranded by cycles — tuck below.
            var orphans = visible.Where(n => !placed.Contains(n)).ToList();
            if (orphans.Count > 0)
            {
                int y = placed.Count > 0 ? placed.Max(p => p.Bounds.Bottom) + 30 : CanvasMargin;
                int xx = CanvasMargin;
                foreach (var o in orphans)
                {
                    o.Bounds = new Rectangle(xx, y, NodeWidth, NodeHeight);
                    placed.Add(o);
                    xx += NodeWidth + 24;
                }
            }

            ShiftToPositive(placed);
        }

        void PlaceRadialAt(TableNode node, double cx, double cy,
            double sliceStart, double sliceEnd, int depth, double radiusStep,
            Dictionary<TableNode, List<TableNode>> kids,
            Dictionary<TableNode, int> leafCount,
            HashSet<TableNode> placed)
        {
            if (!placed.Add(node)) return;

            double angle = (sliceStart + sliceEnd) / 2;
            double radius = depth * radiusStep;
            double nx = cx + Math.Cos(angle) * radius - NodeWidth / 2.0;
            double ny = cy + Math.Sin(angle) * radius - NodeHeight / 2.0;
            node.Bounds = new Rectangle((int)nx, (int)ny, NodeWidth, NodeHeight);

            PlaceRadialChildren(node, kids, leafCount, placed, cx, cy, sliceStart, sliceEnd - sliceStart, depth + 1, radiusStep);
        }

        void PlaceRadialChildren(TableNode parent,
            Dictionary<TableNode, List<TableNode>> kids,
            Dictionary<TableNode, int> leafCount,
            HashSet<TableNode> placed,
            double cx, double cy,
            double sliceStart, double sliceExtent,
            int depth, double radiusStep)
        {
            var ch = kids[parent].Where(k => !placed.Contains(k)).ToList();
            if (ch.Count == 0) return;
            int total = 0;
            foreach (var k in ch) total += Lookup(leafCount, k, 1);
            double cur = sliceStart;
            foreach (var k in ch)
            {
                double slice = sliceExtent * Lookup(leafCount, k, 1) / (double)Math.Max(1, total);
                PlaceRadialAt(k, cx, cy, cur, cur + slice, depth, radiusStep, kids, leafCount, placed);
                cur += slice;
            }
        }

        static int Lookup(Dictionary<TableNode, int> dict, TableNode key, int fallback)
        {
            int v; return dict.TryGetValue(key, out v) ? v : fallback;
        }

        static void ComputeLeafCount(List<TableNode> roots,
            Dictionary<TableNode, List<TableNode>> kids,
            Dictionary<TableNode, int> leafCount,
            HashSet<TableNode> visiting)
        {
            foreach (var r in roots) ComputeLeafCountFor(r, kids, leafCount, visiting);
        }

        static int ComputeLeafCountFor(TableNode n,
            Dictionary<TableNode, List<TableNode>> kids,
            Dictionary<TableNode, int> leafCount,
            HashSet<TableNode> visiting)
        {
            int existing;
            if (leafCount.TryGetValue(n, out existing)) return existing;
            if (!visiting.Add(n)) { leafCount[n] = 1; return 1; }
            var ch = kids[n].Where(k => !leafCount.ContainsKey(k) || leafCount[k] > 0).ToList();
            if (ch.Count == 0) { leafCount[n] = 1; visiting.Remove(n); return 1; }
            int total = 0;
            foreach (var k in ch) total += ComputeLeafCountFor(k, kids, leafCount, visiting);
            leafCount[n] = Math.Max(1, total);
            visiting.Remove(n);
            return leafCount[n];
        }

        void ShiftToPositive(HashSet<TableNode> placed)
        {
            if (placed.Count == 0) return;
            int minX = placed.Min(p => p.Bounds.X);
            int minY = placed.Min(p => p.Bounds.Y);
            int dx = CanvasMargin - minX;
            int dy = CanvasMargin - minY;
            if (dx == 0 && dy == 0) return;
            foreach (var n in placed)
                n.Bounds = new Rectangle(n.Bounds.X + dx, n.Bounds.Y + dy, NodeWidth, NodeHeight);
        }

        // --- Bipartite: parent-side | both | child-side ---------------------
        void LayoutBipartite(IList<TableNode> visible)
        {
            var inDeg = new Dictionary<TableNode, int>();
            var outDeg = new Dictionary<TableNode, int>();
            foreach (var n in visible) { inDeg[n] = 0; outDeg[n] = 0; }
            foreach (var e in edges)
            {
                if (outDeg.ContainsKey(e.From)) outDeg[e.From]++;
                if (inDeg.ContainsKey(e.To))    inDeg[e.To]++;
            }
            var parents  = visible.Where(n => outDeg[n] > inDeg[n])
                                  .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var children = visible.Where(n => inDeg[n] > outDeg[n])
                                  .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var both     = visible.Where(n => inDeg[n] == outDeg[n])
                                  .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();

            int col1X = CanvasMargin;
            int col2X = col1X + NodeWidth + 100;
            int col3X = col2X + NodeWidth + 100;
            StackColumn(parents,  col1X);
            StackColumn(both,     col2X);
            StackColumn(children, col3X);
        }

        void StackColumn(IList<TableNode> list, int x)
        {
            int y = CanvasMargin;
            foreach (var n in list)
            {
                n.Bounds = new Rectangle(x, y, NodeWidth, NodeHeight);
                y += NodeHeight + 20;
            }
        }

        // --- Matrix view: adjacency heatmap (not spatial) -------------------
        void LayoutMatrix(IList<TableNode> visible)
        {
            matrixMode = true;
            matrixNodes = visible.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var n in visible) n.Bounds = Rectangle.Empty;
        }

        // --- Sugiyama: Layered + barycentre-based crossing reduction --------
        void LayoutSugiyama(IList<TableNode> visible)
        {
            var set = new HashSet<TableNode>(visible);
            var indeg = visible.ToDictionary(n => n, n => 0);
            foreach (var e in edges)
                if (set.Contains(e.From) && set.Contains(e.To)) indeg[e.To]++;

            var layer = new Dictionary<TableNode, int>();
            var q = new Queue<TableNode>();
            foreach (var n in visible.Where(x => indeg[x] == 0)) { layer[n] = 0; q.Enqueue(n); }
            if (q.Count == 0 && visible.Count > 0) { layer[visible[0]] = 0; q.Enqueue(visible[0]); }
            while (q.Count > 0)
            {
                var u = q.Dequeue();
                int lv = layer[u];
                foreach (var e in edges.Where(x => x.From == u && set.Contains(x.To)))
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
            foreach (var n in visible) if (!layer.ContainsKey(n)) layer[n] = 0;

            var layers = visible.GroupBy(n => layer[n]).OrderBy(g => g.Key)
                .Select(g => g.OrderBy(nn => nn.Name, StringComparer.OrdinalIgnoreCase).ToList())
                .ToList();

            // Barycentre iterations — alternate top-down and bottom-up.
            var position = new Dictionary<TableNode, int>();
            Action refresh = delegate
            {
                for (int li = 0; li < layers.Count; li++)
                    for (int i = 0; i < layers[li].Count; i++)
                        position[layers[li][i]] = i;
            };
            refresh();

            for (int iter = 0; iter < 8; iter++)
            {
                for (int li = 1; li < layers.Count; li++)
                {
                    var cur = layers[li];
                    var prev = layers[li - 1];
                    cur.Sort((a, b) => Barycentre(a, prev, true,  position).CompareTo(Barycentre(b, prev, true,  position)));
                }
                refresh();
                for (int li = layers.Count - 2; li >= 0; li--)
                {
                    var cur = layers[li];
                    var next = layers[li + 1];
                    cur.Sort((a, b) => Barycentre(a, next, false, position).CompareTo(Barycentre(b, next, false, position)));
                }
                refresh();
            }

            int colIdx = 0;
            foreach (var lyr in layers)
            {
                int x = CanvasMargin + colIdx * ColSpacing;
                int y = CanvasMargin;
                foreach (var n in lyr)
                {
                    n.Bounds = new Rectangle(x, y, NodeWidth, NodeHeight);
                    y += RowSpacing;
                }
                colIdx++;
            }
        }

        double Barycentre(TableNode n, IList<TableNode> otherLayer, bool incoming,
            Dictionary<TableNode, int> position)
        {
            var positions = new List<int>();
            foreach (var e in edges)
            {
                if (incoming && e.To == n && otherLayer.Contains(e.From))
                    positions.Add(position[e.From]);
                else if (!incoming && e.From == n && otherLayer.Contains(e.To))
                    positions.Add(position[e.To]);
            }
            return positions.Count == 0 ? double.MaxValue / 2 : positions.Average();
        }

        // --- Arc diagram: nodes on horizontal line, edges as arcs above -----
        void LayoutArcDiagram(IList<TableNode> visible)
        {
            currentEdgeStyle = EdgeStyle.Arc;
            int x = CanvasMargin;
            int y = CanvasMargin + 260;
            foreach (var n in visible.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
            {
                n.Bounds = new Rectangle(x, y, NodeWidth, NodeHeight);
                x += NodeWidth + 30;
            }
        }

        int PlaceSubtree(TableNode n, int x, int depth,
            Dictionary<TableNode, List<TableNode>> childrenOf,
            HashSet<TableNode> placed,
            Dictionary<TableNode, int> width,
            int unitW, int unitH)
        {
            if (!placed.Add(n)) return 0;

            var kids = childrenOf[n].Where(k => !placed.Contains(k)).ToList();
            int childX = x;
            int used = 0;
            foreach (var k in kids)
            {
                int u = PlaceSubtree(k, childX, depth + 1, childrenOf, placed, width, unitW, unitH);
                childX += u * unitW;
                used   += u;
            }

            int myX;
            if (used > 0)
                myX = x + ((used - 1) * unitW) / 2;
            else
            {
                myX = x;
                used = 1;
            }
            n.Bounds = new Rectangle(myX, depth * unitH, NodeWidth, NodeHeight);
            return used;
        }

        int CountConnections(TableNode n)
        {
            return edges.Count(e => e.From == n || e.To == n);
        }

        void LayoutLayered(IList<TableNode> visible)
        {
            var visibleSet = new HashSet<TableNode>(visible);
            var incoming = visible.ToDictionary(n => n, n => 0);
            foreach (var e in edges)
                if (visibleSet.Contains(e.From) && visibleSet.Contains(e.To)) incoming[e.To]++;

            var layer = new Dictionary<TableNode, int>();
            var q = new Queue<TableNode>();
            foreach (var n in visible.Where(x => incoming[x] == 0)) { layer[n] = 0; q.Enqueue(n); }
            if (q.Count == 0 && visible.Count > 0) { layer[visible[0]] = 0; q.Enqueue(visible[0]); }

            while (q.Count > 0)
            {
                var u = q.Dequeue();
                int lv = layer[u];
                foreach (var e in edges.Where(x => x.From == u && visibleSet.Contains(x.To)))
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
            foreach (var n in visible) if (!layer.ContainsKey(n)) layer[n] = 0;

            var isolated = visible.Where(n => !edges.Any(e =>
                (e.From == n && visibleSet.Contains(e.To)) ||
                (e.To == n   && visibleSet.Contains(e.From)))).ToList();
            int maxLayer = layer.Values.DefaultIfEmpty(0).Max();
            if (isolated.Count > 0)
            {
                int isoLayer = maxLayer + 1;
                foreach (var n in isolated) layer[n] = isoLayer;
            }

            int colIndex = 0;
            foreach (var grp in visible.GroupBy(n => layer[n]).OrderBy(g => g.Key))
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

        void LayoutCircle(IList<TableNode> visible)
        {
            int count = visible.Count;
            if (count == 0) return;
            double radius = Math.Max(180, count * 26);
            double centerX = CanvasMargin + radius + NodeWidth / 2.0;
            double centerY = CanvasMargin + radius + NodeHeight / 2.0;
            int i = 0;
            foreach (var n in visible.OrderBy(nn => nn.Name, StringComparer.OrdinalIgnoreCase))
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
            if (matrixMode && matrixNodes != null)
            {
                int n = matrixNodes.Count;
                contentSize = new Size(
                    MatrixLabelWidth + n * MatrixCellSize + CanvasMargin,
                    MatrixLabelHeight + n * MatrixCellSize + CanvasMargin);
                canvas.Size = new Size(
                    Math.Max(10, (int)(contentSize.Width  * zoom)),
                    Math.Max(10, (int)(contentSize.Height * zoom)));
                return;
            }

            int maxR = 0, maxB = 0;
            foreach (var n in nodes)
            {
                if (!n.Visible) continue;
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
            if (matrixMode) return;
            if (e.Button != MouseButtons.Left) return;
            var p = ScreenToLogical(e.Location);
            // Topmost hit wins — iterate back to front.
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                if (!nodes[i].Visible) continue;
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
            if (matrixMode) { canvas.Cursor = Cursors.Default; return; }
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
            bool over = nodes.Any(n => n.Visible && n.Bounds.Contains(lp));
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

            if (e.Button == MouseButtons.Right && !matrixMode)
                ShowRightClickMenu(e.Location);
        }

        void ShowRightClickMenu(Point screenRelToCanvas)
        {
            var p = ScreenToLogical(screenRelToCanvas);
            TableNode hit = null;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                if (!nodes[i].Visible) continue;
                if (nodes[i].Bounds.Contains(p)) { hit = nodes[i]; break; }
            }
            if (hit == null || hit.Table == null) return;

            var ctx = new ContextMenuStrip();
            var node = hit;
            ctx.Items.Add("Export SQL DDL...", null, delegate
            {
                using (var dlg = new SqlDdlDialog(dict, node.Table)) dlg.ShowDialog(FindForm());
            });
            ctx.Items.Add("Show fields...", null, delegate
            {
                using (var dlg = new FieldListDialog(node.Table)) dlg.ShowDialog(FindForm());
            });
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add("Lint this table...", null, delegate
            {
                using (var dlg = new LintReportDialog(dict, node.Table)) dlg.ShowDialog(FindForm());
            });
            ctx.Show(canvas, screenRelToCanvas);
        }

        // --- paint ---
        void OnCanvasPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.ScaleTransform(zoom, zoom);

            if (matrixMode)
            {
                DrawMatrix(g);
                return;
            }

            using (var edgePen = new Pen(EdgeColor, 1.3f))
            using (var labelFont = new Font("Segoe UI", 7.5F))
            using (var labelBrush = new SolidBrush(EdgeLabel))
            {
                edgePen.CustomEndCap = new AdjustableArrowCap(5, 6, true);
                foreach (var edge in edges)
                {
                    if (!edge.From.Visible || !edge.To.Visible) continue;

                    Point p1, p2;
                    switch (currentEdgeStyle)
                    {
                        case EdgeStyle.Arc:
                            DrawArcEdge(g, edgePen, edge, labelFont, labelBrush);
                            continue;
                        case EdgeStyle.Chord:
                            p1 = new Point(edge.From.Bounds.X + edge.From.Bounds.Width / 2,
                                           edge.From.Bounds.Y + edge.From.Bounds.Height / 2);
                            p2 = new Point(edge.To.Bounds.X + edge.To.Bounds.Width / 2,
                                           edge.To.Bounds.Y + edge.To.Bounds.Height / 2);
                            g.DrawLine(edgePen, p1, p2);
                            break;
                        case EdgeStyle.Orthogonal:
                            ComputeEdgePoints(edge.From.Bounds, edge.To.Bounds, out p1, out p2);
                            var mid = new Point(p2.X, p1.Y);
                            g.DrawLine(edgePen, p1, mid);
                            g.DrawLine(edgePen, mid, p2);
                            break;
                        default:
                            ComputeEdgePoints(edge.From.Bounds, edge.To.Bounds, out p1, out p2);
                            g.DrawLine(edgePen, p1, p2);
                            break;
                    }

                    if (!string.IsNullOrEmpty(edge.Name) && currentEdgeStyle != EdgeStyle.Arc)
                    {
                        // Default / Orthogonal / Chord label at geometric midpoint.
                        Point a = edge.From.Bounds.Location, b = edge.To.Bounds.Location;
                        float mx = (a.X + b.X + edge.From.Bounds.Width) / 2F;
                        float my = (a.Y + b.Y + edge.From.Bounds.Height) / 2F - 10;
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
                    if (!n.Visible) continue;
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

        void DrawArcEdge(Graphics g, Pen pen, RelEdge edge, Font labelFont, Brush labelBrush)
        {
            var pA = new Point(edge.From.Bounds.X + edge.From.Bounds.Width / 2, edge.From.Bounds.Y);
            var pB = new Point(edge.To.Bounds.X   + edge.To.Bounds.Width   / 2, edge.To.Bounds.Y);
            float height = Math.Max(40, Math.Abs(pB.X - pA.X) * 0.55f);
            var c1 = new PointF(pA.X, pA.Y - height);
            var c2 = new PointF(pB.X, pB.Y - height);
            g.DrawBezier(pen, pA, c1, c2, pB);

            if (!string.IsNullOrEmpty(edge.Name))
            {
                float mx = (pA.X + pB.X) / 2F;
                float my = Math.Min(pA.Y, pB.Y) - height * 0.75f;
                var sz = g.MeasureString(edge.Name, labelFont);
                using (var bg = new SolidBrush(Color.FromArgb(230, Color.White)))
                    g.FillRectangle(bg, mx - sz.Width / 2f - 2, my - 1, sz.Width + 4, sz.Height + 2);
                g.DrawString(edge.Name, labelFont, labelBrush, mx - sz.Width / 2f, my);
            }
        }

        void DrawMatrix(Graphics g)
        {
            if (matrixNodes == null || matrixNodes.Count == 0) return;

            int n = matrixNodes.Count;
            int cs = MatrixCellSize;
            int gridX = MatrixLabelWidth + CanvasMargin;
            int gridY = MatrixLabelHeight + CanvasMargin;

            var idx = new Dictionary<TableNode, int>();
            for (int i = 0; i < n; i++) idx[matrixNodes[i]] = i;
            var cells = new HashSet<long>();
            foreach (var e in edges)
            {
                int a, b;
                if (!idx.TryGetValue(e.From, out a)) continue;
                if (!idx.TryGetValue(e.To,   out b)) continue;
                cells.Add(((long)a << 32) | (uint)b);
            }

            using (var bg   = new SolidBrush(Color.FromArgb(248, 250, 253)))
                g.FillRectangle(bg, gridX, gridY, n * cs, n * cs);

            using (var fill = new SolidBrush(NodeBorder))
            using (var diag = new SolidBrush(Color.FromArgb(225, 230, 235)))
            using (var grid = new Pen(Color.FromArgb(218, 223, 228), 1f))
            {
                for (int r = 0; r < n; r++)
                    for (int c = 0; c < n; c++)
                    {
                        var rect = new Rectangle(gridX + c * cs, gridY + r * cs, cs, cs);
                        if (r == c)
                            g.FillRectangle(diag, rect);
                        else if (cells.Contains(((long)r << 32) | (uint)c))
                            g.FillRectangle(fill,
                                rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4);
                    }
                for (int i = 0; i <= n; i++)
                {
                    g.DrawLine(grid, gridX + i * cs, gridY, gridX + i * cs, gridY + n * cs);
                    g.DrawLine(grid, gridX, gridY + i * cs, gridX + n * cs, gridY + i * cs);
                }
            }

            using (var font = new Font("Segoe UI", 8F))
            using (var br   = new SolidBrush(NodeText))
            {
                var rightAlign = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
                for (int i = 0; i < n; i++)
                {
                    var r = new RectangleF(CanvasMargin, gridY + i * cs, MatrixLabelWidth - 6, cs);
                    g.DrawString(matrixNodes[i].Name, font, br, r, rightAlign);
                }
                for (int i = 0; i < n; i++)
                {
                    var state = g.Save();
                    g.TranslateTransform(gridX + i * cs + cs / 2f, gridY - 4);
                    g.RotateTransform(-50);
                    g.DrawString(matrixNodes[i].Name, font, br, 0, -cs / 2f);
                    g.Restore(state);
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
            public readonly object Table;
            public Rectangle Bounds;
            public bool Visible = true;
            public TableNode(string name, int fc, string drv, object table)
            {
                Name = name; FieldCount = fc; Driver = drv; Table = table;
            }
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
