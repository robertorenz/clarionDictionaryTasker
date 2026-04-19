using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Read-only preview window for JSON exports. Mirrors the SqlDdlDialog /
    // MarkdownDialog pattern — monospace viewer with Copy / Save / Close.
    // Style toolbar toggles between pretty (2sp / 4sp / tabs), minified,
    // and a tree view. Copy / Save always use the currently-rendered text.
    internal class JsonPreviewDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);
        static readonly Color KeyColor    = Color.FromArgb(45, 90, 135);
        static readonly Color StringColor = Color.FromArgb(130, 50, 30);
        static readonly Color NumberColor = Color.FromArgb(30, 110, 80);
        static readonly Color NullColor   = Color.FromArgb(140, 90, 40);
        static readonly Color SummaryColor = Color.FromArgb(110, 120, 140);

        readonly string title;
        readonly string originalJson;
        readonly string suggestedFileName;
        readonly string initialDir;

        Panel    viewHost;   // holds either txtOutput or tree, swapped on style change
        TextBox  txtOutput;
        TreeView tree;
        Label    stats;
        Button   btnPretty2, btnPretty4, btnTabs, btnMinified, btnTree;

        enum Style { Pretty2, Pretty4, Tabs, Minified, Tree }
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
            var lblStyle = new Label { Text = "View:", Left = 0, Top = 10, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            btnPretty2  = StyleButton("Pretty (2 sp)",  Style.Pretty2,   44);
            btnPretty4  = StyleButton("Pretty (4 sp)",  Style.Pretty4,   162);
            btnTabs     = StyleButton("Tabs",           Style.Tabs,      280);
            btnMinified = StyleButton("Minified",       Style.Minified,  370);
            btnTree     = StyleButton("Tree",           Style.Tree,      480);
            toolbar.Controls.Add(lblStyle);
            toolbar.Controls.Add(btnPretty2);
            toolbar.Controls.Add(btnPretty4);
            toolbar.Controls.Add(btnTabs);
            toolbar.Controls.Add(btnMinified);
            toolbar.Controls.Add(btnTree);

            stats = new Label
            {
                Dock = DockStyle.Top, Height = 26,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 6, 0, 0),
                Text = ""
            };

            viewHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

            txtOutput = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new Font("Consolas", 9.5F),
                BackColor = Color.White
            };
            tree = new TreeView
            {
                Dock = DockStyle.Fill, BackColor = Color.White,
                Font = new Font("Consolas", 9.5F),
                HideSelection = false, ShowNodeToolTips = true,
                BorderStyle = BorderStyle.None,
                DrawMode = TreeViewDrawMode.OwnerDrawText
            };
            tree.DrawNode += OnTreeDrawNode;
            viewHost.Controls.Add(txtOutput);
            viewHost.Controls.Add(tree);
            tree.Visible = false;

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

            Controls.Add(viewHost);
            Controls.Add(bottom);
            Controls.Add(stats);
            Controls.Add(toolbar);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        Button StyleButton(string text, Style style, int left)
        {
            var width = style == Style.Tree ? 80 : (style == Style.Tabs ? 80 : (style == Style.Minified ? 100 : 110));
            var b = new Button
            {
                Text = text, Left = left, Top = 4, Width = width, Height = 28,
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
                if (style == Style.Tree)
                {
                    PopulateTree();
                    tree.Visible = true;
                    txtOutput.Visible = false;
                }
                else
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
                    txtOutput.Visible = true;
                    tree.Visible = false;
                }
                stats.Text = StatsForCurrent() + "   ·   view: " + StyleLabel(style);
                HighlightActive();
            }
            finally { Cursor = Cursors.Default; }
        }

        string StatsForCurrent()
        {
            if (currentStyle == Style.Tree)
            {
                int count = CountNodes(tree.Nodes);
                return count.ToString("N0") + " tree nodes";
            }
            return CharsLines(txtOutput.Text);
        }

        static int CountNodes(TreeNodeCollection nodes)
        {
            int n = 0;
            foreach (TreeNode t in nodes) { n++; n += CountNodes(t.Nodes); }
            return n;
        }

        void PopulateTree()
        {
            tree.BeginUpdate();
            tree.Nodes.Clear();
            try
            {
                var root = JsonParser.Parse(originalJson);
                var rootNode = tree.Nodes.Add(DescribeRoot(root));
                rootNode.Tag = root;
                AppendChildren(rootNode, root);
                rootNode.Expand();
                if (rootNode.Nodes.Count > 0 && rootNode.Nodes.Count <= 20) rootNode.ExpandAll();
                else if (rootNode.Nodes.Count > 0) rootNode.Nodes[0].EnsureVisible();
            }
            catch (Exception ex)
            {
                var err = tree.Nodes.Add("Parse error: " + ex.Message);
                err.ForeColor = Color.FromArgb(160, 30, 30);
            }
            tree.EndUpdate();
        }

        static string DescribeRoot(JsonParser.JsonNode node)
        {
            if (node is JsonParser.JsonObject || node is JsonParser.JsonArray)
                return "(root)    " + node.Summary;
            return "(root)    " + node.Summary;
        }

        static void AppendChildren(TreeNode parent, JsonParser.JsonNode node)
        {
            var obj = node as JsonParser.JsonObject;
            if (obj != null)
            {
                foreach (var kv in obj.Members)
                {
                    var child = parent.Nodes.Add(FormatMember(kv.Key, kv.Value));
                    child.Tag = kv.Value;
                    if (IsContainer(kv.Value)) AppendChildren(child, kv.Value);
                }
                return;
            }
            var arr = node as JsonParser.JsonArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Items.Count; i++)
                {
                    var item = arr.Items[i];
                    var child = parent.Nodes.Add(FormatMember("[" + i + "]", item));
                    child.Tag = item;
                    if (IsContainer(item)) AppendChildren(child, item);
                }
            }
        }

        static bool IsContainer(JsonParser.JsonNode n)
        {
            return n is JsonParser.JsonObject || n is JsonParser.JsonArray;
        }

        static string FormatMember(string key, JsonParser.JsonNode value)
        {
            // For containers, show the summary after the key; for scalars, inline the value.
            if (value is JsonParser.JsonObject || value is JsonParser.JsonArray)
                return key + "   " + value.Summary;
            return key + " : " + value.Summary;
        }

        void OnTreeDrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            // Custom colouring: left of " : " or " " gets KeyColor; the value part is tinted by its JSON type.
            var text = e.Node.Text ?? "";
            var rect = e.Bounds;
            if (rect.Width <= 0 || rect.Height <= 0) { e.DrawDefault = true; return; }

            bool selected = (e.State & TreeNodeStates.Selected) != 0;
            var bgBrush = selected ? SystemBrushes.Highlight : SystemBrushes.Window;
            e.Graphics.FillRectangle(bgBrush, rect);

            // Split into key / value — matches "key : value" first, else "key   summary" with multiple spaces.
            string left = text, right = "";
            int sepIdx = text.IndexOf(" : ", StringComparison.Ordinal);
            string sep = " : ";
            if (sepIdx < 0)
            {
                sepIdx = text.IndexOf("   ", StringComparison.Ordinal);
                sep = "   ";
            }
            if (sepIdx >= 0)
            {
                left = text.Substring(0, sepIdx);
                right = text.Substring(sepIdx + sep.Length);
            }

            var font = e.Node.TreeView.Font;
            var keyBrush   = selected ? (Brush)SystemBrushes.HighlightText : new SolidBrush(KeyColor);
            var valueBrush = selected ? (Brush)SystemBrushes.HighlightText : new SolidBrush(ColourForValueText(right, e.Node.Tag as JsonParser.JsonNode));

            float x = rect.X + 2;
            e.Graphics.DrawString(left, font, keyBrush, x, rect.Y);
            if (right.Length > 0)
            {
                var leftSize = e.Graphics.MeasureString(left, font);
                e.Graphics.DrawString(sep, font, selected ? (Brush)SystemBrushes.HighlightText : new SolidBrush(MutedColor),
                    x + leftSize.Width, rect.Y);
                var sepSize = e.Graphics.MeasureString(sep, font);
                e.Graphics.DrawString(right, font, valueBrush,
                    x + leftSize.Width + sepSize.Width, rect.Y);
            }

            if (!selected)
            {
                if (!object.ReferenceEquals(keyBrush,   SystemBrushes.HighlightText)) ((SolidBrush)keyBrush).Dispose();
                if (!object.ReferenceEquals(valueBrush, SystemBrushes.HighlightText)) ((SolidBrush)valueBrush).Dispose();
            }
        }

        static Color ColourForValueText(string value, JsonParser.JsonNode node)
        {
            if (node is JsonParser.JsonString) return StringColor;
            if (node is JsonParser.JsonNumber) return NumberColor;
            if (node is JsonParser.JsonBool)   return NumberColor;
            if (node is JsonParser.JsonNull)   return NullColor;
            if (node is JsonParser.JsonObject || node is JsonParser.JsonArray) return SummaryColor;
            // fallback: guess by shape
            if (value.StartsWith("\"", StringComparison.Ordinal)) return StringColor;
            if (value == "null") return NullColor;
            if (value == "true" || value == "false") return NumberColor;
            return MutedColor;
        }

        static string StyleLabel(Style s)
        {
            switch (s)
            {
                case Style.Pretty2:  return "pretty, 2-space indent";
                case Style.Pretty4:  return "pretty, 4-space indent";
                case Style.Tabs:     return "pretty, tab indent";
                case Style.Minified: return "minified (single line)";
                case Style.Tree:     return "tree";
                default:             return "?";
            }
        }

        void HighlightActive()
        {
            SetActive(btnPretty2,  currentStyle == Style.Pretty2);
            SetActive(btnPretty4,  currentStyle == Style.Pretty4);
            SetActive(btnTabs,     currentStyle == Style.Tabs);
            SetActive(btnMinified, currentStyle == Style.Minified);
            SetActive(btnTree,     currentStyle == Style.Tree);
        }

        static void SetActive(Button b, bool active)
        {
            b.Font = new Font("Segoe UI", 9F, active ? FontStyle.Bold : FontStyle.Regular);
        }

        static string CharsLines(string json)
        {
            int chars = json == null ? 0 : json.Length;
            int lines = 1;
            if (!string.IsNullOrEmpty(json))
                for (int i = 0; i < json.Length; i++) if (json[i] == '\n') lines++;
            return chars.ToString("N0") + " chars, " + lines.ToString("N0") + " lines";
        }

        void CopyToClipboard()
        {
            // In tree mode, copy the current pretty-2sp rendering so the clipboard is useful.
            string payload = currentStyle == Style.Tree
                ? JsonFormatter.Pretty(originalJson, "  ")
                : txtOutput.Text;
            if (string.IsNullOrEmpty(payload)) return;
            try { Clipboard.SetText(payload); } catch { }
        }

        void SaveAs()
        {
            // Tree mode saves the pretty-2sp version; text modes save whatever's on screen.
            var payload = currentStyle == Style.Tree
                ? JsonFormatter.Pretty(originalJson, "  ")
                : txtOutput.Text;
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
                    File.WriteAllText(dlg.FileName, payload);
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
