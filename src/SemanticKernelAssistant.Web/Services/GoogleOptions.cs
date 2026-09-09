namespace SemanticKernelAssistant.Web.Services;

public sealed class GoogleOptions
{
    public const string SectionName = "Google";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = "http://localhost:5118/api/auth/google/callback";

    public string Scopes { get; set; } =
        "openid email https://www.googleapis.com/auth/calendar.events";
}
