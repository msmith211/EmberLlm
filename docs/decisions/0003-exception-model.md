# 0003. Separate exception types for HTTP errors, unreachable servers, timeouts and cancellation

- Status: Accepted
- Date: 2026-10-09

## Context

A call to a local server can fail in ways that need different responses from the caller:

- The server answered with an error (bad request, unknown model, out of memory)
- The server isn't running
- The server is running but too slow, which is common with large models on modest hardware
- The caller gave up

.NET's defaults blur these. `HttpClient` throws `TaskCanceledException` both for its own timeout and for caller cancellation, and returns non-success responses without throwing at all. Error bodies also differ by backend: OpenAI-style servers send `{"error":{"message":...}}`, Ollama sends `{"error":"..."}`, and some proxies return HTML.

## Decision

- All library failures derive from `EmberLlmException`, so `catch (EmberLlmException)` handles everything.
- `EmberLlmHttpException`: non-success status. It exposes `StatusCode`, `IsServerError` (5xx), `ServerMessage` (extracted from any of the recognized body shapes) and the raw `ResponseBody`. The exception message includes the server's message, or a truncated body when no message can be extracted.
- `EmberLlmConnectionException`: the server could not be reached (wraps `HttpRequestException`).
- `EmberLlmTimeoutException`: `EmberLlmOptions.RequestTimeout` elapsed, or an injected `HttpClient`'s own `Timeout` did.
- Caller cancellation stays a plain `OperationCanceledException` carrying the caller's own token, following .NET convention so existing cancellation handling keeps working.
- `NoModelAvailableException`: see [0002](0002-model-selection-order.md).

## Alternatives considered

- **Use `HttpRequestException` and `TaskCanceledException` directly.** No new types, but callers can't tell a timeout from their own cancellation without inspecting inner exceptions, and backend error messages stay buried in unread response bodies.
- **Return a result type instead of throwing.** Explicit, but out of step with `HttpClient`, `IChatClient` and most .NET libraries, and awkward with `IAsyncEnumerable` streaming.
- **Report timeouts as `TimeoutException`.** Familiar, but it wouldn't derive from `EmberLlmException`, breaking the single catch-all.

## Consequences

- Callers can retry `IsServerError` and timeouts, surface `ServerMessage` to users, and treat connection failures as "is the server running?".
- `ResponseBody` makes backend quirks easy to diagnose and to capture as test fixtures.
- Each error response is read into memory as a string. That's fine for error bodies, and chat responses are read fully anyway in non-streaming mode.
