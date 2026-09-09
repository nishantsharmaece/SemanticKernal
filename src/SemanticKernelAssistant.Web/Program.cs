using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using SemanticKernelAssistant.Web.Background;
using SemanticKernelAssistant.Web.Data;
using SemanticKernelAssistant.Web.Models;
using SemanticKernelAssistant.Web.Plugins;
using SemanticKernelAssistant.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<AssistantOptions>()
    .Bind(builder.Configuration.GetSection(AssistantOptions.SectionName))
    .Validate(
        settings => Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out _),
        "Assistant:Endpoint must be an absolute URI.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Model), "Assistant:Model is required.")
    .Validate(settings => settings.ContextSize >= 512, "Assistant:ContextSize must be at least 512.")
    .Validate(
        settings => settings.MaximumOutputTokens is >= 32 and <= 2_048,
        "Assistant:MaximumOutputTokens must be between 32 and 2048.")
    .Validate(
        settings => settings.MaximumHistoryMessages is >= 1 and <= 100,
        "Assistant:MaximumHistoryMessages must be between 1 and 100.")
    .Validate(settings => settings.KeepAliveMinutes >= 1, "Assistant:KeepAliveMinutes must be positive.")
    .Validate(
        settings => settings.WarmupIntervalMinutes >= 1,
        "Assistant:WarmupIntervalMinutes must be positive.")
    .ValidateOnStart();

builder.Services
    .AddOptions<GoogleOptions>()
    .Bind(builder.Configuration.GetSection(GoogleOptions.SectionName))
    .Validate(
        settings => Uri.TryCreate(settings.RedirectUri, UriKind.Absolute, out _),
        "Google:RedirectUri must be an absolute URI.")
    .ValidateOnStart();

var assistantSettings = builder.Configuration
    .GetSection(AssistantOptions.SectionName)
    .Get<AssistantOptions>() ?? new AssistantOptions();
var ollamaEndpoint = new Uri(assistantSettings.Endpoint.TrimEnd('/') + "/");

#pragma warning disable SKEXP0070
builder.Services.AddOllamaChatCompletion(assistantSettings.Model, ollamaEndpoint);
#pragma warning restore SKEXP0070

var configuredDatabasePath =
    builder.Configuration["Database:Path"] ?? "App_Data/assistant.db";
var databasePath = Path.IsPathRooted(configuredDatabasePath)
    ? configuredDatabasePath
    : Path.Combine(builder.Environment.ContentRootPath, configuredDatabasePath);
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

