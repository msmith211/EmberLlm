# EmberLlm .NET Client: Project Spec

Oct 8, 2026 · @Michael Smith (revised Oct 9, 2026)

## Overview

EmberLlm is an open-source .NET library (NuGet package) that makes local LLM servers feel native in C#. It targets developers who run models on their own hardware through LM Studio, Ollama, llama.cpp server or vLLM, and want to call them from ASP.NET, Blazor, console apps, games or bots.

The official OpenAI SDK and Microsoft.Extensions.AI handle generic chat well, and OllamaSharp covers one backend. None of them focus on what is specific to local inference: finding running servers, managing which model is loaded, and getting reliable structured output from smaller models. That gap is this library's reason to exist.

The library complements existing tools rather than replacing them. It implements `IChatClient` and reuses Microsoft.Extensions.AI's message and response types, so it drops into that ecosystem without conversion code.

Name: EmberLlm (package ID, root namespace and repo). Chosen in milestone 1 after confirming it was free on NuGet and GitHub.

## v0.1 scope

v0.1 ships five capabilities and nothing else. Keep it small enough to publish in a few weekends.

1. **Chat completion** against any OpenAI-compatible `/v1/chat/completions` endpoint: full-response, simple text streaming via `IAsyncEnumerable<string>`, and rich streaming via `IAsyncEnumerable<ChatResponseUpdate>` that carries usage and finish reason.
2. **Model listing** through `/v1/models`, returning typed model info.
3. **Server discovery** that probes common local ports and identifies which backend is running.
4. **Typed structured output** via `GetAsync<T>()`: generate a JSON schema from a C# type, request it from the server, clean and parse the response, and retry with the error on failure.
5. **`IChatClient` adapter** so the client works anywhere Microsoft.Extensions.AI is used.

### Non-goals for v0.1

- Tool/function calling (planned for v0.2)
- Loading or unloading models through backend-native APIs (v0.2)
- Embeddings, images, audio
- Running models in-process (no llama.cpp bindings; this library only talks to servers)
- Cloud providers (OpenAI, Anthropic); users have official SDKs for those
- Full JSON Schema validation of structured output (v0.1 validates by strict deserialization; see below)

## Tech constraints and layout

Target .NET 10 (`net10.0`, current LTS) with nullable reference types and warnings-as-errors. .NET 8 and 9 reach end of support in November 2026, so they are not targeted.

Runtime dependencies (the complete list; ask before adding anything else):

- `System.Text.Json` (in-box), including the built-in `JsonSchemaExporter` for schema generation
- `Microsoft.Extensions.AI.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions` (for `AddEmberLlm()`)
- `Microsoft.Extensions.Http` (for `IHttpClientFactory` registration)

Test-only packages may be added without asking: xUnit v3 (its `Assert.Skip` handles opt-in integration tests), test SDK and runner packages, and coverage tools. They must never be referenced from `src/`.

Other constraints:

- Async everywhere, every public async method takes a `CancellationToken`
- Accept an injected `HttpClient`; ship an `AddEmberLlm()` extension for `IServiceCollection` that registers a typed client through `IHttpClientFactory`
- `HttpClient` ownership: a client the caller passes in is never disposed by the library. When the library creates its own (URL-only constructor, discovery without an injected client), it owns and disposes it, and `EmberLlmClient` implements `IDisposable`
- Timeouts: library-created `HttpClient`s use `Timeout = Infinite`. Requests are bounded by `EmberLlmOptions.RequestTimeout` (default 10 minutes, since local generation can be slow) applied through a linked `CancellationTokenSource`. A timeout throws `EmberLlmTimeoutException`, distinct from caller cancellation (`OperationCanceledException`)
- No static state, no singletons hidden inside the library
- XML doc comments on every public member
- MIT license

