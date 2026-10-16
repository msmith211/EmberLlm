# 0011. Build GetAsync<T> on Microsoft.Extensions.AI's GetResponseAsync<T>

- Status: Accepted (Michael, 2026-10-13). Adding the `Microsoft.Extensions.AI` package as a runtime dependency still needs approval before milestone 6 starts.
- Date: 2026-10-13

## Context

SPEC.md planned `GetAsync<T>` as a complete structured-output pipeline: generate a JSON schema from `T`, request it through `response_format` or the prompt, clean the reply, deserialize strictly, and retry with the error.

[Spike 0001](../spikes/0001-comparison-with-openai-sdk.md) found that Microsoft.Extensions.AI's own `GetResponseAsync<T>` already covers most of that, and works through EmberLlm because `EmberLlmClient` is an `IChatClient`. Against LM Studio with a small thinking model:

- **JSON schema mode on:** 14 of 15 replies parsed. Schema generation, the request and deserialization work.
- **No retry:** the failed replies (about 1 in 10) weren't attempted again.
- **Schema mode off:** it asks for `response_format: json_object`, which LM Studio rejects with HTTP 400, so it fails every time. No prompt-based fallback exists.
- **No cleanup:** nothing removes `<think>` blocks or code fences, which matters for servers that don't enforce a schema.

## Decision

`GetAsync<T>` is a thin layer over `GetResponseAsync<T>` that adds what it lacks:

1. **Corrective retries.** When the reply doesn't deserialize, the reply and the parse error go back to the model, up to `MaxStructuredRetries` times (default 2, so 3 attempts in total). If every attempt fails, it throws `StructuredOutputException` with each attempt's raw text and error.
2. **Backend-aware fallback.** Use JSON schema mode where the backend supports it, and otherwise put the schema in the prompt instead of sending `json_object`.
3. **Cleanup before parsing** in the prompt fallback: remove `<think>` blocks, surrounding code fences, and text outside the outermost JSON value.

Schema generation and deserialization come from Microsoft.Extensions.AI rather than being re-implemented.

## Alternatives considered

- **Build the full pipeline as the spec described.** EmberLlm controls everything, but it duplicates schema generation and parsing that Microsoft already ships and maintains, and the two could drift apart.
- **Drop `GetAsync<T>`** and document using `GetResponseAsync<T>` directly. No new code, but it leaves the measured gaps (about 1 in 10 failures with no retry, and no working mode for servers without schema support) unaddressed.

## Consequences

- **A new runtime dependency:** `GetResponseAsync<T>` lives in `Microsoft.Extensions.AI`, not in `Microsoft.Extensions.AI.Abstractions`. Adding it requires approval under the project's dependency rule, and milestone 6 starts only once it's approved.
- **Smaller milestone:** milestone 6 shrinks to retries, fallback and cleanup.
- **Microsoft's improvements carry over:** EmberLlm benefits from future improvements to `GetResponseAsync<T>`, and also follows its behavior changes.
- **Strict deserialization isn't decided here.** SPEC.md planned it (`RespectNullableAnnotations`, `RespectRequiredConstructorParameters`); whether to pass those options through, or rely on Microsoft's defaults, is settled in milestone 6.
