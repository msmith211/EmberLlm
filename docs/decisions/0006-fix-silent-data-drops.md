# 0006. Fix silent data drops in the chat mapping

- Status: Accepted
- Date: 2026-10-12

This is a fix, not a new design decision. A review found places where the chat mapping dropped data without telling anyone, which breaks the project's existing rule: **never fail silently**. A request value a server could act on is either sent or rejected with an exception, and every response field is either mapped or kept in `AdditionalProperties`. The rule was first applied to request options in [0005](0005-emberllmclient-implements-ichatclient.md).

Fixed:

- `ChatMessage.AuthorName` is now sent as `name`.
- `ChatMessage.AdditionalProperties` are passed through as extra message fields. A key that would override `role`, `content` or `name` throws.
- A response or stream chunk with more than one choice now throws, instead of everything after the first choice being dropped.
- Unmodeled message, choice and usage fields in responses are kept in `AdditionalProperties` instead of being discarded.

The only things deliberately left out are listed explicitly: message metadata (`MessageId`, `CreatedAt`, `RawRepresentation`), and reasoning from earlier turns, which servers expect not to be replayed. A guard test fails if `ChatMessage` gains a property that isn't classified.
