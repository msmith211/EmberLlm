# 0001. Reuse Microsoft.Extensions.AI types for messages and responses

- Status: Accepted
- Date: 2026-10-09

## Context

LocalLlm implements `IChatClient` from `Microsoft.Extensions.AI.Abstractions`, so it already depends on that package. That package defines `ChatMessage`, `ChatRole`, `ChatResponse`, `ChatResponseUpdate` and `UsageDetails`.

The first API sketch defined LocalLlm's own `ChatMessage` with static factories (`ChatMessage.System("...")`). Any project that imports both `LocalLlm` and `Microsoft.Extensions.AI`, which is the exact audience for the `IChatClient` adapter, would then get ambiguous-reference errors (CS0104) on every use and need aliases or fully qualified names to compile.

## Decision

Use the Microsoft.Extensions.AI types directly wherever a concept already exists there:

- Messages and roles: `ChatMessage`, `ChatRole`
- Full responses, including usage and finish reason: `ChatResponse`, `UsageDetails`
- Streaming updates: `ChatResponseUpdate`

LocalLlm defines its own types only for concepts Microsoft.Extensions.AI does not have: `ChatRequest` (local-specific options), `ModelInfo`, `DiscoveredServer` and `BackendType`. Backend-specific extras, such as llama.cpp's `timings` object, go into the existing `AdditionalProperties` dictionaries rather than new types.

## Alternatives considered

- **Own types with the same names.** The nicest syntax in isolation (`ChatMessage.User("Hi")`), but causes CS0104 for every user of the `IChatClient` adapter. Rejected.
- **Own types with a prefix** (`LlmMessage`, `LlmResponse`). No name clash, but two parallel vocabularies for the same concepts, and the adapter must convert in both directions on every call. That conversion code is easy to get subtly wrong (lost metadata, roles, content parts) and adds nothing for users. Rejected.
- **Own types in a separate namespace** that users import selectively. Pushes the problem onto users and still requires conversion code. Rejected.

## Consequences

- No ambiguous references; LocalLlm and Microsoft.Extensions.AI can be imported together freely.
- `AsChatClient()` becomes a thin adapter, because requests and responses already use the shared types.
- Usage and finish reason come for free on `ChatResponse`, which the planned benchmark tool needs.
- Message construction is slightly more verbose: `new ChatMessage(ChatRole.User, "Hi")` or target-typed `new(ChatRole.User, "Hi")` instead of `ChatMessage.User("Hi")`.
- LocalLlm's public API is coupled to Microsoft.Extensions.AI's versioning. A breaking change there (the package is still evolving) can force a breaking change here. This is accepted because the adapter would be coupled to it anyway.
