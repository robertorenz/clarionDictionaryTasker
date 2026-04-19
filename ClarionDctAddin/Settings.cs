using System;
using System.Collections.Generic;
using System.IO;

namespace ClarionDctAddin
{
    // Small per-user persistent settings store — plain key=value text file
    // under %LOCALAPPDATA%\ClarionDctAddin\settings.txt. All reads/writes are
    // best-effort; any IO failure leaves the cache intact and is swallowed.
    internal static class Settings
    {
        const string KeyPreferredDialect = "preferred_sql_dialect";
        const string KeyFixKeysStyle     = "fix_keys_style";
        const string KeyFixKeysOwner     = "fix_keys_owner";
        const string KeyFixKeysKey       = "fix_keys_key";
        const string KeyFixKeysShow      = "fix_keys_show";

        const string KeyFixFieldsDescStyle = "fix_fields_desc_style";
        const string KeyJsonPreviewStyle   = "json_preview_style";

        const string KeyTableListSortColumn = "tablelist_sort_col";
        const string KeyTableListSortAsc    = "tablelist_sort_asc";

        const string KeyModelLanguage        = "model_language";
        const string KeyModelNamespace       = "model_namespace";
        const string KeyModelIncludeDescs    = "model_include_descs";

        const string KeyNamingTblUpper         = "naming_tbl_upper";
        const string KeyNamingPrefix           = "naming_prefix";
        const string KeyNamingLblNoSpace       = "naming_lbl_no_space";
        const string KeyNamingLblNoDigitStart  = "naming_lbl_no_digit_start";
        const string KeyNamingKeyConvention    = "naming_key_convention";

        const string KeyGlobalSearchRegex      = "global_search_regex";
        const string KeyGlobalSearchTables     = "global_search_tables";
        const string KeyGlobalSearchFields     = "global_search_fields";
        const string KeyGlobalSearchKeys       = "global_search_keys";
        const string KeyGlobalSearchRelations  = "global_search_relations";
        const string KeyGlobalSearchTriggers   = "global_search_triggers";
        const string KeyGlobalSearchDescriptions = "global_search_descriptions";

        static readonly string DataDir =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClarionDctAddin");

        static readonly string SettingsPath = Path.Combine(DataDir, "settings.txt");

        static Dictionary<string, string> cache;

        static Dictionary<string, string> LoadCached()
        {
            if (cache != null) return cache;
            cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(SettingsPath))
                {
                    foreach (var rawLine in File.ReadAllLines(SettingsPath))
                    {
                        var line = (rawLine ?? "").Trim();
                        if (line.Length == 0 || line[0] == '#') continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        cache[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                }
            }
            catch { /* start with an empty cache */ }
            return cache;
        }

