using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PriceCheck.Data;
using PriceCheck.Models;
using PriceCheck.Services;
using Xunit;

namespace PriceCheck.Tests;

public sealed class PriceHistoryServiceTests
{
    [Fact]
    public async Task ImportAndRecordAsync_AddsObservationOnlyWhenPriceChanges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PriceCheckContext>().UseSqlite(connection).Options;
        await using var database = new PriceCheckContext(options);
        await database.Database.EnsureCreatedAsync();
        var trackedProduct = new Product
        {
            Name = "Filtered British Whole Milk",
            ProductUrl = "https://1.1.1.1/product/milk",
            PriceObservations =
            [
                new()
                {
                    Retailer = "grocer.example",
                    Amount = 1.59m,
                    Currency = "GBP",
                    ObservedAt = DateTimeOffset.UtcNow.AddMonths(-13)
                }
            ]
        };
        database.Products.Add(trackedProduct);
        await database.SaveChangesAsync();
        var handler = new StubPageHandler("""
            <script type="application/ld+json">
            {"@type":"Product","name":"Filtered British Whole Milk","offers":{"price":"1.69","priceCurrency":"GBP"}}
            </script>
            """);
        using var client = new HttpClient(handler);
        var importer = new ProductPagePriceImporter(client);
        var history = new PriceHistoryService(database, importer);
        const string url = "https://1.1.1.1/product/milk";

        var firstCheck = await history.ImportAndRecordAsync(url, "Food & Drinks");
        var secondCheck = await history.ImportAndRecordAsync(url, "Food & Drinks");
        handler.Html = """
            <script type="application/ld+json">
            {"@type":"Product","name":"Filtered British Whole Milk","offers":{"price":"1.79","priceCurrency":"GBP"}}
            </script>
            """;
        var changedCheck = await history.ImportAndRecordAsync(url);

        Assert.True(firstCheck.PriceChanged);
        Assert.False(secondCheck.PriceChanged);
        Assert.True(changedCheck.PriceChanged);
        Assert.Equal(2, await database.PriceObservations.CountAsync());
        Assert.Equal([1.69m, 1.79m], await database.PriceObservations.OrderBy(item => item.Amount).Select(item => item.Amount).ToArrayAsync());
        Assert.Equal("Food & Drinks", (await database.Products.SingleAsync()).Category);
    }

    private sealed class StubPageHandler(string html) : HttpMessageHandler
    {
        public string Html { get; set; } = html;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(Html),
                RequestMessage = request
            };
            return Task.FromResult(response);
        }
    }
}