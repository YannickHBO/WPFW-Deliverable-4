using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;

namespace PortfolioApi.Tests.Support;

/// <summary>
/// A fresh SQLite database in memory for one unit test, with the tables and seed data of the model.
/// Each test makes its own (new instance per test), so tests never share data.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public SqliteTestDatabase()
    {
        _connection.Open();
        Options = new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite(_connection).Options;

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    /// <summary>Options for a DbContext on this database (for a hand-made DbContext subclass in a test).</summary>
    public DbContextOptions<PortfolioDbContext> Options { get; }

    /// <summary>
    /// A new DbContext on the same database. Use separate ones for Arrange, Act and Assert, so the
    /// Assert reads what is really stored and not what an earlier context still has in memory.
    /// </summary>
    public PortfolioDbContext CreateContext() => new(Options);

    public void Dispose() => _connection.Dispose();
}
