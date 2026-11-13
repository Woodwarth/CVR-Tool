// Protocol / UDP — serialized exchanges restricted to the local loopback interface.
// Module: Encodes CVR messages, reassembles replies and manages UDP exchanges.
// Purpose: handle explicit network targets and bounded packet data.
// Input: validated addresses or byte sequences.
// State: transport ownership stays with the application session.
// Scope: no discovery scans or firmware operations.
using System.Net;
using System.Net.Sockets;

namespace CvrTool.Protocol;
/// <summary>UDP transport preparation restricted to the local machine. Never enables LAN access.</summary>
public sealed class LoopbackUdpTransport : IDisposable
{
    public const int MaximumDatagramBytes = 4096;
    private readonly UdpClient client;
    private readonly SemaphoreSlim exchangeGate = new(1, 1);
    private readonly CancellationTokenSource shutdown = new();
    private readonly int timeoutMs;
    private int disposed;
    public LoopbackUdpTransport(NetworkTarget target)
    {
        var plan = NetworkPreparation.Validate(target);
        if (!plan.IsLoopback)
            throw new InvalidOperationException("This transport only permits loopback. LAN hardware access is not enabled.");
        timeoutMs = target.TimeoutMs;
        client = new UdpClient(new IPEndPoint(NetworkPreparation.ParseAddress(target.LocalAddress), target.LocalPort));
        try
        {
            client.Connect(NetworkPreparation.ParseAddress(target.RemoteAddress), target.RemotePort);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public IPEndPoint LocalEndpoint => new(((IPEndPoint)client.Client.LocalEndPoint!).Address, ((IPEndPoint)client.Client.LocalEndPoint!).Port);

    /// <summary>Returns one datagram from the configured peer; does not assert protocol correlation or ACK meaning.</summary>
    public async Task<byte[]> ExchangeAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (payload.Length is < 1 or > MaximumDatagramBytes)
            throw new ArgumentOutOfRangeException(nameof(payload), "UDP payload must contain 1 to 4096 bytes.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, shutdown.Token);
        await exchangeGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(timeoutMs);
            try
            {
                await client.SendAsync(payload, timeout.Token).ConfigureAwait(false);
                var response = await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                if (response.Buffer.Length is < 1 or > MaximumDatagramBytes)
                    throw new InvalidDataException("UDP response size is not supported.");
                return response.Buffer;
            }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
            {
                throw new TimeoutException($"No UDP response within {timeoutMs} ms.");
            }
        }
        finally
        {
            exchangeGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        shutdown.Cancel();
        client.Dispose();
        shutdown.Dispose();
        // Retain the managed semaphore until canceled waiters finish their finally paths.
    }
}
