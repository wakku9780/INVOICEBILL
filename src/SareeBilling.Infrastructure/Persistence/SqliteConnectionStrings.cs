using Microsoft.Data.Sqlite;

namespace SareeBilling.Infrastructure.Persistence;

internal static class SqliteConnectionStrings
{
    public static string For(string databasePath, bool pooling = true) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = pooling
        }.ToString();
}
