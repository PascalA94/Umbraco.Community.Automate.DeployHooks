namespace Umbraco.Community.Automate.DeployHooks;

/// <summary>Names that are permanent once the package is released: the NuGet ID and the Automate alias prefix.</summary>
public static class PackageConstants
{
    public const string PackageName = "Umbraco.Community.Automate.DeployHooks";

    /// <summary>Prefix for every Automate alias this package registers. Frozen at first release.</summary>
    public const string AliasPrefix = "community.deployHooks.";
}
