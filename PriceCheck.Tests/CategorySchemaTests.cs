using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PriceCheck.Data;
using Xunit;

namespace PriceCheck.Tests;

public sealed class CategorySchemaTests
{
    [Fact]
    public async Task EnsureCategoryColumnAsync_AddsCategoryToExistingProductsTable()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PriceCheckContext>().UseSqlite(connection).Options;
        await using var database = new PriceCheckContext(options);
        await database.Database.ExecuteSqlRawAsync("CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, ProductUrl TEXT NOT NULL, AddedAt TEXT NOT NULL);");
        await database.Database.ExecuteSqlRawAsync("INSERT INTO Products (Name, ProductUrl, AddedAt) VALUES ('Existing product', '', '2026-01-01T00:00:00Z');");

        await database.EnsureCategoryColumnAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Category FROM Products LIMIT 1;";
        Assert.Equal("Other", await command.ExecuteScalarAsync());
    }
}