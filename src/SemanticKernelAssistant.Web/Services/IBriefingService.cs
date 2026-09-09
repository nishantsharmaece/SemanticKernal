using SemanticKernelAssistant.Web.Models;

namespace SemanticKernelAssistant.Web.Services;

public interface IBriefingService
{
    Task<DailyBriefingResponse> GetTodayAsync(CancellationToken cancellationToken);
}
