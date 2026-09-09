using SemanticKernelAssistant.Web.Services;

namespace SemanticKernelAssistant.Web.Background;

public sealed class ReminderDispatchHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReminderDispatchHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IReminderStore>();
                var fired = await store.FireDueAsync(stoppingToken);
                if (fired > 0)
                {
                    logger.LogInformation("Fired {Count} due reminder(s).", fired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to dispatch due reminders.");
            }
        }
    }
}
