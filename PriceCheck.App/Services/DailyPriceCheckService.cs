using Microsoft.Extensions.DependencyInjection;

namespace PriceCheck.Services;

public sealed class DailyPriceCheckService(IServiceScopeFactory scopeFactory, ILogger<DailyPriceCheckService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var history = scope.ServiceProvider.GetRequiredService<PriceHistoryService>();
                await history.RefreshTrackedProductsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The daily product price refresh failed.");
            }
        }
    }
}