using Microsoft.EntityFrameworkCore;
using PriceCheck.Data;
using PriceCheck.Models;

namespace PriceCheck.Services;

public sealed record PriceUpdateResult(string ProductName, decimal Price, string Currency, bool PriceChanged);
public sealed record CatalogImportResult(int ProductsFound, int ProductsAdded, int PricesChanged, string Retailer);

public sealed class PriceHistoryService(PriceCheckContext database, ProductPagePriceImporter importer)
{
    public async Task<PriceUpdateResult> ImportAndRecordAsync(
        string productUrl,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var imported = await importer.ImportAsync(productUrl, cancellationToken);
        var product = await database.Products
            .Include(item => item.PriceObservations)
            .SingleOrDefaultAsync(item => item.ProductUrl == imported.Url, cancellationToken);

        if (product is null)
        {
            product = new Product
            {
                Name = imported.Name,
                ProductUrl = imported.Url,
                Category = Models.ProductCategory.Normalize(category)
            };
            database.Products.Add(product);
        }
        else
        {
            product.Name = imported.Name;
            if (!string.IsNullOrWhiteSpace(category)) product.Category = Models.ProductCategory.Normalize(category);
        }

        var latest = product.PriceObservations.OrderByDescending(item => item.ObservedAt).FirstOrDefault();
        var changed = latest is null || latest.Amount != imported.Price || latest.Currency != imported.Currency;
        if (changed)
        {
            product.PriceObservations.Add(new PriceObservation
            {
                Retailer = imported.Retailer,
                Amount = imported.Price,
                Currency = imported.Currency,
                ObservedAt = DateTimeOffset.UtcNow
            });
        }

        await database.SaveChangesAsync(cancellationToken);
        await PruneExpiredHistoryAsync(product.Id, cancellationToken);
        return new PriceUpdateResult(product.Name, imported.Price, imported.Currency, changed);
    }

    public async Task<CatalogImportResult> ImportCatalogAsync(
        string catalogUrl,
        string? category,
        CancellationToken cancellationToken = default)
    {
        var catalog = await importer.ImportCatalogAsync(catalogUrl, cancellationToken);
        var added = 0;
        var changed = 0;
        foreach (var item in catalog.Products)
        {
            if (string.IsNullOrWhiteSpace(item.Url)) continue;
            var product = await database.Products
                .Include(existing => existing.PriceObservations)
                .SingleOrDefaultAsync(existing => existing.ProductUrl == item.Url, cancellationToken);

            if (product is null)
            {
                product = new Product
                {
                    Name = item.Name,
                    ProductUrl = item.Url,
                    Category = Models.ProductCategory.Normalize(category)
                };
                database.Products.Add(product);
                added++;
            }
            else
            {
                product.Name = item.Name;
                product.Category = Models.ProductCategory.Normalize(category);
            }

            var latest = product.PriceObservations.OrderByDescending(observation => observation.ObservedAt).FirstOrDefault();
            if (latest is null || latest.Amount != item.Price || latest.Currency != item.Currency)
            {
                product.PriceObservations.Add(new PriceObservation
                {
                    Retailer = catalog.Retailer,
                    Amount = item.Price,
                    Currency = item.Currency,
                    ObservedAt = DateTimeOffset.UtcNow
                });
                changed++;
            }
        }

        await database.SaveChangesAsync(cancellationToken);
        await PruneExpiredHistoryAsync(null, cancellationToken);
        return new CatalogImportResult(catalog.Products.Count, added, changed, catalog.Retailer);
    }

    public async Task RefreshTrackedProductsAsync(CancellationToken cancellationToken = default)
    {
        var productUrls = await database.Products
            .Where(product => product.ProductUrl != string.Empty)
            .Select(product => product.ProductUrl)
            .ToListAsync(cancellationToken);

        foreach (var productUrl in productUrls)
        {
            if (!Uri.TryCreate(productUrl, UriKind.Absolute, out var uri) || uri.AbsolutePath == "/") continue;

            try
            {
                await ImportAndRecordAsync(productUrl, null, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"PriceCheck could not refresh {productUrl}: {exception.Message}");
            }
        }

        await PruneExpiredHistoryAsync(null, cancellationToken);
    }

    private async Task PruneExpiredHistoryAsync(int? productId, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMonths(-12);
        var candidates = database.PriceObservations.AsQueryable();
        if (productId.HasValue) candidates = candidates.Where(observation => observation.ProductId == productId.Value);
        var expiredObservations = (await candidates.ToListAsync(cancellationToken))
            .Where(observation => observation.ObservedAt < cutoff)
            .ToList();
        database.PriceObservations.RemoveRange(expiredObservations);
        await database.SaveChangesAsync(cancellationToken);
    }
}