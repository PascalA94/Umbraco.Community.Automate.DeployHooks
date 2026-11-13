using Umbraco.Automate.Core.Actions;

namespace Umbraco.Community.Automate.DeployHooks.Http;

/// <summary>
/// The outcome of one send. A failure is a value, not an exception: the action turns it into a failed step through
/// <see cref="ToException"/>, so the exception that reaches Automate always has a null <see cref="Exception.InnerException"/>.
/// </summary>
internal sealed record SendResult(bool Ok, int? StatusCode, string? ProviderJobId, StepRunErrorCategory? Category, string? Message)
{
    public static SendResult Success(int? statusCode, string? providerJobId = null) => new(true, statusCode, providerJobId, null, null);

    public static SendResult Failure(StepRunErrorCategory category, string message, int? statusCode = null) => new(false, statusCode, null, category, message);

    public DeployHooksException ToException() =>
        Ok
            ? throw new InvalidOperationException("A successful send has no exception.")
            : new DeployHooksException(Category ?? StepRunErrorCategory.Unknown, Message ?? "The request failed.");
}
