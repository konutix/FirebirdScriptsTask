using FirebirdSql.Data.FirebirdClient;
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
                UserID = "SYSDBA",
                Password = "masterkey",
                Charset = "UTF8"
            }.ToString();

            // 1) Tworzenie bazy
            FbConnection.CreateDatabase(connStr, 16384, true);

            // 2) Wykonanie skryptów
            using var conn = new FbConnection(connStr);
            conn.Open();

            foreach (var file in Directory.GetFiles(scriptsDirectory, "*.sql"))
            {
                string sql = File.ReadAllText(file);

                ExecuteBatch(conn, sql);
            }
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

            // 1) Domeny
            var domains = new List<object>();
            using (var cmd = new FbCommand(
                   @"SELECT RDB$FIELD_NAME, RDB$FIELD_TYPE, RDB$FIELD_LENGTH
                    FROM RDB$FIELDS
                    WHERE RDB$SYSTEM_FLAG = 0", conn))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    domains.Add(new
                    {
                        Name = r.GetString(0).Trim(),
                        Type = r.GetInt32(1),
                        Length = r.GetInt32(2)
                    });
                }
            }

            File.WriteAllText(Path.Combine(outputDirectory, "domains.json"),
                JsonSerializer.Serialize(domains, new JsonSerializerOptions { WriteIndented = true }));

            // 2) Tabele
            var tables = new List<object>();
            using (var cmd = new FbCommand(
                   @"SELECT RDB$RELATION_NAME
                    FROM RDB$RELATIONS
                    WHERE RDB$SYSTEM_FLAG = 0 AND RDB$VIEW_SOURCE IS NULL", conn))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    string tableName = r.GetString(0).Trim();

                    var columns = new List<object>();
                    using var cmdCol = new FbCommand(
                        @"SELECT RDB$FIELD_NAME, RDB$FIELD_SOURCE
                            FROM RDB$RELATION_FIELDS
                            WHERE RDB$RELATION_NAME = @t
                            ORDER BY RDB$FIELD_POSITION", conn);
                    cmdCol.Parameters.AddWithValue("t", tableName);

                    using var rc = cmdCol.ExecuteReader();
                    while (rc.Read())
                    {
                        columns.Add(new
                        {
                            Name = rc.GetString(0).Trim(),
                            Domain = rc.GetString(1).Trim()
                        });
                    }

                    tables.Add(new { Table = tableName, Columns = columns });
                }
            }

            File.WriteAllText(Path.Combine(outputDirectory, "tables.json"),
                JsonSerializer.Serialize(tables, new JsonSerializerOptions { WriteIndented = true }));

            // 3) Procedury
            var procedures = new List<object>();

            using (var cmd = new FbCommand(
                   @"SELECT RDB$PROCEDURE_NAME, RDB$PROCEDURE_SOURCE
                    FROM RDB$PROCEDURES
                    WHERE RDB$SYSTEM_FLAG = 0", conn))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    procedures.Add(new
                    {
                        Name = r.GetString(0).Trim(),
                        Source = r.GetString(1)
                    });
                }
            }

            File.WriteAllText(Path.Combine(outputDirectory, "procedures.json"),
                JsonSerializer.Serialize(procedures, new JsonSerializerOptions { WriteIndented = true }));
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
        /// Wykonuje skrypt SQL, dzieląc go po ';'
        /// </summary>
        private static void ExecuteBatch(FbConnection conn, string script)
        {
            // Usuwamy komentarze i dzielimy na polecenia
            var batches = Regex.Split(script, @"(?<=;)\s*(?=(CREATE|ALTER|DROP|INSERT|UPDATE|DELETE))",
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
