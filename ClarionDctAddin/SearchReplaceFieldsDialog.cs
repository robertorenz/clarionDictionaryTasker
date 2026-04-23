using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Dictionary-wide Search and Replace on the ExternalName attribute of
    // fields. Motivated by Bruce's new object-based drivers: rewrite the
    // pipe-separated "Label | ATTR | KEY=VAL" extended name in bulk, e.g.
    // stamp NOTNULL + DEFAULT=0 onto every DATE field in one pass.
    //
    // Flow is identical to every other batch dialog in the add-in:
    //   1. user defines match criteria (data type / label / table / extname)
    //   2. user defines replacement rules (base-name rule + per-attribute
    //      Set / Remove rows, plus Additive vs Rewrite merge mode)
    //   3. Preview lists every field + Before/After ExternalName
    //   4. Apply runs through FieldMutator with a .DCT backup
    internal class SearchReplaceFieldsDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color WarningBg   = Color.FromArgb(255, 247, 225);
        static readonly Color WarningFg   = Color.FromArgb(120,  80, 10);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);
        static readonly Color SectionColor= Color.FromArgb(45,  90, 135);

        readonly object dict;

        // Match pane
        CheckedListBox clbTypes;
        TextBox  txtLabelPattern;
        TextBox  txtTableFilter;
        TextBox  txtExtNamePattern;
        CheckBox chkIgnoreCase;
        CheckBox chkExcludeAliases;

        // Replace pane
        RadioButton rbBaseLeave;
        RadioButton rbBaseLabel;
        RadioButton rbBasePrefixColon;
        RadioButton rbBasePrefixUnderscore;
        RadioButton rbBaseClear;
        RadioButton rbBaseCustom;
        TextBox     txtBaseCustomTemplate;

        RadioButton rbMergeAdditive;
        RadioButton rbMergeRewrite;

        FlowLayoutPanel pnlAttrRules;
        Button          btnAddRule;

        // Preview/results
        ListView lvPreview;
        Label    lblPreviewSummary;
        Button   btnPreview;
        Button   btnApply;

        readonly List<PlanItem> currentPlan = new List<PlanItem>();

        sealed class PlanItem
        {
            public object Field;
            public string TableName;
            public string FieldLabel;
            public string DataType;
            public string BeforeExt;
            public string AfterExt;
        }

        // One row in the attribute-rule grid.
        sealed class AttrRuleRow : Panel
        {
            public ComboBox CboAttr;
            public ComboBox CboAction;
            public TextBox  TxtValue;
            public Button   BtnRemove;

            public string Attr     { get { return (CboAttr.Text   ?? "").Trim(); } }
            public string Action   { get { return (CboAction.SelectedItem as string) ?? "Set"; } }
            public string Value    { get { return TxtValue.Text    ?? ""; } }
        }

        // Attribute catalog: name + one-line help + template the textbox is
        // seeded with when the user picks this entry from the dropdown.
        sealed class AttrDef
        {
            public string Name;
            public string Template;
            public string Help;
        }

        static readonly AttrDef[] Catalog = new[]
        {
            new AttrDef { Name = "BINARY",        Template = "BINARY",        Help = "Field will hold binary data (STRING/PSTRING, not CSTRING). Driver takes this into account on CREATE." },
            new AttrDef { Name = "BOOLEAN",       Template = "BOOLEAN",       Help = "Object-based drivers. String-typed fields must still hold '0'/'1'. Unnecessary if SQLTYPE=BOOLEAN/BIT." },
            new AttrDef { Name = "CASE",          Template = "CASE",          Help = "Create with case-sensitive collation. Ignored if COLLATE or BINARY is set." },
            new AttrDef { Name = "CHECK",         Template = "CHECK",         Help = "See VALIDATE. Server-side CHECK constraint." },
            new AttrDef { Name = "CHECKFORNULL",  Template = "CHECKFORNULL",  Help = "Traditional. Alters WHERE clauses so rows with NULLs are included in results." },
            new AttrDef { Name = "COLLATE=",      Template = "COLLATE=",      Help = "Object-based. COLLATE=xxx determines sort/compare rules, trailing-space handling, case sensitivity." },
            new AttrDef { Name = "DEFAULT=",      Template = "DEFAULT=",      Help = "Server-side default on CREATE. Prefix a function with '!', e.g. DEFAULT=!gen_random_uuid(). No quotes." },
            new AttrDef { Name = "ISIDENTITY",    Template = "ISIDENTITY",    Help = "Server-side auto-increment. Field excluded from ADD/APPEND/PUT/UPSERT." },
            new AttrDef { Name = "NOCASE",        Template = "NOCASE",        Help = "Create with case-insensitive collation. Ignored if COLLATE or BINARY is set." },
            new AttrDef { Name = "NOTNULL",       Template = "NOTNULL",       Help = "CREATE sets this column NOT NULL. Usually paired with DEFAULT=." },
            new AttrDef { Name = "NOWHERE",       Template = "NOWHERE",       Help = "Traditional. Manually advise the driver that the back-end can't put this type in a WHERE clause." },
            new AttrDef { Name = "READONLY",      Template = "READONLY",      Help = "Excluded from ADD/APPEND/PUT/UPSERT. Buffer refreshed from DB on ADD/PUT/UPSERT." },
            new AttrDef { Name = "REQ",           Template = "REQ",           Help = "Equivalent to NOTNULL and <> 0 or <> ''." },
            new AttrDef { Name = "SELECTNAME=",   Template = "SELECTNAME=",   Help = "Use this name on SELECT instead of External Name / Label. No quotes." },
            new AttrDef { Name = "UPDATENAME=",   Template = "UPDATENAME=",   Help = "Use this name on UPDATE instead of External Name / Label. No quotes." },
            new AttrDef { Name = "INSERTNAME=",   Template = "INSERTNAME=",   Help = "Use this name on INSERT instead of External Name / Label. No quotes." },
            new AttrDef { Name = "SQLTYPE=",      Template = "SQLTYPE=",      Help = "Force a specific DB type on CREATE. Generic UUID or STRING get translated per driver. No quotes." },
            new AttrDef { Name = "UUID4",         Template = "UUID4",         Help = "Auto-fill a UUID v4 on ADD/APPEND when blank." },
            new AttrDef { Name = "UUID7",         Template = "UUID7",         Help = "Auto-fill a UUID v7 on ADD/APPEND when blank." },
            new AttrDef { Name = "UUID8",         Template = "UUID8",         Help = "Auto-fill a UUID v8 on ADD/APPEND when blank." },
            new AttrDef { Name = "VALIDATE()",    Template = "VALIDATE()",    Help = "Server-side CHECK expression included on CREATE, e.g. VALIDATE(LEN(name)>0)." },
            new AttrDef { Name = "WATCH",         Template = "WATCH",         Help = "Traditional. Only fields with WATCH are checked when writing a record with PUT." },
            new AttrDef { Name = "> n",           Template = "> ",            Help = "Server-side validation: value must be > n. Ignored on BOOLEAN." },
            new AttrDef { Name = ">= n",          Template = ">= ",           Help = "Server-side validation: value must be >= n." },
            new AttrDef { Name = "< n",           Template = "< ",            Help = "Server-side validation: value must be < n." },
            new AttrDef { Name = "<= n",          Template = "<= ",           Help = "Server-side validation: value must be <= n." },
            new AttrDef { Name = "<> n",          Template = "<> ",           Help = "Server-side validation: value must be <> n." },
            new AttrDef { Name = "Custom...",     Template = "",              Help = "Type any pipe-segment text. Inserted / removed verbatim." },
        };

        static readonly string[] KnownTypes = new[]
        {
            "BYTE", "SHORT", "USHORT", "LONG", "ULONG",
            "DATE", "TIME",
            "STRING", "CSTRING", "PSTRING",
            "REAL", "SREAL", "DECIMAL", "PDECIMAL",
            "MEMO", "BLOB", "GROUP", "LIKE", "USER"
        };

        public SearchReplaceFieldsDialog(object dict)
        {
            this.dict = dict;
            BuildUi();
        }

        void BuildUi()
        {
            Text = "Search and replace fields (Bruce's new drivers) - " + DictModel.GetDictionaryName(dict);
            Width = 1280;
            Height = 820;
            MinimumSize = new Size(1060, 620);
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
                Text = "Search and replace fields (Bruce's new drivers)   " + DictModel.GetDictionaryFileName(dict)
            };

            var warning = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = WarningBg,
                ForeColor = WarningFg,
                Font = new Font("Segoe UI", 9F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Text = "Rewrites the External Name (pipe-separated) on matching fields. A .DCT backup is written first; press Ctrl+S in Clarion to save."
            };

            // Body = TableLayout with Match on the left (45%) and Replace on the right (55%).
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = BgColor,
                Padding = new Padding(12)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 400));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            body.Controls.Add(BuildMatchPane(),   0, 0);
            body.Controls.Add(BuildReplacePane(), 1, 0);
            body.Controls.Add(BuildPreviewPane(), 0, 1);
            body.SetColumnSpan(body.GetControlFromPosition(0, 1), 2);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            btnApply = new Button { Text = "Apply plan...", Width = 150, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System, Enabled = false };
            btnApply.Click += delegate { Apply(); };
            btnPreview = new Button { Text = "Preview", Width = 110, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnPreview.Click += delegate { RefreshPreview(); };
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(btnApply);
            bottom.Controls.Add(btnPreview);

            Controls.Add(body);
            Controls.Add(bottom);
            Controls.Add(warning);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        Panel BuildMatchPane()
        {
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 8, 8), BackColor = BgColor };
            var grp = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "Match fields",
                Font = new Font("Segoe UI Semibold", 9F),
                BackColor = BgColor,
                Padding = new Padding(8, 4, 8, 8)
            };

            var lblTypes = new Label { Text = "Data types (none checked = any):", Left = 10, Top = 24, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            clbTypes = new CheckedListBox
            {
                Left = 10, Top = 46, Width = 220, Height = 180,
                CheckOnClick = true,
                Font = new Font("Consolas", 9.5F),
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };
            foreach (var t in KnownTypes) clbTypes.Items.Add(t);

            var lblL = new Label { Text = "Field label regex:", Left = 246, Top = 24, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            txtLabelPattern = new TextBox { Left = 246, Top = 44, Width = 240, Font = new Font("Consolas", 9.5F) };

            var lblT = new Label { Text = "Table filter (regex, blank = all):", Left = 246, Top = 76, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            txtTableFilter = new TextBox { Left = 246, Top = 96, Width = 240, Font = new Font("Consolas", 9.5F) };

            var lblE = new Label { Text = "Current External Name contains (regex, blank = any):", Left = 246, Top = 128, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            txtExtNamePattern = new TextBox { Left = 246, Top = 148, Width = 240, Font = new Font("Consolas", 9.5F) };

            chkIgnoreCase = new CheckBox
            {
                Text = "Case-insensitive regex",
                Left = 246, Top = 180, AutoSize = true, Checked = true,
                Font = new Font("Segoe UI", 9F)
            };
            chkExcludeAliases = new CheckBox
            {
                Text = "Exclude aliases",
                Left = 246, Top = 204, AutoSize = true, Checked = Settings.BatchExcludeAliases,
                Font = new Font("Segoe UI", 9F)
            };
            chkExcludeAliases.CheckedChanged += delegate { Settings.BatchExcludeAliases = chkExcludeAliases.Checked; };

            grp.Controls.Add(lblTypes);
            grp.Controls.Add(clbTypes);
            grp.Controls.Add(lblL);
            grp.Controls.Add(txtLabelPattern);
            grp.Controls.Add(lblT);
            grp.Controls.Add(txtTableFilter);
            grp.Controls.Add(lblE);
            grp.Controls.Add(txtExtNamePattern);
            grp.Controls.Add(chkIgnoreCase);
            grp.Controls.Add(chkExcludeAliases);

            host.Controls.Add(grp);
            return host;
        }

        Panel BuildReplacePane()
        {
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 8), BackColor = BgColor };
            var grp = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "Replacement rules",
                Font = new Font("Segoe UI Semibold", 9F),
                BackColor = BgColor,
                Padding = new Padding(8, 4, 8, 8)
            };

            var lblBase = new Label { Text = "Base name (first pipe segment):", Left = 10, Top = 24, AutoSize = true, Font = new Font("Segoe UI Semibold", 9F) };
            rbBaseLeave            = new RadioButton { Text = "Leave current",              Left = 10,  Top = 44, AutoSize = true, Checked = true, Font = new Font("Segoe UI", 9F) };
            rbBaseLabel            = new RadioButton { Text = "Use Label",                  Left = 130, Top = 44, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            rbBasePrefixColon      = new RadioButton { Text = "PREFIX:Label",               Left = 220, Top = 44, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            rbBasePrefixUnderscore = new RadioButton { Text = "prefix_label (lowercase)",   Left = 340, Top = 44, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            rbBaseClear            = new RadioButton { Text = "Clear",                      Left = 490, Top = 44, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            rbBaseCustom           = new RadioButton { Text = "Custom:",                    Left = 560, Top = 44, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            txtBaseCustomTemplate  = new TextBox
            {
                Left = 634, Top = 42, Width = 180,
                Font = new Font("Consolas", 9.5F),
                Text = "{prefix}_{label}"
            };
            var lblTplHelp = new Label
            {
                Left = 10, Top = 66, AutoSize = true,
                Text = "Custom tokens: {label}  {prefix}  {type}",
                ForeColor = MutedColor,
                Font = new Font("Segoe UI", 8.5F)
            };

            var lblAttrs = new Label { Text = "Attribute rules (Set / Remove):", Left = 10, Top = 96, AutoSize = true, Font = new Font("Segoe UI Semibold", 9F) };

            var hdr = new Panel { Left = 10, Top = 118, Width = 810, Height = 20, BackColor = BgColor };
            hdr.Controls.Add(new Label { Text = "Attribute",   Left = 6,   Top = 0, AutoSize = true, Font = new Font("Segoe UI", 8.5F), ForeColor = MutedColor });
            hdr.Controls.Add(new Label { Text = "Action",      Left = 206, Top = 0, AutoSize = true, Font = new Font("Segoe UI", 8.5F), ForeColor = MutedColor });
            hdr.Controls.Add(new Label { Text = "Value / token (exact text written between | separators)", Left = 306, Top = 0, AutoSize = true, Font = new Font("Segoe UI", 8.5F), ForeColor = MutedColor });

            pnlAttrRules = new FlowLayoutPanel
            {
                Left = 10, Top = 140,
                Width = 820, Height = 150,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            btnAddRule = new Button
            {
                Text = "+ Add rule", Left = 10, Top = 298, Width = 120, Height = 28,
                FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 9F)
            };
            btnAddRule.Click += delegate { AddAttrRuleRow(); };

            var lblMerge = new Label { Text = "Merge mode:", Left = 10, Top = 340, AutoSize = true, Font = new Font("Segoe UI Semibold", 9F) };
            rbMergeAdditive = new RadioButton
            {
                Text = "Additive (keep existing attrs, overlay Set/Remove)",
                Left = 90, Top = 338, AutoSize = true, Checked = true,
                Font = new Font("Segoe UI", 9F)
            };
            rbMergeRewrite  = new RadioButton
            {
                Text = "Rewrite (drop everything not in Set)",
                Left = 420, Top = 338, AutoSize = true,
                Font = new Font("Segoe UI", 9F)
            };

            grp.Controls.Add(lblBase);
            grp.Controls.Add(rbBaseLeave);
            grp.Controls.Add(rbBaseLabel);
            grp.Controls.Add(rbBasePrefixColon);
            grp.Controls.Add(rbBasePrefixUnderscore);
            grp.Controls.Add(rbBaseClear);
            grp.Controls.Add(rbBaseCustom);
            grp.Controls.Add(txtBaseCustomTemplate);
            grp.Controls.Add(lblTplHelp);
            grp.Controls.Add(lblAttrs);
            grp.Controls.Add(hdr);
            grp.Controls.Add(pnlAttrRules);
            grp.Controls.Add(btnAddRule);
            grp.Controls.Add(lblMerge);
            grp.Controls.Add(rbMergeAdditive);
            grp.Controls.Add(rbMergeRewrite);

            host.Controls.Add(grp);

            // Seed with one empty row so the grid isn't blank on open.
            AddAttrRuleRow();

            return host;
        }

        Panel BuildPreviewPane()
        {
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0), BackColor = BgColor };
            var lbl  = new Label { Text = "Preview — Before / After ExternalName", Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI Semibold", 9F), ForeColor = SectionColor };

            lvPreview = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };
            lvPreview.Columns.Add("Table",               170);
            lvPreview.Columns.Add("Field",               160);
            lvPreview.Columns.Add("Type",                 70);
            lvPreview.Columns.Add("Current ExternalName", 340);
            lvPreview.Columns.Add("New ExternalName",     340);

            lblPreviewSummary = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(4, 4, 0, 0),
                Text = "Define match + replace rules, then click Preview."
            };

            host.Controls.Add(lvPreview);
            host.Controls.Add(lblPreviewSummary);
            host.Controls.Add(lbl);
            return host;
        }

        void AddAttrRuleRow()
        {
            var row = new AttrRuleRow { Width = 800, Height = 28, BackColor = Color.White, Margin = new Padding(2) };
            var tips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400, ShowAlways = true };

            row.CboAttr = new ComboBox
            {
                Left = 4, Top = 2, Width = 190,
                Font = new Font("Consolas", 9.5F),
                DropDownStyle = ComboBoxStyle.DropDown
            };
            foreach (var a in Catalog) row.CboAttr.Items.Add(a.Name);
            row.CboAttr.SelectedIndexChanged += delegate
            {
                var def = LookupCatalog(row.CboAttr.Text);
                if (def != null)
                {
                    row.TxtValue.Text = def.Template;
                    tips.SetToolTip(row.CboAttr, def.Help);
                    tips.SetToolTip(row.TxtValue, def.Help);
                }
            };

            row.CboAction = new ComboBox
            {
                Left = 200, Top = 2, Width = 90,
                Font = new Font("Segoe UI", 9.5F),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            row.CboAction.Items.Add("Set");
            row.CboAction.Items.Add("Remove");
            row.CboAction.SelectedIndex = 0;

            row.TxtValue = new TextBox
            {
                Left = 296, Top = 2, Width = 460,
                Font = new Font("Consolas", 9.5F)
            };

            row.BtnRemove = new Button
            {
                Text = "x", Left = 764, Top = 1, Width = 28, Height = 24,
                FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 9F)
            };
            row.BtnRemove.Click += delegate
            {
                pnlAttrRules.Controls.Remove(row);
                row.Dispose();
            };

            row.Controls.Add(row.CboAttr);
            row.Controls.Add(row.CboAction);
            row.Controls.Add(row.TxtValue);
            row.Controls.Add(row.BtnRemove);

            pnlAttrRules.Controls.Add(row);
        }

        static AttrDef LookupCatalog(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var a in Catalog)
                if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }

        // ---------- preview ----------

        void RefreshPreview()
        {
            currentPlan.Clear();
            lvPreview.BeginUpdate();
            lvPreview.Items.Clear();
            btnApply.Enabled = false;

            Regex labelRe = null, tableRe = null, extRe = null;
            var opts = chkIgnoreCase.Checked ? RegexOptions.IgnoreCase : RegexOptions.None;
            try
            {
                var lp = (txtLabelPattern.Text   ?? "").Trim(); if (lp.Length > 0) labelRe = new Regex(lp, opts);
                var tp = (txtTableFilter.Text    ?? "").Trim(); if (tp.Length > 0) tableRe = new Regex(tp, opts);
                var ep = (txtExtNamePattern.Text ?? "").Trim(); if (ep.Length > 0) extRe   = new Regex(ep, opts);
            }
            catch (Exception ex)
            {
                lvPreview.EndUpdate();
                lblPreviewSummary.Text = "Invalid regex: " + ex.Message;
                return;
            }

            // Collect checked types (empty = any).
            var typeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in clbTypes.CheckedItems) typeSet.Add((it as string ?? "").Trim());

            var rules = CollectRules();
            var baseRule = ResolveBaseRule();
            bool additive = rbMergeAdditive.Checked;

            int scanned = 0, matched = 0, willChange = 0;

            foreach (var t in DictModel.GetTables(dict))
            {
                if (chkExcludeAliases.Checked && DictModel.IsAlias(t)) continue;
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "";
                if (tableRe != null && !tableRe.IsMatch(tName)) continue;
                var prefix = DictModel.AsString(DictModel.GetProp(t, "Prefix")) ?? "";

                foreach (var f in FieldMutator.EnumerateFields(t))
                {
                    scanned++;
                    var lbl = DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "";
                    if (string.IsNullOrEmpty(lbl)) continue;
                    if (labelRe != null && !labelRe.IsMatch(lbl)) continue;

                    var dt = (DictModel.AsString(DictModel.GetProp(f, "DataType")) ?? "").Trim();
                    if (typeSet.Count > 0 && !typeSet.Contains(dt)) continue;

                    var beforeExt = DictModel.AsString(DictModel.GetProp(f, "ExternalName")) ?? "";
                    if (extRe != null && !extRe.IsMatch(beforeExt)) continue;

                    matched++;
                    var afterExt = ApplyRules(beforeExt, baseRule, prefix, lbl, dt, rules, additive);
                    if (afterExt == beforeExt) continue; // skip no-ops in the plan

                    willChange++;
                    var plan = new PlanItem
                    {
                        Field = f,
                        TableName = tName,
                        FieldLabel = lbl,
                        DataType = dt,
                        BeforeExt = beforeExt,
                        AfterExt = afterExt
                    };
                    currentPlan.Add(plan);

                    var lvi = new ListViewItem(new[] { tName, lbl, dt, beforeExt, afterExt });
                    lvi.Tag = plan;
                    lvPreview.Items.Add(lvi);
                }
            }

            lvPreview.EndUpdate();
            lblPreviewSummary.Text = string.Format(
                "{0} field(s) scanned   ·   {1} matched   ·   {2} will change.",
                scanned, matched, willChange);
            btnApply.Enabled = currentPlan.Count > 0;
        }

        enum BaseRuleKind { Leave, Label, PrefixColon, PrefixUnderscore, Clear, Custom }

        BaseRuleKind ResolveBaseRule()
        {
            if (rbBaseLabel.Checked)            return BaseRuleKind.Label;
            if (rbBasePrefixColon.Checked)      return BaseRuleKind.PrefixColon;
            if (rbBasePrefixUnderscore.Checked) return BaseRuleKind.PrefixUnderscore;
            if (rbBaseClear.Checked)            return BaseRuleKind.Clear;
            if (rbBaseCustom.Checked)           return BaseRuleKind.Custom;
            return BaseRuleKind.Leave;
        }

        List<AttrRuleRow> CollectRules()
        {
            var list = new List<AttrRuleRow>();
            foreach (Control c in pnlAttrRules.Controls)
            {
                var row = c as AttrRuleRow;
                if (row == null) continue;
                if (string.IsNullOrEmpty(row.Value.Trim()) && row.Action != "Remove") continue;
                list.Add(row);
            }
            return list;
        }

        // ---------- pipe-separated ExternalName parsing + emission ----------

        // Tokenise on '|', trim; first token is the "base" (Label or
        // label-like), remainder are attributes. We preserve the user's
        // original case on emit; name-matching for duplicate/remove is
        // done case-insensitively because object-based drivers don't care
        // about case and traditional drivers' case-sensitivity is a sharper
        // edge than accidentally ending up with two NOTNULLs would be.
        static List<string> ParseTokens(string ext, out string baseName)
        {
            baseName = "";
            var list = new List<string>();
            if (string.IsNullOrEmpty(ext)) return list;
            var parts = ext.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                var t = (parts[i] ?? "").Trim();
                if (i == 0) { baseName = t; continue; }
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        static string TokenAttrName(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";
            var s = token.TrimStart();
            int cut = s.Length;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '=' || ch == '(' || char.IsWhiteSpace(ch)) { cut = i; break; }
            }
            return s.Substring(0, cut);
        }

        // Join with " | ", skipping empty segments — so a blank base name
        // with attributes emits "UUID8 | BINARY", not "| UUID8 | BINARY".
        static string JoinTokens(string baseName, List<string> attrs)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(baseName)) parts.Add(baseName);
            foreach (var t in attrs)
                if (!string.IsNullOrEmpty(t)) parts.Add(t);
            return string.Join(" | ", parts.ToArray());
        }

        string ApplyRules(
            string beforeExt,
            BaseRuleKind baseRule,
            string prefix,
            string label,
            string dataType,
            List<AttrRuleRow> rules,
            bool additive)
        {
            string currentBase;
            var existing = ParseTokens(beforeExt, out currentBase);

            // ----- decide base -----
            string newBase;
            switch (baseRule)
            {
                case BaseRuleKind.Label:            newBase = label; break;
                case BaseRuleKind.PrefixColon:      newBase = (string.IsNullOrEmpty(prefix) ? "" : prefix + ":") + label; break;
                case BaseRuleKind.PrefixUnderscore: newBase = (string.IsNullOrEmpty(prefix) ? label : prefix + "_" + label).ToLowerInvariant(); break;
                case BaseRuleKind.Clear:            newBase = ""; break;
                case BaseRuleKind.Custom:
                    newBase = (txtBaseCustomTemplate.Text ?? "")
                        .Replace("{label}",  label ?? "")
                        .Replace("{prefix}", prefix ?? "")
                        .Replace("{type}",   dataType ?? "");
                    break;
                default:                            newBase = currentBase; break;
            }

            // ----- decide attribute list -----
            List<string> working;
            if (additive)
            {
                working = new List<string>(existing);
            }
            else
            {
                // Rewrite: start empty; only Set rules contribute.
                working = new List<string>();
            }

            // First pass: Remove rules strip any token whose attribute-name
            // prefix matches (case-insensitive), so "DEFAULT" removes
            // DEFAULT=0, DEFAULT=!gen_random_uuid(), DEFAULT() etc.
            foreach (var r in rules)
            {
                if (r.Action != "Remove") continue;
                var target = TokenAttrName(r.Value).Trim();
                if (string.IsNullOrEmpty(target))
                {
                    // Nothing to match on — skip rather than wipe the list.
                    continue;
                }
                for (int i = working.Count - 1; i >= 0; i--)
                {
                    var existingName = TokenAttrName(working[i]);
                    if (string.Equals(existingName, target, StringComparison.OrdinalIgnoreCase))
                        working.RemoveAt(i);
                }
            }

            // Second pass: Set rules either replace any existing token with
            // the same attribute-name or append if not present.
            foreach (var r in rules)
            {
                if (r.Action != "Set") continue;
                var token = (r.Value ?? "").Trim();
                if (token.Length == 0) continue;
                var name = TokenAttrName(token);
                bool replaced = false;
                for (int i = 0; i < working.Count; i++)
                {
                    if (string.Equals(TokenAttrName(working[i]), name, StringComparison.OrdinalIgnoreCase))
                    {
                        working[i] = token;
                        replaced = true;
                        break;
                    }
                }
                if (!replaced) working.Add(token);
            }

            return JoinTokens(newBase, working);
        }

        // ---------- apply ----------

        void Apply()
        {
            if (currentPlan.Count == 0) return;

            var confirm = MessageBox.Show(this,
                currentPlan.Count + " field(s) will have ExternalName rewritten.\r\n"
                + "A .tasker-bak-<timestamp> backup of the .DCT is written first.\r\n\r\nProceed?",
                "Search and replace fields",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK) return;

            var mr = new FieldMutator.Result();
            FieldMutator.Backup(DictModel.GetDictionaryFileName(dict), mr);
            if (mr.BackupFailed)
            {
                ShowTextModal("Backup failed",
                    "Backup failed - aborting.\r\n\r\n" + string.Join("\r\n", mr.Messages.ToArray()));
                return;
            }

            foreach (var p in currentPlan)
            {
                var tag = p.TableName + "." + p.FieldLabel + ".ExternalName";
                if (FieldMutator.SetStringProp(p.Field, "ExternalName", p.AfterExt, mr, tag))
                    mr.Changed++;
                else
                    mr.Failed++;
            }
            FieldMutator.ForceMarkDirty(dict, DictModel.GetActiveDictionaryView(), mr);

            var summary =
                "Rewritten:  " + mr.Changed + "\r\n" +
                "Failed:     " + mr.Failed  + "\r\n" +
                (string.IsNullOrEmpty(mr.BackupPath) ? "" : "Backup:    " + mr.BackupPath + "\r\n") +
                "\r\nThe dictionary is now DIRTY. Press Ctrl+S in Clarion to save.";

            ShowTextModal(
                mr.Failed > 0 ? "Search and replace - finished with errors" : "Search and replace - done",
                summary + "\r\n\r\n--- details ---\r\n" + string.Join("\r\n", mr.Messages.ToArray()));

            RefreshPreview();
        }

        void ShowTextModal(string title, string text)
        {
            using (var f = new Form
            {
                Text = title,
                Width = 900, Height = 520,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = BgColor, ShowIcon = false, ShowInTaskbar = false
            })
            {
                var tb = new TextBox
                {
                    Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                    Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5F),
                    Text = text, WordWrap = false
                };
                var bp = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = PanelColor, Padding = new Padding(12, 8, 12, 8) };
                var btn = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
                btn.Click += delegate { f.Close(); };
                bp.Controls.Add(btn);
                f.Controls.Add(tb); f.Controls.Add(bp);
                f.CancelButton = btn;
                f.ShowDialog(this);
            }
        }
    }
}
