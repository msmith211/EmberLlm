# Roadmap

EmberLlm aims to stay small and focused on what is specific to local inference. This page lists what is planned, what is being considered, and what is deliberately out of scope. Nothing here has a date; versions ship when they are ready.

Have an idea? Open an issue to discuss it before sending a pull request.

## v0.1: first release (released)

- Chat completion: full responses, plain-text streaming, rich streaming with token usage
- Model listing from `/v1/models`
- Server discovery with backend fingerprinting (LM Studio, Ollama, llama.cpp server, vLLM, generic OpenAI-compatible)
- Typed structured output with `GetAsync<T>()`, output cleanup and corrective retries
- `IChatClient` adapter and `AddEmberLlm()` dependency injection

See [SPEC.md](SPEC.md) for the full v0.1 design and build order.

## v0.2: planned

- **Tool/function calling** through the OpenAI-compatible API, exposed through `IChatClient` so Microsoft.Extensions.AI function invocation works
- **Model management:** load and unload models through backend-native APIs (LM Studio, Ollama), and report which models are currently loaded

## Companion tool: `llmbench`

A separate `dotnet tool`, started after v0.1 is on NuGet and built on the published package. It measures time to first token, prompt-processing and generation speed, peak VRAM and power draw, speed versus context length, and throughput under parallel requests. Results come out as a terminal table, JSON, and a shareable Markdown snippet with hardware info.

## Under consideration

Ideas that may land later, depending on demand:

- Full JSON Schema validation for structured output (v0.1 validates by strict deserialization)
- Discovery that also reports which models are currently loaded, not just installed

## Out of scope

These are not planned. Other libraries already cover them, or they don't fit a client for local servers.

- **Running models in-process** (llama.cpp bindings). EmberLlm only talks to servers.
- **Cloud providers** (OpenAI, Anthropic and others). Use their official SDKs, most of which plug into Microsoft.Extensions.AI as well.
- **Embeddings, images and audio** are not planned for now. Open an issue if you have a strong use case.
