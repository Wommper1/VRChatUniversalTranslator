using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.IO;
using System.Net.Http;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;

namespace VRChatTranslator.App.Services;

public static class PhoneServerSelfTest
{
    public static async Task RunAsync()
    {
        var hub = new CaptionHub(2);
        await using var server = new PhoneDisplayServer(hub, () => new PhoneStatusSnapshot(
            false, false, "Listening", "Japanese", 125, 0));
        var port = GetAvailablePort();
        await server.StartAsync(port, loopbackOnly: true);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };

        var health = await client.GetStringAsync($"http://127.0.0.1:{port}/health");
        Require(health.Contains("ready", StringComparison.Ordinal), "Health endpoint should report readiness.");
        Require(!health.Contains("localOnly", StringComparison.Ordinal), "Health endpoint must not claim a local-only bind: the port listens on every adapter.");
        Require(server.DisplayUrls.Count > 0, "At least one usable display address should be reported.");
        var pin = server.Pin ?? throw new InvalidOperationException("The server must always have a PIN.");
        Require(pin.Length == PhoneAccessPolicy.PinLength && pin.All(char.IsAsciiDigit), "The PIN should be six digits.");
        Require(server.DisplayUrls.All(url => url.EndsWith($"/?pin={pin}", StringComparison.Ordinal)), "Every display link should carry the PIN.");

        await RequireStatusAsync(client, $"http://127.0.0.1:{port}/events", HttpStatusCode.Unauthorized, "A stream without a PIN must be refused.");
        await RequireStatusAsync(client, $"http://127.0.0.1:{port}/events?pin=000000x", HttpStatusCode.Unauthorized, "A stream with a wrong PIN must be refused.");

        using (var foreignHost = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/health"))
        {
            foreignHost.Headers.Host = "rebound.example";
            using var refused = await client.SendAsync(foreignHost);
            Require(refused.StatusCode == HttpStatusCode.Forbidden, "A request under a foreign Host name must be refused (DNS rebinding).");
        }

        using (var page = await client.GetAsync($"http://127.0.0.1:{port}/"))
        {
            var html = await page.Content.ReadAsStringAsync();
            Require(html.Contains("textContent", StringComparison.Ordinal), "Phone UI must render captions as text.");
            Require(!html.Contains("innerHTML", StringComparison.Ordinal), "Phone UI must not inject caption HTML.");

            // The page and every refusal carry the same browser-side protections.
            RequireHeader(page, "X-Content-Type-Options", "nosniff");
            RequireHeader(page, "Referrer-Policy", "no-referrer");
            RequireHeader(page, "X-Frame-Options", "DENY");
            RequireHeader(page, "Cache-Control", "no-store");
            var policy = string.Join(' ', page.Headers.GetValues("Content-Security-Policy"));
            Require(policy.Contains("default-src 'none'", StringComparison.Ordinal) && policy.Contains("script-src 'sha256-", StringComparison.Ordinal) &&
                    !policy.Contains("unsafe-", StringComparison.Ordinal), "The page must carry a hash-based Content-Security-Policy with no unsafe allowances.");
            Require(policy == PhonePageSecurity.ContentSecurityPolicy(html), "The served policy must match the hashes of the served page.");
            Require(!page.Headers.Contains("Server"), "The server must not announce itself.");
        }

        using (var refusedStream = await client.GetAsync($"http://127.0.0.1:{port}/events"))
        {
            RequireHeader(refusedStream, "X-Content-Type-Options", "nosniff");
        }

        using var streamResponse = await client.GetAsync(
            $"http://127.0.0.1:{port}/events?pin={pin}",
            HttpCompletionOption.ResponseHeadersRead);
        streamResponse.EnsureSuccessStatusCode();
        await using var stream = await streamResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var unsafeText = "</script><script>alert('no')</script>";
        hub.Publish(new CaptionEvent(DateTimeOffset.UtcNow, "ja", "Japanese", unsafeText, unsafeText, 1, TimeSpan.Zero));
        var firstCaption = await ReadCaptionAsync(reader, TimeSpan.FromSeconds(4));
        Require(!firstCaption.RawJson.Contains("<script>", StringComparison.OrdinalIgnoreCase), "SSE JSON should escape script delimiters.");
        Require(firstCaption.TranslatedText == unsafeText, "Escaping must preserve the caption text value.");

        using var reconnectResponse = await client.GetAsync(
            $"http://127.0.0.1:{port}/events?pin={pin}",
            HttpCompletionOption.ResponseHeadersRead);
        reconnectResponse.EnsureSuccessStatusCode();
        await using var reconnectStream = await reconnectResponse.Content.ReadAsStreamAsync();
        using var reconnectReader = new StreamReader(reconnectStream);
        var replayed = await ReadCaptionAsync(reconnectReader, TimeSpan.FromSeconds(4));
        Require(replayed.TranslatedText == unsafeText, "A reconnect should receive bounded caption history.");

        var occupiedPort = GetAvailablePort();
        var blocker = new TcpListener(IPAddress.Loopback, occupiedPort);
        blocker.ExclusiveAddressUse = true;
        blocker.Start();
        try
        {
            await using var conflicting = new PhoneDisplayServer(hub, () => new PhoneStatusSnapshot(false, false, "Stopped", "—", null, 0));
            var conflictDetected = false;
            try
            {
                await conflicting.StartAsync(occupiedPort, loopbackOnly: true);
            }
            catch (IOException)
            {
                conflictDetected = true;
            }
            catch (InvalidOperationException)
            {
                conflictDetected = true;
            }

            Require(conflictDetected, "A port conflict should be reported instead of crashing later.");
        }
        finally
        {
            blocker.Stop();
        }

        await RunPinThrottleCheckAsync(hub);
        await RunClientCapCheckAsync(hub);

        await server.StopAsync();
        var stopped = false;
        try
        {
            await client.GetStringAsync($"http://127.0.0.1:{port}/health");
        }
        catch (HttpRequestException)
        {
            stopped = true;
        }

        Require(stopped, "Stopping the server should release the listener.");
    }

