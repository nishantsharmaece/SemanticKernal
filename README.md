# Semantic Kernel Assistant

A local-first .NET 10 web chat assistant using Microsoft Semantic Kernel, Ollama,
and SQLite. Conversation history is stored on your computer and the database is
created automatically on first startup.

Technical design (architecture, chat/reminder logic, APIs, and data model):
[docs/TECHNICAL.md](docs/TECHNICAL.md).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Ollama for Windows](https://ollama.com/download/windows)

Ollama is not currently installed on this machine. After installing it, open a
new terminal and download the default model:

```powershell
ollama pull llama3.2:3b
```

Ollama normally starts with Windows. If it is not running, start the Ollama
application or run:

```powershell
ollama serve
```

## Run

From the repository root:

```powershell
dotnet restore
dotnet run --project .\src\SemanticKernelAssistant.Web
```

Open [http://localhost:5118](http://localhost:5118). The UI status card reports
whether Ollama and the configured model are ready.

The application creates `src/SemanticKernelAssistant.Web/App_Data/assistant.db`
and applies database migrations automatically. No database setup command is
required.

## Configuration

Settings are in
`src/SemanticKernelAssistant.Web/appsettings.json`:

- `Assistant:Endpoint`: Ollama server URL
- `Assistant:Model`: installed Ollama chat model
- `Assistant:ContextSize`: prompt context window; lower values use less CPU
- `Assistant:MaximumOutputTokens`: response length cap
- `Assistant:MaximumHistoryMessages`: recent messages sent to the model
- `Assistant:KeepAliveMinutes`: how long Ollama should retain the loaded model
- `Assistant:WarmupIntervalMinutes`: interval used to prevent idle unloads
- `Assistant:SystemPrompt`: assistant behavior
- `Database:Path`: SQLite file path, relative to the web project by default

Environment variables can override any setting. For example:

```powershell
$env:Assistant__Model = "qwen3:4b"
dotnet run --project .\src\SemanticKernelAssistant.Web
```

Pull an alternate model with Ollama before selecting it. This project does not
store or require API keys.

## API

- `GET /api/status` — Ollama and model readiness
- `GET /api/conversations` — conversation list
- `POST /api/conversations` — create a conversation
- `GET /api/conversations/{id}/messages` — message history
- `POST /api/conversations/{id}/messages` — send a message
- `POST /api/conversations/{id}/messages/stream` — stream NDJSON response events
- `GET /api/reminders` — upcoming and recently fired reminders
- `POST /api/reminders/{id}/complete` — mark a reminder complete
- `POST /api/reminders/{id}/cancel` — cancel a reminder

## Reminders

The assistant registers a Semantic Kernel `Reminders` plugin. Ask in chat:

```text
Remind me in 10 minutes to stretch
Set an alarm for tomorrow 9am called standup
What reminders do I have?
```

Reminders are stored in SQLite. A background worker marks due items as fired, and the web UI shows them in the sidebar. Use **Enable notifications** for browser alerts when a reminder fires.

Ask for a local daily briefing (overdue + due today):

```text
Give me my daily briefing
What's my agenda
```

## Performance

The default profile is tuned for CPU-only use: the 3B model is preloaded and
kept warm, chat history is limited to 12 recent messages, context is 2048
tokens, and responses are capped at 256 tokens. The browser renders response
chunks as they arrive, so text appears before generation has finished.

## Tests

```powershell
dotnet test
```

Tests use temporary SQLite databases and a fake AI client, so they do not need
Ollama. A live AI response requires Ollama and the configured model.

## Dependency note

Microsoft's Semantic Kernel connector for Ollama is currently an experimental
prerelease package. It is isolated behind `IAiChatClient` so the application and
tests are not coupled directly to that connector.
