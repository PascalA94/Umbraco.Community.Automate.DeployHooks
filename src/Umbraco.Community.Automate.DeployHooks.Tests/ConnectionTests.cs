using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Umbraco.Automate.Core.Connections;
using Umbraco.Automate.Core.Notifications;
using Umbraco.Automate.Core.Settings;
using Umbraco.Cms.Core.Events;
using Umbraco.Community.Automate.DeployHooks.Configuration;
using Umbraco.Community.Automate.DeployHooks.Connection;
using Umbraco.Community.Automate.DeployHooks.Credentials;
using Umbraco.Community.Automate.DeployHooks.Http;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

public sealed class ConnectionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("deployhooks-conn-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ConnectionTypeInfrastructure Infrastructure() => new(Substitute.For<IEditableModelResolver>());

    private (IServiceProvider Provider, SenderRig Rig) Compose(SenderRig rig, IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(rig.Store);
        services.AddSingleton(rig.Rules);
        services.AddSingleton(rig.Factory);
        services.AddSingleton(rig.Time);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(rig.Options));
        services.AddSingleton<IDeployHookSender>(rig.DeploySender());
        services.AddSingleton<IRevalidateSender>(rig.RevalidateSender());
        services.AddSingleton(configuration ?? rig.Configuration);
        services.AddSingleton<DeployHooksCredentialSourceCheck>();
        return (services.BuildServiceProvider(), rig);
    }

    private DeployHookConnectionType DeployType(SenderRig rig, IConfiguration? configuration = null) =>
        ActivatorUtilities.CreateInstance<DeployHookConnectionType>(Compose(rig, configuration).Provider, Infrastructure());

    private NextRevalidateConnectionType RevalidateType(SenderRig rig, IConfiguration? configuration = null) =>
        ActivatorUtilities.CreateInstance<NextRevalidateConnectionType>(Compose(rig, configuration).Provider, Infrastructure());

    [Fact]
    public async Task Deploy_Test_DoesNotCallHook()
    {
        var rig = new SenderRig(new RecordingHandler());

        var result = await DeployType(rig).ValidateAsync(new DeployHookConnectionSettings { Provider = "Vercel", CredentialName = "mysite" }, CancellationToken.None);

        Assert.Empty(rig.Handler.Requests);
        Assert.Equal(ConnectionValidationStatus.Success, result.Status);
        Assert.StartsWith("Credential found and the host is a known Vercel host. The hook was not called.", result.Message);
        Assert.DoesNotContain("SENTINEL", result.Message);
    }

    [Fact]
    public async Task Deploy_Test_BadHost_Failure()
    {
        var rig = new SenderRig(new RecordingHandler(), new() { [$"{DeployHooksCredentialStore.Prefix}:mysite:DeployHookUrl"] = "https://evil.example/hook/SENTINELHOOK" });

        var result = await DeployType(rig).ValidateAsync(new DeployHookConnectionSettings { Provider = "Vercel", CredentialName = "mysite" }, CancellationToken.None);

        Assert.Equal(ConnectionValidationStatus.Failure, result.Status);
        Assert.Equal("The credential isn't a Vercel deploy hook URL.", result.Message);
        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public async Task Deploy_Test_UnknownProvider_Failure()
    {
        var rig = new SenderRig(new RecordingHandler());

        var result = await DeployType(rig).ValidateAsync(new DeployHookConnectionSettings { Provider = "Heroku", CredentialName = "mysite" }, CancellationToken.None);

        Assert.Equal(ConnectionValidationStatus.Failure, result.Status);
        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public async Task Revalidate_Test_SendsTestBody()
    {
        var rig = new SenderRig(new RecordingHandler());

        var result = await RevalidateType(rig).ValidateAsync(new NextRevalidateConnectionSettings { CredentialName = "mysite" }, CancellationToken.None);

        Assert.Equal(ConnectionValidationStatus.Success, result.Status);
        Assert.Equal("{\"paths\":[],\"tags\":[],\"test\":true}", Assert.Single(rig.Handler.Requests).Body);
        Assert.DoesNotContain("SENTINELSECRET", result.Message);
    }

    [Fact]
    public async Task Revalidate_Test_UnlistedHost_WarnsWithHost()
    {
        var rig = new SenderRig(new RecordingHandler());

        var result = await RevalidateType(rig).ValidateAsync(new NextRevalidateConnectionSettings { CredentialName = "mysite" }, CancellationToken.None);

        Assert.Contains("www.sentinel-site.example", result.Message);
        Assert.Contains("AllowedRevalidateHosts", result.Message);
        Assert.DoesNotContain("SENTINELROUTE", result.Message);
    }

    [Fact]
    public async Task Revalidate_Test_EndpointRefuses_FailsWithoutSecret()
    {
        var rig = new SenderRig(new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized))));

        var result = await RevalidateType(rig).ValidateAsync(new NextRevalidateConnectionSettings { CredentialName = "mysite" }, CancellationToken.None);

        Assert.Equal(ConnectionValidationStatus.Failure, result.Status);
        Assert.Equal("The endpoint refused the secret.", result.Message);
    }

    [Theory]
    [InlineData("https://api.vercel.com/v1/integrations/deploy/prj_X/SENTINELHOOK")]
    [InlineData("$Umbraco:Automate:Secrets:SENTINEL")]
    [InlineData("mysite-site")]
    [InlineData("")]
    [InlineData("MYSITE")]
    public void Saving_PastedUrlOrReference_RefusedWithoutEcho(string pasted)
    {
        var handler = new CredentialNameSavingHandler(new SenderRig(new RecordingHandler()).Store, Microsoft.Extensions.Logging.Abstractions.NullLogger<CredentialNameSavingHandler>.Instance);
        var notification = Saving(DeployHookConnectionType.ConnectionAlias, pasted);

        handler.Handle(notification);

        Assert.True(notification.Cancel);
        Assert.All(notification.Messages.GetAll(), m =>
        {
            Assert.DoesNotContain("SENTINEL", m.Message);
            Assert.DoesNotContain("vercel", m.Message, StringComparison.OrdinalIgnoreCase);
            if (pasted.Length > 0)
            {
                Assert.DoesNotContain(pasted, m.Message);
            }
        });
    }

    [Theory]
    [InlineData(DeployHookConnectionType.ConnectionAlias, "mysite")]
    [InlineData(NextRevalidateConnectionType.ConnectionAlias, "site2")]
    public void Saving_ValidName_Allowed(string alias, string name)
    {
        var notification = Saving(alias, name);

        new CredentialNameSavingHandler(new SenderRig(new RecordingHandler()).Store, Microsoft.Extensions.Logging.Abstractions.NullLogger<CredentialNameSavingHandler>.Instance).Handle(notification);

        Assert.False(notification.Cancel);
    }

    [Fact]
    public void Saving_OtherConnectionType_Ignored()
    {
        var notification = Saving("community.other.thing", "https://anything.example");

        new CredentialNameSavingHandler(new SenderRig(new RecordingHandler()).Store, Microsoft.Extensions.Logging.Abstractions.NullLogger<CredentialNameSavingHandler>.Instance).Handle(notification);

        Assert.False(notification.Cancel);
    }

    [Fact]
    public void Saving_AutomateStillExposesTheConnection_ForTheHandlerToRead() =>
        Assert.NotNull(CredentialNameSavingHandler.TargetProperty);

    [Fact]
    public void SourceCheck_AppsettingsJson_Warns()
    {
        var path = Path.Combine(_dir, "appsettings.json");
        File.WriteAllText(path, AppsettingsWithHook());
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        var rig = new SenderRig(new RecordingHandler(), config: [], configuration: configuration);

        var found = new DeployHooksCredentialSourceCheck(configuration, rig.Store).FindCommittedFile("mysite", DeployHooksCredentialPart.DeployHookUrl);

        Assert.Equal("appsettings.json", found);
    }

    [Fact]
    public void SourceCheck_EnvVar_Silent()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{DeployHooksCredentialStore.Prefix}:mysite:DeployHookUrl"] = SenderRig.VercelHook,
        }).Build();
        var rig = new SenderRig(new RecordingHandler(), configuration: configuration);

        Assert.Null(new DeployHooksCredentialSourceCheck(configuration, rig.Store).FindCommittedFile("mysite", DeployHooksCredentialPart.DeployHookUrl));
    }

    [Fact]
    public void SourceCheck_GuardTripped_ReturnsNull()
    {
        var path = Path.Combine(_dir, "appsettings.json");
        File.WriteAllText(path, AppsettingsWithHook());
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        var rig = new SenderRig(new RecordingHandler(), configuration: configuration);
        rig.AllowListExposesCredentials();

        Assert.Null(new DeployHooksCredentialSourceCheck(configuration, rig.Store).FindCommittedFile("mysite", DeployHooksCredentialPart.DeployHookUrl));
    }

    [Fact]
    public async Task Deploy_Test_AppsettingsSource_AddsWarning()
    {
        var path = Path.Combine(_dir, "appsettings.json");
        File.WriteAllText(path, AppsettingsWithHook());
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        var rig = new SenderRig(new RecordingHandler(), configuration: configuration);

        var result = await DeployType(rig, configuration).ValidateAsync(new DeployHookConnectionSettings { Provider = "Vercel", CredentialName = "mysite" }, CancellationToken.None);

        Assert.Contains("appsettings.json", result.Message);
        Assert.DoesNotContain("SENTINEL", result.Message);
    }

    [Theory]
    [InlineData(typeof(DeployHookConnectionSettings))]
    [InlineData(typeof(NextRevalidateConnectionSettings))]
    public void NoSettingsType_HasSensitiveOrUrlField(Type settings)
    {
        foreach (var property in settings.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var field = property.GetCustomAttribute<EditableModelFieldAttribute>();
            Assert.False(field?.IsSensitive ?? false, $"{property.Name} is marked sensitive: a secret must never be a connection setting.");
            Assert.DoesNotMatch("(?i)url|secret|token|key|password|host", property.Name);
        }
    }

    [Theory]
    [InlineData(typeof(DeployHookConnectionSettings), nameof(DeployHookConnectionSettings.CredentialName))]
    [InlineData(typeof(NextRevalidateConnectionSettings), nameof(NextRevalidateConnectionSettings.CredentialName))]
    public void CredentialName_DoesNotSupportBindings(Type settings, string property) =>
        Assert.False(settings.GetProperty(property)!.GetCustomAttribute<EditableModelFieldAttribute>()!.SupportsBindings);

    private static string AppsettingsWithHook() =>
        "{\"Umbraco\":{\"Automate\":{\"DeployHooks\":{\"Credentials\":{\"mysite\":{\"DeployHookUrl\":\"" + SenderRig.VercelHook + "\"}}}}}}";

    private static ConnectionSavingNotification Saving(string typeAlias, string credentialName) =>
        new(new Umbraco.Automate.Core.Connections.Connection { Alias = "test", Name = "Test", Type = typeAlias, Settings = new Dictionary<string, object?> { ["credentialName"] = credentialName } }, new EventMessages());
}