builder.Services.AddDbContext<AssistantDbContext>(
    options => options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<IConversationStore, ConversationStore>();
builder.Services.AddScoped<IReminderStore, ReminderStore>();
builder.Services.AddScoped<IBriefingService, BriefingService>();
builder.Services.AddScoped<IGoogleTokenStore, GoogleTokenStore>();
builder.Services.AddScoped<GoogleConnection>();
builder.Services.AddScoped<IGoogleConnection>(
    serviceProvider => serviceProvider.GetRequiredService<GoogleConnection>());
builder.Services.AddScoped<IGoogleAuthService>(
    serviceProvider => serviceProvider.GetRequiredService<GoogleConnection>());
builder.Services.AddScoped<ICalendarInviteService, CalendarInviteService>();
builder.Services.AddScoped<ReminderPlugin>();
builder.Services.AddScoped<BriefingPlugin>();
builder.Services.AddScoped<CalendarPlugin>();
builder.Services.AddScoped(serviceProvider =>
{
    var kernel = new Kernel(serviceProvider);
    kernel.Plugins.AddFromObject(
        serviceProvider.GetRequiredService<ReminderPlugin>(),
        "Reminders");
    kernel.Plugins.AddFromObject(
        serviceProvider.GetRequiredService<BriefingPlugin>(),
        "Briefing");
    kernel.Plugins.AddFromObject(
        serviceProvider.GetRequiredService<CalendarPlugin>(),
        "Calendar");
    return kernel;
});
builder.Services.AddScoped<IAiChatClient, SemanticKernelChatClient>();
builder.Services.AddScoped<IChatAssistant, ChatAssistant>();
builder.Services.AddScoped<IOllamaHealthService, OllamaHealthService>();
builder.Services.AddHostedService<ReminderDispatchHostedService>();
builder.Services.AddHostedService<OllamaWarmupHostedService>();
builder.Services.AddHttpClient(
    "OllamaHealth",
    client =>
    {
        client.BaseAddress = ollamaEndpoint;
        client.Timeout = TimeSpan.FromSeconds(3);
    });
builder.Services.AddHttpClient<IOllamaModelWarmer, OllamaModelWarmer>(
    client =>
    {
        client.BaseAddress = ollamaEndpoint;
        client.Timeout = TimeSpan.FromMinutes(2);
    });
builder.Services.AddHttpClient(
    "GoogleOAuth",
    client =>
    {
        client.BaseAddress = new Uri("https://oauth2.googleapis.com/");
        client.Timeout = TimeSpan.FromSeconds(30);
    });
builder.Services.AddHttpClient(
    "GoogleCalendar",
    client =>
    {
        client.BaseAddress = new Uri("https://www.googleapis.com/calendar/v3/");
        client.Timeout = TimeSpan.FromSeconds(30);
    });

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AssistantDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

api.MapGet(
    "/status",
    async (IOllamaHealthService health, CancellationToken cancellationToken) =>
        Results.Ok(await health.GetStatusAsync(cancellationToken)));

api.MapGet(
    "/conversations",
    async (IConversationStore store, CancellationToken cancellationToken) =>
        Results.Ok(await store.ListAsync(cancellationToken)));

api.MapPost(
    "/conversations",
    async (
        CreateConversationRequest request,
        IConversationStore store,
        CancellationToken cancellationToken) =>
    {
        var conversation = await store.CreateAsync(request.Title, cancellationToken);
        return Results.Created(
            $"/api/conversations/{conversation.Id}",
            ToSummary(conversation));
    });

api.MapGet(
    "/conversations/{id:guid}/messages",
    async (
        Guid id,
        IConversationStore store,
        CancellationToken cancellationToken) =>
    {
        var conversation = await store.GetWithMessagesAsync(id, cancellationToken);
        return conversation is null
            ? Results.NotFound(new { error = "Conversation was not found." })
            : Results.Ok(conversation.Messages.Select(ToResponse));
    });

api.MapPost(
    "/conversations/{id:guid}/messages",
    async (
        Guid id,
        SendMessageRequest request,
        IChatAssistant assistant,
        CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(
                ToResponse(await assistant.SendAsync(id, request.Message, cancellationToken)));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (AiServiceUnavailableException exception)
        {
            return Results.Json(
                new { error = exception.Message },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    });

api.MapPost(
    "/conversations/{id:guid}/messages/stream",
    async (
        Guid id,
        SendMessageRequest request,
        IChatAssistant assistant,
        HttpContext context,
        CancellationToken cancellationToken) =>
    {
        context.Response.ContentType = "application/x-ndjson";
        context.Response.Headers.CacheControl = "no-cache";

        try
        {
            var completed = await assistant.SendStreamingAsync(
                id,
                request.Message,
                async (chunk, token) =>
                {
                    await WriteStreamEventAsync(
                        context.Response,
                        new { type = "delta", content = chunk },
                        token);
                },
                cancellationToken);

            await WriteStreamEventAsync(
                context.Response,
                new { type = "complete", message = ToResponse(completed) },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser disconnected; no response can be written.
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or KeyNotFoundException
                or AiServiceUnavailableException)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = exception switch
                {
                    ArgumentException => StatusCodes.Status400BadRequest,
                    KeyNotFoundException => StatusCodes.Status404NotFound,
                    _ => StatusCodes.Status503ServiceUnavailable
                };
            }

            await WriteStreamEventAsync(
                context.Response,
                new { type = "error", error = exception.Message },
                cancellationToken);
        }
    });

api.MapGet(
    "/reminders",
    async (IReminderStore store, CancellationToken cancellationToken) =>
        Results.Ok(await store.ListAsync(cancellationToken)));

api.MapPost(
    "/reminders/{id:guid}/complete",
    async (Guid id, IReminderStore store, CancellationToken cancellationToken) =>
    {
        try
        {
            var reminder = await store.CompleteAsync(id, cancellationToken);
            return Results.Ok(ToReminderResponse(reminder));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    });

api.MapPost(
    "/reminders/{id:guid}/cancel",
    async (Guid id, IReminderStore store, CancellationToken cancellationToken) =>
    {
        try
        {
            var reminder = await store.CancelAsync(id, cancellationToken);
            return Results.Ok(ToReminderResponse(reminder));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    });

api.MapGet(
    "/auth/google/login",
    (IGoogleAuthService googleAuth) =>
    {
        if (!googleAuth.IsConfigured)
        {
            return Results.Json(
                new { error = "Google is not configured." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Redirect(googleAuth.GetAuthorizeUrl());
    });

api.MapGet(
    "/auth/google/callback",
    async (
        string? code,
        IGoogleAuthService googleAuth,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Results.BadRequest(new { error = "A Google authorization code is required." });
        }

        try
        {
            await googleAuth.ExchangeCodeAsync(code, cancellationToken);
            return Results.Redirect("/?google=connected");
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = "Google authorization failed." });
        }
    });

api.MapPost(
    "/auth/google/logout",
    async (IGoogleAuthService googleAuth, CancellationToken cancellationToken) =>
    {
        await googleAuth.LogoutAsync(cancellationToken);
        return Results.Ok(await googleAuth.GetStatusAsync(cancellationToken));
    });

api.MapGet(
    "/auth/google/status",
    async (IGoogleAuthService googleAuth, CancellationToken cancellationToken) =>
        Results.Ok(await googleAuth.GetStatusAsync(cancellationToken)));

app.MapFallbackToFile("index.html");

app.Run();

static ConversationSummary ToSummary(Conversation conversation) =>
    new(
        conversation.Id,
        conversation.Title,
        conversation.CreatedAtUtc,
        conversation.UpdatedAtUtc);

static MessageResponse ToResponse(ConversationMessage message) =>
    new(
        message.Id,
        message.ConversationId,
        message.Role,
        message.Content,
        message.CreatedAtUtc);

static ReminderResponse ToReminderResponse(Reminder reminder) =>
    new(
        reminder.Id,
        reminder.Title,
        reminder.Notes,
        reminder.DueAtUtc,
        reminder.Status,
        reminder.CreatedAtUtc,
        reminder.FiredAtUtc);

static async Task WriteStreamEventAsync(
    HttpResponse response,
    object payload,
    CancellationToken cancellationToken)
{
    await JsonSerializer.SerializeAsync(
        response.Body,
        payload,
        cancellationToken: cancellationToken);
    await response.WriteAsync("\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}

public partial class Program;
