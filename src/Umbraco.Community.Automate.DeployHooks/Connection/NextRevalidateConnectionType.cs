using Microsoft.Extensions.DependencyInjection;
using Umbraco.Automate.Core.Connections;
using Umbraco.Community.Automate.DeployHooks.Credentials;
using Umbraco.Community.Automate.DeployHooks.Http;

namespace Umbraco.Community.Automate.DeployHooks.Connection;

/// <summary>
/// The <c>community.deployHooks.nextRevalidate</c> connection. Test connection sends the contract's test body
/// (<c>{"paths":[],"tags":[],"test":true}</c>), which revalidates nothing (design 3.2, 4.5).
/// </summary>
[ConnectionType(
    ConnectionAlias,
    "Next.js revalidation endpoint",
    Description = "Calls a Next.js revalidation route. The endpoint URL and secret are kept in configuration, never on the connection.",
    Group = "Deploy hooks",
    Icon = "icon-link")]
public sealed class NextRevalidateConnectionType : ConnectionTypeBase<NextRevalidateConnectionSettings>
{
    /// <summary>The connection alias. Frozen at first release.</summary>
    public const string ConnectionAlias = "community.deployHooks.nextRevalidate";

    private readonly IServiceScopeFactory _scopes;

    /// <summary>See <see cref="DeployHookConnectionType"/>: services are resolved per validation, never at construction.</summary>
    public NextRevalidateConnectionType(ConnectionTypeInfrastructure infrastructure, IServiceScopeFactory scopes)
        : base(infrastructure)
    {
        _scopes = scopes;
    }

    /// <inheritdoc />
    public override async Task<ConnectionValidationResult> ValidateAsync(object? settings, CancellationToken cancellationToken)
    {
        if (settings is not NextRevalidateConnectionSettings typed)
        {
            return ConnectionValidationResult.Failure("Enter the credential name.");
        }

        using var scope = _scopes.CreateScope();
        var tested = await scope.ServiceProvider.GetRequiredService<IRevalidateSender>().TestAsync(typed.CredentialName, cancellationToken);
        if (!tested.Result.Ok)
        {
            return ConnectionValidationResult.Failure(tested.Result.Message!);
        }

        var message = "Credential found and the endpoint accepted the test call. Nothing was revalidated.";
        if (tested.Warning is { } warning)
        {
            message += $" Warning: {warning}";
        }

        var sourceCheck = scope.ServiceProvider.GetRequiredService<DeployHooksCredentialSourceCheck>();
        foreach (var part in new[] { DeployHooksCredentialPart.RevalidateUrl, DeployHooksCredentialPart.RevalidateSecret })
        {
            if (sourceCheck.FindCommittedFile(typed.CredentialName, part) is { } file)
            {
                message += $" Warning: the revalidation credential is read from {file}. Move it to environment variables, Umbraco Cloud secrets or Key Vault.";
                break;
            }
        }

        return ConnectionValidationResult.Success(message);
    }
}
