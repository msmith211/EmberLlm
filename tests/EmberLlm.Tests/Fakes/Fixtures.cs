namespace EmberLlm.Tests.Fakes;

/// <summary>
/// Responses recorded from real servers, under tests/fixtures/&lt;backend&gt;/ (copied next to the test binaries).
/// </summary>
internal static class Fixtures
{
    public static string PathOf(string relativePath) => Path.Combine(AppContext.BaseDirectory, "fixtures", relativePath);

    public static bool Exists(string relativePath) => File.Exists(PathOf(relativePath));

    public static string Read(string relativePath) => File.ReadAllText(PathOf(relativePath));

    public static byte[] ReadBytes(string relativePath) => File.ReadAllBytes(PathOf(relativePath));
}
