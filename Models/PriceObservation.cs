namespace PriceCheck.Models;

public sealed class PriceObservation
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public string Retailer { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;
}