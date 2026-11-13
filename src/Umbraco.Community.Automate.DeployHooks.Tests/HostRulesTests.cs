using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Umbraco.Community.Automate.DeployHooks.Configuration;
using Umbraco.Community.Automate.DeployHooks.Http;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

public class HostRulesTests
{
    private const string VercelUrl = "https://api.vercel.com/v1/integrations/deploy/prj_SECRETID/SECRETHOOK";

    private static HostRules Rules(DeployHooksOptions? options = null) => new(Options.Create(options ?? new DeployHooksOptions()));

    [Theory]
    [InlineData(DeployProvider.Vercel, VercelUrl)]
    [InlineData(DeployProvider.Netlify, "https://api.netlify.com/build_hooks/SECRETHOOK")]
    [InlineData(DeployProvider.CloudflarePages, "https://api.cloudflare.com/client/v4/pages/webhooks/deploy_hooks/SECRETHOOK")]
    [InlineData(DeployProvider.Vercel, "HTTPS://API.VERCEL.COM/v1/integrations/deploy/prj_SECRETID/SECRETHOOK")]
    [InlineData(DeployProvider.Vercel, "https://api.vercel.com./v1/integrations/deploy/prj_SECRETID/SECRETHOOK")]
    public void HostRules_EachProvider_Accepted(DeployProvider provider, string url)
    {
        Assert.True(Rules().CheckDeployHook(provider, url, out var uri, out var error), error);
        Assert.NotNull(uri);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("https://api.vercel.com.evil.example/v1/integrations/deploy/prj_SECRETID/SECRETHOOK")]
    [InlineData("https://api-vercel.com/v1/integrations/deploy/prj_SECRETID/SECRETHOOK")]
    [InlineData("https://evil.api.vercel.com/v1/integrations/deploy/prj_SECRETID/SECRETHOOK")]
    [InlineData("https://xn--api-vercl-9ib.com/v1/integrations/deploy/prj_SECRETID/SECRETHOOK")]
    [InlineData("https://api.vercеl.com/v1/integrations/deploy/prj_SECRETID/SECRETHOOK")] // Cyrillic e
    [InlineData("https://api.vercel.com@evil.example/v1/integrations/deploy/prj_SECRETID/SECRETHOOK")]
    public void HostRules_LookAlikes_Blocked(string url)
    {
        Assert.False(Rules().CheckDeployHook(DeployProvider.Vercel, url, out var uri, out var error));
        Assert.Null(uri);
        Assert.Equal("The credential isn't a Vercel deploy hook URL.", error);
    }

    [Theory]
    [InlineData("http://api.vercel.com/v1/integrations/deploy/prj_SECRETID/SECRETHOOK")]
    [InlineData("https://user:SECRETPASS@api.vercel.com/v1/integrations/deploy/prj_SECRETID/SECRETHOOK")]
    [InlineData("https://user@api.vercel.com/v1/integrations/deploy/prj_SECRETID/SECRETHOOK")]
    [InlineData("https://api.vercel.com:8443/v1/integrations/deploy/prj_SECRETID/SECRETHOOK")]
    [InlineData("ftp://api.vercel.com/v1/integrations/deploy/prj_SECRETID/SECRETHOOK")]
    public void HostRules_HttpOrUserInfoOrPort_Blocked(string url)
    {
        Assert.False(Rules().CheckDeployHook(DeployProvider.Vercel, url, out _, out var error));
        Assert.Equal("The credential isn't a Vercel deploy hook URL.", error);
    }

