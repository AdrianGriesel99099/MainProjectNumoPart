using MainProjectNumoPart.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Tests
{
    public static class TestDbContextFactory
    {
        // Real SQLite (not EF's InMemory provider) so unique-index and constraint
        // behavior in tests matches what production SQLite actually enforces.
        public static AppDbContext CreateInMemory()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new AppDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        // A second context against the SAME open connection as an existing CreateInMemory()
        // context, standing in for a concurrent request against the same production database --
        // ":memory:" SQLite is scoped to the connection object itself, so a fresh connection
        // string would just open an empty, unrelated database instead.
        public static AppDbContext CreateSecondaryContext(AppDbContext primary)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(primary.Database.GetDbConnection())
                .Options;

            return new AppDbContext(options);
        }
    }
}
