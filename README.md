# EmberLlm

**A .NET client for local LLM servers (LM Studio, Ollama, llama.cpp, vLLM), built on Microsoft.Extensions.AI.**

[![CI](https://github.com/msmith211/EmberLlm/actions/workflows/ci.yml/badge.svg)](https://github.com/msmith211/EmberLlm/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/EmberLlm.svg)](https://www.nuget.org/packages/EmberLlm)
[![Coverage](https://img.shields.io/badge/coverage-pending-lightgrey.svg)](#)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

> [!NOTE]
> **Status: pre-release.** v0.1 is in development and not yet on NuGet. Chat, streaming and model listing work today, with unit tests in CI and integration tests against a real LM Studio server. Server discovery, typed structured output and dependency injection are still being built (see [Coming next](#coming-next)). The API may change before 1.0.

<!-- TODO (milestone 8): terminal GIF of discovery + streaming goes here. -->

## Contents

- [Why this exists](#why-this-exists)
- [Quick start](#quick-start)
- [What works today](#what-works-today)
- [Coming next](#coming-next)
- [Supported backends](#supported-backends)
- [Design decisions](#design-decisions)
- [Contributing](#contributing)

## Why this exists

If you run models on your own hardware, you can already call them from .NET: the official OpenAI SDK and Microsoft.Extensions.AI handle generic chat, and OllamaSharp covers Ollama. EmberLlm focuses on the parts that are specific to local inference.

Already in place:

- **Reasoning stays separate.** Thinking models' reasoning arrives as `TextReasoningContent`, not mixed into the answer text.
- **No model name required.** If you don't name one, EmberLlm uses your configured default, then the first model the server lists.
- **Clear errors for local setups.** "Server not running", "server returned an error" (with the server's own message) and "request timed out" are separate exception types.
- **Timeouts suited to slow hardware.** The default request timeout is 10 minutes, and it is kept distinct from your own cancellation.
- **Never fails silently.** Every option you set is either sent or rejected before the request goes out, and every response field is either mapped or kept in `AdditionalProperties`. See [SPEC.md, Design rules](SPEC.md#design-rules).

Planned for v0.1: finding running servers automatically, and getting typed objects back from small models that wrap JSON in code fences or `<think>` blocks. A comparison with the OpenAI SDK plus Microsoft's adapter is scheduled before that work, and this section will be updated with its findings.

EmberLlm complements those libraries rather than replacing them. `EmberLlmClient` *is* an `IChatClient` and uses Microsoft.Extensions.AI's message types, so it drops into that ecosystem without conversion code.

## Quick start

### Install

EmberLlm is not on NuGet yet; `dotnet add package EmberLlm` will work once v0.1.0 is published. Until then, reference it from source (requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)):

```bash
git clone https://github.com/msmith211/EmberLlm.git
cd path/to/YourApp
dotnet add reference path/to/EmberLlm/src/EmberLlm/EmberLlm.csproj
```

### Chat and stream

Start a local server with a model loaded (LM Studio listens on port 1234 by default), then:

```csharp
using EmberLlm;

using var llm = new EmberLlmClient("http://127.0.0.1:1234");

// One reply, no model name needed
string answer = await llm.CompleteAsync("Explain Span<T> in two sentences.");
Console.WriteLine(answer);

// Stream the reply as it is generated
await foreach (string token in llm.StreamAsync("Write a haiku about garbage collection."))
    Console.Write(token);
```

> [!TIP]
> Prefer `127.0.0.1` over `localhost`, especially on Windows. .NET tries IPv6 (`::1`) first, and many local servers, LM Studio included, listen only on IPv4, so the first request to `localhost` waits about 2 seconds for the refused IPv6 attempt before falling back.

### Use it as an `IChatClient`

```csharp
using EmberLlm;
using Microsoft.Extensions.AI;

using var llm = new EmberLlmClient(new EmberLlmOptions
{
    BaseUrl = "http://127.0.0.1:1234",
    DefaultModel = "qwen/qwen3-4b-thinking-2507", // optional
});

ChatResponse reply = await llm.GetResponseAsync(
    [new(ChatRole.System, "You are terse."), new(ChatRole.User, "Is 1013 prime?")],
    new ChatOptions { Temperature = 0.2f });

Console.WriteLine(reply.Text); // the answer only

foreach (var reasoning in reply.Messages.SelectMany(m => m.Contents).OfType<TextReasoningContent>())
    Console.WriteLine($"[reasoning] {reasoning.Text}");

Console.WriteLine($"Answered by {reply.ModelId} using {reply.Usage?.OutputTokenCount} output tokens");
```

### List models and handle errors

```csharp
try
{
    foreach (ModelInfo model in await llm.ListModelsAsync())
        Console.WriteLine(model.Id);
}
catch (EmberLlmConnectionException)
{
    Console.WriteLine("No server is running at that address.");
}
catch (EmberLlmHttpException ex)
{
    Console.WriteLine($"Server returned {(int)ex.StatusCode}: {ex.ServerMessage}");
}
```

## What works today

| Area | What you get |
| --- | --- |
| Chat | `GetResponseAsync` returns a `ChatResponse` with text, reasoning, finish reason and token usage. `CompleteAsync(string)` is the one-line version. |
| Streaming | `GetStreamingResponseAsync` yields `ChatResponseUpdate`s, with usage in the final update. `StreamAsync(string)` yields just the reply text. |
| Options | `ChatOptions` model, temperature, max output tokens, top-p, top-k, stop sequences, seed, frequency and presence penalties, instructions, reasoning effort and response format (text, JSON, JSON schema). `AdditionalProperties` pass through as extra request fields. |
| Unsupported options | Options EmberLlm can't honor, such as tools (planned for v0.2), throw `NotSupportedException` before any request is made. |
| Model selection | `ChatOptions.ModelId`, then `EmberLlmOptions.DefaultModel`, then the server's first listed model ([decision 0002](docs/decisions/0002-model-selection-order.md)). |
| Model listing | `ListModelsAsync()` returns a `ModelList` of `ModelInfo` (id, owner, creation time, plus server-specific fields). |
| Errors | `EmberLlmHttpException` (status code and the server's message), `EmberLlmConnectionException`, `EmberLlmTimeoutException`, `NoModelAvailableException` and `ModelMismatchException`, all deriving from `EmberLlmException` ([decision 0003](docs/decisions/0003-exception-model.md)). |
| Strict model matching | Opt-in `EmberLlmOptions.StrictModelMatching` throws when a server answers with a different model than requested ([decision 0008](docs/decisions/0008-model-substitution-and-strict-matching.md)). |
| Interop | `GetService` returns `ChatClientMetadata`, so Microsoft.Extensions.AI middleware and libraries built on `IChatClient` work unchanged. |

Unit tests run on Linux and Windows in CI. Integration tests are opt-in and have passed against LM Studio with gemma-4-12b and qwen3-4b-thinking. See [CHANGELOG.md](CHANGELOG.md) for the full list.

## Coming next

These are **not available yet**. Names may change before they ship.

| Feature | Planned API | Milestone |
| --- | --- | --- |
| Server discovery | `EmberLlmDiscovery.FindAsync()` probes common local ports and identifies the backend | 5 (in progress) |
| Typed structured output | `GetAsync<T>()` cleans small-model output, deserializes into your type, and retries on failure | 6 |
| Dependency injection | `services.AddEmberLlm(...)` registers `EmberLlmClient` and `IChatClient` | 7 |
| NuGet package | `dotnet add package EmberLlm` | 8 (v0.1.0 release) |

Tool calling and loading or unloading models are planned for v0.2. See [ROADMAP.md](ROADMAP.md) for what is planned and what is out of scope.

## Supported backends

EmberLlm talks to any server with an OpenAI-compatible `/v1/chat/completions` endpoint. Only LM Studio has been tested against a real server so far.

| Backend | Default URL | Tested | Notes |
| --- | --- | --- | --- |
| LM Studio | `http://127.0.0.1:1234` | Yes | Lists loaded models. LM Studio may answer a request for an unknown model with whichever model is loaded instead of failing (seen with one model loaded; with two loaded it returned an error). Check `reply.ModelId`, or set `StrictModelMatching = true` to make a mismatch throw. |
| Ollama | `http://127.0.0.1:11434` | Not yet | Set `DefaultModel`: Ollama's `/v1/models` lists every installed model, not just loaded ones, so picking the first could trigger a model load. |
| llama.cpp server | `http://127.0.0.1:8080` | Not yet | |
| vLLM | `http://127.0.0.1:8000` | Not yet | |
| Other OpenAI-compatible servers | (your server's URL) | Not yet | |

Bug reports and recorded responses from untested backends are especially welcome; see [CONTRIBUTING.md](CONTRIBUTING.md#recorded-fixtures).

## Design decisions

Significant choices are recorded as short notes in [docs/decisions](docs/decisions/), for example why EmberLlm reuses Microsoft.Extensions.AI types, why `EmberLlmClient` implements `IChatClient` directly, and how it picks a model when you don't name one.

## Contributing

Bug reports and focused pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for build, test and fixture instructions.

## License

[MIT](LICENSE)
