# Spike 0001: EmberLlm vs. the OpenAI SDK with Microsoft's adapter

- Date: 2026-10-13
- Milestone: 5b
- Code: [spikes/comparison/Comparison.cs](../../spikes/comparison/Comparison.cs)

## Question

A .NET developer can already reach a local LLM server without EmberLlm: create the official OpenAI SDK's `ChatClient`, point it at `http://127.0.0.1:1234/v1`, and wrap it as an `IChatClient` with `Microsoft.Extensions.AI.OpenAI`'s `AsIChatClient()`. What does EmberLlm add over that, measured rather than assumed? And how well does Microsoft.Extensions.AI's own `GetResponseAsync<T>` already produce typed output from a small local model? That second answer sets the scope of milestone 6 (structured output).

## Setup

- **Server:** LM Studio on Windows 11, with one model loaded: `qwen/qwen3-4b-thinking-2507` (Q4_K_M), a small thinking model.
- **EmberLlm:** `main` at the start of milestone 5b.
- **Comparison:** `OpenAI` 2.14.0, `Microsoft.Extensions.AI.OpenAI` 10.10.1, `Microsoft.Extensions.AI` 10.10.0.
- **Method:** both clients ran the same calls through `IChatClient`. Every request body either library sent was recorded, so the comparison covers what actually reached the server.

## Results

| Area | EmberLlm | OpenAI SDK + adapter |
| --- | --- | --- |
| Basic chat | Correct answer, ~2.7 s | Correct answer, ~3.0 s |
| Reasoning from a thinking model | Separate, as `TextReasoningContent` | **Also separate**, as `TextReasoningContent` |
| Streaming with token usage | Usage in the final update | **Also** usage in the final update |
| No model name given | Picks the server's first listed model and answers | Can't be constructed: `ArgumentException` ("Value cannot be an empty string. (Parameter 'model')") |
| Unknown model name, one model loaded | Answered by the loaded model, no error (server behavior); `ModelId` shows the substitution, and `StrictModelMatching` turns it into `ModelMismatchException` | Answered by the loaded model, no error, with no option to detect it |
| Server not running | `EmberLlmConnectionException` ("Could not reach the server at ...") after ~2.0 s | `AggregateException` ("Retry failed after 4 tries") after ~8.2 s |
| Server accepts the connection but never answers | One timeout after `RequestTimeout` (default 10 minutes), as `EmberLlmTimeoutException` | Times out after **100 s** and silently re-sends the request: attempts at 0 s, 100 s and 200 s |
| `ChatOptions.TopK = 40` | Sent as `top_k` | **Silently dropped** (not in the request) |
| `ChatOptions.AdditionalProperties["min_p"]` | Sent as `min_p` | **Silently dropped** |
| `ChatOptions.ConversationId` | `NotSupportedException` before any request | **Silently ignored**: request sent, setting had no effect |
| Discovery (which servers are running, which backend) | `EmberLlmDiscovery.FindAsync` | Not available |

### Typed output with Microsoft.Extensions.AI's `GetResponseAsync<T>`

Prompt: review `public int Add(int a, int b) => a - b;` into `record CodeReview(string Summary, string[] Issues, int Severity)`. There were two runs: 5 calls, then 10 calls, per combination.

| Mode | EmberLlm | OpenAI SDK + adapter |
| --- | --- | --- |
| JSON schema response format on (the default) | 14/15 parsed | 13/15 parsed |
| JSON schema response format off | 0/15: LM Studio rejects the request with HTTP 400 | 0/15: same HTTP 400 |

- **Schema on:** both clients send `response_format: json_schema`, and LM Studio constrains the output, so results are close; the difference is within noise. The failures, about 1 in 10, finished normally (`stop`) with output that was valid JSON containing all three fields, yet didn't deserialize into `CodeReview`. The exact cause wasn't isolated; a field value of the wrong type (for example objects instead of strings in `issues`) fits the evidence.
- **Schema off:** in this mode Microsoft.Extensions.AI asks for `response_format: json_object`. LM Studio supports only `json_schema` and `text`, and rejects it with "'response_format.type' must be 'json_schema' or 'text'".
- **In trimmed or AOT-published apps,** `GetResponseAsync<T>` needs JSON type information supplied by the caller. Under .NET 10's default settings for single-file apps it failed with `NotSupportedException` until reflection-based serialization was enabled.

## What this means

**Not differentiators:** keeping reasoning separate, and streaming with usage. Microsoft's adapter handles both against LM Studio, so they're features of EmberLlm, not reasons to choose it.

**Differentiators, measured:**
1. **Discovery.** No equivalent exists.
2. **Never failing silently.** The adapter quietly dropped `TopK` and `AdditionalProperties` and ignored `ConversationId`; EmberLlm sent the first two and rejected the third before sending anything.
3. **Errors and timeouts suited to local servers.** No model name needed; a direct "could not reach the server" error in about a quarter of the time; no silent re-sending. With the OpenAI SDK's defaults, any generation longer than 100 seconds (common for long answers on local hardware) is cut off and started again, up to three times, before failing.
4. **Detecting model substitution** (`StrictModelMatching`).

