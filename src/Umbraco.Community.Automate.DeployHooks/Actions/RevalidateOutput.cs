using System.ComponentModel;

namespace Umbraco.Community.Automate.DeployHooks.Actions;

/// <summary>What <c>community.deployHooks.revalidate</c> returns. Property names are permanent once released.</summary>
public sealed class RevalidateOutput
{
    /// <summary><c>Sent</c> (called now, or nothing to send), <c>Queued</c> or <c>Joined</c>.</summary>
    [Description("Sent, Queued or Joined. Queued and Joined mean the paths and tags are sent in one call when the connection's window closes.")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Shared by every request that ends up in the same call.</summary>
    [Description("The same value for every request that is merged into one call.")]
    public Guid BatchId { get; set; }

    /// <summary>The site's HTTP status. Only set when a call was made now.</summary>
    [Description("The site's HTTP status code. Only set when Status is Sent and a call was made.")]
    public int? StatusCode { get; set; }

    /// <summary>How many clean paths this step contributed.</summary>
    [Description("How many valid paths this step contributed, after cleaning.")]
    public int PathCount { get; set; }

    /// <summary>How many clean tags this step contributed.</summary>
    [Description("How many valid tags this step contributed, after cleaning.")]
    public int TagCount { get; set; }

    /// <summary>Notes that did not fail the step, such as dropped values (never echoed).</summary>
    [Description("Notes that did not fail the step, such as how many values were dropped.")]
    public string[] Warnings { get; set; } = [];
}
