# Contributing to EmberLlm

Thanks for your interest in EmberLlm. Bug reports, fixture recordings from real backends, and small focused pull requests are all welcome. This page covers what you need to build, test and send a change.

## Scope

v0.1 is deliberately small: chat completion, model listing, server discovery, typed structured output, and the `IChatClient` adapter. Before starting on something new, check the [v0.1 non-goals in SPEC.md](SPEC.md#non-goals-for-v01). Tool calling, model loading/unloading, embeddings, in-process inference and cloud providers are all out of scope for now.

If your idea falls outside that scope, open an issue first or add it to [ROADMAP.md](ROADMAP.md) instead of sending a pull request. That saves you from writing code that can't be merged yet.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Optional: a local LLM server (LM Studio, Ollama, llama.cpp server or vLLM) if you want to run the integration tests

## Build and test

From the repo root:

```sh
dotnet build
dotnet test
```

Unit tests (`tests/EmberLlm.Tests/`) need no server. They use a fake `HttpMessageHandler` that replays recorded responses, so they run anywhere. CI runs the build and unit tests on Linux and Windows for every push and pull request.

### Integration tests (opt-in)

Tests in `tests/EmberLlm.IntegrationTests/` hit a real local server. They are skipped unless the `EMBERLLM_TEST_URL` environment variable points at one. Load the smallest model you have so they run quickly.

```sh
# bash
EMBERLLM_TEST_URL=http://localhost:1234 dotnet test tests/EmberLlm.IntegrationTests
```

```powershell
# PowerShell
$env:EMBERLLM_TEST_URL = "http://localhost:1234"
dotnet test tests/EmberLlm.IntegrationTests
```

Integration tests don't run in CI, so mention in your pull request which backend you ran them against, if any.

### Recorded fixtures

Backend quirks are tested from real responses stored under `tests/fixtures/<backend>/`, one folder per backend: `lmstudio`, `ollama`, `llamacpp`, `vllm`, or `generic` for other OpenAI-compatible servers. To add one:

1. Capture the actual response from the backend (for example with `curl` against its `/v1` endpoint). Don't hand-write or "tidy" it; the quirk is the point.
2. Save it under `tests/fixtures/<backend>/`.
3. Add a unit test in `tests/EmberLlm.Tests/` that replays the fixture through the fake HTTP handler and asserts the behavior you expect.
4. Note the backend name and version in the pull request.

If you're not sure what the backend is supposed to do, write the fixture test, mark it skipped with the reason, and raise the question in the pull request or an issue. Please don't commit failing tests.

## Coding conventions

- Nullable reference types are enabled and warnings are treated as errors. A build with warnings is a failing build.
- Every public async method takes a `CancellationToken`.
- Every public member has an XML doc comment.
- No static state and no hidden singletons inside the library.
- Reuse Microsoft.Extensions.AI types (`ChatMessage`, `ChatResponse`, `ChatResponseUpdate`, ...) rather than defining parallel ones. Backend-specific extras go in `AdditionalProperties`.
- No new runtime dependencies without discussing them in an issue first. The current list is in [SPEC.md](SPEC.md#tech-constraints-and-layout). Test-only packages are fine but must never be referenced from `src/`.

## Commits

Use [conventional commits](https://www.conventionalcommits.org/) and keep each one small and focused:

- `feat:` new user-visible behavior
- `fix:` a bug fix
- `test:` tests or fixtures only
- `docs:` documentation only
- `chore:` build, CI, tooling and other housekeeping

Example: `fix: handle keep-alive comments split across SSE chunks`

## Pull request checklist

Work on a branch and open the pull request against `main`. Before you open it, check that:

- [ ] Tests are included alongside the code change, not as a follow-up
- [ ] `dotnet build` and `dotnet test` pass locally
- [ ] User-visible changes have an entry under "Unreleased" in [CHANGELOG.md](CHANGELOG.md)
- [ ] Significant design choices have a short decision note in [`docs/decisions/`](docs/decisions/) (`NNNN-title.md`: context, decision, alternatives considered, consequences). If a reviewer would ask "why did you do it this way?", it needs one.

## Reporting bugs

Open an issue using the bug-report template and include:

- Backend name and version (LM Studio, Ollama, llama.cpp server, vLLM, or another OpenAI-compatible server)
- The model you were using
- A minimal repro: the smallest code snippet that shows the problem, plus the raw server response if you can capture it

A raw response is especially useful because it can often become a fixture and a regression test directly.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
