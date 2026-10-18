using Microsoft.EntityFrameworkCore;
using PriceCheck.Models;

namespace PriceCheck.Data;

public sealed class PriceCheckContext(DbContextOptions<PriceCheckContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PriceObservation> PriceObservations => Set<PriceObservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>().Property(product => product.Name).HasMaxLength(180).IsRequired();
        modelBuilder.Entity<Product>().Property(product => product.ProductUrl).HasMaxLength(2048);
        modelBuilder.Entity<Product>().Property(product => product.Category).HasMaxLength(80).IsRequired();
        modelBuilder.Entity<PriceObservation>().Property(observation => observation.Amount).HasPrecision(12, 2);
        modelBuilder.Entity<PriceObservation>().Property(observation => observation.Retailer).HasMaxLength(120).IsRequired();
        modelBuilder.Entity<PriceObservation>().Property(observation => observation.Currency).HasMaxLength(3).IsRequired();
        modelBuilder.Entity<Product>()
            .HasMany(product => product.PriceObservations)
            .WithOne(observation => observation.Product)
            .HasForeignKey(observation => observation.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public async Task EnsureCategoryColumnAsync(CancellationToken cancellationToken = default)
    {
        await Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA table_info('Products');";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetString(1) == "Category") return;
            }
        }
        finally
        {
            await Database.CloseConnectionAsync();
        }

        await Database.ExecuteSqlRawAsync(
            "ALTER TABLE Products ADD COLUMN Category TEXT NOT NULL DEFAULT 'Other';",
            cancellationToken);
    }

    public static async Task SeedAsync(PriceCheckContext database)
    {
        if (await database.Products.AnyAsync()) return;

        var now = DateTimeOffset.UtcNow;
        database.Products.AddRange(
            new Product
            {
                Name = "Sony WH-1000XM5 headphones", ProductUrl = "https://www.sony.com/", Category = "Electronics",
                PriceObservations =
                [
                    new() { Retailer = "Best Buy", Amount = 328.00m, ObservedAt = now.AddHours(-2) },
                    new() { Retailer = "Amazon", Amount = 348.00m, ObservedAt = now.AddDays(-2) }
                ]
            },
            new Product
            {
                Name = "Ninja Creami ice cream maker", ProductUrl = "https://www.ninjakitchen.com/", Category = "Kitchen Appliances",
                PriceObservations =
                [
                    new() { Retailer = "Target", Amount = 179.99m, ObservedAt = now.AddHours(-7) },
                    new() { Retailer = "Walmart", Amount = 199.00m, ObservedAt = now.AddDays(-1) }
                ]
            },
            new Product
            {
                Name = "Kindle Paperwhite", ProductUrl = "https://www.amazon.com/kindle-paperwhite/", Category = "Electronics",
                PriceObservations =
                [
                    new() { Retailer = "Amazon", Amount = 149.99m, ObservedAt = now.AddDays(-1) },
                    new() { Retailer = "Best Buy", Amount = 159.99m, ObservedAt = now.AddDays(-4) }
                ]
            });

        await database.SaveChangesAsync();
    }
}