```
EmberLlm/
├── src/
│   └── EmberLlm/
│       ├── EmberLlmClient.cs        # main entry point
│       ├── EmberLlmOptions.cs
│       ├── Chat/                    # request model, streaming (SSE parser)
│       ├── Models/                  # /v1/models types
│       ├── Discovery/               # port probing, backend fingerprinting
│       ├── Structured/              # schema generation, output cleanup, parsing, retry loop
│       ├── Extensions/              # IChatClient adapter, DI registration
│       └── Backends/                # per-backend quirks and capabilities (LM Studio, Ollama, llama.cpp, vLLM)
├── tests/
│   ├── EmberLlm.Tests/              # unit tests, fake HTTP handler
│   ├── EmberLlm.IntegrationTests/   # opt-in, hits a real local server
│   └── fixtures/<backend>/          # recorded responses
├── samples/
│   └── QuickStart/                  # console app from the README
├── .github/workflows/ci.yml
├── ROADMAP.md
├── README.md
├── global.json                  # SDK version, Microsoft.Testing.Platform test runner
├── Directory.Build.props        # shared build settings
├── Directory.Packages.props     # central package versions
└── EmberLlm.slnx
```

## Type naming

Reuse Microsoft.Extensions.AI types instead of defining parallel ones. The library already depends on that package, and same-named types (`ChatMessage`, `ChatRole`, `ChatResponse`) would cause ambiguous-reference errors for anyone importing both namespaces.

| Concept | Type |
| --- | --- |
| Message, role | `Microsoft.Extensions.AI.ChatMessage`, `ChatRole` |
| Full response (text, usage, finish reason) | `Microsoft.Extensions.AI.ChatResponse`, `UsageDetails` |
| Streaming update | `Microsoft.Extensions.AI.ChatResponseUpdate` |
| Request with local-specific options | `EmberLlm.ChatRequest` (own type, no clash) |
| Model info | `EmberLlm.ModelInfo` |
| Discovery result | `EmberLlm.DiscoveredServer`, `BackendType` |

Backend-specific extras (for example llama.cpp `timings`) go into `AdditionalProperties` on the response or update rather than new types.

## Public API sketch

This is the target developer experience. Names can change during implementation, but the README quick start should look this simple.

```csharp
using EmberLlm;
using Microsoft.Extensions.AI;

// Explicit endpoint
var llm = new EmberLlmClient("http://localhost:1234");

// Or find whatever is running
IReadOnlyList<DiscoveredServer> servers = await EmberLlmDiscovery.FindAsync();
var discovered = new EmberLlmClient(servers[0]);

// List models
IReadOnlyList<ModelInfo> models = await llm.ListModelsAsync();

// Simple completion
string answer = await llm.CompleteAsync("Explain Span<T> in two sentences");

// Streaming text
await foreach (string token in llm.StreamAsync("Write a haiku about garbage collection"))
    Console.Write(token);

// Rich streaming (usage, finish reason, timing data for benchmarks)
await foreach (ChatResponseUpdate update in llm.StreamUpdatesAsync(new ChatRequest { /* ... */ }))
    Console.Write(update.Text);

// Multi-turn with options
ChatResponse reply = await llm.ChatAsync(new ChatRequest
{
    Model = "qwen3-14b",
    Messages = [new(ChatRole.System, "You are terse."), new(ChatRole.User, "Hi")],
    Temperature = 0.2f,
    MaxTokens = 256
});
Console.WriteLine($"{reply.Text} ({reply.Usage?.OutputTokenCount} tokens)");

// Typed structured output
public record CodeReview(string Summary, string[] Issues, int Severity);
CodeReview review = await llm.GetAsync<CodeReview>("Review this method: ...");

// Microsoft.Extensions.AI interop
IChatClient chat = llm.AsChatClient();

// DI
services.AddEmberLlm(o => o.BaseUrl = "http://localhost:1234");
```

Constructors: `EmberLlmClient(string baseUrl)`, `EmberLlmClient(EmberLlmOptions options)`, `EmberLlmClient(DiscoveredServer server)`, and `EmberLlmClient(HttpClient httpClient, EmberLlmOptions options)`. The options-only overload lets callers set `DefaultModel` or `RequestTimeout` without supplying an `HttpClient`.

Exceptions (see `docs/decisions/0003-exception-model.md`): all derive from `EmberLlmException`. `EmberLlmHttpException` (non-success status, with `StatusCode`, `ServerMessage`, `ResponseBody`), `EmberLlmConnectionException` (server unreachable), `EmberLlmTimeoutException`, `NoModelAvailableException`, and `StructuredOutputException` (milestone 6). Caller cancellation throws `OperationCanceledException` with the caller's token. `EmberLlmDiscovery.FindAsync` has an overload that accepts an `HttpClient`.

### Model selection

When a call does not name a model, resolve it in this order:

