# 0013. Choose the structured-output mode per client, clean only invalid replies, retry only parse failures

- Status: Proposed
- Date: 2026-10-16

## Context

[0011](0011-structured-output-builds-on-meai.md) settled that `GetAsync<T>` wraps Microsoft.Extensions.AI's `GetResponseAsync<T>` and adds retries, a fallback for servers without JSON schema support, and cleanup. Implementing it left four choices open.

A probe of `GetResponseAsync<T>` (10.10.0) through EmberLlm showed:

- **With JSON schema on**, it sends `response_format: json_schema` with the type's schema. It does not strip code fences or `<think>` blocks.
- **With JSON schema off**, it adds a user message containing the schema *and* sends `response_format: json_object`, which LM Studio rejects with HTTP 400.
- A reply of JSON `null` already fails with "The deserialized response is null."

## Decision

1. **The mode is a client option.** `EmberLlmOptions.StructuredOutputMode` is `JsonSchema` (the default) or `Prompt`.
   - A client built from a `DiscoveredServer` picks the mode itself: `Prompt` for `BackendType.Generic`, `JsonSchema` for the four known backends.
   - A client built from a URL doesn't know its backend, so it uses `JsonSchema`, which all four supported backends accept.
   - `Prompt` mode reuses Microsoft's schema message and removes the `json_object` format before the request goes out. EmberLlm never sends `json_object`.
2. **Cleanup runs only on replies that aren't valid JSON, in both modes.** A valid reply is never altered, so a string value containing `</think>` or a code fence survives intact. An invalid reply loses a leading reasoning block, a surrounding code fence, and any text outside the outermost `{…}` or `[…]`.
3. **Deserialization is strict by default.** `RespectNullableAnnotations` and `RespectRequiredConstructorParameters` are on, on top of `AIJsonUtilities.DefaultOptions`. A caller who passes their own `JsonSerializerOptions` (for source-generated metadata or custom naming) gets those options exactly as given.
4. **Only parse failures are retried.**
   - **When a reply can't be read:** it goes back as an assistant message, followed by a user message with the error. The request is repeated up to `MaxStructuredRetries` times (default 2).
   - **What isn't retried:** HTTP errors, timeouts, connection failures and cancellation propagate immediately.
   - **When every attempt fails:** `StructuredOutputException.Attempts` lists each attempt's raw text, as the server sent it before cleanup, and its error.
5. **`ChatOptions.ResponseFormat` must be unset.** `GetAsync<T>` sets the response format from `T`, so a caller's value would be overwritten. Under the never-fail-silently rule, that throws `ArgumentException` before anything is sent.

## Alternatives considered

- **Detect schema support at runtime:** try `json_schema`, and fall back to the prompt on HTTP 400. This needs no configuration, but a 400 can have other causes (an unknown model, a bad option), and caching a wrong guess would quietly downgrade later calls. Spike 0001 also showed all four target backends accept `json_schema`.
- **Treat an unknown backend as `Generic` and use the prompt:** this matches SPEC.md's capability table literally. But the quick-start client (`new EmberLlmClient(url)`) would then skip schema enforcement on LM Studio, where it measurably works.
- **Clean every reply:** this is simpler, but it can corrupt valid JSON whose string values contain `</think>` or backticks.
- **Clean only in `Prompt` mode, as SPEC.md first described:** this leaves `JsonSchema` mode exposed when a server applies the schema loosely, for example by still emitting a reasoning block in the content.
- **Retry server errors too:** a 400 or a missing model fails the same way every time, and the timeout already allows long generations, so retrying would only multiply the wait.

## Consequences

- One option covers the common cases; `new EmberLlmClient(discoveredServer)` needs no configuration at all.
- On a server that rejects `json_schema`, a URL-built client fails with `EmberLlmHttpException` until `StructuredOutputMode = Prompt` is set. That's loud, not silent.
- Retries cost a full generation each. `MaxStructuredRetries = 0` turns them off.
- Microsoft's prompt wording and schema shape apply in `Prompt` mode. Changes to them in future Microsoft.Extensions.AI versions carry over.
