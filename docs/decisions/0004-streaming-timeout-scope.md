# 0004. RequestTimeout bounds the whole stream

- Status: Accepted
- Date: 2026-10-09

## Context

`EmberLlmOptions.RequestTimeout` (default 10 minutes) limits how long a request may take. For a non-streaming request that's unambiguous. For a stream there are two reasonable meanings:

- **Total:** the whole stream, from sending the request to the last token, must finish in time.
- **Idle:** the stream may run indefinitely as long as tokens keep arriving, and only a gap with no data triggers the timeout.

Long generations on slow hardware can legitimately take many minutes, which favors an idle timeout. A runaway generation (a model that never emits a stop token, a server stuck in a loop) keeps producing data forever, which an idle timeout never catches.

## Decision

`RequestTimeout` bounds the **total** duration of a stream, exactly as it does for non-streaming requests. It covers the model lookup, connecting, and reading every chunk. When it fires mid-stream, the enumeration throws `EmberLlmTimeoutException` after yielding everything received so far.

## Alternatives considered

- **Idle timeout only.** More forgiving for very long outputs, but can't stop runaway generation, and "how long can this call take?" would have no answer.
- **Both, as separate options** (`RequestTimeout` plus `StreamIdleTimeout`). The most flexible, but more API surface than v0.1 needs. It can be added later without breaking anything if real usage shows a need.
- **No timeout for streams; rely on the caller's token.** Simple, but inconsistent with non-streaming calls, and a forgotten token means a hung stream holds a connection forever.

## Consequences

- One option means the same thing everywhere, and every call has a bounded duration.
- Very long generations (for example, long documents from a large model on a CPU) can exceed 10 minutes. Callers set `RequestTimeout` higher, or to `Timeout.InfiniteTimeSpan` and use their own token.
- `max_tokens` is the better tool for limiting runaway output; the timeout is a backstop.