1. `ChatRequest.Model`, if set
2. `EmberLlmOptions.DefaultModel`, if set
3. The first model returned by `/v1/models`, fetched once and cached for the lifetime of the client
4. Otherwise throw `NoModelAvailableException`

Document in the README that on Ollama, `/v1/models` lists every installed model, not just loaded ones, so option 3 may trigger a model load. Ollama users should set `DefaultModel`.

### Structured output (`GetAsync<T>`)

1. Generate a JSON schema from `T` with `JsonSchemaExporter`.
2. If the backend's capability table entry says it supports `response_format: json_schema`, send the schema there. Otherwise (including `BackendType.Generic`) inject the schema into the system prompt with an instruction to reply with JSON only.
3. Clean the raw text before parsing:
   - Strip reasoning blocks (`<think>…</think>`, including an unterminated block at the start) and ignore any separate `reasoning_content` field.
   - Strip a surrounding markdown code fence (` ```json … ``` ` or bare ` ``` `).
   - Trim whitespace; if text remains outside the outermost `{…}` or `[…]`, take the outermost JSON value.
4. Validate by strict deserialization: `System.Text.Json` with `RespectNullableAnnotations` and `RespectRequiredConstructorParameters` enabled, so missing or null non-nullable members fail. Full JSON Schema validation is out of scope for v0.1.
5. On failure, append the model's raw reply and the parse error to the conversation and ask again. `MaxStructuredRetries` (default 2) counts retries after the first attempt, so the default allows **3 attempts in total**.
6. If every attempt fails, throw `StructuredOutputException` containing the raw text of the last attempt and the parse error of each attempt.

## Backend support and discovery

All four backends expose an OpenAI-compatible API, which is the only path v0.1 depends on. Native endpoints are used only to fingerprint the backend. Verify each probe and capability against current backend docs before relying on it, and record a fixture for each.

| Backend | Default port | OpenAI base path | Fingerprint probe | `json_schema` response_format |
| --- | --- | --- | --- | --- |
| LM Studio | 1234 | /v1 | LM Studio native REST endpoint (/api/v0/models) | Yes (verify) |
| Ollama | 11434 | /v1 | GET / returns "Ollama is running"; /api/tags | Yes (verify) |
| llama.cpp server | 8080 | /v1 | /health and /props | Yes (verify) |
| vLLM | 8000 | /v1 | /version | Yes (verify) |
| Generic | — | /v1 | none (`/v1/models` only) | No, use prompt fallback |

Discovery rules:

- Probe localhost on the default ports in parallel, 500 ms timeout per probe
- Accept extra hosts/ports through options (for a LAN box running the server)
- A server is reported only if `/v1/models` returns a valid OpenAI-style model list. Ports 8000 and 8080 are common for unrelated dev servers, so a fingerprint match alone is not enough
- Return `DiscoveredServer` records with base URL, backend type, version if known, and model count
- Unknown but OpenAI-compatible servers return as `BackendType.Generic`
- Never throw from discovery; return an empty list when nothing answers

## Testing strategy

Unit tests run everywhere with no server; integration tests are opt-in and run against a real local backend.

- **Unit tests (xUnit v3):** a fake `HttpMessageHandler` replays recorded responses. Cover request shaping, SSE stream parsing (including split chunks, `[DONE]`, and keep-alive comments), error mapping, timeouts vs cancellation, model selection, output cleanup (think blocks, code fences), and the structured-output retry loop.
- **Recorded fixtures:** capture real responses from each backend once and store them under `tests/fixtures/<backend>/`, where `<backend>` is `lmstudio`, `ollama`, `llamacpp`, `vllm` or `generic`. Backend quirks get tested from these.
- **Integration tests:** skipped via `Assert.Skip` unless `EMBERLLM_TEST_URL` is set. Use the smallest model available so they run fast.
- **CI:** GitHub Actions builds and runs unit tests on Linux and Windows on every push and PR. Integration tests stay local.

## Documentation and repo standards

**README requirements:**

- First screen: one-sentence description, badges, and the "why this exists" paragraph (the gap left by the OpenAI SDK, Microsoft.Extensions.AI and OllamaSharp)
- A quick start that works in under a minute, copied from `samples/QuickStart` so it is guaranteed to compile
- A GIF of discovery plus streaming in a terminal
- A short "Status: v0.1, API may change" note
- Supported backends table and a link to `docs/decisions/`

**Badges:** CI status, NuGet version, license, code coverage.

**History and workflow:**

- One branch per milestone (`milestone/2-chat`, etc.), merged into `main` through a reviewed pull request.
- Conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`), small and focused.

