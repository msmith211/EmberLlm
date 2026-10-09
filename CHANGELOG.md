# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Before 1.0, minor versions may contain breaking changes.

## [Unreleased]

Nothing released yet. v0.1.0 is in development; see [ROADMAP.md](ROADMAP.md).

### Added

- `EmberLlmClient` with `CompleteAsync` and `ChatAsync` for non-streaming chat against any OpenAI-compatible `/v1/chat/completions` endpoint. Responses use Microsoft.Extensions.AI's `ChatResponse`, including token usage, finish reason and reasoning content from thinking models.
- Streaming: `StreamAsync` yields reply text as it is generated; `StreamUpdatesAsync` yields Microsoft.Extensions.AI `ChatResponseUpdate`s with reasoning, finish reason and token usage in the final update. Handles keep-alive comments, chunks split across network packets, error events sent mid-stream, dropped connections, and cancellation at any point.
- `EmberLlmOptions` with `BaseUrl`, `DefaultModel` and `RequestTimeout` (default 10 minutes).
- Automatic model selection when a request names no model: `DefaultModel`, then the server's first listed model.
- Exceptions: `EmberLlmHttpException` (with the server's error message), `EmberLlmConnectionException`, `EmberLlmTimeoutException` and `NoModelAvailableException`, all deriving from `EmberLlmException`.

[Unreleased]: https://github.com/msmith211/EmberLlm/commits/main
