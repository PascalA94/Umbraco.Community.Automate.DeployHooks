using Microsoft.Extensions.DependencyInjection;
using Umbraco.Automate.Core.Connections;
using Umbraco.Community.Automate.DeployHooks.Credentials;
using Umbraco.Community.Automate.DeployHooks.Http;

namespace Umbraco.Community.Automate.DeployHooks.Connection;

/// <summary>
/// The <c>community.deployHooks.deployHook</c> connection. Test connection checks the guard, the credential and the host
/// rule, and never calls the hook: a rebuild costs build minutes (design 3.1).
/// </summary>
[ConnectionType(
    ConnectionAlias,
    "Deploy hook (Vercel, Netlify, Cloudflare Pages)",
    Description = "Triggers a hosting provider's deploy hook. The hook URL is kept in configuration, never on the connection.",
    Group = "Deploy hooks",
    Icon = "icon-link")]
public sealed class DeployHookConnectionType : ConnectionTypeBase<DeployHookConnectionSettings>
{
    /// <summary>The connection alias. Frozen at first release.</summary>
    public const string ConnectionAlias = "community.deployHooks.deployHook";

    private readonly IServiceScopeFactory _scopes;

    /// <summary>
    /// Automate builds the type through DI with a public constructor, and the senders and store are internal, so
    /// services are resolved per validation from a scope; nothing is resolved or fetched at construction (pitfall P7).
    /// </summary>
    public DeployHookConnectionType(ConnectionTypeInfrastructure infrastructure, IServiceScopeFactory scopes)
        : base(infrastructure)
    {
        _scopes = scopes;
    }

    /// <inheritdoc />
    public override Task<ConnectionValidationResult> ValidateAsync(object? settings, CancellationToken cancellationToken)
    {
        if (settings is not DeployHookConnectionSettings typed)
        {
            return Task.FromResult(ConnectionValidationResult.Failure("Choose a provider and enter the credential name."));
        }

        if (!Enum.TryParse<DeployProvider>(typed.Provider, ignoreCase: true, out var provider) || !Enum.IsDefined(provider))
        {
            return Task.FromResult(ConnectionValidationResult.Failure("Choose Vercel, Netlify or Cloudflare Pages."));
        }

        using var scope = _scopes.CreateScope();
        var check = scope.ServiceProvider.GetRequiredService<IDeployHookSender>().Check(provider, typed.CredentialName);
        if (!check.Ok)
        {
            return Task.FromResult(ConnectionValidationResult.Failure(check.Message!));
        }

        var message = $"Credential found and the host is a known {provider.DisplayName()} host. The hook was not called.";
        var sourceCheck = scope.ServiceProvider.GetRequiredService<DeployHooksCredentialSourceCheck>();
        if (sourceCheck.FindCommittedFile(typed.CredentialName, DeployHooksCredentialPart.DeployHookUrl) is { } file)
        {
            message += $" Warning: the hook URL is read from {file}. Move it to environment variables, Umbraco Cloud secrets or Key Vault.";
        }

        return Task.FromResult(ConnectionValidationResult.Success(message));
    }
}
