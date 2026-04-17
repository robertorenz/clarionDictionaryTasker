using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    internal class FieldListDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);

        // Each column probes a list of candidate property names on DDField and
        // takes the first that returns a non-empty value. Columns are always
        // shown so it's obvious which mappings are missing.
        static readonly Column[] Columns = new[]
        {
            new Column("Name",        new[] { "Label", "Name" },            180),
            new Column("Full name",   new[] { "Name" },                     160),
            new Column("Type",        new[] { "DataType", "ActualDataType" }, 90),
            new Column("Size",        new[] { "FieldSize", "ItemSize" },     70),
            new Column("Places",      new[] { "Places" },                    60),
            new Column("Dim",         new[] { "Dimensions" },                60),
            new Column("Picture",     new[] { "ScreenPicture", "RowPicture" }, 120),
            new Column("Initial",     new[] { "InitialValue" },             100),
            new Column("Heading",     new[] { "ColumnHeading" },            130),
            new Column("Prompt",      new[] { "PromptText" },               130),
            new Column("Description", new[] { "Description" },              180),
            new Column("External",    new[] { "ExternalName" },             140),
        };

        readonly object table;
        readonly IList<object> fields;
        ListView lv;

        public FieldListDialog(object table)
        {
            this.table = table;
            this.fields = CollectFields(table);
            BuildUi();
            PopulateList();
        }

        static IList<object> CollectFields(object table)
        {
            var list = new List<object>();
            var en = DictModel.GetProp(table, "Fields") as IEnumerable;
            if (en != null) foreach (var f in en) if (f != null) list.Add(f);
            return list;
        }

        void BuildUi()
        {
            var tname = DictModel.AsString(DictModel.GetProp(table, "Name")) ?? "table";
            Text = "Fields - " + tname;
            Width = 1200;
            Height = 600;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = BgColor;
            MinimumSize = new Size(820, 360);
            ShowIcon = false;
            ShowInTaskbar = false;

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = HeaderColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Text = string.Format("{0}     ({1} fields)", tname, fields.Count)
            };

            lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.None
            };
            lv.DoubleClick += delegate { InspectSelectedField(); };

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 0), BackColor = BgColor };
            host.Controls.Add(lv);

            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(12, 10, 12, 10) };
            var btnClose   = MakeButton("Close",           b => Close());
            var btnInspect = MakeButton("Inspect field...", b => InspectSelectedField());
            btnClose.Dock   = DockStyle.Right;
            btnInspect.Dock = DockStyle.Right;
            btnPanel.Controls.Add(btnClose);
            btnPanel.Controls.Add(btnInspect);

            Controls.Add(host);
            Controls.Add(btnPanel);
            Controls.Add(header);
            AcceptButton = btnInspect;
            CancelButton = btnClose;
        }

        Button MakeButton(string text, Action<Button> onClick)
        {
            var b = new Button
            {
                Text = text,
                Width = 160,
                Height = 32,
                FlatStyle = FlatStyle.System,
                Margin = new Padding(6, 0, 0, 0)
            };
            b.Click += delegate { onClick(b); };
            return b;
        }

        void PopulateList()
        {
            foreach (var c in Columns) lv.Columns.Add(c.Header, c.Width);

            lv.BeginUpdate();
            try
            {
                for (int i = 0; i < fields.Count; i++)
                {
                    var f = fields[i];
                    var row = new string[Columns.Length];
                    for (int c = 0; c < Columns.Length; c++)
                        row[c] = Lookup(f, Columns[c].Candidates) ?? "";
                    if (string.IsNullOrEmpty(row[0])) row[0] = "(field " + (i + 1) + ")";
                    var item = new ListViewItem(row);
                    item.Tag = f;
                    lv.Items.Add(item);
                }
                if (lv.Items.Count > 0) lv.Items[0].Selected = true;
            }
            finally
            {
                lv.EndUpdate();
            }
        }

        void InspectSelectedField()
        {
            if (lv.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select a field first.", "DCT Addin",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var field = lv.SelectedItems[0].Tag;
            if (field == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("Type: " + field.GetType().FullName);
            sb.AppendLine("Assembly: " + field.GetType().Assembly.GetName().Name);
            sb.AppendLine();
            sb.AppendLine("Public properties (skipping indexers and explicit interface re-impls):");
            sb.AppendLine();

            foreach (var p in field.GetType()
                                   .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                   .Where(pp => pp.CanRead && pp.GetIndexParameters().Length == 0 && pp.Name.IndexOf('.') < 0)
                                   .OrderBy(pp => pp.Name))
            {
                object v; string vs;
                try { v = p.GetValue(field, null); vs = Format(v); }
                catch (Exception ex) { vs = "<ex: " + ex.GetType().Name + ">"; }
                sb.AppendFormat("  {0,-30} : {1,-35} = {2}\r\n",
                    p.Name, TypeName(p.PropertyType), vs);
            }

            ShowText("Field inspection", sb.ToString());
        }

        static string Format(object v)
        {
            if (v == null) return "<null>";
            if (v is string) return "\"" + v + "\"";
            if (v is IEnumerable && !(v is string))
            {
                int c = 0; foreach (var _ in (IEnumerable)v) c++;
                return "IEnumerable<" + c + " items>";
            }
            var s = v.ToString();
            if (s.Length > 120) s = s.Substring(0, 120) + "...";
            return s;
        }

        static string TypeName(Type t)
        {
            if (t == null) return "?";
            if (!t.IsGenericType) return t.Name;
            var root = t.Name; var i = root.IndexOf('`'); if (i > 0) root = root.Substring(0, i);
            return root + "<" + string.Join(",", t.GetGenericArguments().Select(TypeName).ToArray()) + ">";
        }

        void ShowText(string title, string text)
        {
            using (var f = new Form
            {
                Text = title,
                Width = 900,
                Height = 640,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = BgColor,
                ShowIcon = false,
                ShowInTaskbar = false
            })
            {
                var tb = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    Dock = DockStyle.Fill,
                    Font = new Font("Consolas", 9F),
                    Text = text,
                    WordWrap = false
                };
                var panel = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = PanelColor, Padding = new Padding(12, 8, 12, 8) };
                var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
                btnClose.Click += delegate { f.Close(); };
                panel.Controls.Add(btnClose);
                f.Controls.Add(tb);
                f.Controls.Add(panel);
                f.CancelButton = btnClose;
                f.ShowDialog(this);
            }
        }

        static string Lookup(object item, string[] candidates)
        {
            foreach (var name in candidates)
            {
                var v = DictModel.GetProp(item, name);
                if (v == null) continue;
                var s = v.ToString();
                if (!string.IsNullOrEmpty(s)) return s;
            }
            return null;
        }

        sealed class Column
        {
            public readonly string Header;
            public readonly string[] Candidates;
            public readonly int Width;
            public Column(string header, string[] candidates, int width)
            {
                Header = header; Candidates = candidates; Width = width;
            }
        }
    }
}
