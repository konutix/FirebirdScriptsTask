using System;
using System.IO;
using Xunit;
using FirebirdSql.Data.FirebirdClient;

namespace DbMetaTool.Tests
{
    public class UpdateTests
    {
        private (string dbPath, string connStr) CreateDb()
        {
            string dbDir = Path.Combine(Path.GetTempPath(), "DbMetaToolTests", "update-db");
            string scriptsDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Scripts1");

            if (Directory.Exists(dbDir))
                Directory.Delete(dbDir, true);

            Directory.CreateDirectory(dbDir);

            Program.BuildDatabase(dbDir, scriptsDir);

            string dbPath = Path.Combine(dbDir, "database.fdb");

            var cs = new FbConnectionStringBuilder
            {
                ServerType = FbServerType.Embedded,
                ClientLibrary = Path.Combine(AppContext.BaseDirectory, "fb5", "fbclient.dll"),
                Database = dbPath,
                UserID = "SYSDBA",
                Password = "masterkey",
                Charset = "UTF8"
            }.ToString();

            return (dbPath, cs);
        }

        [Fact]
        public void UpdateDatabase_AltersTableAndCreatesProcedure()
        {
            // Arrange
            var (_, connStr) = CreateDb();

            string updateDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Scripts2");

            // Act
            Program.UpdateDatabase(connStr, updateDir);

            // Assert changes
            using var conn = new FbConnection(connStr);
            conn.Open();

            // Column added?
            using (var cmd = new FbCommand(
                "SELECT COUNT(*) FROM RDB$RELATION_FIELDS WHERE RDB$FIELD_NAME = 'PRICE'", conn))
            {
                int count = Convert.ToInt32(cmd.ExecuteScalar());
                Assert.Equal(1, count);
            }

            // New procedure?
            using (var cmd = new FbCommand(
                "SELECT COUNT(*) FROM RDB$PROCEDURES WHERE RDB$PROCEDURE_NAME = 'GET_PRODUCT_COUNT'", conn))
            {
                int count = Convert.ToInt32(cmd.ExecuteScalar());
                Assert.Equal(1, count);
            }
        }
    }
}
