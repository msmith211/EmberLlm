using System.Reflection;

namespace EmberLlm.Tests;

public class AssemblyTests
{
    [Fact]
    public void LibraryAssemblyLoads()
    {
        var assembly = Assembly.Load("EmberLlm");

        Assert.Equal("EmberLlm", assembly.GetName().Name);
    }
}
