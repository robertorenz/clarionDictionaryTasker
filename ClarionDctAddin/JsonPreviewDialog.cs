using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Read-only preview window for JSON exports. Mirrors the SqlDdlDialog /
    // MarkdownDialog pattern — monospace viewer with Copy / Save / Close.
    // The JSON is rendered once up front; this dialog just presents it.
    internal class JsonPreviewDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        readonly string title;
        readonly string originalJson;    // what the exporter handed us; reformats are done from this
        readonly string suggestedFileName;
        readonly string initialDir;
        TextBox txtOutput;
        Label   stats;
        Button  btnPretty2, btnPretty4, btnTabs, btnMinified;

        enum Style { Pretty2, Pretty4, Tabs, Minified }
        Style currentStyle = Style.Pretty2;

        public JsonPreviewDialog(string title, string json, string suggestedFileName, string initialDir)
        {
            this.title = title ?? "JSON export";
            this.originalJson = json ?? "";
            this.suggestedFileName = suggestedFileName ?? "export.json";
            this.initialDir = initialDir;
            BuildUi();
            ApplyStyle(Style.Pretty2);
        }

        void BuildUi()
        {
            Text = title;
            Width = 1080; Height = 720;
            MinimumSize = new Size(760, 460);
            StartPosition = FormStartPosition.CenterParent;
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
                Text = title
            };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = BgColor, Padding = new Padding(16, 8, 16, 4) };
            var lblStyle = new Label { Text = "Style:", Left = 0, Top = 10, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            btnPretty2  = StyleButton("Pretty (2 sp)",  Style.Pretty2,  50);
            btnPretty4  = StyleButton("Pretty (4 sp)",  Style.Pretty4,  168);
            btnTabs     = StyleButton("Tabs",           Style.Tabs,     286);
            btnMinified = StyleButton("Minified",       Style.Minified, 376);
            toolbar.Controls.Add(lblStyle);
            toolbar.Controls.Add(btnPretty2);
            toolbar.Controls.Add(btnPretty4);
            toolbar.Controls.Add(btnTabs);
            toolbar.Controls.Add(btnMinified);

            stats = new Label
            {
                Dock = DockStyle.Top, Height = 26,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 6, 0, 0),
                Text = ""
            };

            txtOutput = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new Font("Consolas", 9.5F),
                BackColor = Color.White
            };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            var btnSave  = new Button { Text = "Save as...", Width = 140, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnSave.Click += delegate { SaveAs(); };
            var btnCopy  = new Button { Text = "Copy to clipboard", Width = 160, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnCopy.Click += delegate { CopyToClipboard(); };
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(btnSave);
            bottom.Controls.Add(btnCopy);

            Controls.Add(txtOutput);
            Controls.Add(bottom);
            Controls.Add(stats);
            Controls.Add(toolbar);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        Button StyleButton(string text, Style style, int left)
        {
            var b = new Button
            {
                Text = text, Left = left, Top = 4, Width = 110, Height = 28,
                FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 9F)
            };
            b.Click += delegate { ApplyStyle(style); };
            return b;
        }

        void ApplyStyle(Style style)
        {
            currentStyle = style;
            Cursor = Cursors.WaitCursor;
            try
            {
                string formatted;
                switch (style)
                {
                    case Style.Pretty2:  formatted = JsonFormatter.Pretty(originalJson, "  "); break;
                    case Style.Pretty4:  formatted = JsonFormatter.Pretty(originalJson, "    "); break;
                    case Style.Tabs:     formatted = JsonFormatter.Pretty(originalJson, "\t"); break;
                    case Style.Minified: formatted = JsonFormatter.Minified(originalJson); break;
                    default:             formatted = originalJson; break;
                }
                txtOutput.Text = formatted;
                txtOutput.SelectionStart = 0;
                txtOutput.SelectionLength = 0;
                stats.Text = FormatStats(formatted) + "   ·   style: " + StyleLabel(style);
                HighlightActive();
            }
            finally { Cursor = Cursors.Default; }
        }

        static string StyleLabel(Style s)
        {
            switch (s)
            {
                case Style.Pretty2:  return "pretty, 2-space indent";
                case Style.Pretty4:  return "pretty, 4-space indent";
                case Style.Tabs:     return "pretty, tab indent";
                case Style.Minified: return "minified (single line)";
                default:             return "?";
            }
        }

        void HighlightActive()
        {
            SetActive(btnPretty2,  currentStyle == Style.Pretty2);
            SetActive(btnPretty4,  currentStyle == Style.Pretty4);
            SetActive(btnTabs,     currentStyle == Style.Tabs);
            SetActive(btnMinified, currentStyle == Style.Minified);
        }

        static void SetActive(Button b, bool active)
        {
            b.Font = new Font("Segoe UI", 9F, active ? FontStyle.Bold : FontStyle.Regular);
        }

        static string FormatStats(string json)
        {
            int chars = json == null ? 0 : json.Length;
            int lines = 1;
            if (!string.IsNullOrEmpty(json))
                for (int i = 0; i < json.Length; i++) if (json[i] == '\n') lines++;
            return chars.ToString("N0") + " chars, " + lines.ToString("N0") + " lines.";
        }

        void CopyToClipboard()
        {
            if (string.IsNullOrEmpty(txtOutput.Text)) return;
            try { Clipboard.SetText(txtOutput.Text); } catch { }
        }

        void SaveAs()
        {
            using (var dlg = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = suggestedFileName,
                InitialDirectory = initialDir ?? ""
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, txtOutput.Text);
                    MessageBox.Show(this, "Saved: " + dlg.FileName, "JSON export",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Save failed: " + ex.Message, "JSON export",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
