# 0002. Resolve the model from request, then default, then the server's first listed model

- Status: Accepted
- Date: 2026-10-09

## Context

The OpenAI chat API requires a `model` field, but the simplest EmberLlm calls (`CompleteAsync("...")`) don't name one. Backends disagree on what an empty or unknown model means: LM Studio falls back to whatever model is loaded, while Ollama rejects the request. EmberLlm needs one predictable rule that works on every backend.

## Decision

For each request, use the first of these that is available:

1. `ChatRequest.Model`
2. `EmberLlmOptions.DefaultModel`
3. The first model returned by `GET /v1/models`, fetched once and cached for the lifetime of the client
4. Otherwise throw `NoModelAvailableException`, with a message saying how to fix it

A failed or empty listing is not cached, so loading a model on the server and retrying works without recreating the client. Options are copied at construction, so changing the options object later does not change an existing client's behavior.

## Alternatives considered

- **Always require a model.** Simplest and fully explicit, but it breaks the "it just works" quick start, and every caller would end up writing the same lookup.
- **Send no model and let the server decide.** Works on LM Studio, fails on Ollama and vLLM. Backend-dependent behavior is exactly what this library exists to hide.
- **List models on every request.** Always current, but adds a round trip to every call. Models rarely change during a client's lifetime, and an explicit `DefaultModel` covers the cases where it matters.
- **Pick the currently loaded model through backend-native APIs** (such as Ollama's `/api/ps`). More accurate, but needs per-backend code that v0.1 deliberately avoids. Model management is planned for v0.2 and can revisit this.

## Consequences

- The quick start needs no configuration on LM Studio, llama.cpp and vLLM, where the first listed model is the one being served.
- On Ollama, `/v1/models` lists every installed model, so the first one may not be loaded and using it can trigger a slow model load. The README tells Ollama users to set `DefaultModel`.
- The first call without a configured model costs one extra request.
- If the server swaps models while a client is alive, the cached choice goes stale. Callers who swap models should name them explicitly.
