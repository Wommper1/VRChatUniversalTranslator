using System.Net;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VRChatTranslator.Core.Services;

namespace VRChatTranslator.App.Services;

public sealed class PhoneDisplayServer(ICaptionHub captionHub, Func<PhoneStatusSnapshot> statusProvider) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private const int MaximumClients = 8;
    private readonly object _gate = new();
    private readonly HashSet<Channel<string>> _clients = [];
    private readonly PinAttemptLimiter _pinLimiter = new();
    private WebApplication? _application;
    private CancellationTokenSource? _statusCancellation;
    private Task? _statusTask;
    private string? _pin;

    public bool IsRunning => _application is not null;
    public int Port { get; private set; }

    /// <summary>The PIN every caption stream must present. A new one is made each time the server starts.</summary>
    public string? Pin => _pin;

    /// <summary>Addresses for a phone to open. Each carries the PIN, so opening the link is all a phone has to do.</summary>
    public IReadOnlyList<string> DisplayUrls { get; private set; } = [];

    /// <param name="loopbackOnly">Listen on this PC only. The app never sets it; the self-test does, so it can run without a firewall prompt.</param>
    public async Task StartAsync(int port = 8765, string? pin = null, bool loopbackOnly = false, CancellationToken cancellationToken = default)
    {
        if (_application is not null)
        {
            return;
        }

        if (port is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Choose a port from 1024 through 65535.");
        }

        EnsurePortAvailable(port, loopbackOnly);

        // The display always needs a PIN: captions are other people's speech, and the port listens on every network adapter.
        _pin = string.IsNullOrWhiteSpace(pin) ? PhoneAccessPolicy.NewPin() : pin.Trim();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 1024; // every request is a GET; nothing here accepts an upload
            options.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
            options.Limits.MaxConcurrentConnections = 64;
            if (loopbackOnly)
            {
                options.Listen(IPAddress.Loopback, port);
            }
            else
            {
                options.ListenAnyIP(port);
            }
        });
        var application = builder.Build();

        application.Use(AddSecurityHeadersAsync);
        application.Use(RefuseOutsideLocalNetworkAsync);
        application.MapGet("/", () => Results.Content(PhonePage, "text/html; charset=utf-8"));
        application.MapGet("/health", () => Results.Json(new { ready = true }));
        application.MapGet("/events", StreamEventsAsync);

        try
        {
            await application.StartAsync(cancellationToken);
        }
        catch
        {
            await application.DisposeAsync();
            throw;
        }

        _application = application;
        Port = port;
        DisplayUrls = [.. GetLanAddresses(port).Select(address => $"{address}/?pin={_pin}")];
        captionHub.CaptionPublished += OnCaptionPublished;
        _statusCancellation = new CancellationTokenSource();
        _statusTask = PublishStatusLoopAsync(_statusCancellation.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var application = _application;
        if (application is null)
        {
            return;
        }

        _application = null;
        captionHub.CaptionPublished -= OnCaptionPublished;
        _statusCancellation?.Cancel();
        if (_statusTask is not null)
        {
            try
            {
                await _statusTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        Channel<string>[] clients;
        lock (_gate)
        {
            clients = [.. _clients];
            _clients.Clear();
        }

        foreach (var client in clients)
        {
            client.Writer.TryComplete();
        }

        await application.StopAsync(cancellationToken);
        await application.DisposeAsync();
        _statusCancellation?.Dispose();
        _statusCancellation = null;
        _statusTask = null;
        DisplayUrls = [];
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private static readonly IReadOnlyDictionary<string, string> SecurityHeaders = PhonePageSecurity.ResponseHeaders(PhonePage);

    private static Task AddSecurityHeadersAsync(HttpContext context, Func<Task> next)
    {
        // Set before anything else runs so that refusals carry them too.
        foreach (var header in SecurityHeaders)
        {
            context.Response.Headers[header.Key] = header.Value;
        }

        return next();
    }

    private async Task RefuseOutsideLocalNetworkAsync(HttpContext context, Func<Task> next)
    {
        // Only this PC or a private network may talk to the server, and only under a name this PC answers to. The second check
        // stops a web page the user visits from reaching the server by pointing its own address name at this PC.
        if (!PhoneAccessPolicy.IsLocalNetworkClient(context.Connection.RemoteIpAddress) ||
            !PhoneAccessPolicy.IsExpectedHost(context.Request.Headers.Host.ToString(), Environment.MachineName))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("The phone display only answers on the local network.");
            return;
        }

        await next();
    }

    private async Task StreamEventsAsync(HttpContext context)
    {
        var client = PhoneAccessPolicy.ClientKey(context.Connection.RemoteIpAddress);
        var wait = _pinLimiter.RetryAfter(client, DateTimeOffset.UtcNow);
        if (wait > TimeSpan.Zero)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
            await context.Response.WriteAsync("Too many wrong PINs. Wait a few minutes and try again.");
            return;
        }

        if (_pin is null || !PhoneAccessPolicy.PinMatches(context.Request.Query["pin"], _pin))
        {
            _pinLimiter.RecordFailure(client, DateTimeOffset.UtcNow);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("A valid local display PIN is required.");
            return;
        }

        _pinLimiter.RecordSuccess(client);

        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        bool admitted;
        lock (_gate)
        {
            // A handful of phones is plenty; an unbounded number of open streams is a cheap way to bog the PC down.
            admitted = _clients.Count < MaximumClients && _clients.Add(channel);
        }

        if (!admitted)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("Too many displays are connected.");
            return;
        }

        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Append("X-Accel-Buffering", "no");
        context.Response.ContentType = "text/event-stream";

        try
        {
            foreach (var caption in captionHub.History)
            {
                channel.Writer.TryWrite(Serialize("caption", caption));
            }

            channel.Writer.TryWrite(Serialize("status", statusProvider()));
            await foreach (var message in channel.Reader.ReadAllAsync(context.RequestAborted))
            {
                await context.Response.WriteAsync($"data: {message}\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                _clients.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }

    private void OnCaptionPublished(object? sender, CaptionProducedEventArgs e) =>
        Broadcast(Serialize("caption", e.Caption));

    private async Task PublishStatusLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            Broadcast(Serialize("status", statusProvider()));
        }
    }

    private void Broadcast(string message)
    {
        lock (_gate)
        {
            foreach (var client in _clients)
            {
                client.Writer.TryWrite(message);
            }
        }
    }

    private static string Serialize(string type, object value) =>
        JsonSerializer.Serialize(new { type, value }, JsonOptions);

    private static IReadOnlyList<string> GetLanAddresses(int port)
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                              network.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
            .Distinct()
            .Select(address => $"http://{address}:{port}")
            .ToArray();

        return addresses.Length > 0 ? addresses : [$"http://127.0.0.1:{port}"];
    }

    private static void EnsurePortAvailable(int port, bool loopbackOnly)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                ExclusiveAddressUse = true
            };
            socket.Bind(new IPEndPoint(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, port));
        }
        catch (SocketException exception)
        {
            throw new IOException($"Port {port} is already in use. Stop the other service or choose another phone-display port.", exception);
        }
    }

    private const string PhonePage = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
  <title>VRChat Universal Translator</title>
  <style>
    :root{color-scheme:dark;font-family:system-ui,sans-serif;background:#0b0f17;color:#f3f6fb}
    body{margin:0;min-height:100vh;display:grid;grid-template-rows:auto 1fr auto;background:radial-gradient(circle at top,#152836,#0b0f17 45%)}
    header,footer{padding:16px 20px;background:#111824cc;backdrop-filter:blur(12px)}
    header{display:flex;gap:12px;align-items:center;justify-content:space-between;flex-wrap:wrap}
    h1{font-size:16px;margin:0}.live{color:#66d6c5;font-size:12px}.offline{color:#f2bc67}
    select,button{background:#1c2433;color:#f3f6fb;border:1px solid #344055;border-radius:9px;padding:9px;touch-action:manipulation}
    main{padding:22px;overflow:auto}.item{border-radius:14px;background:#151b27e8;padding:16px;margin:0 0 12px;box-shadow:0 12px 30px #0005}
    .lang{color:#66d6c5;font-size:12px}.translated{font-size:clamp(22px,6vw,38px);line-height:1.25;margin-top:8px}.original{color:#aeb8c9;margin-top:9px}
    footer{font-size:12px;color:#99a4b7;display:grid;gap:4px}.hidden{display:none}
  </style>
</head>
<body>
  <header><div><h1>VRChat Universal Translator</h1><span id="connection" class="offline">Connecting…</span></div><div>
    <select id="mode" aria-label="Display mode"><option value="caption">Caption</option><option value="conversation">Conversation</option><option value="dual">Dual language</option><option value="diagnostic">Diagnostic</option></select>
    <button id="awake">Keep awake</button><button id="fullscreen">Fullscreen</button></div>
  </header>
  <main id="captions" aria-live="polite"></main>
  <footer id="status"><span>Local network display</span><span id="diagnostic"></span></footer>
<script>
const list=document.getElementById('captions'),mode=document.getElementById('mode'),connection=document.getElementById('connection'),diag=document.getElementById('diagnostic'),awake=document.getElementById('awake'),fullscreen=document.getElementById('fullscreen');let wakeLock=null;
const params=new URLSearchParams(location.search);let pin=params.get('pin')||'';
function connect(){const source=new EventSource('/events'+(pin?'?pin='+encodeURIComponent(pin):''));
source.onopen=()=>{connection.textContent='Live';connection.className='live'};
source.onerror=()=>{connection.textContent='Disconnected / PIN required';connection.className='offline'};
source.onmessage=e=>{const message=JSON.parse(e.data);if(message.type==='caption')addCaption(message.value);if(message.type==='status')setStatus(message.value)}}
function addCaption(c){const item=document.createElement('article');item.className='item';const lang=document.createElement('div');lang.className='lang';lang.textContent=(c.languageName||c.detectedLanguage)+' → English';
const translated=document.createElement('div');translated.className='translated';translated.dataset.role='translated';translated.textContent=c.translatedText;
const original=document.createElement('div');original.className='original';original.dataset.role='original';original.textContent=c.originalText||'';item.append(lang,translated,original);list.prepend(item);while(list.children.length>50)list.lastChild.remove();applyMode()}
function setStatus(s){diag.textContent=`VRChat ${s.vrchatDetected?'detected':'waiting'} · audio ${s.capturing?'capturing':'off'} ${Math.round(s.audioLevelPercent||0)}% · speech ${s.speechState} · ${s.model} · ${s.mode} · ${s.detectedLanguage||'language —'} · ${s.latencyMilliseconds?Math.round(s.latencyMilliseconds)+' ms':'latency —'} · CPU translator ${s.translatorCpuPercent==null?'—':s.translatorCpuPercent.toFixed(1)+'%'} / system ${s.systemCpuPercent==null?'—':s.systemCpuPercent.toFixed(1)+'%'} · GPU ${s.gpuPercent==null?'—':s.gpuPercent.toFixed(0)+'%'} · queue ${s.queueLength} · error ${s.lastError||'none'}`;applyMode()}
function applyMode(){const selected=mode.value;[...list.children].forEach((x,i)=>x.classList.toggle('hidden',selected==='caption'&&i>0));document.querySelectorAll('[data-role=translated]').forEach(x=>x.classList.remove('hidden'));document.querySelectorAll('[data-role=original]').forEach(x=>x.classList.toggle('hidden',selected!=='dual'));diag.classList.toggle('hidden',selected!=='diagnostic')}
mode.onchange=applyMode;awake.onclick=async()=>{if(!('wakeLock'in navigator)){awake.disabled=true;awake.textContent='Unavailable';return}try{wakeLock=await navigator.wakeLock.request('screen');awake.textContent='Awake ✓';wakeLock.onrelease=()=>awake.textContent='Keep awake'}catch{awake.textContent='Wake lock blocked'}};fullscreen.onclick=()=>document.documentElement.requestFullscreen?.();connect();
</script>
</body>
</html>
""";
}
