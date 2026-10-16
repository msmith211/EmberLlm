// Registers EmberLlm with dependency injection, resolves IChatClient and chats through it.
// Usage: dotnet run --project samples/QuickStart [server URL]   (default: LM Studio on http://127.0.0.1:1234)

using EmberLlm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

var baseUrl = args.Length > 0 ? args[0] : "http://127.0.0.1:1234";

var services = new ServiceCollection();
services.AddEmberLlm(options => options.BaseUrl = baseUrl);
await using var provider = services.BuildServiceProvider();

IChatClient chat = provider.GetRequiredService<IChatClient>();

try
{
    // One reply; no model name needed, the server's first listed model is used
    ChatResponse reply = await chat.GetResponseAsync("Explain Span<T> in two sentences.");
    Console.WriteLine(reply.Text);
    Console.WriteLine($"({reply.ModelId}, {reply.Usage?.OutputTokenCount} output tokens)");
    Console.WriteLine();

    // Stream a reply as it is generated
    await foreach (ChatResponseUpdate update in chat.GetStreamingResponseAsync("Write a haiku about garbage collection."))
        Console.Write(update.Text);
    Console.WriteLine();
}
catch (EmberLlmConnectionException)
{
    Console.Error.WriteLine($"No server is running at {baseUrl}. Start one with a model loaded, or pass its URL.");
    return 1;
}
catch (EmberLlmException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

return 0;
