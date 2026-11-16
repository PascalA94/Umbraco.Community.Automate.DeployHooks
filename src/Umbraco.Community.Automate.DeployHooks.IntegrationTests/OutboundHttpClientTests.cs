using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Umbraco.Community.Automate.DeployHooks.IntegrationTests;

/// <summary>
/// The package sends revalidate and deploy requests through Automate's named NoRedirect client and relies on it for
/// SSRF protection (private and loopback destinations) and for never following a redirect. Nothing else in this repo
/// proved that, so these tests resolve the real client and observe what it does. No mocks of the client.
///
/// The integration host loads Automate's assemblies but never runs Automate's registration (that needs its persistence
/// and startup packages), so a named client resolved from it would be an unconfigured default and prove nothing. The
/// container here is therefore Automate's own AddUmbracoAutomateCore run on a real UmbracoBuilder, as the isolation
/// contract suite does for its registration checks.
/// </summary>
[TestFixture]
public class OutboundHttpClientTests
{
    private const string ClientName = Umbraco.Automate.Core.Constants.HttpClients.NoRedirect;

    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public void BuildContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var builder = new UmbracoBuilder(services, configuration, new TypeLoader(new EmptyTypeFinder(), NullLogger<TypeLoader>.Instance, []));

        System.Reflection.Assembly.Load("Umbraco.Automate.Core")
            .GetType("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", throwOnError: true)!
            .GetMethod(
                "AddUmbracoAutomateCore",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
                [typeof(IUmbracoBuilder)])!
            .Invoke(null, [builder]);

        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public void DisposeContainer() => _provider.Dispose();

    [Test]
    public async Task NoRedirectClient_refuses_loopback_destination_without_connecting()
    {
        using var listener = new CountingListener();

        HttpClient client = _provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        client.Timeout = TimeSpan.FromSeconds(10);

        Exception? failure = await CaptureAsync(() => client.GetAsync($"http://127.0.0.1:{listener.Port}/probe"));

        Assert.That(failure, Is.Not.Null, "The client reached a loopback destination without error.");
        Assert.That(failure, Is.Not.InstanceOf<TaskCanceledException>(), $"Timed out instead of refusing: {failure}");
        Assert.That(listener.Requests, Is.EqualTo(0), "The loopback listener received a connection or request.");
    }

    [Test]
    public async Task NoRedirectClient_refuses_private_literal_destination_without_timing_out()
    {
        HttpClient client = _provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        client.Timeout = TimeSpan.FromSeconds(5);

        // 10.0.0.1 is unroutable on most networks, so a client that does not guard would hang until the timeout.
        // A refusal must therefore arrive as an exception that is not a timeout.
        Exception? failure = await CaptureAsync(() => client.GetAsync("http://10.0.0.1:81/probe"));

        Assert.That(failure, Is.Not.Null, "The client reached a private destination without error.");
        Assert.That(failure, Is.Not.InstanceOf<TaskCanceledException>(), $"Timed out instead of refusing: {failure}");
        Assert.That(failure!.InnerException, Is.Not.InstanceOf<TimeoutException>(), $"Timed out instead of refusing: {failure}");
    }

    // Hermetic: a real redirect needs a reachable public target, so inspect the primary handler instead.
    [Test]
    public void NoRedirectClient_primary_handler_does_not_follow_redirects()
    {
        HttpMessageHandler handler = _provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(ClientName);

        while (handler is DelegatingHandler delegating)
        {
            Assert.That(delegating.InnerHandler, Is.Not.Null, "Handler chain has no primary handler.");
            handler = delegating.InnerHandler!;
        }

        bool? autoRedirect = handler switch
        {
            SocketsHttpHandler sockets => sockets.AllowAutoRedirect,
            HttpClientHandler clientHandler => clientHandler.AllowAutoRedirect,
            _ => null,
        };

        Assert.That(autoRedirect, Is.Not.Null, $"Unexpected primary handler type {handler.GetType()}; cannot inspect redirects.");
        Assert.That(autoRedirect, Is.False, "The NoRedirect client's primary handler follows redirects.");
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>Finds nothing: registration code runs as on a site, and attribute-discovered collections stay empty.</summary>
    private sealed class EmptyTypeFinder : ITypeFinder
    {
        public IEnumerable<Assembly> AssembliesToScan => [];

        public Type? GetTypeByName(string name) => null;

        public IEnumerable<Type> FindClassesOfTypeWithAttribute(Type assignTypeFrom, Type attributeType, IEnumerable<Assembly>? assemblies = null, bool onlyConcreteClasses = true) => [];

        public IEnumerable<Type> FindClassesOfType(Type assignTypeFrom, IEnumerable<Assembly>? assemblies = null, bool onlyConcreteClasses = true) => [];

        public IEnumerable<Type> FindClassesWithAttribute(Type attributeType, IEnumerable<Assembly>? assemblies = null, bool onlyConcreteClasses = true) => [];
    }

    /// <summary>Accepts TCP connections on a random loopback port and counts them (any connection is a breach).</summary>
    private sealed class CountingListener : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private int _requests;

        public CountingListener()
        {
            _listener.Start();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        using TcpClient c = await _listener.AcceptTcpClientAsync(_cts.Token);
                        Interlocked.Increment(ref _requests);
                    }
                }
                catch (Exception)
                {
                    // Listener stopped.
                }
            });
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public int Requests => Volatile.Read(ref _requests);

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }
}
