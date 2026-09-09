using SemanticKernelAssistant.Web.Services;

namespace SemanticKernelAssistant.Tests.Services;

internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTime UtcNow => now.UtcDateTime;

    public DateTimeOffset Now => now;
}
