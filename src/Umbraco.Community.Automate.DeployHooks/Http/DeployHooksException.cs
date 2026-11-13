using Umbraco.Automate.Core.Actions;

namespace Umbraco.Community.Automate.DeployHooks.Http;

/// <summary>
/// The only exception the senders throw. It carries an Automate error category and a fixed message, and never an
/// <see cref="Exception.InnerException"/>: an <c>HttpRequestException</c> message or <c>RequestUri</c> can hold the hook URL.
/// </summary>
internal sealed class DeployHooksException(StepRunErrorCategory category, string message) : Exception(message)
{
    public StepRunErrorCategory Category { get; } = category;
}
