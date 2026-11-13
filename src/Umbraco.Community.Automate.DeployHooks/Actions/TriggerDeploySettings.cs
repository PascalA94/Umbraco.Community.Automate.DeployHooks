using Umbraco.Automate.Core.Settings;

namespace Umbraco.Community.Automate.DeployHooks.Actions;

/// <summary>Settings for <c>community.deployHooks.triggerDeploy</c>. Property names are permanent once released.</summary>
public sealed class TriggerDeploySettings
{
    /// <summary>A note for the run log. Only its length is logged, and it is never sent to the provider.</summary>
    [Field(Label = "Reason", Description = "Optional note for your own records, up to 200 characters. It is not sent to the provider and only its length is logged.", SupportsBindings = true, SortOrder = 1)]
    public string? Reason { get; set; }
}
