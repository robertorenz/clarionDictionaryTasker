using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using ICSharpCode.Core;

namespace ClarionDctAddin
{
    // Phase A probe: find out what batch-edit primitives the dictionary model
    // actually exposes. We inspect:
    //   - DDFile          (container for fields, keys, relations, triggers)
    //   - DDField         (the field itself)
    //   - DDKey / DDRelation / DDTrigger
    //   - Fields / Keys / ... collection types (UniqueDataDictionaryItemList<T>)
    // For each, report:
    //   - Public + non-public constructors
    //   - Methods matching verbs that suggest mutation
    //     (Add, Insert, Remove, Clear, Clone, Copy, Duplicate, Set, Create, New, Move, Load)
    //   - Events matching Change/Added/Removed names
    // Output goes to %TEMP% so it survives clipboard limits.
    public class DiscoverWriteApiCommand : AbstractMenuCommand
    {
        static readonly string[] MutationVerbs = {
            "add", "insert", "remove", "delete", "clear",
            "clone", "copy", "duplicate", "import",
            "set", "create", "new", "make", "build",
            "move", "swap", "reorder", "sort",
            "rename", "replace",
            "load", "apply", "commit"
        };

        public override void Run()
        {
            object dict;
            string err;
            if (!DictModel.TryGetOpenDictionary(out dict, out err))
            {
                MessageBox.Show(err, "DCT Addin", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var tables = DictModel.GetTables(dict);
            object firstTable = tables.FirstOrDefault();
            object firstField = null;
            object firstKey = null;
            object firstRelation = null;
            object firstTrigger = null;
            object fieldsCollection = null;
            object keysCollection = null;

            if (firstTable != null)
            {
                fieldsCollection = DictModel.GetProp(firstTable, "Fields");
                keysCollection   = DictModel.GetProp(firstTable, "Keys");
                firstField    = First(fieldsCollection);
                firstKey      = First(keysCollection);
                firstRelation = First(DictModel.GetProp(firstTable, "Relations"));
                firstTrigger  = First(DictModel.GetProp(firstTable, "Triggers"));
                // If the first table has no fields, fall back to any table that does.
                if (firstField == null)
                {
                    foreach (var t in tables)
                    {
                        var fc = DictModel.GetProp(t, "Fields");
                        var f  = First(fc);
                        if (f != null) { fieldsCollection = fc; firstField = f; break; }
                    }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== Phase A: write API discovery ===");
            sb.AppendFormat("Dictionary: {0}\r\n", DictModel.GetDictionaryName(dict));
            sb.AppendFormat("File:       {0}\r\n", DictModel.GetDictionaryFileName(dict));
            sb.AppendLine();

            ReportType(sb, "DDDataDictionary", dict == null ? null : dict.GetType());
            ReportType(sb, "DDFile (first table)", firstTable == null ? null : firstTable.GetType());
            ReportType(sb, "Fields collection",    fieldsCollection == null ? null : fieldsCollection.GetType());
            ReportType(sb, "DDField (first field)", firstField == null ? null : firstField.GetType());
            ReportType(sb, "Keys collection",      keysCollection == null ? null : keysCollection.GetType());
            ReportType(sb, "DDKey (first key)",    firstKey == null ? null : firstKey.GetType());
            ReportType(sb, "DDRelation (first)",   firstRelation == null ? null : firstRelation.GetType());
            ReportType(sb, "DDTrigger (first)",    firstTrigger == null ? null : firstTrigger.GetType());

            // Also dump dirty-tracking surface: events & properties that suggest the
            // editor wants to be told about mutations.
            sb.AppendLine();
            sb.AppendLine("=== Dirty/change surface on dictionary ===");
            if (dict != null) ReportDirtySurface(sb, dict.GetType());

            var path = Path.Combine(Path.GetTempPath(), "clarion-dct-addin-writeapi.txt");
            File.WriteAllText(path, sb.ToString());

            ShowReport(path, sb.ToString());
        }

        static object First(object maybeEnumerable)
        {
            var en = maybeEnumerable as IEnumerable;
            if (en == null) return null;
            foreach (var x in en) { if (x != null) return x; }
            return null;
        }

        static void ReportType(StringBuilder sb, string label, Type t)
        {
            sb.AppendLine();
            sb.AppendFormat("--- {0} ---\r\n", label);
            if (t == null) { sb.AppendLine("  (not available)"); return; }
            sb.AppendFormat("Type:     {0}\r\n", t.FullName);
            sb.AppendFormat("Assembly: {0}\r\n", t.Assembly.GetName().Name);

            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            // Constructors
            sb.AppendLine("  Constructors:");
            foreach (var ci in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                .OrderBy(c => c.GetParameters().Length))
            {
                sb.AppendFormat("    {0} .ctor({1})\r\n",
                    ci.IsPublic ? "public " : ci.IsAssembly ? "internal" : "private ",
                    FormatParams(ci.GetParameters()));
            }

            // Mutation methods
            sb.AppendLine("  Mutation-ish methods:");
            int found = 0;
            foreach (var m in t.GetMethods(ALL)
                               .Where(mm => !mm.IsSpecialName)
                               .Where(mm => LooksMutational(mm.Name))
                               .OrderBy(mm => mm.Name)
                               .ThenBy(mm => mm.GetParameters().Length))
            {
                var vis =
                    m.IsPublic   ? "public  " :
                    m.IsAssembly ? "internal" :
                    m.IsFamily   ? "protect." :
                                   "private ";
                sb.AppendFormat("    {0} {1}({2}) : {3}\r\n",
                    vis, m.Name, FormatParams(m.GetParameters()), FormatType(m.ReturnType));
                found++;
            }
            if (found == 0) sb.AppendLine("    (none found)");

            // Events suggest callbacks the editor uses after mutation.
            var evts = t.GetEvents(ALL).Where(e => e.Name.IndexOf('.') < 0).OrderBy(e => e.Name).ToArray();
            if (evts.Length > 0)
            {
                sb.AppendLine("  Events:");
                foreach (var ev in evts)
                    sb.AppendFormat("    {0} : {1}\r\n", ev.Name, FormatType(ev.EventHandlerType));
            }
        }

        static bool LooksMutational(string name)
        {
            foreach (var v in MutationVerbs)
                if (name.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static void ReportDirtySurface(StringBuilder sb, Type t)
        {
            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            string[] hints = { "dirty", "touched", "modif", "change", "save", "commit", "notify", "recalc" };

            foreach (var p in t.GetProperties(ALL)
                               .Where(pp => hints.Any(h => pp.Name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0))
                               .OrderBy(pp => pp.Name))
            {
                sb.AppendFormat("  prop  {0,-30} : {1}\r\n", p.Name, FormatType(p.PropertyType));
            }
            foreach (var ev in t.GetEvents(ALL)
                                .Where(e => hints.Any(h => e.Name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0))
                                .OrderBy(e => e.Name))
            {
                sb.AppendFormat("  event {0,-30} : {1}\r\n", ev.Name, FormatType(ev.EventHandlerType));
            }
            foreach (var m in t.GetMethods(ALL)
                               .Where(mm => !mm.IsSpecialName && hints.Any(h => mm.Name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0))
                               .OrderBy(mm => mm.Name))
            {
                sb.AppendFormat("  meth  {0}({1}) : {2}\r\n",
                    m.Name, FormatParams(m.GetParameters()), FormatType(m.ReturnType));
            }
        }

        static string FormatParams(ParameterInfo[] ps)
        {
            if (ps.Length == 0) return "";
            return string.Join(", ", ps.Select(p => FormatType(p.ParameterType) + " " + p.Name).ToArray());
        }

        static string FormatType(Type t)
        {
            if (t == null) return "?";
            if (t == typeof(void)) return "void";
            if (!t.IsGenericType) return t.Name;
            var root = t.Name;
            var i = root.IndexOf('`');
            if (i > 0) root = root.Substring(0, i);
            return root + "<" + string.Join(",", t.GetGenericArguments().Select(FormatType).ToArray()) + ">";
        }

        static void ShowReport(string path, string text)
        {
            using (var f = new Form
            {
                Text = "Write API discovery  (saved to " + path + ")",
                Width = 1100,
                Height = 720,
                StartPosition = FormStartPosition.CenterScreen,
                ShowIcon = false,
                ShowInTaskbar = false,
                BackColor = Color.FromArgb(245, 247, 250)
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
                var bp = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Color.FromArgb(225, 230, 235), Padding = new Padding(12, 8, 12, 8) };
                var btn = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
                btn.Click += delegate { f.Close(); };
                bp.Controls.Add(btn);
                f.Controls.Add(tb);
                f.Controls.Add(bp);
                f.CancelButton = btn;
                f.ShowDialog();
            }
        }
    }
}
