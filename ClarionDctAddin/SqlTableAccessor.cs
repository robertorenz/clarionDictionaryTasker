using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;

namespace ClarionDctAddin
{
    // Read the first N rows of a SQL-driver table by talking to the database
    // directly via ADO.NET. Bypasses Clarion's runtime entirely — we don't
    // need an abstract CFile, a ClaString, or the native driver DLL.
    //
    // Connection info comes from the DDFile's OwnerName, which Clarion typically
    // uses as the OWNER('...') argument. Common shapes:
    //   - "server,database,user,password"       -> SQL authentication
    //   - "server,database"                     -> Integrated authentication
    //   - "DSN=foo"                             -> not supported here (use ODBC
    //                                              path, which we can add later)
    //
    // Currently MSSQL-only. The shape generalises to Postgres / MySQL by
    // swapping the SqlConnection/SqlCommand for the driver's provider.
    internal static class SqlTableAccessor
    {
        public sealed class ReadResult
        {
            public bool   Ok;
            public string Error;
            public List<string>       ColumnNames = new List<string>();
            public List<string>       ColumnTypes = new List<string>();
            public List<object[]>     Rows        = new List<object[]>();
            public List<string>       Log         = new List<string>();
        }

        public static bool IsSqlServerDriver(string driver)
        {
            var d = (driver ?? "").Trim().ToUpperInvariant();
            return d == "MSSQL" || d == "SQLSERVER" || d == "MS SQL" || d == "MSSQLSERVER";
        }

        public static ReadResult Read(object dict, object table, int maxRows)
        {
            return Read(dict, table, maxRows, null);
        }

        public static ReadResult Read(object dict, object table, int maxRows, string overrideConnStr)
        {
            var r = new ReadResult();
            try { DoRead(dict, table, maxRows, r, overrideConnStr); }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                r.Ok = false;
                r.Error = inner.GetType().Name + ": " + inner.Message;
                r.Log.Add("unhandled: " + inner);
            }
            return r;
        }

        // Returns true when the Owner string is a Clarion variable reference
        // (e.g. "!glo:owner") that can't be resolved from the dict's metadata.
        public static bool IsRuntimeOwnerRef(string owner)
        {
            if (string.IsNullOrWhiteSpace(owner)) return false;
            return owner.TrimStart().StartsWith("!", StringComparison.Ordinal);
        }

        public static string TryBuildConnectionStringFromOwner(string owner, out string error)
        {
            var r = new ReadResult();
            var s = BuildConnectionString(owner, r, out error);
            return s;
        }

        static void DoRead(object dict, object table, int maxRows, ReadResult r, string overrideConnStr)
        {
            var driver   = DictModel.AsString(DictModel.GetProp(table, "FileDriverName")) ?? "";
            var fullPath = DictModel.AsString(DictModel.GetProp(table, "FullPathName"))   ?? "";
            var owner    = DictModel.AsString(DictModel.GetProp(table, "OwnerName"))      ?? "";
            var tName    = DictModel.AsString(DictModel.GetProp(table, "Name"))           ?? "";

            r.Log.Add("driver=" + driver);
            r.Log.Add("fullPath=" + fullPath);
            r.Log.Add("owner=" + (string.IsNullOrEmpty(owner) ? "<empty>" : owner));

            if (!IsSqlServerDriver(driver))
            {
                r.Ok = false;
                r.Error = "This path only supports MSSQL (got driver '" + driver + "').";
                return;
            }

            string connStr = overrideConnStr;
            if (string.IsNullOrEmpty(connStr))
            {
                connStr = BuildConnectionString(owner, r, out string connErr);
                if (connStr == null)
                {
                    r.Ok = false;
                    r.Error = connErr;
                    return;
                }
            }
            else
            {
                r.Log.Add("using saved / prompted connection");
            }

            var sqlTableName = !string.IsNullOrEmpty(fullPath) ? fullPath : tName;
            var qualified = QuoteSqlIdentifier(sqlTableName);
            var sql = "SELECT TOP " + maxRows + " * FROM " + qualified;
            r.Log.Add("sql=" + sql);

            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();
                r.Log.Add("connected to " + conn.DataSource + " / " + conn.Database);
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.CommandTimeout = 15;
                    using (var reader = cmd.ExecuteReader())
                    {
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            r.ColumnNames.Add(reader.GetName(i));
                            r.ColumnTypes.Add(reader.GetDataTypeName(i));
                        }
                        while (reader.Read())
                        {
                            var row = new object[reader.FieldCount];
                            for (int i = 0; i < reader.FieldCount; i++)
                                row[i] = reader.IsDBNull(i) ? (object)null : reader.GetValue(i);
                            r.Rows.Add(row);
                        }
                    }
                }
            }
            r.Ok = true;
        }

        static string BuildConnectionString(string owner, ReadResult r, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(owner))
            {
                error = "No OwnerName on the table — supply a connection below.";
                return null;
            }
            if (IsRuntimeOwnerRef(owner))
            {
                error = "Owner is a runtime variable ('" + owner + "') — enter a connection below.";
                return null;
            }

            // Trim matched outer single-quotes sometimes carried in from OWNER('...').
            owner = owner.Trim();
            if (owner.Length >= 2 && owner[0] == '\'' && owner[owner.Length - 1] == '\'')
                owner = owner.Substring(1, owner.Length - 2);
            if (owner.Length >= 2 && owner[0] == '"' && owner[owner.Length - 1] == '"')
                owner = owner.Substring(1, owner.Length - 2);

            var parts = owner.Split(',');
            if (parts.Length < 2)
            {
                error = "OwnerName '" + owner + "' doesn't look like 'server,database[,user,password]' — enter a connection below.";
                return null;
            }

            var b = new SqlConnectionStringBuilder
            {
                DataSource      = parts[0].Trim(),
                InitialCatalog  = parts[1].Trim(),
                ConnectTimeout  = 10,
                ApplicationName = "Dictionary Tasker"
            };

            if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                b.UserID   = parts[2].Trim();
                b.Password = parts[3];
                r.Log.Add("auth=SQL user " + b.UserID);
            }
            else
            {
                b.IntegratedSecurity = true;
                r.Log.Add("auth=Integrated (Windows)");
            }
            return b.ConnectionString;
        }

        // Turn "dbo.clientes" into "[dbo].[clientes]" — handles the schema.table
        // pattern and escapes any ']' in an identifier.
        static string QuoteSqlIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var parts = name.Split('.');
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append('.');
                sb.Append('[');
                sb.Append(parts[i].Replace("]", "]]"));
                sb.Append(']');
            }
            return sb.ToString();
        }
    }
}
