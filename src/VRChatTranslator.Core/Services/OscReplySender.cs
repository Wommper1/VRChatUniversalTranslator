using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VRChatTranslator.Core.Services;

/// <summary>Explicit, local-only chatbox sending. UDP has no delivery acknowledgement.</summary>
public sealed class OscReplySender : IDisposable
{
    private readonly UdpClient _udp = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IPEndPoint _target;
    private DateTimeOffset _lastSent = DateTimeOffset.MinValue;
    /// <param name="target">Where to send. Defaults to VRChat's OSC port on this PC; tests pass a throwaway local socket.</param>
    public OscReplySender(IPEndPoint? target = null) => _target = target ?? new IPEndPoint(IPAddress.Loopback, Port);
    /// <summary>VRChat's default OSC input port.</summary>
    public const int Port = 9000;
    /// <summary>The latest transport result. Validation and rate-limit rejections never reach the network and are not recorded.</summary>
    public OscSendOutcome? LastOutcome { get; private set; }
    public static byte[] BuildPacket(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 144 || text.Split('\n').Length > 9 || text.Contains('\0'))
            throw new ArgumentException("Replies must contain 1–144 characters, at most 9 lines, and no null characters.");
        using var stream = new MemoryStream();
        foreach (var value in new[] { "/chatbox/input", ",sTF", text })
        {
            stream.Write(Encoding.UTF8.GetBytes(value)); stream.WriteByte(0);
            while (stream.Length % 4 != 0) stream.WriteByte(0);
        }
        return stream.ToArray();
    }
    public async Task SendAsync(string text, CancellationToken token = default)
    {
        var packet = BuildPacket(text);
        await _gate.WaitAsync(token);
        try
        {
            if (DateTimeOffset.UtcNow - _lastSent < TimeSpan.FromSeconds(1.5)) throw new InvalidOperationException("Wait a moment before sending another reply.");
            try { await _udp.SendAsync(packet, _target, token); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LastOutcome = new OscSendOutcome(DateTimeOffset.Now, ex.Message);
                throw;
            }
            _lastSent = DateTimeOffset.UtcNow;
            LastOutcome = new OscSendOutcome(DateTimeOffset.Now, null);
        }
        finally { _gate.Release(); }
    }
    public void Dispose() => _udp.Dispose();
}
