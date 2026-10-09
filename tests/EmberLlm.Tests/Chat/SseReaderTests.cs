using EmberLlm.Chat;
using EmberLlm.Tests.Fakes;

namespace EmberLlm.Tests.Chat;

public class SseReaderTests
{
    [Fact]
    public async Task ReadsDataPayloadsUntilDone()
    {
        var events = await ReadAllAsync("data: one\n\ndata: two\n\ndata: [DONE]\n\ndata: after-done\n\n");

        Assert.Equal(["one", "two"], events);
    }

    [Fact]
    public async Task IgnoresKeepAliveCommentsAndOtherFields()
    {
        var events = await ReadAllAsync(": keep-alive\n\nevent: message\nid: 7\nretry: 1000\ndata: payload\n\n: ping\n\ndata: [DONE]\n\n");

        Assert.Equal(["payload"], events);
    }

    [Fact]
    public async Task JoinsMultipleDataLinesWithNewline()
    {
        var events = await ReadAllAsync("data: first\ndata: second\n\n");

        Assert.Equal(["first\nsecond"], events);
    }

    [Theory]
    [InlineData("data: value\n\n")]
    [InlineData("data:value\n\n")]
    [InlineData("data: value\r\n\r\n")]
    [InlineData("data: value\r\r")]
    public async Task AcceptsSpacingAndLineEndingVariants(string input)
    {
        var events = await ReadAllAsync(input);

        Assert.Equal(["value"], events);
    }

    [Fact]
    public async Task StripsOnlyOneLeadingSpace()
    {
        var events = await ReadAllAsync("data:  indented\n\n");

        Assert.Equal([" indented"], events);
    }

    [Fact]
    public async Task DispatchesFinalEventWithoutTrailingBlankLine()
    {
        var events = await ReadAllAsync("data: one\n\ndata: last");

        Assert.Equal(["one", "last"], events);
    }

    [Fact]
    public async Task EmptyStream_YieldsNothing()
    {
        Assert.Empty(await ReadAllAsync(""));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public async Task ChunkBoundaries_DoNotAffectResult(int chunkSize)
    {
        // Includes multi-byte UTF-8 characters, which tiny chunks split mid-character
        const string input = "data: {\"content\":\"héllo 🔥\"}\n\n: keep-alive\n\ndata: {\"content\":\"wörld\"}\n\ndata: [DONE]\n\n";

        var events = await ReadAllAsync(input, chunkSize);

        Assert.Equal(["{\"content\":\"héllo 🔥\"}", "{\"content\":\"wörld\"}"], events);
    }

    private static async Task<List<string>> ReadAllAsync(string input, int chunkSize = int.MaxValue)
    {
        using var stream = new ScriptedStream(input, chunkSize);
        var events = new List<string>();
        await foreach (var data in SseReader.ReadDataAsync(stream, TestContext.Current.CancellationToken))
        {
            events.Add(data);
        }

        return events;
    }
}
