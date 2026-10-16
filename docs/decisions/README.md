# Design decisions

Short records of significant design choices: what was decided, why, and what else was considered. Each note answers the question a reviewer would ask: "why did you do it this way?"

| # | Decision | Status |
| --- | --- | --- |
| [0001](0001-reuse-microsoft-extensions-ai-types.md) | Reuse Microsoft.Extensions.AI types for messages and responses | Accepted; `ChatRequest` part superseded by 0005 |
| [0002](0002-model-selection-order.md) | Resolve the model from request, then default, then the server's first listed model | Accepted |
| [0003](0003-exception-model.md) | Separate exception types for HTTP errors, unreachable servers, timeouts and cancellation | Accepted |
| [0004](0004-streaming-timeout-scope.md) | RequestTimeout bounds the whole stream | Accepted |
| [0005](0005-emberllmclient-implements-ichatclient.md) | Make EmberLlmClient an IChatClient instead of wrapping one | Accepted |
| [0006](0006-fix-silent-data-drops.md) | Fix silent data drops in the chat mapping | Accepted |
| [0007](0007-map-topk-reasoning-and-response-format.md) | Map TopK, reasoning effort and response format | Accepted |
| [0008](0008-model-substitution-and-strict-matching.md) | Report model substitution, with opt-in strict matching | Accepted |
| [0009](0009-discovery-design.md) | Discovery: confirm with /v1/models, identify by content, report what was skipped | Accepted |
| [0010](0010-parallel-connect-for-owned-http-clients.md) | Connect to all of a host's addresses at once in EmberLlm-owned HttpClients | Accepted |
| [0011](0011-register-a-singleton-client-with-addemberllm.md) | Register a singleton EmberLlmClient with AddEmberLlm, not a transient typed client | Proposed |

## Writing a new note

Copy the structure below into `NNNN-short-title.md`, using the next number. Keep it under a page.

```markdown
# NNNN. Title in the imperative ("Use X for Y")

- Status: Proposed | Accepted | Superseded by [NNNN](NNNN-title.md)
- Date: YYYY-MM-DD

## Context
What problem or constraint forced a choice.

## Decision
What we chose, stated plainly.

## Alternatives considered
Each option and why it lost.

## Consequences
What gets easier, what gets harder, and anything we now have to live with.
```

Notes are not edited after they are accepted, except to update the status. If a decision changes, write a new note that supersedes the old one.
