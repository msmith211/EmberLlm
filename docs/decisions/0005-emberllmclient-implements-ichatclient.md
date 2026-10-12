# 0005. Make EmberLlmClient an IChatClient instead of wrapping one

- Status: Accepted
- Date: 2026-10-12
- Supersedes: the `ChatRequest` part of [0001](0001-reuse-microsoft-extensions-ai-types.md)

## Context

Milestones 2 and 3 gave EmberLlm its own request type, `ChatRequest` (`Model`, `Messages`, `Temperature`, `MaxTokens`), used by `ChatAsync` and `StreamUpdatesAsync`. The plan was to add an `AsChatClient()` adapter in milestone 7 so EmberLlm could also be used as a Microsoft.Extensions.AI `IChatClient`.

`ChatRequest` overlaps with Microsoft.Extensions.AI's `ChatOptions`, which already carries the model, temperature, output token limit and much more. The main argument for keeping our own type was that it can only express what EmberLlm supports. Users can't set tool definitions, for example, because the type has no property for them.

That argument doesn't survive the adapter. `IChatClient` takes `ChatOptions`, so EmberLlm has to handle every `ChatOptions` property anyway, supported or not. `ChatRequest` protected only one of two ways in, and it added a second vocabulary for the same concepts (`MaxTokens` here, `MaxOutputTokens` there). It also went against 0001's own rule: reuse Microsoft.Extensions.AI types wherever the concept already exists.

## Decision

1. **`EmberLlmClient` implements `IChatClient` directly**, with the interface methods public on the class (implicit implementation), so they appear in IntelliSense as the main API.
2. **`GetResponseAsync` and `GetStreamingResponseAsync` replace `ChatAsync` and `StreamUpdatesAsync`.** `ChatRequest` and the `StreamAsync(ChatRequest)` overload are removed.
3. **The simple methods stay** as the easy entry point: `CompleteAsync(string)`, `StreamAsync(string)` and, in milestone 6, `GetAsync<T>`.
4. **Every OpenAI-compatible option in `ChatOptions` is supported** and mapped to the request: model, temperature, max output tokens, top-p, stop sequences, seed, frequency penalty and presence penalty.
5. **Anything EmberLlm cannot honor throws `NotSupportedException`. It is never silently ignored.** This includes tools and tool mode until tool calling lands in v0.2, response format until structured output lands in milestone 6, and any option with no OpenAI-compatible equivalent. The exception message names the option and says whether support is planned.
6. **`GetService` returns `ChatClientMetadata`** (provider name, server URL, default model), which Microsoft.Extensions.AI middleware uses for logging and telemetry.
7. **`AsChatClient()` is dropped.** The client already is one. Milestone 7 becomes only `AddEmberLlm()` for dependency injection.
8. **Model selection is unchanged** ([0002](0002-model-selection-order.md)), except that `ChatOptions.ModelId` takes the place of `ChatRequest.Model` as the first choice.
9. **`ChatOptions.AdditionalProperties` are passed through** to the request body as extra fields. This lets callers send backend-specific parameters such as `top_k`. EmberLlm honors the request by sending it; whether a given server acts on an unfamiliar field is up to that server.
10. **A test guards against future options.** It lists every property on `ChatOptions` and fails if it finds one that EmberLlm neither maps nor explicitly rejects. When a Microsoft.Extensions.AI upgrade adds an option, the build fails until someone decides how to handle it, so a new option can never be ignored silently.

## Alternatives considered

- **Keep `ChatRequest` and add the adapter** (the original plan). A small, discoverable type with familiar names and room for typed local-only settings. Rejected because the adapter must handle unsupported `ChatOptions` anyway, so `ChatRequest` adds a second vocabulary without removing the problem it was meant to prevent, and every new option would have to be added twice.
- **Keep `ChatRequest` but have it wrap `ChatOptions`.** Avoids duplicating each option, but leaves two ways to make the same call and still needs the adapter. Rejected as the complexity of both approaches with the benefits of neither.
- **Implement `IChatClient` explicitly** (methods only visible through the interface). Keeps the class's own surface small, but hides the main API from IntelliSense behind a cast. Rejected.
- **Ignore unsupported options instead of throwing.** Matches how some providers behave, but a caller who sets `Tools` would get a reply that silently skipped them and might never notice. Rejected: EmberLlm never fails silently.
- **Reject `AdditionalProperties` entirely.** Stricter, but it blocks a legitimate use (backend-specific parameters) to avoid a risk that belongs to the server, not to EmberLlm. Rejected.

## Consequences

- One way to describe a chat call, shared with every other Microsoft.Extensions.AI provider. Code written against EmberLlm moves to another `IChatClient` unchanged, and the other way round.
- EmberLlm works with Microsoft.Extensions.AI middleware (logging, caching, OpenTelemetry, and later function invocation through `ChatClientBuilder`) without an adapter.
- The rule for unsupported options lives in one place, the `ChatOptions` mapping.
- Local-only settings added later (such as Ollama's `keep_alive`) have no typed home on `ChatOptions`. They go in `AdditionalProperties`, possibly with typed extension methods for discoverability.
- Method names follow Microsoft's (`GetResponseAsync` rather than `ChatAsync`), which is slightly longer but familiar to anyone who has used `IChatClient`.
- Milestones 2 and 3 are reworked. Nothing has been released, so no users are affected.
- 0001 still holds for messages, responses and updates. Its statement that `ChatRequest` is EmberLlm's own type is superseded by this note.
- Upgrading Microsoft.Extensions.AI may fail the guard test. That is intended: it forces a decision about each new option instead of letting one through unhandled.
