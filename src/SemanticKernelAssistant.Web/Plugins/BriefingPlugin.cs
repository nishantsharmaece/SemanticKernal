using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;
using SemanticKernelAssistant.Web.Services;

namespace SemanticKernelAssistant.Web.Plugins;

public sealed class BriefingPlugin(IBriefingService briefingService)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [KernelFunction]
    [Description("Get today's local personal-assistant briefing: overdue items and reminders due today. Use when the user asks for a briefing, agenda, or what they have today.")]
    public async Task<string> GetDailyBriefingAsync()
    {
        var briefing = await briefingService.GetTodayAsync(CancellationToken.None);
        return JsonSerializer.Serialize(briefing, JsonOptions);
    }
}
