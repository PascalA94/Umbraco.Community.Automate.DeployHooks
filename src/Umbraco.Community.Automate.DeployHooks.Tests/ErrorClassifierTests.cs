using System.Net;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.DeployHooks.Http;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

public class ErrorClassifierTests
{
    // Spec 4.6, verbatim.
    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently, StepRunErrorCategory.InvalidResponse, "The endpoint answered with a redirect. Check the URL.")]
    [InlineData(HttpStatusCode.TemporaryRedirect, StepRunErrorCategory.InvalidResponse, "The endpoint answered with a redirect. Check the URL.")]
    [InlineData(HttpStatusCode.BadRequest, StepRunErrorCategory.InvalidResponse, "The endpoint rejected the request (400).")]
    [InlineData(HttpStatusCode.Unauthorized, StepRunErrorCategory.Authentication, "The endpoint refused the secret.")]
    [InlineData(HttpStatusCode.Forbidden, StepRunErrorCategory.Authentication, "The endpoint refused the secret.")]
    [InlineData(HttpStatusCode.NotFound, StepRunErrorCategory.ConfigurationError, "The deploy hook was deleted. Create a new one and update the credential.")]
    [InlineData(HttpStatusCode.TooManyRequests, StepRunErrorCategory.RateLimiting, "The provider is throttling this hook. Use the step's Retry setting.")]
    [InlineData(HttpStatusCode.InternalServerError, StepRunErrorCategory.ServiceUnavailable, "The endpoint returned 500.")]
    [InlineData(HttpStatusCode.BadGateway, StepRunErrorCategory.ServiceUnavailable, "The endpoint returned 502.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, StepRunErrorCategory.ServiceUnavailable, "The endpoint returned 503.")]
    public void Classify_EveryRow(HttpStatusCode code, StepRunErrorCategory category, string message)
    {
        var result = ErrorClassifier.Classify(code, ErrorClassifier.DeployKind);

        Assert.Equal(category, result.Category);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public void Classify_RevalidateNotFound_SaysEndpointNotHook()
    {
        var result = ErrorClassifier.Classify(HttpStatusCode.NotFound, ErrorClassifier.RevalidateKind);

        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.Category);
        Assert.Equal("The revalidation endpoint was not found. Check the route and the credential.", result.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, 410)]
    [InlineData(HttpStatusCode.UnprocessableEntity, 422)]
    public void Classify_OtherClientErrors_InvalidResponseWithTheCode(HttpStatusCode code, int number)
    {
        var result = ErrorClassifier.Classify(code, ErrorClassifier.DeployKind);

        Assert.Equal(StepRunErrorCategory.InvalidResponse, result.Category);
        Assert.Equal($"The endpoint rejected the request ({number}).", result.Message);
    }

    [Fact]
    public void Classify_SuccessCode_IsACallerBug()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ErrorClassifier.Classify(HttpStatusCode.OK, ErrorClassifier.DeployKind));

    [Fact]
    public void NetworkAndTimeoutRows_UseSpecText()
    {
        Assert.Equal((StepRunErrorCategory.ServiceUnavailable, "Couldn't reach the endpoint."), ErrorClassifier.Unreachable);
        Assert.Equal((StepRunErrorCategory.Timeout, "No answer within 15 seconds."), ErrorClassifier.TimedOut(15));
    }

    [Fact]
    public void Exception_HasNullInnerException()
    {
        var exception = new DeployHooksException(StepRunErrorCategory.ServiceUnavailable, "Couldn't reach the endpoint.");

        Assert.Null(exception.InnerException);
        Assert.Equal(StepRunErrorCategory.ServiceUnavailable, exception.Category);
        Assert.Equal("Couldn't reach the endpoint.", exception.Message);
        Assert.DoesNotContain("HttpRequestException", exception.ToString());
    }
}
