using FamilyTree.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure.Tests;

/// <summary>
/// Builds a fresh in-memory SQLite database for a single test. The connection
/// must be kept open for the lifetime of the context — closing it drops the schema.
/// </summary>
internal sealed class TestDb : IDisposable
{
    public SqliteConnection Connection { get; }
    public FamilyTreeDbContext Context { get; }

    private TestDb(SqliteConnection connection, FamilyTreeDbContext context)
    {
        Connection = connection;
        Context = context;
    }

    public static TestDb Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<FamilyTreeDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new FamilyTreeDbContext(options);
        context.Database.EnsureCreated();
        return new TestDb(connection, context);
    }

    public void Dispose()
    {
        Context.Dispose();
        Connection.Dispose();
    }
}
