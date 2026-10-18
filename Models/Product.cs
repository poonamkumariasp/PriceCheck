namespace PriceCheck.Models;

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProductUrl { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<PriceObservation> PriceObservations { get; set; } = [];
}