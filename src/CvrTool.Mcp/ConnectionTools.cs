// MCP / Connections
// Purpose: connect the shared application owner to an explicitly selected CVR peer.
// Input: literal unicast IPv4 and local interface; no discovery.
// State: sockets belong to the desktop or headless session, not the MCP client.
// Scope: operations use hardware readback; unsupported replies produce errors.
using System.ComponentModel;
using CvrTool.Application;
using CvrTool.Core;
using CvrTool.Protocol;
using ModelContextProtocol.Server;

namespace CvrTool.Mcp;

[McpServerToolType]
public sealed class ConnectionTools(AmplifierService service)
{
    [McpServerTool(Name = "connect_amplifier"), Description("Connect one explicitly selected CVR IPv4 amplifier through the shared session. The default observed profile provides settings, telemetry and capability-gated verified edits; readOnly=false selects the older individual-query path. This tool connects only the explicit target.")]
    public string Connect(string address, string localAddress, int localPort = 45454, int remotePort = 45455, int timeoutMs = 1000, bool readOnly = true)
        => System.Text.Json.JsonSerializer.Serialize(service.Connect(new NetworkTarget("CVR amplifier", address, remotePort, localAddress, localPort, timeoutMs, readOnly)));

    [McpServerTool(Name = "disconnect_amplifier"), Description("Disconnect an explicit CVR target and release its UDP socket in the shared session.")]
    public string Disconnect(string deviceId) => System.Text.Json.JsonSerializer.Serialize(service.Disconnect(deviceId));
}
