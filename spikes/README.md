# Spikes

Time-boxed experiments that answer a question before a feature is built. Spike code is throwaway: it isn't part of the solution, isn't built or tested in CI, and may use packages EmberLlm itself doesn't depend on. Findings are written up in [docs/spikes/](../docs/spikes/).

| Spike | Question | Findings |
| --- | --- | --- |
| [comparison](comparison/Comparison.cs) | What does EmberLlm add over the official OpenAI SDK plus Microsoft's `IChatClient` adapter, pointed at a local server? How well does Microsoft.Extensions.AI's own `GetResponseAsync<T>` handle small local models? | [0001](../docs/spikes/0001-comparison-with-openai-sdk.md) |

Each spike is a single-file .NET 10 app. Run one from the repository root, for example:

```bash
dotnet run spikes/comparison/Comparison.cs -- http://127.0.0.1:1234 qwen/qwen3-4b-thinking-2507 5
```
