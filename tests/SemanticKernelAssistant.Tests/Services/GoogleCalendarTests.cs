using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SemanticKernelAssistant.Tests.Data;
using SemanticKernelAssistant.Web.Data;
using SemanticKernelAssistant.Web.Services;

namespace SemanticKernelAssistant.Tests.Services;

public sealed class GoogleCalendarTests
{
    [Fact]
    public async Task GetAccessTokenRefreshesWhenExpiryIsWithinTwoMinutes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeClock(
            new DateTimeOffset(2026, 9, 6, 11, 0, 0, TimeSpan.FromHours(5.5)));
        var store = new GoogleTokenStore(database.Context, clock);
        await store.SaveAsync(
            OAuthProviders.Google,
            "expired-access",
            "refresh-token",
            clock.UtcNow.AddMinutes(1),
            CancellationToken.None);

        HttpRequestMessage? captured = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            captured = request;
            return JsonResponse(
                HttpStatusCode.OK,
                """{"access_token":"refreshed-access","expires_in":3600,"token_type":"Bearer"}""");
        });
        var connection = CreateConnection(store, clock, handler);

        var accessToken = await connection.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("refreshed-access", accessToken);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("/token", captured.RequestUri?.AbsolutePath);
        var saved = await store.GetAsync(OAuthProviders.Google, CancellationToken.None);
        Assert.Equal("refreshed-access", saved?.AccessToken);
        Assert.Equal("refresh-token", saved?.RefreshToken);
    }

    [Fact]
    public async Task CreateInvitePostsCalendarEventWithSendUpdates()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            captured = request;
            body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "id": "event-1",
                  "summary": "Project review",
                  "htmlLink": "https://www.google.com/calendar/event?eid=event-1",
                  "start": { "dateTime": "2026-09-07T10:00:00+05:30" },
                  "end": { "dateTime": "2026-09-07T10:30:00+05:30" },
                  "attendees": [{ "email": "teammate@gmail.com" }]
                }
                """);
        });
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.googleapis.com/calendar/v3/")
        };
        var service = new CalendarInviteService(
            new StubGoogleConnection("access-token"),
            new StubHttpClientFactory("GoogleCalendar", client),
            new FakeClock(new DateTimeOffset(2026, 9, 6, 11, 0, 0, TimeSpan.FromHours(5.5))));

        var result = await service.CreateInviteAsync(
            "Project review",
            new DateTime(2026, 9, 7, 4, 30, 0, DateTimeKind.Utc),
            30,
            ["teammate@gmail.com"],
            "Planning",
            CancellationToken.None);

        Assert.Equal("event-1", result.Id);
        Assert.Equal("Project review", result.Subject);
        Assert.Contains("teammate@gmail.com", result.Attendees);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Contains("calendars/primary/events", captured.RequestUri?.ToString());
        Assert.Contains("sendUpdates=all", captured.RequestUri?.Query);
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("access-token", captured.Headers.Authorization?.Parameter);
        Assert.Contains("\"summary\":\"Project review\"", body);
        Assert.Contains("\"email\":\"teammate@gmail.com\"", body);
    }

    [Fact]
    public async Task CreateInviteThrowsWhenGoogleIsNotConnected()
    {
        var service = new CalendarInviteService(
            new StubGoogleConnection(null),
            new StubHttpClientFactory(
                "GoogleCalendar",
                new HttpClient { BaseAddress = new Uri("https://www.googleapis.com/calendar/v3/") }),
            new FakeClock(DateTimeOffset.UtcNow));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateInviteAsync(
                "Project review",
                DateTime.UtcNow.AddHours(1),
                30,
                ["teammate@gmail.com"],
                null,
                CancellationToken.None));

        Assert.Equal(GoogleConnection.NotConnectedMessage, exception.Message);
    }

    private static GoogleConnection CreateConnection(
        IGoogleTokenStore store,
        IClock clock,
        HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://oauth2.googleapis.com/")
        };

        return new GoogleConnection(
            store,
            new StubHttpClientFactory("GoogleOAuth", client),
            Options.Create(
                new GoogleOptions
                {
                    ClientId = "test-client-id",
                    ClientSecret = "test-client-secret",
                    RedirectUri = "http://localhost:5118/api/auth/google/callback"
                }),
            clock,
            NullLogger<GoogleConnection>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubGoogleConnection(string? accessToken) : IGoogleConnection
    {
        public bool IsConfigured => true;

        public bool IsConnected => !string.IsNullOrWhiteSpace(accessToken);

        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(accessToken);
        }
    }

    private sealed class StubHttpClientFactory(string name, HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string clientName)
        {
            if (clientName != name)
            {
                throw new InvalidOperationException($"Unexpected HTTP client '{clientName}'.");
            }

            return client;
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return send(request, cancellationToken);
        }
    }
}
