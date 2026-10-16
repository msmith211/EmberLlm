# EmberLlm

A .NET client for local LLM servers (LM Studio, Ollama, llama.cpp, vLLM), built on Microsoft.Extensions.AI.

> **Beta.** The API may change before 1.0. Typed structured output (`GetAsync<T>()`) is still to come.

- **Finds running servers.** `EmberLlmDiscovery.FindAsync()` probes the default ports of LM Studio, Ollama, llama.cpp and vLLM and identifies the backend.
- **Is an `IChatClient`.** Works anywhere Microsoft.Extensions.AI is used, including its middleware.
- **Keeps reasoning separate.** Thinking models' reasoning arrives as `TextReasoningContent`, not mixed into the answer.
- **Never fails silently.** Every option you set is sent or rejected before the request goes out.
- **Suits local hardware.** No model name required, a 10-minute default timeout with no silent re-sends, and clear errors when the server isn't running.

## Quick start

```csharp
using EmberLlm;

using var llm = new EmberLlmClient("http://127.0.0.1:1234");

string answer = await llm.CompleteAsync("Explain Span<T> in two sentences.");
Console.WriteLine(answer);

await foreach (string token in llm.StreamAsync("Write a haiku about garbage collection."))
    Console.Write(token);
```

With dependency injection:

```csharp
services.AddEmberLlm(o => o.BaseUrl = "http://127.0.0.1:1234");
// then inject IChatClient or EmberLlmClient
```

## Learn more

- [README, with more examples and supported backends](https://github.com/msmith211/EmberLlm#readme)
- [Changelog](https://github.com/msmith211/EmberLlm/blob/main/CHANGELOG.md)
- [Design decisions](https://github.com/msmith211/EmberLlm/tree/main/docs/decisions)
- [Report a bug](https://github.com/msmith211/EmberLlm/issues)

MIT licensed.
