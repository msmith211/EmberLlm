// Spike (milestone 5b): EmberLlm vs. the official OpenAI SDK + Microsoft.Extensions.AI.OpenAI adapter,
// against a real local server. Throwaway code: not part of the solution or CI. Findings: docs/spikes/.
//
// Run:  dotnet run spikes/comparison/Comparison.cs -- [baseUrl] [model] [structuredRuns] [sections, e.g. 1,2,7]
#:package OpenAI@2.14.0
#:package Microsoft.Extensions.AI.OpenAI@10.10.1
#:package Microsoft.Extensions.AI@10.10.0
#:project ../../src/EmberLlm/EmberLlm.csproj
#:property ManagePackageVersionsCentrally=false
#:property TreatWarningsAsErrors=false
#:property EnableNETAnalyzers=false
#:property EnforceCodeStyleInBuild=false
// File-based apps default to AOT-friendly settings, which disable the reflection GetResponseAsync<T> uses
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using EmberLlm;
using Microsoft.Extensions.AI;
using OpenAI;

var baseUrl = args.Length > 0 ? args[0] : "http://127.0.0.1:1234";
var model = args.Length > 1 ? args[1] : "qwen/qwen3-4b-thinking-2507";
var structuredRuns = args.Length > 2 ? int.Parse(args[2]) : 5;
var sections = args.Length > 3 ? args[3].Split(',').Select(int.Parse).ToHashSet() : Enumerable.Range(1, 9).ToHashSet();
const int TokenBudget = 4000;

// Every request either library sends is recorded, so the exact JSON on the wire can be compared
var wire = new RecordingHandler();
IChatClient OpenAiClient(string modelId, string url = "", TimeSpan? networkTimeout = null) =>
    new OpenAI.Chat.ChatClient(modelId, new ApiKeyCredential("not-needed"), new OpenAIClientOptions
    {
        Endpoint = new Uri((url == "" ? baseUrl : url) + "/v1"),
        Transport = new HttpClientPipelineTransport(new HttpClient(wire) { Timeout = Timeout.InfiniteTimeSpan }),
        NetworkTimeout = networkTimeout,
    }).AsIChatClient();

using var ember = new EmberLlmClient(new HttpClient(new RecordingHandler(wire)), new EmberLlmOptions { BaseUrl = baseUrl });
var openAi = OpenAiClient(model);
var clients = new (string Name, IChatClient Client)[] { ("EmberLlm", ember), ("OpenAI SDK + adapter", openAi) };

Console.WriteLine($"# Comparison spike\n\nServer: {baseUrl}, model: {model}, run {DateTimeOffset.Now:yyyy-MM-dd HH:mm}\n");

if (sections.Contains(1))
{
    Section("1. Basic chat (\"What is 17 + 25?\")");
    foreach (var (name, client) in clients)
    {
        await Measure(name, async () =>
        {
            var r = await client.GetResponseAsync(
                [new(ChatRole.User, "What is 17 + 25? Reply with just the number.")],
                new ChatOptions { ModelId = model, MaxOutputTokens = TokenBudget, Temperature = 0f });
            return $"text=[{r.Text.Trim()}] reasoningChars={Reasoning(r.Messages.SelectMany(m => m.Contents))} " +
                   $"<think>InText={r.Text.Contains("<think>")} model={r.ModelId} usage(in/out)={r.Usage?.InputTokenCount}/{r.Usage?.OutputTokenCount} finish={r.FinishReason}";
        });
    }
}

if (sections.Contains(2))
{
    Section("2. Streaming");
    foreach (var (name, client) in clients)
    {
        await Measure(name, async () =>
        {
            var updates = new List<ChatResponseUpdate>();
            await foreach (var u in client.GetStreamingResponseAsync(
                [new(ChatRole.User, "What is 6 times 7? Reply with just the number.")],
                new ChatOptions { ModelId = model, MaxOutputTokens = TokenBudget, Temperature = 0f }))
            {
                updates.Add(u);
            }

            var r = updates.ToChatResponse();
            return $"updates={updates.Count} text=[{r.Text.Trim()}] reasoningChars={Reasoning(updates.SelectMany(u => u.Contents))} " +
                   $"usageReported={r.Usage is not null} usage(out)={r.Usage?.OutputTokenCount} finish={r.FinishReason}";
        });
    }
}

