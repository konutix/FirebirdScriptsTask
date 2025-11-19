using System;
using System.IO;
using Xunit;
using FirebirdSql.Data.FirebirdClient;

namespace DbMetaTool.Tests
{
    public class ExportTests
    {
        private static string CreateDbWithScripts()
        {
            string dbDir = Path.Combine(Path.GetTempPath(), "DbMetaToolTests", "export-db");
            string scriptsDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Scripts1");

            if (Directory.Exists(dbDir))
                Directory.Delete(dbDir, true);

            Directory.CreateDirectory(dbDir);

            Program.BuildDatabase(dbDir, scriptsDir);

            return Path.Combine(dbDir, "database.fdb");
        }

        [Fact]
        public void ExportScripts_CreatesSqlFiles_WithCorrectMetadata()
        {
            // Arrange
            string dbPath = CreateDbWithScripts();
            string outputDir = Path.Combine(Path.GetTempPath(), "DbMetaToolTests", "export-out");

            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);

            string cs = new FbConnectionStringBuilder
            {
                ServerType = FbServerType.Embedded,
                ClientLibrary = Path.Combine(AppContext.BaseDirectory, "fb5", "fbclient.dll"),
                Database = dbPath,
                UserID = "SYSDBA",
                Password = "masterkey",
                Charset = "UTF8"
            }.ToString();

            // Act
            Program.ExportScripts(cs, outputDir);

            // Assert — pliki istnieją
            string domainsFile = Path.Combine(outputDir, "domains.sql");
            string tablesFile = Path.Combine(outputDir, "tables.sql");
            string proceduresFile = Path.Combine(outputDir, "procedures.sql");

            Assert.True(File.Exists(domainsFile), "Brak pliku domains.sql");
            Assert.True(File.Exists(tablesFile), "Brak pliku tables.sql");
            Assert.True(File.Exists(proceduresFile), "Brak pliku procedures.sql");

            // Weryfikujemy treść domen
            string domainsSql = File.ReadAllText(domainsFile);
            Assert.Contains("CREATE DOMAIN DM_NAME AS VARCHAR(100)", domainsSql);

            // Weryfikujemy treść tabel
            string tablesSql = File.ReadAllText(tablesFile);
            Assert.Contains("CREATE TABLE PRODUCTS", tablesSql);
            Assert.Contains("ID INTEGER", tablesSql);
            Assert.Contains("NAME DM_NAME", tablesSql);

            // Weryfikujemy treść procedur
            string procSql = File.ReadAllText(proceduresFile);

            // procedura istnieje?
            Assert.Contains("CREATE OR ALTER PROCEDURE ADD_PRODUCT", procSql);

            // parametry wejściowe?
            Assert.Contains("    P_ID INTEGER,", procSql.Replace("\r", ""));
            Assert.Contains("P_NAME DM_NAME", procSql);

            // ciało procedury?
            Assert.Contains("INSERT INTO PRODUCTS", procSql);
        }
    }
}
