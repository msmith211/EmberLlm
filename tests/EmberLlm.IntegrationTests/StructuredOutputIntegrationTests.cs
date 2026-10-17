using Microsoft.Extensions.AI;

namespace EmberLlm.IntegrationTests;

/// <summary>
/// <c>GetAsync&lt;T&gt;</c> against a real server, in both modes. Skipped unless EMBERLLM_TEST_URL is set.
/// </summary>
public class StructuredOutputIntegrationTests
{
    public sealed record Country(string Name, string Capital, int Continents);

    public sealed record Inventory(string[] Fruits, int Total);

    [Theory]
    [InlineData(StructuredOutputMode.JsonSchema)]
    [InlineData(StructuredOutputMode.Prompt)]
    public async Task ReturnsATypedObject(StructuredOutputMode mode)
    {
        using var client = new EmberLlmClient(new EmberLlmOptions { BaseUrl = LocalServer.RequireUrl().ToString(), StructuredOutputMode = mode });

        var country = await client.GetAsync<Country>(
            [new(ChatRole.User, "Describe France. Continents is how many continents the country has territory on, counting overseas regions.")],
            new ChatOptions { Temperature = 0f, MaxOutputTokens = 2000 },
            TestContext.Current.CancellationToken);

        Assert.Equal("France", country.Name);
        Assert.Equal("Paris", country.Capital);
        Assert.True(country.Continents >= 1);
    }

    [Theory]
    [InlineData(StructuredOutputMode.JsonSchema)]
    [InlineData(StructuredOutputMode.Prompt)]
    public async Task ReturnsArraysAndNumbers(StructuredOutputMode mode)
    {
        using var client = new EmberLlmClient(new EmberLlmOptions { BaseUrl = LocalServer.RequireUrl().ToString(), StructuredOutputMode = mode });

        var inventory = await client.GetAsync<Inventory>(
            [new(ChatRole.User, "A basket holds 2 apples, 3 pears and 1 banana. List the fruit names (singular, lowercase) and the total count.")],
            new ChatOptions { Temperature = 0f, MaxOutputTokens = 2000 },
            TestContext.Current.CancellationToken);

        Assert.Equal(6, inventory.Total);
        Assert.Equal(["apple", "banana", "pear"], inventory.Fruits.Order());
    }
}
