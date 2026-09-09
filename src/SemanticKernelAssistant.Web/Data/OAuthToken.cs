namespace SemanticKernelAssistant.Web.Data;

public sealed class OAuthToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Provider { get; set; } = OAuthProviders.Google;

    public string AccessToken { get; set; } = string.Empty;

    public string? RefreshToken { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class OAuthProviders
{
    public const string Google = "Google";
}
