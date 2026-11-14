using System.Reflection;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

public class ScaffoldTests
{
    [Fact]
    public void Assembly_IsBuiltForTheSelectedAutomateMajor()
    {
        // The constant comes from -p:AutomateMajor, so it names the line this test project was built for.
#if AUTOMATE_17
        const string selected = "17";
#else
        const string selected = "18";
#endif
        var assembly = typeof(DeployHooksAssemblyMarker).Assembly;
        var built = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().SingleOrDefault(a => a.Key == "AutomateMajor")?.Value;
        Assert.Equal(selected, built);
    }
}
