using System.Diagnostics.CodeAnalysis;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.DeployHooks.Http;

namespace Umbraco.Community.Automate.DeployHooks.Actions;

/// <summary>The failure results both actions return, so categories and texts match design 4.6 everywhere.</summary>
internal static class ActionFailures
{
    /// <summary>A send failed: keep its category and its already-scrubbed message. The exception has no inner exception.</summary>
    public static ActionResult FromSend(SendResult result) =>
        ActionResult.Failed(result.ToException(), result.Category ?? StepRunErrorCategory.Unknown);

    public static ActionResult Configuration(string message) =>
        ActionResult.Failed(new DeployHooksException(StepRunErrorCategory.ConfigurationError, message), StepRunErrorCategory.ConfigurationError);

    /// <summary>Reads the step's connection settings, or fails with a fixed message that never echoes a value.</summary>
    public static bool TryGetConnection<T>(ActionContext context, string typeName, [NotNullWhen(true)] out Guid? connectionId, [NotNullWhen(true)] out T? settings, [NotNullWhen(false)] out ActionResult? failure)
        where T : class, new()
    {
        connectionId = null;
        settings = null;
        failure = null;
        if (context.Connection is not { } connection)
        {
            failure = Configuration($"This step needs a {typeName} connection.");
            return false;
        }

        settings = connection.GetSettings<T>();
        if (settings is null)
        {
            failure = Configuration("The connection settings could not be read.");
            return false;
        }

        connectionId = connection.Id;
        return true;
    }
}
