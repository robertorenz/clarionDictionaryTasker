using System;
using System.Data.SqlClient;
using System.Drawing;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Prompt for MSSQL connection parameters when the dict's Owner string
    // can't be parsed (typically because it's a Clarion runtime variable like
    // "!glo:owner"). Seeded from whatever the user last saved for this dict;
    // saves back on OK so subsequent View-data calls skip the prompt.
    internal class SqlConnectionPromptDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        public string ResultConnectionString { get; private set; }

        TextBox  txtServer, txtDatabase, txtUser, txtPassword;
        CheckBox chkIntegrated;
        Label    lblHint;

        public SqlConnectionPromptDialog(string dictName, string initialConnStr)
        {
            Text = "SQL connection - " + (string.IsNullOrEmpty(dictName) ? "dictionary" : dictName);
            Width = 520; Height = 360;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = BgColor;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ShowIcon = false; ShowInTaskbar = false;

            var header = new Label
            {
                Dock = DockStyle.Top, Height = 44,
                BackColor = HeaderColor, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Text = "MSSQL connection"
            };

            var body = new Panel { Dock = DockStyle.Fill, BackColor = BgColor, Padding = new Padding(18, 14, 18, 12) };

            lblHint = new Label
            {
                Left = 0, Top = 0, Width = 470,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor, AutoSize = false, Height = 34,
                Text = "The table's Owner string is a Clarion runtime variable, so the add-in can't read "
                     + "the connection from the dict. Enter it here — saved per-dict for next time."
            };
            body.Controls.Add(lblHint);

            int y = 46, lblW = 90, ctlW = 340, lineH = 32;
            body.Controls.Add(new Label { Text = "Server:",   Left = 0, Top = y + 4, Width = lblW, Font = new Font("Segoe UI", 9.5F) });
            txtServer = new TextBox { Left = lblW, Top = y, Width = ctlW, Font = new Font("Segoe UI", 10F) };
            body.Controls.Add(txtServer);
            y += lineH;

            body.Controls.Add(new Label { Text = "Database:", Left = 0, Top = y + 4, Width = lblW, Font = new Font("Segoe UI", 9.5F) });
            txtDatabase = new TextBox { Left = lblW, Top = y, Width = ctlW, Font = new Font("Segoe UI", 10F) };
            body.Controls.Add(txtDatabase);
            y += lineH;

            chkIntegrated = new CheckBox
            {
                Text = "Use Windows (Integrated) authentication",
                Left = lblW, Top = y, AutoSize = true, Font = new Font("Segoe UI", 9.5F)
            };
            chkIntegrated.CheckedChanged += delegate { UpdateAuthEnabled(); };
            body.Controls.Add(chkIntegrated);
            y += lineH;

            body.Controls.Add(new Label { Text = "User:",     Left = 0, Top = y + 4, Width = lblW, Font = new Font("Segoe UI", 9.5F) });
            txtUser = new TextBox { Left = lblW, Top = y, Width = ctlW, Font = new Font("Segoe UI", 10F) };
            body.Controls.Add(txtUser);
            y += lineH;

            body.Controls.Add(new Label { Text = "Password:", Left = 0, Top = y + 4, Width = lblW, Font = new Font("Segoe UI", 9.5F) });
            txtPassword = new TextBox { Left = lblW, Top = y, Width = ctlW, UseSystemPasswordChar = true, Font = new Font("Segoe UI", 10F) };
            body.Controls.Add(txtPassword);
            y += lineH;

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnCancel = new Button { Text = "Cancel", Width = 100, Height = 30, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            var btnTest   = new Button { Text = "Test",   Width = 80,  Height = 30, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnTest.Click += delegate { TestConnection(); };
            var btnOk     = new Button { Text = "Save",   Width = 100, Height = 30, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnOk.Click += delegate { Commit(); };
            bottom.Controls.Add(btnCancel);
            bottom.Controls.Add(btnTest);
            bottom.Controls.Add(btnOk);

            Controls.Add(body);
            Controls.Add(bottom);
            Controls.Add(header);
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            SeedFromInitial(initialConnStr);
            UpdateAuthEnabled();
        }

        void SeedFromInitial(string connStr)
        {
            if (string.IsNullOrEmpty(connStr)) return;
            try
            {
                var b = new SqlConnectionStringBuilder(connStr);
                txtServer.Text    = b.DataSource     ?? "";
                txtDatabase.Text  = b.InitialCatalog ?? "";
                chkIntegrated.Checked = b.IntegratedSecurity;
                if (!b.IntegratedSecurity)
                {
                    txtUser.Text     = b.UserID   ?? "";
                    txtPassword.Text = b.Password ?? "";
                }
            }
            catch { /* ignore invalid stored value */ }
        }

        void UpdateAuthEnabled()
        {
            txtUser.Enabled     = !chkIntegrated.Checked;
            txtPassword.Enabled = !chkIntegrated.Checked;
        }

        string BuildConnectionString()
        {
            var b = new SqlConnectionStringBuilder
            {
                DataSource      = (txtServer.Text   ?? "").Trim(),
                InitialCatalog  = (txtDatabase.Text ?? "").Trim(),
                ConnectTimeout  = 10,
                ApplicationName = "Dictionary Tasker"
            };
            if (chkIntegrated.Checked)
            {
                b.IntegratedSecurity = true;
            }
            else
            {
                b.UserID   = (txtUser.Text ?? "").Trim();
                b.Password = txtPassword.Text ?? "";
            }
            return b.ConnectionString;
        }

        void Commit()
        {
            if (string.IsNullOrWhiteSpace(txtServer.Text) || string.IsNullOrWhiteSpace(txtDatabase.Text))
            {
                MessageBox.Show(this, "Server and Database are both required.",
                    "SQL connection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ResultConnectionString = BuildConnectionString();
            DialogResult = DialogResult.OK;
            Close();
        }

        void TestConnection()
        {
            var connStr = BuildConnectionString();
            Cursor = Cursors.WaitCursor;
            try
            {
                using (var conn = new SqlConnection(connStr))
                {
                    conn.Open();
                    MessageBox.Show(this,
                        "Connected successfully to " + conn.DataSource + " / " + conn.Database + ".",
                        "Test connection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Test connection failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; }
        }
    }
}
