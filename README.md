# EmberLlm

**Make local LLM servers feel native in C#. Find running servers, stream chat, and get typed, validated objects back from small models.**

[![CI](https://github.com/msmith211/EmberLlm/actions/workflows/ci.yml/badge.svg)](https://github.com/msmith211/EmberLlm/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/EmberLlm.svg)](https://www.nuget.org/packages/EmberLlm)
[![Coverage](https://img.shields.io/badge/coverage-pending-lightgrey.svg)](#)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

> **Status:** in development, not yet released. Nothing below works yet. It shows the API v0.1 is being built toward. Once v0.1 is out, the API may still change before 1.0.

## Why this exists

If you run models on your own hardware through LM Studio, Ollama, llama.cpp or vLLM, you can already call them from .NET. The official OpenAI SDK and Microsoft.Extensions.AI handle generic chat well, and OllamaSharp covers Ollama. None of them focus on what is specific to local inference:

- **Finding the server.** Which backend is running, on which port, with which models loaded?
- **Structured output from small models.** Local models often wrap JSON in code fences, prepend `<think>` blocks or miss a field. EmberLlm cleans the output, deserializes it into your C# type, and retries with the error when it fails.
- **Backend quirks.** Each server is slightly different. EmberLlm records real responses from each one and tests against them.

EmberLlm complements those libraries rather than replacing them. It implements `IChatClient` and uses Microsoft.Extensions.AI's message types, so it drops into that ecosystem without conversion code.

<!-- TODO (milestone 8): terminal GIF of discovery + streaming goes here. -->

## Quick start

<!-- TODO (milestone 8): replace with code copied from samples/QuickStart so it is guaranteed to compile. -->

```bash
dotnet add package EmberLlm
```

```csharp
using EmberLlm;

// Find whatever is running locally (LM Studio, Ollama, llama.cpp, vLLM)
var servers = await EmberLlmDiscovery.FindAsync();
using var llm = new EmberLlmClient(servers[0]);

// Stream a reply
await foreach (string token in llm.StreamAsync("Write a haiku about garbage collection"))
    Console.Write(token);

// Get a typed object back
CodeReview review = await llm.GetAsync<CodeReview>("Review this method: ...");

public record CodeReview(string Summary, string[] Issues, int Severity);
```

Already know the endpoint? Use `new EmberLlmClient("http://localhost:1234")`. Using dependency injection? Call `services.AddEmberLlm(o => o.BaseUrl = "http://localhost:1234")`.

## Features (v0.1)

- **Chat completion:** full responses, plain-text streaming, and rich streaming with token usage
- **Model listing:** typed info from `/v1/models`
- **Server discovery:** parallel probing of common local ports with backend fingerprinting
- **Typed structured output:** `GetAsync<T>()` generates a JSON schema from your type, cleans and parses the reply, and retries on failure
- **Microsoft.Extensions.AI interop:** `llm.AsChatClient()` returns an `IChatClient`

Planned for later versions: tool calling, loading and unloading models. See [ROADMAP.md](ROADMAP.md).

## Supported backends

| Backend | Default port | Notes |
| --- | --- | --- |
| LM Studio | 1234 | |
| Ollama | 11434 | Set `DefaultModel`; `/v1/models` lists installed models, not just loaded ones |
| llama.cpp server | 8080 | |
| vLLM | 8000 | |
| Any OpenAI-compatible server | (configure) | Detected as `Generic`; structured output uses a prompt-based fallback |

## Design decisions

Significant design choices are recorded in [docs/decisions](docs/decisions/), for example why EmberLlm reuses Microsoft.Extensions.AI types and how it picks a model when you don't specify one.

## Contributing

Bug reports and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
