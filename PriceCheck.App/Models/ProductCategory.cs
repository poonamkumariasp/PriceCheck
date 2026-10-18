namespace PriceCheck.Models;

public static class ProductCategory
{
    public static readonly IReadOnlyList<string> All =
    [
        "Food & Drinks",
        "Clothing & Shoes",
        "Kitchen Appliances",
        "Electronics",
        "Home & Garden",
        "Health & Beauty",
        "Toys & Games",
        "Other"
    ];

    public static string Normalize(string? category) =>
        All.FirstOrDefault(item => string.Equals(item, category, StringComparison.OrdinalIgnoreCase)) ?? "Other";
}