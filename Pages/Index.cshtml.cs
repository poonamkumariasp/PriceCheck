using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PriceCheck.Data;
using PriceCheck.Models;

namespace PriceCheck.Pages;

public class IndexModel : PageModel
{
    private readonly PriceCheckContext _database;

    public IndexModel(PriceCheckContext database)
    {
        _database = database;
    }

    public IList<Product> Products { get; private set; } = [];

    [BindProperty]
    public string ProductName { get; set; } = string.Empty;

    [BindProperty]
    public string RetailerName { get; set; } = string.Empty;

    [BindProperty]
    public string ProductUrl { get; set; } = string.Empty;

    [BindProperty]
    public decimal Price { get; set; }

    public async Task OnGetAsync()
    {
        await LoadProductsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(ProductName) || string.IsNullOrWhiteSpace(RetailerName) || Price <= 0)
        {
            ModelState.AddModelError(string.Empty, "Add a product, retailer, and price greater than zero.");
            await LoadProductsAsync();
            return Page();
        }

        var product = new Product
        {
            Name = ProductName.Trim(),
            ProductUrl = ProductUrl?.Trim() ?? string.Empty,
            PriceObservations =
            [
                new PriceObservation
                {
                    Retailer = RetailerName.Trim(),
                    Amount = Price,
                    Currency = "USD",
                    ObservedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        _database.Products.Add(product);
        await _database.SaveChangesAsync();
        return RedirectToPage();
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
