# 0012. Register a singleton EmberLlmClient with AddEmberLlm, not a transient typed client

- Status: Accepted (Michael, 2026-10-16)
- Date: 2026-10-16

## Context

`AddEmberLlm()` registers `EmberLlmClient` and `IChatClient` and gets its `HttpClient` from `IHttpClientFactory` (SPEC, Tech constraints). The usual way to do that is a typed client, `AddHttpClient<EmberLlmClient>()`, which registers the client as **transient**: every resolution builds a new `EmberLlmClient` around a fresh `HttpClient`.

That fits EmberLlm poorly:

- **`EmberLlmClient` caches the server's first listed model** when no model is named ([0002](0002-model-selection-order.md)). A new instance per resolution repeats the `/v1/models` request every time.
- **It is `IDisposable`.** The container keeps every transient disposable it creates from the root provider until the provider is disposed, so a console app that resolves `IChatClient` in a loop grows without bound. Measured: 20,000 resolutions of a transient typed `EmberLlmClient` from the root provider kept 17.8 MB alive, with every sampled instance still reachable after a full GC; the singleton registration kept nothing.
- **`IChatClient` is usually injected into long-lived services.** Microsoft.Extensions.AI's own `AddChatClient()` registers a singleton, and middleware pipelines are built once. A transient captured by a singleton keeps its `HttpClient` for good anyway, so the rotation that transient registration exists for doesn't happen.

## Decision

`AddEmberLlm(Action<EmberLlmOptions>)`:

- registers **one singleton** `EmberLlmClient`, created with `IHttpClientFactory.CreateClient("EmberLlm")` and the configured `EmberLlmOptions`, and resolves `IChatClient` to the same instance (both with `TryAdd`, so calling it twice still gives one client);
- configures the named `HttpClient` with `Timeout = Infinite` (requests are bounded by `RequestTimeout`; the factory's 100-second default would cut off slow local generation), the parallel-connect primary handler from [0010](0010-parallel-connect-for-owned-http-clients.md), and an infinite handler lifetime, since the singleton keeps its handler for its whole life;
- returns the `IHttpClientBuilder`, so callers can add handlers (logging, resilience, headers) or replace the primary handler.

Options are read once, when the client is first resolved, matching the constructors, which copy options.

## Alternatives considered

- **Transient typed client** (`AddHttpClient<EmberLlmClient>()`). Idiomatic, but has the model-lookup, disposal and capture problems above.
- **Singleton with no `IHttpClientFactory`**, built like the URL constructor does. Simplest, but callers lose the factory's handler pipeline, which is the main reason to use DI for an HTTP client.
- **Scoped.** Suits per-request web work, but `IChatClient` consumers are typically singletons, which can't take a scoped dependency.
- **Leave the factory defaults** (100-second timeout, sequential connect). Would quietly time out long generations and bring back the `localhost` delay that 0010 fixed.

## Consequences

- One `/v1/models` lookup per application, not per resolution, and nothing for the container to accumulate.
- The handler is never rotated, so a DNS change for the server's host isn't seen until the app restarts. For servers on the same machine or LAN this is the same behavior as the URL constructor. Callers who need rotation can replace the primary handler with a `SocketsHttpHandler` that sets `PooledConnectionLifetime`.
- Applying 0010's handler here is consistent with 0010: the library creates this `HttpClient`'s handler, so it is not a caller's `HttpClient` being changed.
- One server per container. Registering several servers (keyed services) can be added later without breaking this API.
- The library now depends on `Microsoft.Extensions.Http` and `Microsoft.Extensions.DependencyInjection.Abstractions`, both on SPEC's allowed list.
