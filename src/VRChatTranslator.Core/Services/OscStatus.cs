using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VRChatTranslator.Core.Services;

/// <summary>The result of the most recent attempt to hand a reply to the local network stack.</summary>
public sealed record OscSendOutcome(DateTimeOffset At, string? Error)
{
    public bool Succeeded => Error is null;
}

public enum OscReadiness
{
    VrchatNotRunning,
    ListenerUnknown,
    NoListener,
    Ready
}

public sealed record OscStatus(OscReadiness Readiness, string Text, bool IsWarning);

/// <summary>
/// Describes whether a reply sent to VRChat's OSC port has a chance of arriving. OSC over UDP has no connection
/// or acknowledgement, and sending to a loopback port nobody listens on still "succeeds", so the useful signals
/// are whether VRChat is running, whether something is bound to the port, and what the last send did.
/// </summary>
public static class OscStatusEvaluator
{
    public const int Port = OscReplySender.Port;

    /// <param name="listenerOnPort">True/false when the port was checked, null when the check itself failed.</param>
    public static OscStatus Evaluate(bool consentGiven, bool vrchatRunning, bool? listenerOnPort, OscSendOutcome? last)
    {
        var (readiness, headline) = (vrchatRunning, listenerOnPort) switch
        {
            (false, _) => (OscReadiness.VrchatNotRunning, "VRChat is not running, so a reply would go nowhere."),
            (true, null) => (OscReadiness.ListenerUnknown,
                $"VRChat is running, but this app could not check whether its OSC port (UDP {Port}) is open."),
            (true, false) => (OscReadiness.NoListener,
                $"VRChat is running, but nothing is listening on UDP port {Port}. Turn on OSC in VRChat (Action Menu → Options → OSC). " +
                "If it is already on, VRChat may be using a different port, which this app does not support."),
            _ => (OscReadiness.Ready, $"VRChat is running and something is listening on UDP port {Port}.")
        };
        var consent = consentGiven ? "" : " Sending is off until you tick the box above.";
        var lastSend = last switch
        {
            null => "Nothing sent yet this session.",
            { Succeeded: true } => $"Last send: {last.At:HH:mm:ss} — handed to 127.0.0.1:{Port}. UDP cannot confirm VRChat received it.",
            _ => $"Last send failed at {last.At:HH:mm:ss}: {last.Error}"
        };
        var warning = readiness != OscReadiness.Ready || last is { Succeeded: false };
        return new OscStatus(readiness, headline + consent + "\n" + lastSend, warning);
    }

    /// <summary>
    /// True when one of the endpoints can receive a datagram sent to 127.0.0.1:<paramref name="port"/>. A socket bound
    /// only to a LAN address or only to IPv6 loopback would not receive it.
    /// </summary>
    public static bool IsReachableOnLoopback(IEnumerable<IPEndPoint> listeners, int port) =>
        listeners.Any(endpoint => endpoint.Port == port &&
            (IPAddress.Loopback.Equals(endpoint.Address) || IPAddress.Any.Equals(endpoint.Address) || IPAddress.IPv6Any.Equals(endpoint.Address)));

    /// <summary>Reads the local UDP listener table. Returns null when the table cannot be read.</summary>
    public static bool? ProbeLocalListener(int port = Port)
    {
        try { return IsReachableOnLoopback(IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners(), port); }
        catch (Exception ex) when (ex is NetworkInformationException or SocketException or InvalidOperationException) { return null; }
    }
}