    private static async Task RunPinThrottleCheckAsync(CaptionHub hub)
    {
        await using var server = new PhoneDisplayServer(hub, () => new PhoneStatusSnapshot(false, false, "Stopped", "—", null, 0));
        var port = GetAvailablePort();
        await server.StartAsync(port, loopbackOnly: true);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await RequireStatusAsync(client, $"http://127.0.0.1:{port}/events?pin=wrong{attempt}", HttpStatusCode.Unauthorized, "Wrong PINs are refused.");
        }

        await RequireStatusAsync(client, $"http://127.0.0.1:{port}/events?pin={server.Pin}", HttpStatusCode.TooManyRequests,
            "After five wrong PINs even the right one must wait, so a PIN cannot be guessed by trying them in turn.");
    }

    private static async Task RunClientCapCheckAsync(CaptionHub hub)
    {
        await using var server = new PhoneDisplayServer(hub, () => new PhoneStatusSnapshot(false, false, "Stopped", "—", null, 0));
        var port = GetAvailablePort();
        await server.StartAsync(port, loopbackOnly: true);
        var open = new List<HttpResponseMessage>();
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            for (var phone = 0; phone < 8; phone++)
            {
                var response = await client.GetAsync($"http://127.0.0.1:{port}/events?pin={server.Pin}", HttpCompletionOption.ResponseHeadersRead);
                open.Add(response);
                Require(response.StatusCode == HttpStatusCode.OK, "The first eight displays should connect.");
            }

            await RequireStatusAsync(client, $"http://127.0.0.1:{port}/events?pin={server.Pin}", HttpStatusCode.ServiceUnavailable,
                "A ninth display must be turned away rather than piling up open streams.");
        }
        finally
        {
            foreach (var response in open)
            {
                response.Dispose();
            }
        }
    }

    private static void RequireHeader(HttpResponseMessage response, string name, string expected) =>
        Require(response.Headers.TryGetValues(name, out var values) && values.Contains(expected, StringComparer.Ordinal),
            $"Response header {name}: {expected} is required.");

    private static async Task RequireStatusAsync(HttpClient client, string url, HttpStatusCode expected, string message)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        Require(response.StatusCode == expected, $"{message} (got {(int)response.StatusCode}).");
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<(string RawJson, string TranslatedText)> ReadCaptionAsync(StreamReader reader, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (await reader.ReadLineAsync(cancellation.Token) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var json = line[6..];
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.GetProperty("type").GetString() != "caption")
            {
                continue;
            }

            var translated = document.RootElement.GetProperty("value").GetProperty("translatedText").GetString() ?? string.Empty;
            return (json, translated);
        }

        throw new InvalidOperationException("SSE stream ended before a caption arrived.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
