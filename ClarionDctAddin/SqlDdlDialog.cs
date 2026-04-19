using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    internal class SqlDdlDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);

        readonly object dict;
        readonly object singleTable;

        ComboBox cboDialect;
        CheckBox chkDrop;
        CheckBox chkIndexes;
        CheckBox chkComments;
        CheckBox chkFullPath;
        TextBox  txtOutput;

        bool inSetup = true;

        public SqlDdlDialog(object dict) : this(dict, null) { }

        public SqlDdlDialog(object dict, object singleTable)
        {
            this.dict = dict;
            this.singleTable = singleTable;
            BuildUi();
            inSetup = false;
            Regenerate();
        }

        void BuildUi()
        {
            var singleLabel = singleTable == null ? "" :
                (DictModel.AsString(DictModel.GetProp(singleTable, "Label")) ?? "");
            Text = singleTable == null
                ? "SQL DDL export - " + DictModel.GetDictionaryName(dict)
                : "SQL DDL - " + singleLabel;
            Width = 1080;
            Height = 720;
            MinimumSize = new Size(820, 480);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            SizeGripStyle = SizeGripStyle.Show;

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = HeaderColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Text = singleTable == null
                    ? "SQL DDL export   " + DictModel.GetDictionaryName(dict)
                    : "SQL DDL   table: " + singleLabel + "     dict: " + DictModel.GetDictionaryName(dict)
            };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = BgColor, Padding = new Padding(16, 10, 16, 10) };

            var lblDialect = new Label { Text = "Dialect:", Top = 10, Left = 0, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            cboDialect = new ComboBox
            {
                Top = 6, Left = 60, Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F)
            };
            cboDialect.Items.AddRange(new object[] { "SQL Server", "PostgreSQL", "SQLite", "MySQL", "MariaDB" });
            // Load the remembered dialect as the starting selection.
            int preferredIdx = (int)Settings.PreferredDialect;
            cboDialect.SelectedIndex = (preferredIdx >= 0 && preferredIdx < cboDialect.Items.Count) ? preferredIdx : 0;
            cboDialect.SelectedIndexChanged += delegate
            {
                if (inSetup) return;
                // Persist the user's choice so every future export defaults to it.
                Settings.PreferredDialect = (SqlDdlGenerator.Dialect)cboDialect.SelectedIndex;
                Regenerate();
            };

            chkDrop     = MakeCheck("Drop if exists",     240, 10, true);
            chkIndexes  = MakeCheck("Include indexes",    390, 10, true);
            chkComments = MakeCheck("Include comments",   540, 10, true);
            chkFullPath = MakeCheck("Use full path name", 700, 10, true);

            var btnRegen = new Button { Text = "Regenerate", Top = 4, Width = 110, Height = 30, FlatStyle = FlatStyle.System, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnRegen.Left = toolbar.ClientSize.Width - btnRegen.Width - 16;
            btnRegen.Click += delegate { Regenerate(); };
            toolbar.Resize += delegate { btnRegen.Left = toolbar.ClientSize.Width - btnRegen.Width - 16; };

            toolbar.Controls.Add(lblDialect);
            toolbar.Controls.Add(cboDialect);
            toolbar.Controls.Add(chkDrop);
            toolbar.Controls.Add(chkIndexes);
            toolbar.Controls.Add(chkComments);
            toolbar.Controls.Add(chkFullPath);
            toolbar.Controls.Add(btnRegen);

            txtOutput = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9.5F),
                BackColor = Color.White
            };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close",  Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
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
        }

        CheckBox MakeCheck(string text, int left, int top, bool on)
        {
            var c = new CheckBox
            {
                Text = text,
                Left = left, Top = top,
                Width = 150, Height = 22,
                Checked = on,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F)
            };
            c.CheckedChanged += delegate { if (!inSetup) Regenerate(); };
            return c;
        }

        void Regenerate()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                var opt = new SqlDdlGenerator.Options
                {
                    Dialect          = (SqlDdlGenerator.Dialect)cboDialect.SelectedIndex,
                    IncludeDropTable = chkDrop.Checked,
                    IncludeIndexes   = chkIndexes.Checked,
                    IncludeComments  = chkComments.Checked,
                    UseFullPathName  = chkFullPath.Checked,
                };
                txtOutput.Text = singleTable != null
                    ? SqlDdlGenerator.GenerateForTable(singleTable, opt)
                    : SqlDdlGenerator.Generate(dict, opt);
                txtOutput.SelectionStart = 0;
                txtOutput.SelectionLength = 0;
            }
            catch (Exception ex)
            {
                txtOutput.Text = "-- Error while generating DDL:\r\n-- " + ex.Message + "\r\n-- " + ex.StackTrace;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        void CopyToClipboard()
        {
            if (string.IsNullOrEmpty(txtOutput.Text)) return;
            try
            {
                Clipboard.SetText(txtOutput.Text);
                MessageBox.Show(this, "Copied " + txtOutput.Text.Length.ToString("N0") + " characters to clipboard.",
                    "SQL DDL export", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy: " + ex.Message,
                    "SQL DDL export", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void SaveAs()
        {
            var suggested = (singleTable == null
                ? DictModel.GetDictionaryName(dict)
                : DictModel.AsString(DictModel.GetProp(singleTable, "Label")) ?? "table") + ".sql";
            using (var dlg = new SaveFileDialog
            {
                Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*",
                FileName = suggested
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, txtOutput.Text);
                    MessageBox.Show(this, "Saved: " + dlg.FileName,
                        "SQL DDL export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Save failed: " + ex.Message,
                        "SQL DDL export", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
