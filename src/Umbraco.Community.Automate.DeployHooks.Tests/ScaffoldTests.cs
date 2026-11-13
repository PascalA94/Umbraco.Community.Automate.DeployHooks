using System.Reflection;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

public class ScaffoldTests
{
    [Fact]
    public void Assembly_IsBuiltForTheSelectedAutomateMajor()
    {
        var selected = Environment.GetEnvironmentVariable("AutomateMajor") ?? "18";
        var assembly = typeof(DeployHooksAssemblyMarker).Assembly;
        var built = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().SingleOrDefault(a => a.Key == "AutomateMajor")?.Value;
        Assert.Equal("18", built);
        Assert.Equal(selected, built);
    }
}
