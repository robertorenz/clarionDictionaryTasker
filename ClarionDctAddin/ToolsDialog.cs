using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // A browse of every planned dictionary-level tool. Implemented tools open
    // their UI directly; planned-but-unimplemented ones show a modal with the
    // tool's description so we can fill them in iteratively.
    internal class ToolsDialog : Form
    {
        static readonly Color BgColor      = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor   = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor  = Color.FromArgb(45,  90, 135);
        static readonly Color SectionColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor   = Color.FromArgb(120, 135, 150);

        readonly object dict;

        sealed class ToolDef
        {
            public string Name;
            public string Description;
            public bool   Implemented;
            public Action OnClick;
        }

        public ToolsDialog(object dict)
        {
            this.dict = dict;
            BuildUi();
        }

        void BuildUi()
        {
            Text = "Dictionary tools";
            Width = 1040;
            Height = 760;
            MinimumSize = new Size(720, 460);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;
            ShowIcon = false;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
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
                Text = "Dictionary tools   " + DictModel.GetDictionaryName(dict)
            };

            var body = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 16, 20, 16),
                BackColor = BgColor,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };

            body.Controls.Add(MakeSection("Validation & lint", new[]
            {
                new ToolDef { Name = "Lint report",
                              Description = "Scan for missing primary keys, empty tables, orphaned relations, duplicate keys, undocumented fields.",
                              Implemented = true,
                              OnClick = delegate { OpenLint(); } },
                new ToolDef { Name = "Picture consistency",
                              Description = "Flag fields where a DATE doesn't use @d*, money fields without @n$*.*, STRING without @s*." },
                new ToolDef { Name = "Naming conventions",
                              Description = "Configurable rules: prefixes must be uppercase 2-4 chars, labels snake_case, no whitespace, etc." },
            }));

            body.Controls.Add(MakeSection("Analysis & stats", new[]
            {
                new ToolDef { Name = "Health dashboard",
                              Description = "Table count, field-per-table distribution, relation density, driver mix, largest tables." },
                new ToolDef { Name = "Dead tables report",
                              Description = "Tables with no relations and no references anywhere — candidates for deletion." },
                new ToolDef { Name = "Duplicate fields",
                              Description = "Fields with identical label + type + size appearing in many tables — candidates for extraction." },
            }));

            body.Controls.Add(MakeSection("Search & navigation", new[]
            {
                new ToolDef { Name = "Global search",
                              Description = "Ctrl-F across tables, fields, keys, relations, triggers. Results jump straight to the item." },
                new ToolDef { Name = "Where used",
                              Description = "Pick a field — see every key, relation, and trigger that references it." },
                new ToolDef { Name = "Path finder",
                              Description = "\"How is CLIENTES related to INVOICES?\" BFS through relations for the shortest path." },
            }));

            body.Controls.Add(MakeSection("Compare & diff", new[]
            {
                new ToolDef { Name = "Compare dictionaries",
                              Description = "Open two .DCTs, structural diff — added/removed tables, changed fields, renamed items. Exportable report." },
                new ToolDef { Name = "Compare tables",
                              Description = "Diff two tables within one dictionary. Useful for CLIENTES vs CLIENTES_ARCHIVO." },
            }));

            body.Controls.Add(MakeSection("Generation & export", new[]
            {
                new ToolDef { Name = "SQL DDL export",
                              Description = "Generate CREATE TABLE + indexes for SQL Server / Postgres / SQLite. Respects per-table driver." },
                new ToolDef { Name = "Model classes (C#)",
                              Description = "Emit a C# class per table with properties typed from Clarion types. API-ready POCOs." },
                new ToolDef { Name = "Model classes (TypeScript)",
                              Description = "Same idea, TypeScript interfaces — usable from front-end code and OpenAPI generation." },
                new ToolDef { Name = "Markdown documentation",
                              Description = "Per-table .md reference pages: fields table, keys, relations, sample JSON. Browseable intranet documentation." },
            }));

            body.Controls.Add(MakeSection("Refactoring", new[]
            {
                new ToolDef { Name = "Safe rename field",
                              Description = "Rename a field's label and auto-update every key component, relation mapping, and trigger that referenced it." },
                new ToolDef { Name = "Batch rename (regex)",
                              Description = "Pattern find/replace across field labels, descriptions, headings, prompts." },
                new ToolDef { Name = "Batch retype fields",
                              Description = "Select every field matching a name pattern, change type / size / picture in one shot." },
                new ToolDef { Name = "Standard audit pack",
                              Description = "Preset: adds guid, CreatedOn/By, ModifiedOn/By, DeletedOn + unique key + triggers to every selected table." },
            }));

            body.Controls.Add(MakeSection("Visualization", new[]
            {
                new ToolDef { Name = "Export relations map",
                              Description = "Render the current relations-tab layout as SVG or PDF — suitable for a printable poster." },
            }));

            body.Controls.Add(MakeSection("Enterprise glue", new[]
            {
                new ToolDef { Name = "Git commit hook",
                              Description = "After save, if the DCT is in a repo, auto-commit with a generated message based on the diff from the previous version." },
                new ToolDef { Name = "Change-log generator",
                              Description = "Compare the current DCT to a previous save-point, emit a human-readable changelog." },
            }));

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(btnClose);

            Controls.Add(body);
            Controls.Add(bottom);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        Control MakeSection(string title, ToolDef[] tools)
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 10),
                BackColor = BgColor
            };
            var heading = new Label
            {
                Text = title.ToUpper(),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9F),
                ForeColor = SectionColor,
                Margin = new Padding(2, 0, 0, 6)
            };
            panel.Controls.Add(heading);

            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0),
                BackColor = BgColor
            };
            var tips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400, ShowAlways = true };
            foreach (var tool in tools)
            {
                var btn = new Button
                {
                    Text = tool.Name + (tool.Implemented ? "" : "   (planned)"),
                    Width = 210,
                    Height = 32,
                    FlatStyle = FlatStyle.System,
                    Margin = new Padding(0, 0, 10, 6),
                    Font = new Font("Segoe UI", 9F),
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = tool.Implemented ? SystemColors.ControlText : MutedColor,
                };
                tips.SetToolTip(btn, tool.Description);
                var captured = tool;
                btn.Click += delegate
                {
                    if (captured.Implemented && captured.OnClick != null) captured.OnClick();
                    else ShowPlanned(captured);
                };
                row.Controls.Add(btn);
            }
            panel.Controls.Add(row);
            return panel;
        }

        void ShowPlanned(ToolDef tool)
        {
            MessageBox.Show(this,
                tool.Description +
                "\r\n\r\nThis tool is planned but not yet implemented.",
                tool.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void OpenLint()
        {
            Hide();
            using (var dlg = new LintReportDialog(dict, null)) dlg.ShowDialog(this);
            Show();
        }
    }
}
