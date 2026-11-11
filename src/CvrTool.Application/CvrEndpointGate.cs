// Application / Shared CVR LAN ownership
// Purpose: serialize discovery and device I/O on each local CVR UDP endpoint.
// Units: local IPv4 address and UDP source port.
// State: short-lived exclusive sockets are released before another reader starts.
// Scope: several live devices coexist with exclusive ownership during each exchange.
using System.Collections.Concurrent;
using CvrTool.Protocol;

namespace CvrTool.Application;

internal static class CvrEndpointGate
{
    private static readonly ConcurrentDictionary<string, object> Gates = new();
    public static T Run<T>(string address, int port, Func<T> action)
    {
        lock (Gates.GetOrAdd(address + ":" + port, _ => new object())) return action();
    }
    public static void UseClient(NetworkTarget target, Action<CvrUdpClient> action) =>
        Run(target.LocalAddress, target.LocalPort, () =>
        {
            using var client = new CvrUdpClient(target);
            action(client);
            return true;
        });
}
