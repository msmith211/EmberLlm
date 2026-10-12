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
- Message `AuthorName` is sent as `name`, and message `AdditionalProperties` are passed through as extra fields.
- Response fields EmberLlm doesn't model, including those inside messages, choices and `usage`, are kept in `AdditionalProperties`.

### Fixed

- Data is no longer dropped silently: unsupported or conflicting message data throws, and a response with more than one choice throws instead of being truncated. See [decision 0006](docs/decisions/0006-fix-silent-data-drops.md).

[Unreleased]: https://github.com/msmith211/EmberLlm/commits/main