**Typed output (milestone 6):** Microsoft.Extensions.AI's `GetResponseAsync<T>` already works through EmberLlm, because EmberLlm is an `IChatClient`. With JSON schema mode on LM Studio it parsed about 9 times in 10. The gaps it leaves:
- **No retry:** about 1 in 10 parses failed with no second attempt.
- **No fallback for servers without JSON-object support:** its non-schema mode fails outright on LM Studio.
- **No cleanup** of `<think>` blocks or code fences for servers that don't enforce a schema. Not observed here, because LM Studio enforces the schema and separates reasoning.

Re-implementing schema generation and parsing would duplicate what's already there; the remaining value is retries and backend-aware fallbacks.

## Limits

- One server (LM Studio), one model, one machine. Ollama, llama.cpp and vLLM may behave differently, especially on `json_object` support and reasoning fields.
- The typed-output sample is 30 calls per client. That's enough to show a single-digit-percent failure rate, not to compare the two clients precisely.
- The "never answers" test used a socket that accepts connections but never replies, not a real slow generation. The 100 s cut-off and retries come straight from the SDK's exception message and the observed connection times.

## Raw output

<details>
<summary>Run 1: sections 1–7, 5 typed-output calls per combination</summary>

```text
## 1. Basic chat ("What is 17 + 25?")
- EmberLlm (2742 ms): text=[42] reasoningChars=1285 <think>InText=False model=qwen/qwen3-4b-thinking-2507 usage(in/out)=26/378 finish=stop
- OpenAI SDK + adapter (3034 ms): text=[42] reasoningChars=1457 <think>InText=False model=qwen/qwen3-4b-thinking-2507 usage(in/out)=26/418 finish=stop

## 2. Streaming
- EmberLlm (2707 ms): updates=392 text=[42] reasoningChars=1372 usageReported=True usage(out)=393 finish=stop
- OpenAI SDK + adapter (2411 ms): updates=351 text=[42] reasoningChars=1199 usageReported=True usage(out)=352 finish=stop

## 3. No model name given
- EmberLlm (956 ms): CompleteAsync answered: [ok]
- OpenAI SDK + adapter (1 ms): constructor requires a model: ArgumentException: Value cannot be an empty string. (Parameter 'model')

## 4. Unknown model name ("does-not-exist")
- EmberLlm (2057 ms): no error; answered by model=qwen/qwen3-4b-thinking-2507
- EmberLlm, StrictModelMatching (2327 ms): ModelMismatchException: Requested model "does-not-exist" but http://127.0.0.1:1234/v1/chat/completions answered with "qwen/qwen3-4b-thinking-2507".
- OpenAI SDK + adapter (3993 ms): no error; answered by model=qwen/qwen3-4b-thinking-2507

## 5. Server down (nothing listening on port 1299)
- EmberLlm (2035 ms): EmberLlmConnectionException: Could not reach the server at http://127.0.0.1:1299/v1/chat/completions: No connection could be made because the target machine actively refused it.
- OpenAI SDK + adapter (8167 ms): AggregateException: Retry failed after 4 tries. (No connection could be made because the target machine actively refused it. ...)

## 6. Defaults
- OpenAI SDK NetworkTimeout default: null (library default)
- EmberLlm RequestTimeout default: 00:10:00

## 7. Typed output, 5 runs each
- EmberLlm, json schema response format on: 4/5 parsed (32 s)
- EmberLlm, json schema response format off: 0/5 parsed: EmberLlmHttpException: 400 BadRequest: 'response_format.type' must be 'json_schema' or 'text'
- OpenAI SDK + adapter, json schema response format on: 4/5 parsed (39 s)
- OpenAI SDK + adapter, json schema response format off: 0/5 parsed: ClientResultException: Service request failed. Status: 400 (Bad Request)
```

</details>

<details>
<summary>Run 2: sections 7–9, 10 typed-output calls per combination</summary>

```text
## 7. Typed output, 10 runs each
- EmberLlm, json schema response format on: 10/10 parsed (58 s)
- EmberLlm, json schema response format off: 0/10 parsed: EmberLlmHttpException: 400 BadRequest: 'response_format.type' must be 'json_schema' or 'text'
- OpenAI SDK + adapter, json schema response format on: 9/10 parsed (92 s)
  - unparseable: finish=stop outTokens=1002 textChars=590 validJson=True missing=[] severityKind=Number fenced=False
- OpenAI SDK + adapter, json schema response format off: 0/10 parsed: ClientResultException: Service request failed. Status: 400 (Bad Request)

## 8. What actually goes on the wire
- EmberLlm: sent top_k=True sent min_p=True
- OpenAI SDK + adapter: sent top_k=False sent min_p=False
- EmberLlm, ConversationId set: NotSupportedException: ChatOptions.ConversationId is not supported by EmberLlm. OpenAI-compatible chat completions have no server-side conversations; send the full message history instead.
- OpenAI SDK + adapter, ConversationId set: no error (ConversationId not honored by chat completions)

## 9. A server that never answers
- OpenAI SDK + adapter (defaults) (230071 ms): AggregateException after attempts at 0 s, 100 s, 200 s: Retry failed after 3 tries. (The operation was cancelled because it exceeded the configured timeout of 0:01:40. ...)
- EmberLlm: RequestTimeout default 00:10:00, no retries; times out once with EmberLlmTimeoutException.
```

</details>
