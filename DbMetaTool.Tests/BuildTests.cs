using FirebirdSql.Data.FirebirdClient;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using System;
using System.Data.Common;
using System.IO;
using Xunit;

namespace DbMetaTool.Tests
{
    public class BuildTests
    {
        private static string CreateTempDir(string name)
        {
            string dir = Path.Combine(Path.GetTempPath(), "DbMetaToolTests", name);
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void BuildDatabase_CreatesDatabase_WithTablesDomainsProcedures()
        {
            // Arrange
            string dbDir = CreateTempDir("build-db");
            string scriptsDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Scripts1");

            // Act
            Program.BuildDatabase(dbDir, scriptsDir);

            // Assert
            string dbPath = Path.Combine(dbDir, "database.fdb");
            Assert.True(File.Exists(dbPath));

            var cs = new FbConnectionStringBuilder
            {
                ServerType = FbServerType.Embedded,
                ClientLibrary = Path.Combine(AppContext.BaseDirectory, "fb5", "fbclient.dll"),
                Database = dbPath,
                UserID = "SYSDBA",
                Password = "masterkey",
                Charset = "UTF8"
            }.ToString();

            using var conn = new FbConnection(cs);
            conn.Open();

            // Check table existence
            using (var cmd = new FbCommand(
                "SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'PRODUCTS'", conn))
            {
                int count = Convert.ToInt32(cmd.ExecuteScalar());
                Assert.Equal(1, count);
            }

            // Check domain
            using (var cmd = new FbCommand(
                "SELECT COUNT(*) FROM RDB$FIELDS WHERE RDB$FIELD_NAME = 'DM_NAME'", conn))
            {
                int count = Convert.ToInt32(cmd.ExecuteScalar());
                Assert.Equal(1, count);
            }

            // Check procedure
            using (var cmd = new FbCommand(
                "SELECT COUNT(*) FROM RDB$PROCEDURES WHERE RDB$PROCEDURE_NAME = 'ADD_PRODUCT'", conn))
            {
                int count = Convert.ToInt32(cmd.ExecuteScalar());
                Assert.Equal(1, count);
            }
        }
    }
}
