using PriceCheck.Services;
using System.Net;
using Xunit;

namespace PriceCheck.Tests;

public sealed class ProductPagePriceImporterTests
{
    [Fact]
    public void ParseProduct_ExtractsAldiProductNamePriceAndCurrency()
    {
        const string html = """
            <script type="application/ld+json">
            {"@context":"https://schema.org/","@type":"Product","name":"Filtered British Whole Milk","offers":{"@type":"Offer","price":"1.69","priceCurrency":"GBP"}}
            </script>
            """;

        var product = ProductPagePriceImporter.ParseProduct(html);

        Assert.Equal("Filtered British Whole Milk", product.Name);
        Assert.Equal(1.69m, product.Price);
        Assert.Equal("GBP", product.Currency);
    }

    [Fact]
    public void ParseProduct_RejectsPagesWithoutStructuredPrice()
    {
        var exception = Assert.Throws<ProductImportException>(() =>
            ProductPagePriceImporter.ParseProduct("<html><title>Not a product</title></html>"));

        Assert.Contains("does not expose", exception.Message);
    }

    [Fact]
    public async Task ImportAsync_RejectsRedirectToPrivateAddress()
    {
        using var client = new HttpClient(new RedirectHandler());
        var importer = new ProductPagePriceImporter(client);

        var exception = await Assert.ThrowsAsync<ProductImportException>(() =>
            importer.ImportAsync("https://1.1.1.1/product/milk"));

        Assert.Contains("public website", exception.Message);
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("http://127.0.0.1/admin") },
                RequestMessage = request
            });
        }
    }
}