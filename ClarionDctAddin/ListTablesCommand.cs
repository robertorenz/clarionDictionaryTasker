using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Gui;

namespace ClarionDctAddin
{
    public class ListTablesCommand : AbstractMenuCommand
    {
        public override void Run()
        {
            var sb = new StringBuilder();
            try
            {
                var workbench = WorkbenchSingleton.Workbench;
                var active = workbench.ActiveContent;

                sb.AppendLine("=== Workbench contents ===");
                foreach (IViewContent vc in workbench.ViewContentCollection)
                {
                    sb.AppendFormat(" - {0}   ({1})\r\n",
                        vc.TitleName ?? "<no title>",
                        vc.GetType().FullName);
                }
                sb.AppendLine();

                if (active == null)
                {
                    sb.AppendLine("No active content to inspect.");
                }
                else
                {
                    sb.AppendLine("=== Active content inspection ===");
                    DumpType(sb, active, depth: 0, maxDepth: 2, seen: new HashSet<Type>());

                    sb.AppendLine();
                    sb.AppendLine("=== Interface members on active content ===");
                    DumpInterfaces(sb, active);

                    var t = active.GetType();
                    sb.AppendLine();
                    sb.AppendLine("=== Public types in " + t.Assembly.GetName().Name + " ===");
                    DumpAssemblyTypes(sb, t.Assembly);

                    // Also follow the DCTExplorer property if present
                    var dctExplorerProp = t.GetProperty("DCTExplorer");
                    if (dctExplorerProp != null)
                    {
                        object explorer = null;
                        try { explorer = dctExplorerProp.GetValue(active, null); } catch { }
                        if (explorer != null)
                        {
                            sb.AppendLine();
                            sb.AppendLine("=== Interface members on DCTExplorer ===");
                            DumpInterfaces(sb, explorer);
                            sb.AppendLine();
                            sb.AppendLine("=== Non-public instance members on DCTExplorer wrapper ===");
                            DumpNonPublic(sb, explorer);
                        }
                    }

                    // And the Control (DCTContent) — often holds the model
                    var controlProp = t.GetProperty("Control");
                    if (controlProp != null)
                    {
                        object ctrl = null;
                        try { ctrl = controlProp.GetValue(active, null); } catch { }
                        if (ctrl != null)
                        {
                            sb.AppendLine();
                            sb.AppendLine("=== Control (" + ctrl.GetType().FullName + ") public properties ===");
                            foreach (var p in ctrl.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                                  .Where(pp => IsInterestingType(pp.PropertyType) || pp.Name.IndexOf("File", StringComparison.OrdinalIgnoreCase) >= 0 || pp.Name.IndexOf("Table", StringComparison.OrdinalIgnoreCase) >= 0 || pp.Name.IndexOf("Dict", StringComparison.OrdinalIgnoreCase) >= 0)
                                                  .OrderBy(pp => pp.Name))
                            {
                                object v = null; try { v = p.GetValue(ctrl, null); } catch { }
                                sb.AppendFormat("  {0} : {1} = {2}\r\n", p.Name, TypeName(p.PropertyType), FormatValue(v));
                            }
                            sb.AppendLine();
                            sb.AppendLine("=== Control non-public fields (looking for model) ===");
                            DumpNonPublic(sb, ctrl);

                            var dctProp = ctrl.GetType().GetProperty("DCT");
                            if (dctProp != null)
                            {
                                object dct = null; try { dct = dctProp.GetValue(ctrl, null); } catch { }
                                if (dct != null)
                                {
                                    sb.AppendLine();
                                    sb.AppendLine("=== DDDataDictionary model deep dive ===");
                                    DumpEverything(sb, dct);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("ERROR: " + ex);
            }

            var path = Path.Combine(Path.GetTempPath(), "clarion-dct-addin-dump.txt");
            try { File.WriteAllText(path, sb.ToString()); } catch { }

            ShowResult(sb.ToString(), path);
        }

        static void DumpType(StringBuilder sb, object instance, int depth, int maxDepth, HashSet<Type> seen)
        {
            if (instance == null) return;
            var t = instance.GetType();
            if (seen.Contains(t)) { Indent(sb, depth); sb.AppendLine("(already dumped " + t.FullName + ")"); return; }
            seen.Add(t);

            Indent(sb, depth);
            sb.AppendFormat("TYPE {0}\r\n", t.FullName);
            Indent(sb, depth);
            sb.AppendFormat("  Assembly: {0}\r\n", SafeLocation(t.Assembly));

            // Base chain
            Indent(sb, depth);
            sb.Append("  Bases:");
            for (var b = t.BaseType; b != null && b != typeof(object); b = b.BaseType)
                sb.Append(" -> " + b.FullName);
            sb.AppendLine();

            // Interfaces
            var ifaces = t.GetInterfaces().Select(i => i.Name).ToArray();
            if (ifaces.Length > 0)
            {
                Indent(sb, depth);
                sb.AppendLine("  Interfaces: " + string.Join(", ", ifaces));
            }

            // Properties
            Indent(sb, depth);
            sb.AppendLine("  Properties:");
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .OrderBy(p => p.Name)
                         .ToArray();
            foreach (var p in props)
            {
                object val = null; string valStr = "";
                if (p.CanRead && p.GetIndexParameters().Length == 0)
                {
                    try { val = p.GetValue(instance, null); valStr = FormatValue(val); }
                    catch (Exception ex) { valStr = "<ex: " + ex.GetType().Name + ">"; }
                }
                Indent(sb, depth);
                sb.AppendFormat("    {0,-35} : {1,-40} = {2}\r\n",
                    p.Name, TypeName(p.PropertyType), valStr);
            }

            // Fields (public only — rare on editors but cheap)
            var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
            if (fields.Length > 0)
            {
                Indent(sb, depth);
                sb.AppendLine("  Public fields:");
                foreach (var f in fields.OrderBy(x => x.Name))
                {
                    Indent(sb, depth);
                    sb.AppendFormat("    {0} : {1}\r\n", f.Name, TypeName(f.FieldType));
                }
            }

            // Methods — names only, skip accessors/inherited object methods
            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                           .Where(m => !m.IsSpecialName)
                           .OrderBy(m => m.Name)
                           .ToArray();
            if (methods.Length > 0)
            {
                Indent(sb, depth);
                sb.AppendLine("  Declared methods:");
                foreach (var m in methods)
                {
                    Indent(sb, depth);
                    sb.AppendFormat("    {0}({1}) : {2}\r\n",
                        m.Name,
                        string.Join(", ", m.GetParameters().Select(pp => TypeName(pp.ParameterType) + " " + pp.Name)),
                        TypeName(m.ReturnType));
                }
            }

            if (depth >= maxDepth) return;

            // Recurse into properties that look "interesting" — SoftVelocity/Clarion types only
            sb.AppendLine();
            foreach (var p in props)
            {
                if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
                if (!IsInterestingType(p.PropertyType)) continue;
                object val;
                try { val = p.GetValue(instance, null); }
                catch { continue; }
                if (val == null) continue;

                Indent(sb, depth);
                sb.AppendFormat(">> Property '{0}' ({1}) =>\r\n", p.Name, TypeName(p.PropertyType));
                DumpType(sb, val, depth + 1, maxDepth, seen);
                sb.AppendLine();
            }
        }

        static void DumpInterfaces(StringBuilder sb, object instance)
        {
            var t = instance.GetType();
            foreach (var iface in t.GetInterfaces()
                                   .Where(i => IsInterestingType(i))
                                   .OrderBy(i => i.FullName))
            {
                sb.AppendFormat("  INTERFACE {0}\r\n", iface.FullName);
                foreach (var p in iface.GetProperties().OrderBy(x => x.Name))
                {
                    object v = null; string vs = "<not read>";
                    if (p.CanRead && p.GetIndexParameters().Length == 0)
                    {
                        try { v = p.GetValue(instance, null); vs = FormatValue(v); }
                        catch (Exception ex) { vs = "<ex: " + ex.GetType().Name + ">"; }
                    }
                    sb.AppendFormat("    {0,-35} : {1,-40} = {2}\r\n",
                        p.Name, TypeName(p.PropertyType), vs);
                }
                foreach (var m in iface.GetMethods()
                                       .Where(mm => !mm.IsSpecialName)
                                       .OrderBy(mm => mm.Name))
                {
                    sb.AppendFormat("    {0}({1}) : {2}\r\n",
                        m.Name,
                        string.Join(", ", m.GetParameters().Select(pp => TypeName(pp.ParameterType) + " " + pp.Name)),
                        TypeName(m.ReturnType));
                }
                sb.AppendLine();
            }
        }

        static void DumpNonPublic(StringBuilder sb, object instance)
        {
            var t = instance.GetType();
            foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                               .Where(ff => IsInterestingType(ff.FieldType))
                               .OrderBy(ff => ff.Name))
            {
                object v = null; try { v = f.GetValue(instance); } catch { }
                sb.AppendFormat("  field {0} : {1} = {2}\r\n",
                    f.Name, TypeName(f.FieldType), FormatValue(v));
            }
            foreach (var p in t.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance)
                               .Where(pp => pp.CanRead && pp.GetIndexParameters().Length == 0 && IsInterestingType(pp.PropertyType))
                               .OrderBy(pp => pp.Name))
            {
                object v = null; try { v = p.GetValue(instance, null); } catch { }
                sb.AppendFormat("  prop  {0} : {1} = {2}\r\n",
                    p.Name, TypeName(p.PropertyType), FormatValue(v));
            }
        }

        static void DumpEverything(StringBuilder sb, object instance)
        {
            var t = instance.GetType();
            sb.AppendFormat("  Type: {0}\r\n", t.FullName);
            sb.AppendFormat("  Assembly: {0}\r\n", SafeLocation(t.Assembly));
            sb.Append("  Interfaces:");
            foreach (var i in t.GetInterfaces()) sb.Append(" " + i.Name);
            sb.AppendLine();

            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            sb.AppendLine("  Properties (all):");
            foreach (var p in t.GetProperties(ALL)
                               .Where(pp => pp.CanRead && pp.GetIndexParameters().Length == 0)
                               .OrderBy(pp => pp.Name))
            {
                object v = null; string vs = "<not read>";
                try { v = p.GetValue(instance, null); vs = FormatValue(v); }
                catch (Exception ex) { vs = "<ex: " + ex.GetType().Name + ">"; }
                sb.AppendFormat("    {0,-35} : {1,-40} = {2}\r\n",
                    p.Name, TypeName(p.PropertyType), vs);
            }

            sb.AppendLine("  Fields (all):");
            foreach (var f in t.GetFields(ALL).OrderBy(ff => ff.Name))
            {
                object v = null; string vs = "<not read>";
                try { v = f.GetValue(instance); vs = FormatValue(v); }
                catch (Exception ex) { vs = "<ex: " + ex.GetType().Name + ">"; }
                sb.AppendFormat("    {0,-35} : {1,-40} = {2}\r\n",
                    f.Name, TypeName(f.FieldType), vs);
            }

            // Sample a few elements from any property/field whose name suggests tables
            sb.AppendLine();
            sb.AppendLine("  Collection samples (File/Table/Entity/Item-named members):");
            foreach (var member in GetTableLikeMembers(t))
            {
                object v = null;
                try
                {
                    var pi = member as PropertyInfo;
                    var fi = member as FieldInfo;
                    if (pi != null) v = pi.GetValue(instance, null);
                    else if (fi != null) v = fi.GetValue(instance);
                }
                catch { continue; }
                if (v == null) continue;
                var en = v as IEnumerable;
                if (en == null || v is string) continue;

                sb.AppendFormat("    [{0}] type={1}\r\n", member.Name, TypeName(v.GetType()));
                int i = 0;
                foreach (var item in en)
                {
                    if (item == null) continue;
                    sb.AppendFormat("      [{0}] {1}   ({2})\r\n", i, item, item.GetType().FullName);
                    if (++i >= 3) { sb.AppendLine("      ..."); break; }
                }

                // If we have at least one item, dump the FIRST item's shape so we can read fields later
                foreach (var item in en)
                {
                    if (item == null) continue;
                    sb.AppendLine();
                    sb.AppendFormat("      --- shape of first item ({0}) ---\r\n", item.GetType().FullName);
                    DumpItemShape(sb, item, "        ");
                    break;
                }
            }
        }

        static IEnumerable<MemberInfo> GetTableLikeMembers(Type t)
        {
            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            string[] hints = { "file", "table", "entity", "item", "schema" };
            foreach (var p in t.GetProperties(ALL).Where(pp => pp.GetIndexParameters().Length == 0))
                if (hints.Any(h => p.Name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0)) yield return p;
            foreach (var f in t.GetFields(ALL))
                if (hints.Any(h => f.Name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0)) yield return f;
        }

        static void DumpItemShape(StringBuilder sb, object item, string indent)
        {
            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var t = item.GetType();
            foreach (var p in t.GetProperties(ALL)
                               .Where(pp => pp.CanRead && pp.GetIndexParameters().Length == 0)
                               .OrderBy(pp => pp.Name))
            {
                object v = null; string vs = "<err>";
                try { v = p.GetValue(item, null); vs = FormatValue(v); } catch { }
                sb.AppendFormat("{0}{1,-30} : {2,-35} = {3}\r\n",
                    indent, p.Name, TypeName(p.PropertyType), vs);
            }
        }

        static void DumpAssemblyTypes(StringBuilder sb, Assembly a)
        {
            Type[] types;
            try { types = a.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(x => x != null).ToArray(); }

            foreach (var t in types.Where(x => x.IsPublic)
                                    .OrderBy(x => x.FullName))
            {
                sb.AppendFormat("  {0,-9} {1}\r\n",
                    t.IsInterface ? "interface" : (t.IsEnum ? "enum" : (t.IsValueType ? "struct" : "class")),
                    t.FullName);
            }
        }

        static bool IsInterestingType(Type t)
        {
            if (t == null) return false;
            if (t.IsPrimitive || t == typeof(string) || t == typeof(object)) return false;
            var ns = t.Namespace ?? "";
            return ns.StartsWith("SoftVelocity", StringComparison.Ordinal)
                || ns.StartsWith("Clarion", StringComparison.Ordinal)
                || ns.StartsWith("DataDictionary", StringComparison.Ordinal);
        }

        static string FormatValue(object v)
        {
            if (v == null) return "<null>";
            if (v is string) return "\"" + v + "\"";
            var en = v as IEnumerable;
            if (en != null && !(v is string))
            {
                int c = 0; foreach (var _ in en) c++;
                return "IEnumerable<" + c + " items>  (" + TypeName(v.GetType()) + ")";
            }
            var s = v.ToString();
            if (s.Length > 80) s = s.Substring(0, 80) + "...";
            return s;
        }

        static string TypeName(Type t)
        {
            if (t == null) return "?";
            if (!t.IsGenericType) return t.Name;
            var root = t.Name; var i = root.IndexOf('`'); if (i > 0) root = root.Substring(0, i);
            return root + "<" + string.Join(",", t.GetGenericArguments().Select(TypeName)) + ">";
        }

        static string SafeLocation(Assembly a)
        {
            try { return a.Location; } catch { return a.FullName; }
        }

        static void Indent(StringBuilder sb, int depth)
        {
            for (int i = 0; i < depth; i++) sb.Append("    ");
        }

        static void ShowResult(string text, string path)
        {
            var f = new Form
            {
                Text = "DCT Addin - reflection dump  (saved to " + path + ")",
                Width = 1100,
                Height = 700,
                StartPosition = FormStartPosition.CenterScreen
            };
            var tb = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Font = new System.Drawing.Font("Consolas", 9F),
                Text = text,
                WordWrap = false
            };
            f.Controls.Add(tb);
            f.ShowDialog();
        }
    }
}