**Decision notes:** record each significant design choice in `docs/decisions/NNNN-title.md` (short format: context, decision, alternatives considered, consequences). Write one whenever a choice would prompt "why did you do it this way?" in a code review. Starting set:

- Reusing Microsoft.Extensions.AI types instead of defining our own
- Strict deserialization instead of JSON Schema validation for structured output
- Requiring a valid `/v1/models` response before reporting a discovered server
- Model selection order

**Repo hygiene:** `CHANGELOG.md` (Keep a Changelog format), `ROADMAP.md`, `CONTRIBUTING.md`, one bug-report issue template, MIT `LICENSE`, `.editorconfig`, `.gitattributes`. Nothing heavier; governance documents look odd on a one-person project.

## Build order

Build in this order, one milestone per session or PR. Each milestone ends with passing tests and a commit.

1. **Scaffold.** Solution, projects, CI workflow (with coverage reporting), `.editorconfig`, `LICENSE`, stub README with badge placeholders, empty `ROADMAP.md` and `CHANGELOG.md`, `CONTRIBUTING.md`, bug-report issue template, `docs/decisions/` with the first decision note, and choose a final package name. Done when `dotnet build` and `dotnet test` pass in CI.
2. **Chat (non-streaming).** `EmberLlmClient`, options, `ChatRequest`, `CompleteAsync` and `ChatAsync` returning `ChatResponse` with usage, model selection, timeout handling, typed exceptions for HTTP and server errors. Done when unit tests cover success, 4xx, 5xx, timeouts, caller cancellation, and each model-selection branch.
3. **Streaming.** SSE parser, `StreamAsync` (overloads for a prompt string and a `ChatRequest`) and `StreamUpdatesAsync`; request `stream_options.include_usage` so the final update carries usage. Verify each backend accepts `stream_options` when recording fixtures. Done when fixture tests cover split chunks, `[DONE]`, keep-alive comments, usage in the final update, and cancellation mid-stream.
4. **Models.** `ListModelsAsync` with typed `ModelInfo`. Done when fixtures from at least LM Studio and Ollama parse.
5. **Discovery.** Parallel probing and fingerprinting per the table above. Done when fixture-based unit tests identify each backend, reject a non-LLM server on port 8000/8080, and return empty when nothing answers. Verifying against a running LM Studio is a manual check, not a CI test.
6. **Structured output.** Schema generation, `response_format` vs prompt fallback, output cleanup, strict deserialization, retry loop. Done when tests prove a malformed first response triggers exactly one corrective retry, a `<think>`-prefixed or fenced response parses on the first attempt, and exhausting retries throws with all attempt errors.
7. **Interop and DI.** `AsChatClient()` and `AddEmberLlm()`. Done when the sample app runs through `IChatClient`.
8. **Release prep.** README meeting every requirement in Documentation and repo standards (quick start, GIF, live badges, status note, decision-note links), samples, package metadata (description, tags, icon, repository URL), SourceLink, `CHANGELOG.md` entry for 0.1.0, decision notes complete, a tagged release workflow that publishes to NuGet and creates a GitHub release from the changelog. **Requires Michael:** create the NuGet API key or configure trusted publishing and add it to the GitHub repo secrets; record the GIF. Done when `v0.1.0` installs from NuGet and the GitHub release page is up.

## Phase 2: benchmark CLI

After v0.1 ships, build `llmbench` as a separate repo and `dotnet tool` that depends on the published package. It doubles as the library's showcase app. Do not start it until the library is on NuGet.

```
llmbench run --models qwen3-14b,llama-3.1-8b --ctx 4k,16k,32k --concurrency 1,4
```

Planned measurements: time to first token, prompt processing tokens/sec, generation tokens/sec, peak VRAM and power draw (polled from nvidia-smi), speed versus context length, and throughput under parallel requests. Output is a terminal table, a JSON results file, and a Markdown snippet with hardware info for sharing.

v0.1 must already expose what these measurements need, so Phase 2 does not force a breaking change: `StreamUpdatesAsync` for time to first token, usage on `ChatResponse` and the final streaming update, and backend timing data (where the server reports it) in `AdditionalProperties`.
