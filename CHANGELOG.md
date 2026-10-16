# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Before 1.0, minor versions may contain breaking changes.

## [Unreleased]

## [0.1.0-beta.1] - 2026-10-16

First public pre-release. Typed structured output (`GetAsync<T>()`) is still to come in v0.1.0; see [ROADMAP.md](ROADMAP.md). The API may change before 1.0.

### Added

- `EmberLlmClient`, a Microsoft.Extensions.AI `IChatClient` for any OpenAI-compatible `/v1/chat/completions` endpoint. `GetResponseAsync` returns a `ChatResponse` with token usage, finish reason and reasoning content from thinking models; `CompleteAsync(string)` is the one-line version. `GetService` returns `ChatClientMetadata`.
- Streaming: `GetStreamingResponseAsync` yields `ChatResponseUpdate`s with reasoning, finish reason and token usage in the final update; `StreamAsync(string)` yields just the reply text. Handles keep-alive comments, chunks split across network packets, error events sent mid-stream, dropped connections, and cancellation at any point.
- `ChatOptions` support: model, temperature, max output tokens, top-p, top-k, stop sequences, seed, frequency and presence penalties, instructions, reasoning effort and response format (text, JSON, JSON schema) are sent; `AdditionalProperties` pass through as extra request fields. Options EmberLlm can't honor, such as tools (planned for v0.2), throw `NotSupportedException` before any request is made.
- `ListModelsAsync` returns the server's models as a `ModelList` of `ModelInfo` (id, owner, creation time), keeping server-specific fields such as vLLM's `max_model_len` in `AdditionalProperties`.
- Server discovery: `EmberLlmDiscovery.FindAsync` probes LM Studio, Ollama, llama.cpp and vLLM's default ports (and any extra endpoints) in parallel, confirms each server with `/v1/models`, identifies the backend from response content, and reports endpoints that answered but weren't usable in `DiscoveryResult.Skipped`. `new EmberLlmClient(discoveredServer)` connects to one.
- Dependency injection: `services.AddEmberLlm(o => ...)` registers a singleton `EmberLlmClient`, also resolvable as `IChatClient`, whose `HttpClient` comes from `IHttpClientFactory`. It returns the `IHttpClientBuilder` for adding handlers. See [decision 0012](docs/decisions/0012-register-a-singleton-client-with-addemberllm.md).
- `samples/QuickStart`: a console app that resolves `IChatClient` from dependency injection, then chats and streams through it.
- `HttpClient`s that EmberLlm creates connect to IPv4 and IPv6 addresses at the same time, so `http://localhost:...` no longer waits ~2 s on Windows for servers that listen only on IPv4.
- `EmberLlmOptions` with `BaseUrl`, `DefaultModel`, `RequestTimeout` (default 10 minutes) and `StrictModelMatching` (opt-in: throw `ModelMismatchException` when a server answers with a different model than requested).
- Automatic model selection when a request names no model: `DefaultModel`, then the server's first listed model.
- Exceptions: `EmberLlmHttpException` (with the server's error message), `EmberLlmConnectionException`, `EmberLlmTimeoutException` and `NoModelAvailableException`, all deriving from `EmberLlmException`.
- Message `AuthorName` is sent as `name`, and message `AdditionalProperties` are passed through as extra fields.
- Response fields EmberLlm doesn't model, including those inside messages, choices and `usage`, are kept in `AdditionalProperties`.

### Fixed

- Streaming: a server-sent event named `error` now throws even if its JSON has no `error` field, and other event names are kept in the update's `AdditionalProperties` as `sse.event` instead of being ignored.
- Data is no longer dropped silently: unsupported or conflicting message data throws, and a response with more than one choice throws instead of being truncated. See [decision 0006](docs/decisions/0006-fix-silent-data-drops.md).

[Unreleased]: https://github.com/msmith211/EmberLlm/compare/v0.1.0-beta.1...HEAD
[0.1.0-beta.1]: https://github.com/msmith211/EmberLlm/releases/tag/v0.1.0-beta.1
