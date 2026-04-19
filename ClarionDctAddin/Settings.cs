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