if (sections.Contains(3))
{
    Section("3. No model name given");
    await Measure("EmberLlm", async () => $"CompleteAsync answered: [{(await ember.CompleteAsync("Reply with just: ok")).Trim()}]");
    await Measure("OpenAI SDK + adapter", () =>
    {
        try { _ = OpenAiClient(""); return Task.FromResult("empty model accepted at construction"); }
        catch (Exception ex) { return Task.FromResult($"constructor requires a model: {ex.GetType().Name}: {First(ex.Message)}"); }
    });
}

if (sections.Contains(4))
{
    Section("4. Unknown model name (\"does-not-exist\")");
    await Measure("EmberLlm", async () => Describe(await ember.GetResponseAsync([new(ChatRole.User, "Say hi.")], new ChatOptions { ModelId = "does-not-exist", MaxOutputTokens = TokenBudget })));
    await Measure("EmberLlm, StrictModelMatching", async () =>
    {
        using var strict = new EmberLlmClient(new EmberLlmOptions { BaseUrl = baseUrl, StrictModelMatching = true });
        return Describe(await strict.GetResponseAsync([new(ChatRole.User, "Say hi.")], new ChatOptions { ModelId = "does-not-exist", MaxOutputTokens = TokenBudget }));
    });
    await Measure("OpenAI SDK + adapter", async () => Describe(await OpenAiClient("does-not-exist").GetResponseAsync([new(ChatRole.User, "Say hi.")], new ChatOptions { MaxOutputTokens = TokenBudget })));
}

if (sections.Contains(5))
{
    Section("5. Server down (nothing listening on port 1299)");
    await Measure("EmberLlm", async () =>
    {
        using var down = new EmberLlmClient(new EmberLlmOptions { BaseUrl = "http://127.0.0.1:1299", DefaultModel = model });
        return Describe(await down.GetResponseAsync([new(ChatRole.User, "Hi")]));
    });
    await Measure("OpenAI SDK + adapter", async () => Describe(await OpenAiClient(model, "http://127.0.0.1:1299").GetResponseAsync([new(ChatRole.User, "Hi")])));
}

if (sections.Contains(6))
{
    Section("6. Defaults");
    var defaults = new OpenAIClientOptions();
    Console.WriteLine($"- OpenAI SDK NetworkTimeout default: {defaults.NetworkTimeout?.ToString() ?? "null (library default)"}");
    Console.WriteLine($"- EmberLlm RequestTimeout default: {EmberLlmOptions.DefaultRequestTimeout}");
}

if (sections.Contains(7))
{
    Section($"7. Typed output: Microsoft.Extensions.AI GetResponseAsync<T>, {structuredRuns} runs each");
    const string ReviewPrompt = "Review this C# method and report its problems: public int Add(int a, int b) => a - b;";
    foreach (var (name, client) in clients)
    {
        foreach (var useSchema in new[] { true, false })
        {
            var successes = 0;
            var failures = new List<string>();
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < structuredRuns; i++)
            {
                try
                {
                    var r = await client.GetResponseAsync<CodeReview>(
                        ReviewPrompt,
                        new ChatOptions { ModelId = model, MaxOutputTokens = TokenBudget },
                        useJsonSchemaResponseFormat: useSchema);
                    if (r.TryGetResult(out var review) && review is { Summary: not null })
                    {
                        successes++;
                    }
                    else
                    {
                        failures.Add($"unparseable: finish={r.FinishReason} outTokens={r.Usage?.OutputTokenCount} textChars={r.Text.Length} {JsonProblem(r.Text)}");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{ex.GetType().Name}: {First(ex.Message)}");
                }
            }

            Console.WriteLine($"- **{name}**, json schema response format {(useSchema ? "on" : "off")}: {successes}/{structuredRuns} parsed ({sw.Elapsed.TotalSeconds:F0} s)");
            foreach (var f in failures.Distinct())
            {
                Console.WriteLine($"  - {f}");
            }
        }
    }
}

if (sections.Contains(8))
{
    Section("8. What actually goes on the wire (TopK, AdditionalProperties, ConversationId)");
    var options = new ChatOptions
    {
        ModelId = model,
        MaxOutputTokens = 50,
        TopK = 40,
        AdditionalProperties = new() { ["min_p"] = 0.05 },
    };
    foreach (var (name, client) in clients)
    {
        wire.Bodies.Clear();
        await Measure(name, async () =>
        {
            await client.GetResponseAsync([new(ChatRole.User, "Say hi.")], options);
            var body = wire.Bodies.LastOrDefault() ?? "";
            return $"sent top_k={body.Contains("\"top_k\"")} sent min_p={body.Contains("\"min_p\"")}";
        });
    }

    foreach (var (name, client) in clients)
    {
        await Measure($"{name}, ConversationId set", async () =>
        {
            var r = await client.GetResponseAsync([new(ChatRole.User, "Say hi.")], new ChatOptions { ModelId = model, MaxOutputTokens = 50, ConversationId = "conversation-1" });
            return $"no error (ConversationId not honored by chat completions); text=[{First(r.Text.Trim())}]";
        });
    }
}

