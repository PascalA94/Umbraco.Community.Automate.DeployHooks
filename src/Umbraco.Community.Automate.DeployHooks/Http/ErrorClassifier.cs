using System.Net;
using Umbraco.Automate.Core.Actions;

namespace Umbraco.Community.Automate.DeployHooks.Http;

/// <summary>Maps an HTTP failure to an Automate error category and a text that never holds a URL or a secret (design 4.6).</summary>
internal static class ErrorClassifier
{
    public const string DeployKind = "deploy";
    public const string RevalidateKind = "revalidate";

    public static readonly (StepRunErrorCategory Category, string Message) Unreachable =
        (StepRunErrorCategory.ServiceUnavailable, "Couldn't reach the endpoint.");

    public static (StepRunErrorCategory Category, string Message) TimedOut(int seconds) =>
        (StepRunErrorCategory.Timeout, $"No answer within {seconds} seconds.");

    /// <summary>Classifies a non-success status. A 1xx or 2xx code is a caller bug.</summary>
    public static (StepRunErrorCategory Category, string Message) Classify(HttpStatusCode code, string kind)
    {
        var number = (int)code;
        return number switch
        {
            < 300 => throw new ArgumentOutOfRangeException(nameof(code), "Only failure statuses are classified."),
            < 400 => (StepRunErrorCategory.InvalidResponse, "The endpoint answered with a redirect. Check the URL."),
            401 or 403 => (StepRunErrorCategory.Authentication, "The endpoint refused the secret."),
            404 when kind == RevalidateKind => (StepRunErrorCategory.ConfigurationError, "The revalidation endpoint was not found. Check the route and the credential."),
            404 => (StepRunErrorCategory.ConfigurationError, "The deploy hook was deleted. Create a new one and update the credential."),
            429 => (StepRunErrorCategory.RateLimiting, "The provider is throttling this hook. Use the step's Retry setting."),
            >= 500 => (StepRunErrorCategory.ServiceUnavailable, $"The endpoint returned {number}."),
            _ => (StepRunErrorCategory.InvalidResponse, $"The endpoint rejected the request ({number})."),
        };
    }
}
