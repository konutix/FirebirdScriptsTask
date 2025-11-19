using FirebirdSql.Data.FirebirdClient;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DbMetaTool
{
    public static class Program
    {
        // Przykładowe wywołania:
        // DbMetaTool build-db --db-dir "C:\db\fb5" --scripts-dir "C:\scripts"
        // DbMetaTool export-scripts --connection-string "..." --output-dir "C:\out"
        // DbMetaTool update-db --connection-string "..." --scripts-dir "C:\scripts"
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Użycie:");
                Console.WriteLine("  build-db --db-dir <ścieżka> --scripts-dir <ścieżka>");
                Console.WriteLine("  export-scripts --connection-string <connStr> --output-dir <ścieżka>");
                Console.WriteLine("  update-db --connection-string <connStr> --scripts-dir <ścieżka>");
                return 1;
            }

            try
            {
                var command = args[0].ToLowerInvariant();

                switch (command)
                {
                    case "build-db":
                        {
                            string dbDir = GetArgValue(args, "--db-dir");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            BuildDatabase(dbDir, scriptsDir);
                            Console.WriteLine("Baza danych została zbudowana pomyślnie.");
                            return 0;
                        }

                    case "export-scripts":
                        {
                            string connStr = GetArgValue(args, "--connection-string");
                            string outputDir = GetArgValue(args, "--output-dir");

                            ExportScripts(connStr, outputDir);
                            Console.WriteLine("Skrypty zostały wyeksportowane pomyślnie.");
                            return 0;
                        }

                    case "update-db":
                        {
                            string connStr = GetArgValue(args, "--connection-string");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            UpdateDatabase(connStr, scriptsDir);
                            Console.WriteLine("Baza danych została zaktualizowana pomyślnie.");
                            return 0;
                        }

                    default:
                        Console.WriteLine($"Nieznane polecenie: {command}");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Błąd: " + ex.Message);
                return -1;
            }
        }

        private static string GetArgValue(string[] args, string name)
        {
            int idx = Array.IndexOf(args, name);
            if (idx == -1 || idx + 1 >= args.Length)
                throw new ArgumentException($"Brak wymaganego parametru {name}");
            return args[idx + 1];
        }

        /// <summary>
        /// Buduje nową bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void BuildDatabase(string databaseDirectory, string scriptsDirectory)
        {
            string dbPath = Path.Combine(databaseDirectory, "database.fdb");

            if (!Directory.Exists(databaseDirectory))
                Directory.CreateDirectory(databaseDirectory);

            string connStr = new FbConnectionStringBuilder
            {
                Database = dbPath,
                ServerType = FbServerType.Embedded,
                ClientLibrary = Path.Combine(AppContext.BaseDirectory, "fb5", "fbclient.dll"),
                UserID = "SYSDBA",
                Password = "masterkey",
                Charset = "UTF8"
            }.ToString();

            // 1) Tworzenie bazy
            FbConnection.CreateDatabase(connStr, 16384, true);

            // 2) Wykonanie skryptów
            using var conn = new FbConnection(connStr);
            conn.Open();

            var domainScripts = Directory.GetFiles(scriptsDirectory, "*domain*.sql", SearchOption.AllDirectories);
            var tableScripts = Directory.GetFiles(scriptsDirectory, "*table*.sql", SearchOption.AllDirectories);
            var procedureScripts = Directory.GetFiles(scriptsDirectory, "*procedure*.sql", SearchOption.AllDirectories);

            ExecuteScripts(conn, domainScripts);
            ExecuteScripts(conn, tableScripts);
            ExecuteScripts(conn, procedureScripts);
        }

        /// <summary>
        /// Generuje skrypty metadanych z istniejącej bazy danych Firebird 5.0.
        /// </summary>
        public static void ExportScripts(string connectionString, string outputDirectory)
        {
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            using var conn = new FbConnection(connectionString);
            conn.Open();

            // === DOMENY ===
            var domainsSql = new StringBuilder();

            using (var cmd = new FbCommand(@"
                SELECT 
                    TRIM(RDB$FIELD_NAME) AS NAME, 
                    RDB$FIELD_TYPE,
                    RDB$FIELD_LENGTH,
                    RDB$FIELD_PRECISION,
                    RDB$FIELD_SCALE,
                    RDB$CHARACTER_LENGTH
                FROM RDB$FIELDS
                WHERE RDB$SYSTEM_FLAG = 0
                AND RDB$FIELD_NAME NOT LIKE 'RDB$%'
            ", conn))

            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var name = r.GetString(0);
                    int fieldType = r.GetInt32(1);
                    int length = r.IsDBNull(5) ? 0 : r.GetInt32(5);

                    string sqlType = fieldType switch
                    {
                        7 => "SMALLINT",
                        8 => "INTEGER",
                        16 => "BIGINT",
                        14 => $"CHAR({length})",
                        37 => $"VARCHAR({length})",
                        _ => "BLOB"
                    };

                    domainsSql.AppendLine($"CREATE DOMAIN {name} AS {sqlType};");
                    domainsSql.AppendLine();
                }
            }

            File.WriteAllText(Path.Combine(outputDirectory, "domains.sql"), domainsSql.ToString());

            // === TABELE ===
            var tablesSql = new StringBuilder();
            var tables = new List<string>();

            using (var cmd = new FbCommand(@"
                SELECT TRIM(RDB$RELATION_NAME)
                FROM RDB$RELATIONS
                WHERE RDB$SYSTEM_FLAG = 0
                AND RDB$VIEW_SOURCE IS NULL
            ", conn))

            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                    tables.Add(r.GetString(0));
            }

            foreach (var table in tables)
            {
                tablesSql.AppendLine($"CREATE TABLE {table} (");

                using var cmdCols = new FbCommand(@"
                    SELECT 
                        TRIM(rf.RDB$FIELD_NAME) as COLNAME,
                        TRIM(rf.RDB$FIELD_SOURCE) as DOMNAME,
                        f.RDB$FIELD_TYPE,
                        f.RDB$FIELD_SUB_TYPE,
                        f.RDB$FIELD_LENGTH,
                        f.RDB$CHARACTER_LENGTH,
                        f.RDB$FIELD_PRECISION,
                        f.RDB$FIELD_SCALE
                    FROM RDB$RELATION_FIELDS rf
                    JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE
                    WHERE rf.RDB$RELATION_NAME = @t
                    ORDER BY rf.RDB$FIELD_POSITION
                ", conn);

                cmdCols.Parameters.AddWithValue("t", table);

                using var r = cmdCols.ExecuteReader();

                var colLines = new List<string>();

                while (r.Read())
                {
                    string colName = r.GetString(0);
                    string domName = r.GetString(1);

                    int fieldType = r.GetInt32(2);
                    int charLen = r.IsDBNull(5) ? 0 : r.GetInt32(5);

                    string sqlType;

                    // domena użytkownika?
                    if (!domName.StartsWith("RDB$"))
                    {
                        sqlType = domName;
                    }
                    else
                    {
                        // rozwijamy typ bazowy
                        sqlType = fieldType switch
                        {
                            7 => "SMALLINT",
                            8 => "INTEGER",
                            16 => "BIGINT",
                            14 => $"CHAR({charLen})",
                            37 => $"VARCHAR({charLen})",
                            _ => "BLOB"
                        };
                    }

                    colLines.Add($"    {colName} {sqlType}");
                }

                tablesSql.AppendLine(string.Join(",\n", colLines));
                tablesSql.AppendLine(");");
                tablesSql.AppendLine();
            }

            File.WriteAllText(Path.Combine(outputDirectory, "tables.sql"), tablesSql.ToString());

            // === PROCEDURY ===
            var procSql = new StringBuilder();

            using (var cmd = new FbCommand(@"
                SELECT 
                    TRIM(RDB$PROCEDURE_NAME),
                    RDB$PROCEDURE_SOURCE,
                    RDB$PROCEDURE_TYPE
                FROM RDB$PROCEDURES
                WHERE RDB$SYSTEM_FLAG = 0
            ", conn))

            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    string name = r.GetString(0);
                    string source = r.IsDBNull(1) ? "" : r.GetString(1);

                    // Pobieramy parametry procedury
                    var inParams = new List<string>();
                    var outParams = new List<string>();

                    using var cmdParams = new FbCommand(@"
                        SELECT 
                            TRIM(p.RDB$PARAMETER_NAME) as PNAME,
                            TRIM(p.RDB$FIELD_SOURCE)  as DOMNAME,
                            f.RDB$FIELD_TYPE,
                            f.RDB$FIELD_SUB_TYPE,
                            f.RDB$FIELD_LENGTH,
                            f.RDB$CHARACTER_LENGTH,
                            f.RDB$FIELD_PRECISION,
                            f.RDB$FIELD_SCALE,
                            p.RDB$PARAMETER_TYPE
                        FROM RDB$PROCEDURE_PARAMETERS p
                        JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = p.RDB$FIELD_SOURCE
                        WHERE p.RDB$PROCEDURE_NAME = @pname
                        ORDER BY p.RDB$PARAMETER_TYPE, p.RDB$PARAMETER_NUMBER
                    ", conn);

                    cmdParams.Parameters.AddWithValue("pname", name);

                    using var pr = cmdParams.ExecuteReader();
                    while (pr.Read())
                    {
                        string paramName = pr.GetString(0);
                        string domName = pr.GetString(1);

                        int fieldType = pr.GetInt32(2);
                        int charLen = pr.IsDBNull(5) ? 0 : pr.GetInt32(5);

                        string sqlType;

                        // domena użytkownika?
                        if (!domName.StartsWith("RDB$"))
                        {
                            sqlType = domName;
                        }
                        else
                        {
                            // rozwijamy domenę systemową
                            sqlType = fieldType switch
                            {
                                7 => "SMALLINT",
                                8 => "INTEGER",
                                16 => "BIGINT",
                                14 => $"CHAR({charLen})",
                                37 => $"VARCHAR({charLen})",
                                _ => "BLOB"
                            };
                        }

                        int paramType = pr.GetInt32(8);

                        if (paramType == 0) // IN
                            inParams.Add($"{paramName} {sqlType}");
                        else                // OUT
                            outParams.Add($"{paramName} {sqlType}");
                    }

                    // Generowanie procedury
                    procSql.AppendLine($"CREATE OR ALTER PROCEDURE {name}");

                    if (inParams.Any())
                        procSql.AppendLine($" (\n    {string.Join(",\n    ", inParams)}\n)");

                    if (outParams.Any())
                    {
                        procSql.AppendLine("RETURNS (");
                        procSql.AppendLine($"    {string.Join(",\n    ", outParams)}");
                        procSql.AppendLine(")");
                    }
                    procSql.AppendLine("AS");
                    procSql.AppendLine(source.Trim().EndsWith(';')
                        ? source.Trim()
                        : source.Trim() + ";");

                    procSql.AppendLine();
                }
            }

            File.WriteAllText(Path.Combine(outputDirectory, "procedures.sql"), procSql.ToString());
        }

        /// <summary>
        /// Aktualizuje istniejącą bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void UpdateDatabase(string connectionString, string scriptsDirectory)
        {
            using var conn = new FbConnection(connectionString);
            conn.Open();

            foreach (var file in Directory.GetFiles(scriptsDirectory, "*.sql"))
            {
                string sql = File.ReadAllText(file);
                ExecuteBatch(conn, sql);
            }
        }

        /// <summary>
        /// Wykonuje zestaw skryptów SQL z podanych plików.
        /// </summary>
        private static void ExecuteScripts(FbConnection conn, IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                string sql = File.ReadAllText(file);
                ExecuteBatch(conn, sql);
            }
        }

        /// <summary>
        /// Wykonuje skrypt SQL, dzieląc go po ';'
        /// </summary>
        private static void ExecuteBatch(FbConnection conn, string script)
        {
            // Usuwamy komentarze i dzielimy na polecenia
            var batches = Regex.Split(script, @"(?<=;)\s*(?=(?:CREATE|ALTER|DROP|INSERT|UPDATE|DELETE))",
                                      RegexOptions.IgnoreCase);

            foreach (string batch in batches)
            {
                string sql = batch.Trim();
                if (string.IsNullOrWhiteSpace(sql))
                    continue;

                // Usunięcie końcowych średników
                if (sql.EndsWith(';'))
                    sql = sql[..^1];

                using var cmd = new FbCommand(sql, conn);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
