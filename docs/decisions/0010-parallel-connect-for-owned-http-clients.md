# 0010. Connect to all of a host's addresses at once in EmberLlm-owned HttpClients

- Status: Accepted (Michael, 2026-10-13)
- Date: 2026-10-13

## Context

`localhost` resolves to both `::1` (IPv6) and `127.0.0.1` (IPv4). .NET's `HttpClient` tries them one after another, IPv6 first. Many local LLM servers, LM Studio included, listen only on IPv4, and on Windows a refused connection takes about 2 seconds to fail. Measured on Windows against LM Studio, the first request to `http://localhost:1234` took ~2.1 s; to `http://127.0.0.1:1234`, ~3 ms. The delay recurs for every new connection, not just the first request a program makes.

## Decision

When EmberLlm creates its own `HttpClient` (the `EmberLlmClient` constructors that take a URL, options or a discovered server, and `EmberLlmDiscovery.FindAsync` without an injected client), it connects through a `SocketsHttpHandler.ConnectCallback` that:

- resolves the host and starts a connection to **every** address at once;
- keeps the first one that succeeds, cancels the rest, and closes any that connect afterwards;
- connects directly when the host is already an IP address;
- throws the first connection error if every attempt fails, which surfaces as `EmberLlmConnectionException`.

An `HttpClient` the caller passes in is never changed.

## Alternatives considered

- **Document it only** (recommend `127.0.0.1`). No code, but every user writing `localhost`, the natural choice, pays the delay without knowing why.
- **Rewrite `localhost` to `127.0.0.1`.** Simple, but it breaks servers that listen only on IPv6, and quietly changing the caller's URL goes against the never-fail-silently rule.
- **Full RFC 8305 "Happy Eyeballs"** (prefer IPv6, start IPv4 after a 250 ms head start). Better for remote hosts on dual-stack networks, but it would still cost a quarter of a second here; local servers gain nothing from preferring IPv6.

## Consequences

- `localhost` and `127.0.0.1` perform the same with EmberLlm-created clients. A test using a real IPv4-only loopback server checks this; with the callback removed, the same request took 2,071 ms and the test failed.
- A host with several addresses briefly gets one connection attempt per address. For local servers that's two attempts (IPv4 and IPv6), and the losing one is closed right away.
- Callers who inject their own `HttpClient` keep .NET's default behavior; the README tip recommends `127.0.0.1` for them.
