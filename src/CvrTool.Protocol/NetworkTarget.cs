// Protocol / Targets — validate explicit IPv4 addresses without DNS or discovery.
// Purpose: handle explicit network targets and bounded packet data.
// Input: validated addresses or byte sequences.
// State: transport ownership stays with the application session.
// Scope: no discovery scans or firmware operations.
using System.Net;
using System.Net.Sockets;

namespace CvrTool.Protocol;

public sealed record NetworkTarget(string Name, string RemoteAddress, int RemotePort = 45455, string LocalAddress = "127.0.0.1", int LocalPort = 45454, int TimeoutMs = 1000);
public sealed record NetworkConnectionPlan(NetworkTarget Target, bool IsLoopback, bool HardwareAccessAllowed, string Status);
public static class NetworkPreparation
{
    public static NetworkConnectionPlan Validate(NetworkTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(target.Name) || target.Name.Length > 80)
            throw new ArgumentException("Target name must contain 1 to 80 characters.");
        var remote = ParseAddress(target.RemoteAddress);
        var local = ParseAddress(target.LocalAddress);
        if (target.RemotePort is < 1 or > 65535 || target.LocalPort is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(target), "UDP ports must be 1 to 65535 (local port may be 0 for an available port).");
        if (target.TimeoutMs is < 50 or > 30000)
            throw new ArgumentOutOfRangeException(nameof(target), "Timeout must be between 50 and 30,000 ms.");
        var loopback = IPAddress.IsLoopback(remote) && IPAddress.IsLoopback(local);
        if (IPAddress.IsLoopback(remote) != IPAddress.IsLoopback(local))
            throw new ArgumentException("Local and remote addresses must both be loopback or both be LAN addresses.");
        return new(target with
        {
            Name = target.Name.Trim(),
            RemoteAddress = remote.ToString(),
            LocalAddress = local.ToString()
        }, loopback, true, loopback ? "Loopback target prepared · No amplifier connection" : "LAN target prepared · Connect explicitly to read device settings");
    }

    public static IPAddress ParseAddress(string text)
    {
        // Require dotted decimal IPv4; do not resolve DNS or accept shortened/numeric IP notation.
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("IPv4 address is required.");
        var parts = text.Trim().Split('.');
        if (parts.Length != 4 || parts.Any(p => p.Length is < 1 or > 3 || p.Any(c => c is < '0' or > '9') || (p.Length > 1 && p[0] == '0')) || !IPAddress.TryParse(text.Trim(), out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Enter a full IPv4 address without a hostname.");
        var bytes = address.GetAddressBytes();
        if (bytes[0] == 0 || bytes[0] >= 224 || bytes[3] == 255)
            throw new ArgumentException("Use an explicit unicast address; Any, broadcast and multicast addresses are not supported.");
        return address;
    }
}