        static void Persist()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var lines = new List<string> { "# Dictionary Tasker preferences" };
                foreach (var kvp in LoadCached())
                    lines.Add(kvp.Key + "=" + kvp.Value);
                File.WriteAllLines(SettingsPath, lines.ToArray());
            }
            catch { /* non-fatal */ }
        }

        public static string Get(string key, string fallback)
        {
            string v;
            return LoadCached().TryGetValue(key, out v) ? v : fallback;
        }

        public static void Set(string key, string value)
        {
            LoadCached()[key] = value;
            Persist();
        }

        public static int GetInt(string key, int fallback)
        {
            int n;
            return int.TryParse(Get(key, ""), out n) ? n : fallback;
        }

        public static void SetInt(string key, int value)
        {
            Set(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public static int FixKeysStyle { get { return GetInt(KeyFixKeysStyle, 0); } set { SetInt(KeyFixKeysStyle, value); } }
        public static int FixKeysOwner { get { return GetInt(KeyFixKeysOwner, 0); } set { SetInt(KeyFixKeysOwner, value); } }
        public static int FixKeysKey   { get { return GetInt(KeyFixKeysKey,   0); } set { SetInt(KeyFixKeysKey,   value); } }
        public static int FixKeysShow  { get { return GetInt(KeyFixKeysShow,  0); } set { SetInt(KeyFixKeysShow,  value); } }

        public static bool GetBool(string key, bool fallback)
        {
            var v = Get(key, "");
            if (string.IsNullOrEmpty(v)) return fallback;
            return string.Equals(v, "1", StringComparison.Ordinal)
                || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "yes",  StringComparison.OrdinalIgnoreCase);
        }
        public static void SetBool(string key, bool value)
        {
            Set(key, value ? "1" : "0");
        }

        public static int FixFieldsDescStyle { get { return GetInt(KeyFixFieldsDescStyle, 0); } set { SetInt(KeyFixFieldsDescStyle, value); } }
        public static int JsonPreviewStyle   { get { return GetInt(KeyJsonPreviewStyle,   4); } set { SetInt(KeyJsonPreviewStyle,   value); } } // 4 = Tree

        public static int  TableListSortColumn { get { return GetInt(KeyTableListSortColumn, 0); } set { SetInt(KeyTableListSortColumn, value); } }
        public static bool TableListSortAsc    { get { return GetBool(KeyTableListSortAsc,  true); } set { SetBool(KeyTableListSortAsc, value); } }

        public static int    ModelLanguage    { get { return GetInt(KeyModelLanguage, 0); } set { SetInt(KeyModelLanguage, value); } }
        public static string ModelNamespace   { get { return Get(KeyModelNamespace, "ClarionModels"); } set { Set(KeyModelNamespace, value ?? ""); } }
        public static bool   ModelIncludeDescriptions { get { return GetBool(KeyModelIncludeDescs, true); } set { SetBool(KeyModelIncludeDescs, value); } }

        public static bool NamingTblUpper         { get { return GetBool(KeyNamingTblUpper,         true); } set { SetBool(KeyNamingTblUpper, value); } }
        public static bool NamingPrefix           { get { return GetBool(KeyNamingPrefix,           true); } set { SetBool(KeyNamingPrefix, value); } }
        public static bool NamingLblNoSpace       { get { return GetBool(KeyNamingLblNoSpace,       true); } set { SetBool(KeyNamingLblNoSpace, value); } }
        public static bool NamingLblNoDigitStart  { get { return GetBool(KeyNamingLblNoDigitStart,  true); } set { SetBool(KeyNamingLblNoDigitStart, value); } }
        public static bool NamingKeyConvention    { get { return GetBool(KeyNamingKeyConvention,    true); } set { SetBool(KeyNamingKeyConvention, value); } }

        // MSSQL connection per dict, saved so !glo:owner dicts don't re-prompt
        // every time. Key is slugified to keep the settings file sane.
        public static string MssqlConnectionFor(string dictName)
        {
            return Get("mssql_conn_" + Slug(dictName), "");
        }
        public static void SetMssqlConnectionFor(string dictName, string connStr)
        {
            Set("mssql_conn_" + Slug(dictName), connStr ?? "");
        }
        public static void ClearMssqlConnectionFor(string dictName)
        {
            Set("mssql_conn_" + Slug(dictName), "");
        }
        static string Slug(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }

        public static bool GlobalSearchRegex        { get { return GetBool(KeyGlobalSearchRegex,        false); } set { SetBool(KeyGlobalSearchRegex, value); } }
        public static bool GlobalSearchTables       { get { return GetBool(KeyGlobalSearchTables,       true); } set { SetBool(KeyGlobalSearchTables, value); } }
        public static bool GlobalSearchFields       { get { return GetBool(KeyGlobalSearchFields,       true); } set { SetBool(KeyGlobalSearchFields, value); } }
        public static bool GlobalSearchKeys         { get { return GetBool(KeyGlobalSearchKeys,         true); } set { SetBool(KeyGlobalSearchKeys, value); } }
        public static bool GlobalSearchRelations    { get { return GetBool(KeyGlobalSearchRelations,    true); } set { SetBool(KeyGlobalSearchRelations, value); } }
        public static bool GlobalSearchTriggers     { get { return GetBool(KeyGlobalSearchTriggers,     true); } set { SetBool(KeyGlobalSearchTriggers, value); } }
        public static bool GlobalSearchDescriptions { get { return GetBool(KeyGlobalSearchDescriptions, true); } set { SetBool(KeyGlobalSearchDescriptions, value); } }

        public static SqlDdlGenerator.Dialect PreferredDialect
        {
            get
            {
                var name = Get(KeyPreferredDialect, SqlDdlGenerator.Dialect.SqlServer.ToString());
                try { return (SqlDdlGenerator.Dialect)Enum.Parse(typeof(SqlDdlGenerator.Dialect), name, true); }
                catch { return SqlDdlGenerator.Dialect.SqlServer; }
            }
            set { Set(KeyPreferredDialect, value.ToString()); }
        }
    }
}
