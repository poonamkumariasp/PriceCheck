using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using PriceCheck.Data;
using PriceCheck.Models;
using PriceCheck.Services;
using ProductCategory = PriceCheck.Models.ProductCategory;

namespace PriceCheck.Pages;

public class IndexModel : PageModel
{
    private readonly PriceCheckContext _database;
    private readonly PriceHistoryService _priceHistory;

    public IndexModel(PriceCheckContext database, PriceHistoryService priceHistory)
    {
        _database = database;
        _priceHistory = priceHistory;
    }

    public IList<Product> Products { get; private set; } = [];

    public IReadOnlyList<string> Categories => ProductCategory.All;

    [BindProperty(SupportsGet = true, Name = "category")]
    public string? CategoryFilter { get; set; }

    [BindProperty]
    public string ProductName { get; set; } = string.Empty;

    [BindProperty]
    public string RetailerName { get; set; } = string.Empty;

    [BindProperty]
    public string ProductUrl { get; set; } = string.Empty;

    [BindProperty]
    public string CatalogUrl { get; set; } = string.Empty;

    [BindProperty]
    public string Category { get; set; } = "Other";

    [BindProperty]
    public string Currency { get; set; } = "GBP";

    [BindProperty]
    public decimal Price { get; set; }

    public string? StatusMessage { get; private set; }

    public static string FormatPrice(decimal amount, string currency)
    {
        var cultureName = currency.ToUpperInvariant() switch
        {
            "GBP" => "en-GB",
            "USD" => "en-US",
            "EUR" => "en-IE",
            "CAD" => "en-CA",
            "AUD" => "en-AU",
            "JPY" => "ja-JP",
            _ => null
        };

        return cultureName is null
            ? $"{currency.ToUpperInvariant()} {amount:0.00}"
            : amount.ToString("C", CultureInfo.GetCultureInfo(cultureName));
    }

    public async Task OnGetAsync()
    {
        if (ProductCategory.All.Contains(CategoryFilter ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            Category = ProductCategory.Normalize(CategoryFilter);
        }
        await LoadProductsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(ProductName) || string.IsNullOrWhiteSpace(RetailerName) || Price <= 0 ||
            string.IsNullOrWhiteSpace(Currency) || Currency.Length != 3 || !Currency.All(char.IsAsciiLetter))
        {
            ModelState.AddModelError(string.Empty, "Enter a product, retailer, positive price, and 3-letter currency code.");
            await LoadProductsAsync();
            return Page();
        }

        var product = new Product
        {
            Name = ProductName.Trim(),
            ProductUrl = ProductUrl?.Trim() ?? string.Empty,
            Category = ProductCategory.Normalize(Category),
            PriceObservations =
            [
                new PriceObservation
                {
                    Retailer = RetailerName.Trim(),
                    Amount = Price,
                    Currency = Currency.ToUpperInvariant(),
                    ObservedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        _database.Products.Add(product);
        await _database.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImportAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ProductUrl))
        {
            ModelState.AddModelError(nameof(ProductUrl), "Paste a store product page URL to import it.");
            await LoadProductsAsync();
            return Page();
        }

        try
        {
            var result = await _priceHistory.ImportAndRecordAsync(ProductUrl.Trim(), Category, cancellationToken);
            TempData["ImportMessage"] = result.PriceChanged
                ? $"Added {result.ProductName} at {result.Currency} {result.Price:0.00}. Price tracking starts now; future changes are saved automatically."
                : $"{result.ProductName} is already up to date at {result.Currency} {result.Price:0.00}.";
            return RedirectToPage();
        }
        catch (ProductImportException exception)
        {
            ModelState.AddModelError(nameof(ProductUrl), exception.Message);
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError(nameof(ProductUrl), "The store blocked or could not serve this page. Try a supported product page or enter the price manually.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ModelState.AddModelError(nameof(ProductUrl), "The store page took too long to respond. Please try again later.");
        }

        await LoadProductsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostImportCatalogAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CatalogUrl))
        {
            ModelState.AddModelError(nameof(CatalogUrl), "Paste a retailer category or products-list URL.");
            await LoadProductsAsync();
            return Page();
        }

        try
        {
            var result = await _priceHistory.ImportCatalogAsync(CatalogUrl.Trim(), Category, cancellationToken);
            TempData["ImportMessage"] = $"Scanned {result.Retailer}: {result.ProductsFound} structured products found, {result.ProductsAdded} added, {result.PricesChanged} new or changed prices saved.";
            return RedirectToPage(new { category = CategoryFilter });
        }
        catch (ProductImportException exception)
        {
            ModelState.AddModelError(nameof(CatalogUrl), exception.Message);
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError(nameof(CatalogUrl), "The retailer blocked or could not serve this catalog page. Try a supported category page or add products individually.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ModelState.AddModelError(nameof(CatalogUrl), "The retailer page took too long to respond. Please try again later.");
        }

        await LoadProductsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSetCategoryAsync(int productId, string category)
    {
        var product = await _database.Products.FindAsync(productId);
        if (product is null) return NotFound();

        product.Category = ProductCategory.Normalize(category);
        await _database.SaveChangesAsync();
        return RedirectToPage(new { category = CategoryFilter });
    }

    private async Task LoadProductsAsync()
    {
        var products = await _database.Products
            .Include(product => product.PriceObservations)
            .ToListAsync();
        Products = products
            .OrderByDescending(product => product.PriceObservations.Max(observation => observation.ObservedAt))
            .ToList();

    }
}
