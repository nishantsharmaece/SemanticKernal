using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace SemanticKernelAssistant.Web.Services;

public interface IOllamaModelWarmer
{
    Task<bool> WarmAsync(CancellationToken cancellationToken);
}

public sealed class OllamaModelWarmer(
    HttpClient httpClient,
    IOptions<AssistantOptions> options,
    ILogger<OllamaModelWarmer> logger) : IOllamaModelWarmer
{
    public async Task<bool> WarmAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var request = new
        {
            model = settings.Model,
            prompt = string.Empty,
            stream = false,
            keep_alive = $"{settings.KeepAliveMinutes}m"
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "api/generate",
                request,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "Ollama model {Model} is warm for {Minutes} minutes.",
                    settings.Model,
                    settings.KeepAliveMinutes);
                return true;
            }

            logger.LogWarning(
                "Ollama warm-up returned HTTP {StatusCode}.",
                (int)response.StatusCode);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ollama model warm-up failed; startup will continue.");
            return false;
        }
    }
}

public sealed class OllamaWarmupHostedService(
    IOllamaModelWarmer modelWarmer,
    IOptions<AssistantOptions> options,
    ILogger<OllamaWarmupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WarmWithoutStoppingApplicationAsync(stoppingToken);

        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(options.Value.WarmupIntervalMinutes));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await WarmWithoutStoppingApplicationAsync(stoppingToken);
        }
    }

    private async Task WarmWithoutStoppingApplicationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await modelWarmer.WarmAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unexpected Ollama warm-up failure.");
        }
    }
}
