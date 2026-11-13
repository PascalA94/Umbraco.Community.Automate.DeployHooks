using System.ComponentModel;

namespace Umbraco.Community.Automate.DeployHooks.Actions;

/// <summary>What <c>community.deployHooks.triggerDeploy</c> returns. Property names are permanent once released.</summary>
public sealed class DeployHookOutput
{
    /// <summary><c>Sent</c> (called now), <c>Queued</c> (first request of a window) or <c>Joined</c> (added to a window already open).</summary>
    [Description("Sent, Queued or Joined. Queued and Joined mean the hook is called once when the connection's window closes.")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Shared by every request that ends up in the same call.</summary>
    [Description("The same value for every request that is merged into one hook call.")]
    public Guid BatchId { get; set; }

    /// <summary>The provider's HTTP status. Only set when <see cref="Status"/> is <c>Sent</c>.</summary>
    [Description("The provider's HTTP status code. Only set when Status is Sent.")]
    public int? StatusCode { get; set; }

    /// <summary>The provider's job ID when it returns a safe one. Only set when <see cref="Status"/> is <c>Sent</c>.</summary>
    [Description("The provider's deployment job ID, when it returns one. Only set when Status is Sent.")]
    public string? ProviderJobId { get; set; }

    /// <summary>Notes that did not fail the step.</summary>
    [Description("Notes that did not fail the step.")]
    public string[] Warnings { get; set; } = [];
}

/// <summary>The three <see cref="DeployHookOutput.Status"/> values, shared with the revalidate output.</summary>
internal static class BatchStatus
{
    public const string Sent = "Sent";
    public const string Queued = "Queued";
    public const string Joined = "Joined";
}
