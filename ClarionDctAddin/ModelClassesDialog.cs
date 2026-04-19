using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Unified preview dialog for both C# and TypeScript model-class output.
    // Wired up twice from ToolsDialog (once per language) but shares the code.
    internal class ModelClassesDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);

        readonly object dict;
        readonly string initialLanguage;
        ComboBox cbLanguage;
        TextBox  txtNamespace, txtOutput;
        CheckBox chkDescriptions;
        bool inSetup = true;

        public ModelClassesDialog(object dict, string language)
        {
            this.dict = dict;
            this.initialLanguage = string.Equals(language, "typescript", StringComparison.OrdinalIgnoreCase)
                ? "typescript" : "csharp";
            BuildUi();
            inSetup = false;
            Regenerate();
        }

        void BuildUi()
        {
            Text = "Model classes - " + DictModel.GetDictionaryName(dict);
            Width = 1120; Height = 740;
            MinimumSize = new Size(820, 460);
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
                Text = "Model classes   " + DictModel.GetDictionaryName(dict)
            };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = BgColor, Padding = new Padding(16, 10, 16, 6) };
            var lblLang = new Label { Text = "Language:", Left = 0, Top = 10, Width = 70, Font = new Font("Segoe UI", 9F) };
            cbLanguage = new ComboBox { Left = 72, Top = 6, Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            cbLanguage.Items.Add("C#");
            cbLanguage.Items.Add("TypeScript");
            cbLanguage.SelectedIndex = initialLanguage == "typescript" ? 1 : 0;
            var lblNs = new Label { Text = "Namespace:", Left = 232, Top = 10, Width = 80, Font = new Font("Segoe UI", 9F) };
            txtNamespace = new TextBox { Left = 314, Top = 6, Width = 240, Text = "ClarionModels", Font = new Font("Segoe UI", 9F) };
            chkDescriptions = new CheckBox { Text = "Include descriptions", Left = 574, Top = 8, AutoSize = true, Checked = true, Font = new Font("Segoe UI", 9F) };
            toolbar.Controls.Add(lblLang);
            toolbar.Controls.Add(cbLanguage);
            toolbar.Controls.Add(lblNs);
            toolbar.Controls.Add(txtNamespace);
            toolbar.Controls.Add(chkDescriptions);

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
            Controls.Add(toolbar);
            Controls.Add(header);
            CancelButton = btnClose;

            cbLanguage.SelectedIndexChanged += delegate { if (!inSetup) Regenerate(); };
            txtNamespace.TextChanged        += delegate { if (!inSetup) Regenerate(); };
            chkDescriptions.CheckedChanged  += delegate { if (!inSetup) Regenerate(); };
        }

        void Regenerate()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                var opt = new ModelClassesGenerator.Options
                {
                    Namespace = string.IsNullOrEmpty(txtNamespace.Text) ? "ClarionModels" : txtNamespace.Text,
                    IncludeDescriptions = chkDescriptions.Checked
                };
                txtOutput.Text = cbLanguage.SelectedIndex == 1
                    ? ModelClassesGenerator.GenerateTypeScript(dict, opt)
                    : ModelClassesGenerator.GenerateCSharp(dict, opt);
                txtOutput.SelectionStart = 0;
                txtOutput.SelectionLength = 0;
            }
            catch (Exception ex)
            {
                txtOutput.Text = "// Error: " + ex.Message;
            }
            finally { Cursor = Cursors.Default; }
        }

        void CopyToClipboard()
        {
            if (string.IsNullOrEmpty(txtOutput.Text)) return;
            try { Clipboard.SetText(txtOutput.Text); } catch { }
        }

        void SaveAs()
        {
            bool ts = cbLanguage.SelectedIndex == 1;
            var filter = ts
                ? "TypeScript files (*.ts)|*.ts|All files (*.*)|*.*"
                : "C# files (*.cs)|*.cs|All files (*.*)|*.*";
            var ext = ts ? ".ts" : ".cs";
            var suggested = (DictModel.GetDictionaryName(dict) ?? "dict") + "-models" + ext;
            using (var dlg = new SaveFileDialog { Filter = filter, FileName = suggested })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, txtOutput.Text);
                    MessageBox.Show(this, "Saved: " + dlg.FileName, "Model classes",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Save failed: " + ex.Message, "Model classes",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
