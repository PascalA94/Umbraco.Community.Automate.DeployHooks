using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Automate.Core.Configuration;
using Umbraco.Community.Automate.DeployHooks.Configuration;
using Umbraco.Community.Automate.DeployHooks.Credentials;
using Umbraco.Community.Automate.DeployHooks.Http;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

/// <summary>Records every request the senders make and answers from a script. Never touches the network.</summary>
internal sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond = null) : HttpMessageHandler
{
    private readonly TaskCompletionSource _seen = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Completes once a request has reached the handler, so a test can advance the clock after the send started.</summary>
    public Task WaitForRequestAsync() => _seen.Task.WaitAsync(TimeSpan.FromSeconds(10));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
        _seen.TrySetResult();
        return respond is null ? new HttpResponseMessage(HttpStatusCode.OK) : await respond(request, cancellationToken);
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body);

/// <summary>Everything a sender test needs: a real credential store over in-memory config, a recording client and a fake clock and log.</summary>
internal sealed class SenderRig
{
    // Fake values: the sentinels prove nothing leaks into messages, logs or output.
    public const string VercelHook = "https://api.vercel.com/v1/integrations/deploy/prj_SENTINELPROJECT/SENTINELHOOK"; // gitleaks:allow
    public const string RevalidateUrl = "https://www.sentinel-site.example/api/revalidate-SENTINELROUTE"; // gitleaks:allow
    public const string RevalidateSecret = "SENTINELSECRET-0123456789"; // gitleaks:allow

    public SenderRig(RecordingHandler handler, Dictionary<string, string?>? config = null, DeployHooksOptions? options = null, IConfiguration? configuration = null)
    {
        Handler = handler;
        Options = options ?? new DeployHooksOptions();
        var values = config ?? new Dictionary<string, string?>
        {
            [$"{DeployHooksCredentialStore.Prefix}:mysite:DeployHookUrl"] = VercelHook,
            [$"{DeployHooksCredentialStore.Prefix}:mysite:RevalidateUrl"] = RevalidateUrl,
            [$"{DeployHooksCredentialStore.Prefix}:mysite:RevalidateSecret"] = RevalidateSecret,
        };
        Configuration = configuration ?? new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        _automate = Substitute.For<IOptionsMonitor<AutomateOptions>>();
        _automate.CurrentValue.Returns(new AutomateOptions());
        Store = new DeployHooksCredentialStore(Configuration, _automate, Microsoft.Extensions.Options.Options.Create(new AutomateOptions()));
        Rules = new HostRules(Microsoft.Extensions.Options.Options.Create(Options));
        Factory = Substitute.For<IHttpClientFactory>();
        Factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        Time = new FakeTimeProvider();
        DeployLogger = new FakeLogger<DeployHookSender>();
        RevalidateLogger = new FakeLogger<RevalidateSender>();
    }

    private readonly IOptionsMonitor<AutomateOptions> _automate;

    /// <summary>Simulates a site that widened Automate's allow-list over the credential prefix.</summary>
    public void AllowListExposesCredentials() =>
        _automate.CurrentValue.Returns(new AutomateOptions { AllowedConfigurationKeyPrefixes = [DeployHooksCredentialStore.Prefix] });

    public IConfiguration Configuration { get; }

    public RecordingHandler Handler { get; }
    public DeployHooksOptions Options { get; }
    public DeployHooksCredentialStore Store { get; }
    public HostRules Rules { get; }
    public IHttpClientFactory Factory { get; }
    public FakeTimeProvider Time { get; }
    public FakeLogger<DeployHookSender> DeployLogger { get; }
    public FakeLogger<RevalidateSender> RevalidateLogger { get; }

    public DeployHookSender DeploySender() => new(Store, Rules, Factory, Microsoft.Extensions.Options.Options.Create(Options), Time, DeployLogger);

    public RevalidateSender RevalidateSender() => new(Store, Rules, Factory, Microsoft.Extensions.Options.Options.Create(Options), Time, RevalidateLogger);

    public IEnumerable<string> AllLogText() => DeployLogger.Collector.GetSnapshot().Concat(RevalidateLogger.Collector.GetSnapshot())
        .Select(r => r.Message + " " + string.Join(' ', r.StructuredState?.Select(p => p.Value) ?? []));
}

/// <summary>A response body that counts how many bytes the reader actually pulled from it.</summary>
internal sealed class CountingContent(long length) : HttpContent
{
    public long BytesRead { get; private set; }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();

    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new CountingStream(this, length));

    protected override bool TryComputeLength(out long result)
    {
        result = length;
        return true;
    }

    private sealed class CountingStream(CountingContent owner, long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Fill(buffer.AsSpan(offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Fill(buffer.Span));

        private int Fill(Span<byte> span)
        {
            var n = (int)Math.Min(span.Length, length - _position);
            span[..n].Fill((byte)'x');
            _position += n;
            owner.BytesRead += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
