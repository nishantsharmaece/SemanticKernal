using System.Text.Json;
using Microsoft.Extensions.Options;
using SemanticKernelAssistant.Web.Data;
using SemanticKernelAssistant.Web.Models;

namespace SemanticKernelAssistant.Web.Services;

public interface IGoogleConnection
{
    bool IsConfigured { get; }

    bool IsConnected { get; }

    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);
}

public interface IGoogleAuthService
{
    bool IsConfigured { get; }

    string GetAuthorizeUrl();

    Task ExchangeCodeAsync(string code, CancellationToken cancellationToken);

    Task LogoutAsync(CancellationToken cancellationToken);

    Task<GoogleStatusResponse> GetStatusAsync(CancellationToken cancellationToken);
}

public sealed class GoogleConnection(
    IGoogleTokenStore tokenStore,
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleOptions> options,
    IClock clock,
    ILogger<GoogleConnection> logger) : IGoogleConnection, IGoogleAuthService
{
    public const string NotConnectedMessage =
        "Google account not connected. Click Connect Google in the sidebar.";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ClientId);

    public bool IsConnected => IsUsable(tokenStore.Find(OAuthProviders.Google));

    public string GetAuthorizeUrl()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Google is not configured.");
        }

        var settings = options.Value;
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = settings.ClientId,
            ["redirect_uri"] = settings.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = settings.Scopes,
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        };

        return "https://accounts.google.com/o/oauth2/v2/auth"
            + QueryString.Create(query);
    }

    public async Task ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("A Google authorization code is required.", nameof(code));
        }

        var settings = options.Value;
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret,
                ["redirect_uri"] = settings.RedirectUri,
                ["grant_type"] = "authorization_code"
            });

        await SaveTokenResponseAsync(content, existingRefreshToken: null, cancellationToken);
    }

    public Task LogoutAsync(CancellationToken cancellationToken)
    {
        return tokenStore.DeleteAsync(OAuthProviders.Google, cancellationToken);
    }

    public async Task<GoogleStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return new GoogleStatusResponse(
                false,
                false,
                "Set Google ClientId in User Secrets first.");
        }

        var token = await tokenStore.GetAsync(OAuthProviders.Google, cancellationToken);
        if (!IsUsable(token))
        {
            return new GoogleStatusResponse(
                true,
                false,
                "Click Connect Google in the sidebar.");
        }

        return new GoogleStatusResponse(true, true, "Google Calendar connected.");
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var token = await tokenStore.GetAsync(OAuthProviders.Google, cancellationToken);
        if (token is null)
        {
            return null;
        }

        if (token.ExpiresAtUtc >= clock.UtcNow.AddMinutes(2))
        {
            return token.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            return null;
        }

        try
        {
            var settings = options.Value;
            using var content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["refresh_token"] = token.RefreshToken,
                    ["client_id"] = settings.ClientId,
                    ["client_secret"] = settings.ClientSecret,
                    ["grant_type"] = "refresh_token"
                });

            return await SaveTokenResponseAsync(content, token.RefreshToken, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Google access token refresh failed.");
            return null;
        }
    }

    private async Task<string> SaveTokenResponseAsync(
        HttpContent content,
        string? existingRefreshToken,
        CancellationToken cancellationToken)
    {
        using var response = await httpClientFactory
            .CreateClient("GoogleOAuth")
            .PostAsync("token", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Google token request failed.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var accessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Google token request failed.");
        var expiresIn = root.TryGetProperty("expires_in", out var expiresElement)
            ? expiresElement.GetInt32()
            : 3600;
        var refreshToken = root.TryGetProperty("refresh_token", out var refreshElement)
            ? refreshElement.GetString()
            : existingRefreshToken;

        await tokenStore.SaveAsync(
            OAuthProviders.Google,
            accessToken,
            refreshToken,
            clock.UtcNow.AddSeconds(expiresIn),
            cancellationToken);

        return accessToken;
    }

    private bool IsUsable(OAuthToken? token)
    {
        if (token is null)
        {
            return false;
        }

        return token.ExpiresAtUtc > clock.UtcNow
            || !string.IsNullOrWhiteSpace(token.RefreshToken);
    }
}
