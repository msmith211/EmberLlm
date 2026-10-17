using EmberLlm.Structured;

namespace EmberLlm.Tests.Structured;

public class StructuredOutputCleanerTests
{
    private const string Json = """{"name":"Ada","age":36}""";

    [Theory]
    [InlineData("<think>The user wants a person.</think>\n" + Json)]
    [InlineData("<THINK>\nreasoning {with braces}\n</THINK>" + Json)]
    [InlineData("reasoning whose opening tag was in the prompt</think>\n\n" + Json)]
    [InlineData("```json\n" + Json + "\n```")]
    [InlineData("```\n" + Json + "\n```")]
    [InlineData("```json\n" + Json)]
    [InlineData("Here is the JSON you asked for:\n" + Json + "\nLet me know if you need anything else.")]
    [InlineData("<think>Planning.</think>\nSure!\n```json\n" + Json + "\n```\nDone.")]
    [InlineData("  \n" + Json + "\n  ")]
    public void Clean_RecoversTheJsonValue(string reply)
    {
        Assert.Equal(Json, StructuredOutputCleaner.Clean(reply));
    }

    [Fact]
    public void Clean_KeepsAnArrayWhole()
    {
        Assert.Equal("""[{"a":1},{"a":2}]""", StructuredOutputCleaner.Clean("""Result: [{"a":1},{"a":2}] (two items)"""));
    }

    [Fact]
    public void Clean_UnterminatedReasoningLeavesNothing()
    {
        // The model ran out of tokens while thinking, so there's no JSON to find
        Assert.Equal(string.Empty, StructuredOutputCleaner.Clean("<think>Let me consider {the schema} carefully and"));
    }

    [Fact]
    public void Clean_TextWithoutJsonIsReturnedTrimmed()
    {
        Assert.Equal("I can't help with that.", StructuredOutputCleaner.Clean("  I can't help with that.  "));
    }

    [Theory]
    [InlineData(Json, true)]
    [InlineData("""{"note":"ends with </think> inside a string"}""", true)]
    [InlineData("[1, 2]", true)]
    [InlineData("```json\n{}\n```", false)]
    [InlineData("{\"name\": ", false)]
    [InlineData("", false)]
    public void IsJson_DetectsValidJson(string text, bool expected)
    {
        Assert.Equal(expected, StructuredOutputCleaner.IsJson(text));
    }
}
