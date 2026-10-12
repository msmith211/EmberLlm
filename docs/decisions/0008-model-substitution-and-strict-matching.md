# 0008. Report model substitution, with opt-in strict matching

- Status: Accepted (Michael, 2026-10-12)
- Date: 2026-10-12

## Context

Testing against LM Studio with `google/gemma-4-12b` loaded, a request for a model that doesn't exist (`does-not-exist`) returned HTTP 200 and an answer from Gemma, with no error. Later, with just-in-time model loading reported as disabled, the same request returned a clear 400 error instead. So at least one server, depending on its settings, answers a request for one model with another.

EmberLlm already reports the model that answered in `ChatResponse.ModelId` (and on each streaming update), so nothing is hidden. But a caller who doesn't check that field won't notice.

## Decision

- **Default: report it and document it.** EmberLlm returns the server's reply as is; `ModelId` shows the model that actually answered. The README's backend notes tell LM Studio users about substitution.
- **Opt-in strict mode:** `EmberLlmOptions.StrictModelMatching`. When on:
  - A reply reporting a different model than the one requested throws `ModelMismatchException`, with `RequestedModel` and `ReportedModel`.
  - A reply that reports no model at all also throws, since it can't be verified.
  - The comparison ignores case but is otherwise exact.
  - When streaming, the first chunk that reports a model is checked before any update reaches the caller. A stream that never reports one throws at the end.

## Alternatives considered

- **Always throw on a mismatch.** Catches substitution for everyone, but servers legitimately answer with a different spelling of the same model (for example Ollama answering `llama3` as `llama3:latest`). That would turn working calls into failures.
- **Match loosely** (ignore tags, publisher prefixes). Fewer false alarms, but every normalization rule is a guess about some server's naming, and a wrong guess hides a real substitution. Exact matching in an opt-in mode keeps the behavior predictable.

## Consequences

- Nothing changes for callers who don't opt in.
- Strict mode can fail on servers that answer with aliases; the option's documentation says so.
- With strict mode on, a server that omits the `model` field can't be used, which is the price of guaranteeing the check.
