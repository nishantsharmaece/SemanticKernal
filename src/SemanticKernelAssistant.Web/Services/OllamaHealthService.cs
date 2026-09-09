using System.Text.Json;
using Microsoft.Extensions.Options;
using SemanticKernelAssistant.Web.Models;

namespace SemanticKernelAssistant.Web.Services;

public interface IOllamaHealthService
{
    Task<OllamaStatusResponse> GetStatusAsync(CancellationToken cancellationToken);
}

public sealed class OllamaHealthService(
    IHttpClientFactory httpClientFactory,
    IOptions<AssistantOptions> options) : IOllamaHealthService
{
    public async Task<OllamaStatusResponse> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        try
        {
            using var response = await httpClientFactory
                .CreateClient("OllamaHealth")
                .GetAsync("api/tags", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Unavailable(
                    settings,
                    $"Ollama returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);

            var modelInstalled = document.RootElement
                .GetProperty("models")
                .EnumerateArray()
                .Any(model =>
                    string.Equals(
                        model.GetProperty("name").GetString(),
                        settings.Model,
                        StringComparison.OrdinalIgnoreCase));

            return modelInstalled
                ? new OllamaStatusResponse(true, settings.Endpoint, settings.Model, null)
                : Unavailable(
                    settings,
                    $"Model '{settings.Model}' is not installed. Run: ollama pull {settings.Model}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Unavailable(
                settings,
                "Ollama is not reachable. Install and start Ollama, then pull the configured model.");
        }
    }

    private static OllamaStatusResponse Unavailable(
        AssistantOptions settings,
        string message)
    {
        return new OllamaStatusResponse(false, settings.Endpoint, settings.Model, message);
    }
}
