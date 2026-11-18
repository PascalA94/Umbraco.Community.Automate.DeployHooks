// credential-helper-contract: 1.0.3
global using Umbraco.Community.Automate.DeployHooks.Credentials;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Automate.Core.Configuration;
using Umbraco.Cms.Core.Notifications;
using Xunit.Abstractions;

namespace CredentialHelperContract;

// The only test file a consumer package writes (contract section 5.3). The global using supplies the
// namespace of the package's store to the verbatim suites.
public sealed class DeployHooksCredentialStoreContractTests : CredentialStoreContractTests<DeployHooksCredentialPart>
{
    protected override string Prefix => DeployHooksCredentialStore.Prefix;

    protected override string Namespace => "Umbraco.Community.Automate.DeployHooks.Credentials";

    protected override string StoreClassName => "DeployHooksCredentialStore";

    protected override string PartEnumName => "DeployHooksCredentialPart";

    // Contract section 4 item 11 (C31): every package type outside the store file that is handed configuration,
    // fetches it, or names a parent of the credential section, with the reason it cannot read that section.
    protected override IReadOnlyDictionary<string, string> PinnedConfigurationReaders { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Umbraco.Community.Automate.DeployHooks.Configuration.DeployHooksComposer"] =
            "Compose binds DeployHooksOptions from Umbraco:Automate:DeployHooks (a parent of the credential section); DeployHooksOptions has no catch-all member, so the Credentials child binds to nothing.",
        ["Umbraco.Community.Automate.DeployHooks.Credentials.DeployHooksCredentialSourceCheck"] =
            "Test connection's committed-file warning. It applies the store's allow-list guard and name rule before it builds the key, asks providers only which one holds the store-built key, and returns a file name, never a value.",
    };

    // DeployHooksOptions is bound from Umbraco:Automate:DeployHooks, a parent of the credential section, so it must have no catch-all member.
    protected override IReadOnlyList<Type> ParentSectionBoundTypes { get; } = [typeof(Umbraco.Community.Automate.DeployHooks.Configuration.DeployHooksOptions)];

    protected override IReadOnlyDictionary<string, string> PinnedKeyForCallers { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Umbraco.Community.Automate.DeployHooks.Credentials.DeployHooksCredentialSourceCheck"] =
            "Builds the key to find which provider holds it, only after the guard and name rule passed through the store; the value is discarded and only a file name is returned.",
    };

    internal override ICredentialStore<DeployHooksCredentialPart> CreateStore(IConfiguration configuration, IOptionsMonitor<AutomateOptions> options, IOptions<AutomateOptions> startupOptions) =>
        new DeployHooksCredentialStore(configuration, options, startupOptions);

    internal override object CreateStartupCheck(ICredentialStore<DeployHooksCredentialPart> store, ILoggerFactory loggerFactory) =>
        new DeployHooksCredentialStoreStartupCheck((DeployHooksCredentialStore)store, loggerFactory.CreateLogger<DeployHooksCredentialStoreStartupCheck>());

    internal override Task RunStartupCheck(object check)
    {
        ((DeployHooksCredentialStoreStartupCheck)check).Handle(new UmbracoApplicationStartedNotification(false));
        return Task.CompletedTask;
    }
}

public sealed class DeployHooksCredentialStoreIsolationTests(ITestOutputHelper output) : CredentialIsolationContractTests<DeployHooksCredentialPart>(output)
{
    protected override string Prefix => DeployHooksCredentialStore.Prefix;

    protected override DeployHooksCredentialPart SamplePart => DeployHooksCredentialPart.DeployHookUrl;

    internal override ICredentialStore<DeployHooksCredentialPart> CreateStore(IConfiguration configuration, IOptionsMonitor<AutomateOptions> options, IOptions<AutomateOptions> startupOptions) =>
        new DeployHooksCredentialStore(configuration, options, startupOptions);
}
