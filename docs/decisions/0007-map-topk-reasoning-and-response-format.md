# 0007. Map TopK, reasoning effort and response format

- Status: Accepted (Michael, 2026-10-12)
- Date: 2026-10-12

## Context

Decision 0005 maps every OpenAI-compatible `ChatOptions` setting and rejects the rest. Three settings don't fit cleanly on either side:

- **`TopK`** is not part of OpenAI's own API, but local servers (llama.cpp, vLLM, LM Studio) accept `top_k`.
- **`Reasoning`** has an effort level (`None` to `ExtraHigh`) and an `Output` setting. OpenAI-compatible servers accept `reasoning_effort` with values up to `high`; nothing controls whether reasoning is returned.
- **`ResponseFormat`** (text, JSON, JSON schema) is OpenAI-compatible, but the spec had deferred it to milestone 6 (structured output).

## Decision

- **`TopK`** is sent as `top_k`. If a server ignores it, that is the server's behavior; EmberLlm sent what was asked.
- **`Reasoning.Effort`** is sent as `reasoning_effort` for `None`, `Low`, `Medium` and `High`. `ExtraHigh` has no OpenAI-compatible value and throws `NotSupportedException`. **`Reasoning.Output`** can't be honored and throws.
- **`ResponseFormat`** is sent now, rather than in milestone 6: text becomes `{"type":"text"}`, JSON without a schema becomes `{"type":"json_object"}`, and JSON with a schema becomes `{"type":"json_schema", ...}` (schema name `"response"` when none is given, since OpenAI requires one). Milestone 6 then focuses on the hard part: cleanup and retries for small models.

## Alternatives considered

- **Throw for `TopK`** and point callers to `AdditionalProperties["top_k"]`. Strict about "OpenAI-compatible", but it blocks the setting local users reach for most, for no real gain.
- **Throw for any `Reasoning` value until checked against real servers.** Safer, but LM Studio already accepts `reasoning_effort` for models that support it.
- **Keep `ResponseFormat` throwing until milestone 6**, as the spec said. Consistent with the original plan, but it contradicts "support everything OpenAI-compatible" for a simple field.

## Consequences

- Local-server users get the settings they expect through the standard `ChatOptions` API.
- JSON schema output works today: checked against LM Studio with `google/gemma-4-12b`, which returned `{"answer": 42}` for a schema requiring an integer `answer`.
- `reasoning_effort` and `top_k` have not been checked against Ollama, llama.cpp or vLLM yet; milestone 4's recorded responses should confirm each server accepts them.
