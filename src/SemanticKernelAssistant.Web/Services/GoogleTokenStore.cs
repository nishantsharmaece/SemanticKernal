using Microsoft.EntityFrameworkCore;
using SemanticKernelAssistant.Web.Data;

namespace SemanticKernelAssistant.Web.Services;

public interface IGoogleTokenStore
{
    OAuthToken? Find(string provider);

    Task<OAuthToken?> GetAsync(string provider, CancellationToken cancellationToken);

    Task SaveAsync(
        string provider,
        string accessToken,
        string? refreshToken,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken);

    Task DeleteAsync(string provider, CancellationToken cancellationToken);
}

public sealed class GoogleTokenStore(AssistantDbContext dbContext, IClock clock) : IGoogleTokenStore
{
    public OAuthToken? Find(string provider)
    {
        return dbContext.OAuthTokens
            .AsNoTracking()
            .SingleOrDefault(item => item.Provider == provider);
    }

    public Task<OAuthToken?> GetAsync(string provider, CancellationToken cancellationToken)
    {
        return dbContext.OAuthTokens.SingleOrDefaultAsync(
            item => item.Provider == provider,
            cancellationToken);
    }

    public async Task SaveAsync(
        string provider,
        string accessToken,
        string? refreshToken,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken)
    {
        var existing = await GetAsync(provider, cancellationToken);
        if (existing is null)
        {
            dbContext.OAuthTokens.Add(
                new OAuthToken
                {
                    Provider = provider,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    ExpiresAtUtc = expiresAtUtc,
                    UpdatedAtUtc = clock.UtcNow
                });
        }
        else
        {
            existing.AccessToken = accessToken;
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                existing.RefreshToken = refreshToken;
            }

            existing.ExpiresAtUtc = expiresAtUtc;
            existing.UpdatedAtUtc = clock.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string provider, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(provider, cancellationToken);
        if (existing is null)
        {
            return;
        }

        dbContext.OAuthTokens.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