    [Theory]
    [InlineData("https://api.vercel.com/v1/other/prj_SECRETID/SECRETHOOK")]
    [InlineData("https://api.vercel.com/v1/integrations/deploy/")]
    [InlineData("https://api.vercel.com/v1/integrations/deploy")]
    [InlineData("https://api.vercel.com/V1/INTEGRATIONS/DEPLOY/prj_SECRETID/SECRETHOOK")]
    [InlineData("https://api.vercel.com/v1/integrations/deploy/../../x")]
    [InlineData("https://api.vercel.com/")]
    public void HostRules_WrongPath_Blocked(string url)
    {
        Assert.False(Rules().CheckDeployHook(DeployProvider.Vercel, url, out _, out var error));
        Assert.Equal("The credential isn't a Vercel deploy hook URL.", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mysite-site")]
    [InlineData("/v1/integrations/deploy/x")]
    public void HostRules_NotAnAbsoluteUrl_Blocked(string? url)
    {
        Assert.False(Rules().CheckDeployHook(DeployProvider.Netlify, url, out var uri, out var error));
        Assert.Null(uri);
        Assert.Equal("The credential isn't a Netlify deploy hook URL.", error);
    }

    [Fact]
    public void HostRules_WrongProviderHost_Blocked_AndErrorNamesProviderNotUrl()
    {
        Assert.False(Rules().CheckDeployHook(DeployProvider.CloudflarePages, VercelUrl, out _, out var error));
        Assert.Equal("The credential isn't a Cloudflare Pages deploy hook URL.", error);
        Assert.DoesNotContain("SECRET", error);
    }

    [Fact]
    public void Options_ConfiguredProviderHosts_ReplaceDefaults()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Umbraco:Automate:DeployHooks:ProviderHosts:Vercel:Host"] = "hooks.vercel.internal",
            ["Umbraco:Automate:DeployHooks:ProviderHosts:Vercel:PathPrefix"] = "/custom/",
        }).Build();
        var options = new DeployHooksOptions();
        config.GetSection("Umbraco:Automate:DeployHooks").Bind(options);
        var rules = Rules(options);

        Assert.True(rules.CheckDeployHook(DeployProvider.Vercel, "https://hooks.vercel.internal/custom/abc", out _, out _));
        Assert.False(rules.CheckDeployHook(DeployProvider.Vercel, VercelUrl, out _, out _));
        // The other providers keep their defaults.
        Assert.True(rules.CheckDeployHook(DeployProvider.Netlify, "https://api.netlify.com/build_hooks/abc", out _, out _));
    }

    [Fact]
    public void Revalidate_AnyHttpsHost_AcceptedByDefault_WithTestWarningNamingTheHost()
    {
        Assert.True(Rules().CheckRevalidate("https://Site.Example/api/revalidate", out var uri, out var error, out var warning));
        Assert.Equal("site.example", uri!.IdnHost);
        Assert.Null(error);
        Assert.NotNull(warning);
        Assert.Contains("site.example", warning);
    }

    [Fact]
    public void Revalidate_AllowedHosts_Enforced()
    {
        var rules = Rules(new DeployHooksOptions { AllowedRevalidateHosts = ["site.example", "WWW.Site.Example."] });

        Assert.True(rules.CheckRevalidate("https://site.example/api/revalidate", out _, out _, out var warning));
        Assert.Null(warning);
        Assert.True(rules.CheckRevalidate("https://www.site.example/api/revalidate", out _, out _, out _));

        Assert.False(rules.CheckRevalidate("https://evil.example/api/revalidate", out var uri, out var error, out _));
        Assert.Null(uri);
        Assert.Equal("The revalidation host isn't in AllowedRevalidateHosts.", error);
        Assert.DoesNotContain("evil", error);
        Assert.False(rules.CheckRevalidate("https://site.example.evil.example/x", out _, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://site.example/api/revalidate")]
    [InlineData("https://user:SECRETPASS@site.example/api/revalidate")]
    [InlineData("site.example/api/revalidate")]
    public void Revalidate_NotHttpsOrHasUserInfo_Blocked(string? url)
    {
        Assert.False(Rules().CheckRevalidate(url, out var uri, out var error, out _));
        Assert.Null(uri);
        Assert.Equal("The revalidation URL must be an absolute https URL without credentials.", error);
        Assert.DoesNotContain("SECRET", error);
    }
}