if (sections.Contains(9))
{
    Section("9. A server that never answers (OpenAI SDK default timeout and retries)");
    using var silent = new TcpListener(IPAddress.Loopback, 0);
    silent.Start();
    var port = ((IPEndPoint)silent.LocalEndpoint).Port;
    var accepted = new ConcurrentQueue<TimeSpan>();
    var clock = Stopwatch.StartNew();
    _ = Task.Run(async () =>
    {
        var held = new List<Socket>();
        while (true)
        {
            var socket = await silent.AcceptSocketAsync();
            accepted.Enqueue(clock.Elapsed);
            held.Add(socket); // accept, then never answer
        }
    });

    using var cap = new CancellationTokenSource(TimeSpan.FromSeconds(230));
    await Measure("OpenAI SDK + adapter (defaults)", async () =>
    {
        try
        {
            await OpenAiClient(model, $"http://127.0.0.1:{port}").GetResponseAsync([new(ChatRole.User, "Hi")], cancellationToken: cap.Token);
            return "answered (unexpected)";
        }
        catch (OperationCanceledException) when (cap.IsCancellationRequested)
        {
            return $"still retrying when the 230 s cap hit; connection attempts at {string.Join(", ", accepted.Select(t => $"{t.TotalSeconds:F0} s"))}";
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name} after attempts at {string.Join(", ", accepted.Select(t => $"{t.TotalSeconds:F0} s"))}: {First(ex.Message)}";
        }
    });
    Console.WriteLine($"- **EmberLlm**: RequestTimeout default {EmberLlmOptions.DefaultRequestTimeout}, no retries; times out once with EmberLlmTimeoutException (covered by unit tests).");
}

static void Section(string title) => Console.WriteLine($"\n## {title}\n");

static async Task Measure(string name, Func<Task<string>> action)
{
    var sw = Stopwatch.StartNew();
    string result;
    try { result = await action(); }
    catch (Exception ex) { result = $"{ex.GetType().Name}: {First(ex.Message)}"; }
    Console.WriteLine($"- **{name}** ({sw.ElapsedMilliseconds} ms): {result}");
}

static string Describe(ChatResponse r) => $"no error; answered by model={r.ModelId}, text=[{First(r.Text.Trim())}]";

static int Reasoning(IEnumerable<AIContent> contents) => contents.OfType<TextReasoningContent>().Sum(c => c.Text.Length);

static string First(string text)
{
    var line = text.ReplaceLineEndings(" ");
    return line.Length <= 220 ? line : line[..220] + "...";
}

static string JsonProblem(string text)
{
    try
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var missing = new[] { "summary", "issues", "severity" }.Where(p => !root.TryGetProperty(p, out _)).ToList();
        var severity = root.TryGetProperty("severity", out var s) ? s.ValueKind.ToString() : "absent";
        return $"validJson=True missing=[{string.Join(",", missing)}] severityKind={severity} fenced={text.Contains("```")}";
    }
    catch (JsonException ex)
    {
        return $"validJson=False ({First(ex.Message)}) fenced={text.Contains("```")} think={text.Contains("<think>")} tail=[{First(text[Math.Max(0, text.Length - 60)..])}]";
    }
}

public sealed record CodeReview(string Summary, string[] Issues, int Severity);

/// <summary>Records request bodies, then passes requests on (or sends them itself when it's the last handler).</summary>
sealed class RecordingHandler : DelegatingHandler
{
    private readonly RecordingHandler? _shared;

    public RecordingHandler() : base(new HttpClientHandler()) { }

    public RecordingHandler(RecordingHandler shared) : base(new HttpClientHandler()) => _shared = shared;

    public ConcurrentQueue<string> Bodies => _shared?.Bodies ?? _bodies;

    private readonly ConcurrentQueue<string> _bodies = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            Bodies.Enqueue(await request.Content.ReadAsStringAsync(cancellationToken));
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
