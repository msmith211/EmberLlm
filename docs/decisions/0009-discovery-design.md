# 0009. Discovery: confirm with /v1/models, identify by content, report what was skipped

- Status: Proposed (awaiting Michael's decision). The `/v1/models` requirement was already agreed in SPEC.md; the rest is new.
- Date: 2026-10-12

## Context

`EmberLlmDiscovery.FindAsync` probes common local ports and reports which LLM servers are running. Testing against a real LM Studio on Windows turned up three facts that shape the design:

1. **LM Studio answers unknown endpoints with HTTP 200** and an error body, for example `GET /health` returns `200 {"error":"Unexpected endpoint or method. (GET /health)"}`. A status-code fingerprint ("does `/health` return 200?") would misidentify it as llama.cpp, and "does `/version` return 200?" as vLLM.
2. **`localhost` is slow on Windows.** .NET tries IPv6 (`::1`) first; LM Studio listens only on IPv4, and the refused IPv6 attempt costs about 2.1 seconds, against 3 ms for `127.0.0.1`. With 500 ms probes, discovery on `localhost` found nothing.
3. **A closed port on Windows doesn't refuse quickly.** It times out, like a slow server, so "nothing there" and "too slow" can't be told apart.

The spec also says discovery never throws, which on its own would mean problems vanish silently, against the never-fail-silently rule.

## Decision

- **A server is reported only if `GET /v1/models` returns a valid model list** (already in SPEC.md). Unrelated web servers on 8000/8080 are never reported as LLM servers.
- **The backend is identified by response content**, checked in this order:
  1. **Ollama:** the root page contains "Ollama is running"; version from `/api/version`.
  2. **LM Studio:** `/api/v0/models` items carry LM Studio's own fields (`publisher`, `arch`, `state`), or `/api/v1/models` items carry `key`.
  3. **llama.cpp:** `/props` contains `default_generation_settings`; version from `build_info`.
  4. **vLLM:** model entries are `owned_by: "vllm"` or carry `max_model_len`; version from `/version`.
  5. **Generic** otherwise.

  Port numbers play no part: an Ollama server on port 8080 is still Ollama.
- **Default endpoints use `127.0.0.1`,** not `localhost`, on ports 1234, 11434, 8080 and 8000.
- **`FindAsync` returns a `DiscoveryResult`:** the servers, in endpoint order, plus `Skipped`, which lists endpoints that **answered but weren't usable**, with the reason (for example "GET .../v1/models returned 404 NotFound"). Endpoints where nothing answered are not listed: on Windows a closed port looks exactly like that, so listing them would be noise. `DiscoveryResult` is an `IReadOnlyList<DiscoveredServer>`, so the spec's `IReadOnlyList<DiscoveredServer> servers = ...` still works.
- **Never throws because of a server;** caller cancellation still throws `OperationCanceledException`.

## Alternatives considered

- **Identify by status code, or by port.** Simple, but wrong for LM Studio (fact 1) and for any server not on its default port.
- **Keep `localhost` defaults.** Friendlier URLs, but discovery fails on Windows for IPv4-only servers (fact 2).
- **Return a plain list and drop problems.** Matches the spec's original sketch exactly, but a misconfigured server would just vanish, which breaks the never-fail-silently rule.
- **Also report endpoints that timed out.** More complete, but on Windows every closed default port would be reported (fact 3).

## Consequences

- Discovery takes about twice `ProbeTimeout` (~0.5–1 s with defaults): one round for `/v1/models`, one for the fingerprints.
- Only the LM Studio fingerprint is verified against a real server. The Ollama, llama.cpp and vLLM fingerprints follow their documentation and are tested against hand-written responses until real recordings exist.
- Open question for Michael, not part of this decision: `new EmberLlmClient("http://localhost:1234")` pays the same ~2 s on its first request (see the milestone 5 PR).
