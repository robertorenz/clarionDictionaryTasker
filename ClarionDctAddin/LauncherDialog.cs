using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    internal enum LauncherTileKind { BrowseTables, CopyFields, CopyKeys }

    internal class LauncherDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color SubHeader   = Color.FromArgb(200, 213, 228);

        readonly object dict;
        readonly Bitmap backgroundImage;

        public LauncherDialog(object dict)
        {
            this.dict = dict;
            this.backgroundImage = EmbeddedAssets.LoadBackground();
            BuildUi();
            var ico = EmbeddedAssets.LoadIcon();
            if (ico != null)
            {
                Icon = ico;
                ShowIcon = true;
            }
            DoubleBuffered = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            var rect = ClientRectangle;

            // Solid base so unpainted areas never flicker black.
            using (var b = new SolidBrush(BgColor)) g.FillRectangle(b, rect);

            if (backgroundImage != null)
            {
                // Cover the client area — paint the image letterboxed so it
                // never distorts, centred, and then overlay a translucent
                // wash so text and tiles stay comfortably readable.
                float srcRatio = (float)backgroundImage.Width / backgroundImage.Height;
                float dstRatio = (float)rect.Width / rect.Height;
                RectangleF dst;
                if (srcRatio > dstRatio)
                {
                    float h = rect.Width / srcRatio;
                    dst = new RectangleF(0, (rect.Height - h) / 2f, rect.Width, h);
                }
                else
                {
                    float w = rect.Height * srcRatio;
                    dst = new RectangleF((rect.Width - w) / 2f, 0, w, rect.Height);
                }
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(backgroundImage, dst);

                // Subtle wash so tiles + text stay readable while the image
                // still comes through as branding. Lower alpha = more image.
                using (var overlay = new SolidBrush(Color.FromArgb(195, BgColor)))
                    g.FillRectangle(overlay, rect);
            }
        }

        void BuildUi()
        {
            Text = "Dictionary Tasker";
            Width = 640;
            Height = 520;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;

            var header = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = HeaderColor };
            var title = new Label
            {
                Text = "Dictionary Tasker",
                Location = new Point(22, 14),
                Size = new Size(500, 28),
                Font = new Font("Segoe UI Semibold", 14F),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var sub = new Label
            {
                Text = DictModel.GetDictionaryName(dict) + "     " + DictModel.GetDictionaryFileName(dict),
                Location = new Point(24, 46),
                Size = new Size(580, 20),
                Font = new Font("Segoe UI", 9F),
                ForeColor = SubHeader,
                TextAlign = ContentAlignment.MiddleLeft
            };
            header.Controls.Add(title);
            header.Controls.Add(sub);

            var body = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 20, 24, 8),
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            body.Controls.Add(MakeTile("Browse tables",
                "Tabbed explorer with table list, hierarchy tree, and relations diagram. Export tables to JSON.",
                LauncherTileKind.BrowseTables,
                OpenBrowse));
            body.Controls.Add(MakeTile("Batch copy fields",
                "Replicate selected fields (audit columns, timestamps, GUIDs) across many tables in one pass.",
                LauncherTileKind.CopyFields,
                OpenCopyFields));
            body.Controls.Add(MakeTile("Batch copy keys",
                "Copy keys to other tables with automatic component remap by field label and backup before writing.",
                LauncherTileKind.CopyKeys,
                OpenCopyKeys));

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(btnClose);

            Controls.Add(body);
            Controls.Add(bottom);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        LauncherTile MakeTile(string title, string desc, LauncherTileKind icon, Action onClick)
        {
            var tile = new LauncherTile
            {
                TileTitle = title,
                TileDescription = desc,
                Kind = icon,
                Width = 560,
                Height = 92,
                Margin = new Padding(0, 0, 0, 14)
            };
            tile.TileClicked += delegate { onClick(); };
            return tile;
        }

        void OpenBrowse()     { Hide(); using (var d = new TableListDialog(dict))      d.ShowDialog(this); Show(); }
        void OpenCopyFields() { Hide(); using (var d = new BatchCopyDialog(dict))      d.ShowDialog(this); Show(); }
        void OpenCopyKeys()   { Hide(); using (var d = new BatchCopyKeysDialog(dict))  d.ShowDialog(this); Show(); }
    }

    internal class LauncherTile : Panel
    {
        static readonly Color BorderColor  = Color.FromArgb(200, 210, 220);
        static readonly Color BorderHover  = Color.FromArgb(45,  90, 135);
        static readonly Color FillHover    = Color.FromArgb(235, 242, 250);
        static readonly Color TitleColor   = Color.FromArgb(30,  40,  55);
        static readonly Color DescColor    = Color.FromArgb(100, 115, 135);
        static readonly Color AccentColor  = Color.FromArgb(45,  90, 135);
        static readonly Color AccentLight  = Color.FromArgb(210, 225, 240);

        public string TileTitle;
        public string TileDescription;
        public LauncherTileKind Kind;

        public event EventHandler TileClicked;

        bool hovered;

        public LauncherTile()
        {
            BackColor = Color.White;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw, true);
            MouseEnter += delegate { hovered = true;  Invalidate(); };
            MouseLeave += delegate { hovered = false; Invalidate(); };
            Click      += delegate { if (TileClicked != null) TileClicked(this, EventArgs.Empty); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundedRect(rect, 10))
            {
                using (var fill = new SolidBrush(hovered ? FillHover : Color.White))
                    g.FillPath(fill, path);
                using (var pen = new Pen(hovered ? BorderHover : BorderColor, hovered ? 1.8f : 1f))
                    g.DrawPath(pen, path);
            }

            // Icon area on the left.
            var iconBox = new Rectangle(20, (Height - 56) / 2, 56, 56);
            DrawIcon(g, Kind, iconBox);

            using (var titleFont = new Font("Segoe UI Semibold", 12F))
            using (var titleBrush = new SolidBrush(TitleColor))
                g.DrawString(TileTitle ?? "", titleFont, titleBrush, new PointF(92, 18));

            using (var descFont = new Font("Segoe UI", 9F))
            using (var descBrush = new SolidBrush(DescColor))
                g.DrawString(TileDescription ?? "", descFont, descBrush,
                    new RectangleF(92, 44, Width - 110, Height - 48));
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

        static void DrawIcon(Graphics g, LauncherTileKind kind, Rectangle box)
        {
            using (var accentBrush = new SolidBrush(AccentColor))
            using (var lightBrush  = new SolidBrush(AccentLight))
            using (var whiteBrush  = new SolidBrush(Color.White))
            using (var thinPen     = new Pen(AccentColor, 1.6f))
            using (var boldPen     = new Pen(AccentColor, 2.2f))
            {
                switch (kind)
                {
                    case LauncherTileKind.BrowseTables:
                    {
                        // 3x3 grid — first row accented like a table header.
                        int cell = 12, gap = 3;
                        int total = cell * 3 + gap * 2;
                        int x = box.X + (box.Width - total) / 2;
                        int y = box.Y + (box.Height - total) / 2;
                        for (int r = 0; r < 3; r++)
                            for (int c = 0; c < 3; c++)
                            {
                                var cellRect = new Rectangle(x + c * (cell + gap), y + r * (cell + gap), cell, cell);
                                g.FillRectangle(r == 0 ? accentBrush : lightBrush, cellRect);
                            }
                        break;
                    }
                    case LauncherTileKind.CopyFields:
                    {
                        // Two overlapping sheets with horizontal "rows".
                        var back  = new Rectangle(box.X + 8,  box.Y + 14, 30, 38);
                        var front = new Rectangle(box.X + 18, box.Y + 4,  30, 38);
                        g.FillRectangle(lightBrush, back);
                        g.DrawRectangle(thinPen, back);
                        g.FillRectangle(whiteBrush, front);
                        g.DrawRectangle(boldPen, front);
                        for (int i = 0; i < 4; i++)
                        {
                            int yy = front.Y + 8 + i * 7;
                            g.DrawLine(thinPen, front.X + 4, yy, front.Right - 4, yy);
                        }
                        break;
                    }
                    case LauncherTileKind.CopyKeys:
                    {
                        // Simplified key silhouette.
                        int cx = box.X + 18;
                        int cy = box.Y + box.Height / 2;
                        int outer = 12;
                        int inner = 5;
                        g.FillEllipse(lightBrush, cx - outer, cy - outer, outer * 2, outer * 2);
                        g.DrawEllipse(boldPen,    cx - outer, cy - outer, outer * 2, outer * 2);
                        g.FillEllipse(whiteBrush, cx - inner, cy - inner, inner * 2, inner * 2);
                        g.DrawEllipse(thinPen,    cx - inner, cy - inner, inner * 2, inner * 2);
                        // shaft
                        int shaftStart = cx + outer;
                        int shaftEnd   = box.Right - 6;
                        g.DrawLine(boldPen, shaftStart, cy, shaftEnd, cy);
                        g.DrawLine(boldPen, shaftEnd, cy, shaftEnd, cy + 8);
                        g.DrawLine(boldPen, shaftEnd - 8, cy, shaftEnd - 8, cy + 6);
                        break;
                    }
                }
            }
        }
    }
}
