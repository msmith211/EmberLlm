using Microsoft.Extensions.AI;

namespace EmberLlm.IntegrationTests;

/// <summary>
/// End-to-end checks through EmberLlm against a real server. Skipped unless EMBERLLM_TEST_URL is set.
/// </summary>
public class ClientIntegrationTests
{
    // Thinking models can spend hundreds of tokens reasoning before they answer
    private const int OutputTokenBudget = 2000;

    [Fact]
    public async Task ListsAtLeastOneModel()
    {
        using var client = new EmberLlmClient(LocalServer.RequireUrl().ToString());

        var models = await client.ListModelsAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(models);
        Assert.All(models, m => Assert.False(string.IsNullOrWhiteSpace(m.Id)));
    }

    [Fact]
    public async Task EveryListedModel_AnswersAndReportsItself()
    {
        using var client = new EmberLlmClient(new EmberLlmOptions { BaseUrl = LocalServer.RequireUrl().ToString(), StrictModelMatching = true });
        var ct = TestContext.Current.CancellationToken;

        foreach (var model in await client.ListModelsAsync(ct))
        {
            var response = await client.GetResponseAsync(
                [new(ChatRole.User, "What is 2 + 3? Reply with just the number.")],
                new ChatOptions { ModelId = model.Id, Temperature = 0f, MaxOutputTokens = OutputTokenBudget },
                ct);

            Assert.Contains("5", response.Text, StringComparison.Ordinal);
            Assert.NotNull(response.Usage?.OutputTokenCount);
        }
    }

    [Fact]
    public async Task Streaming_EndsWithUsage()
    {
        using var client = new EmberLlmClient(LocalServer.RequireUrl().ToString());
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in client.GetStreamingResponseAsync(
            [new(ChatRole.User, "Say hello.")],
            new ChatOptions { MaxOutputTokens = OutputTokenBudget },
            TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        var response = updates.ToChatResponse();
        Assert.NotNull(response.FinishReason);
        Assert.NotNull(response.Usage?.OutputTokenCount);
    }
}
