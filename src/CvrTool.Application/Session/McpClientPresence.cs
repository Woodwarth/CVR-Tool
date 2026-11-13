// Application / MCP connection presence
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: track initialized MCP clients independently of short-lived tool RPCs.
// Units: live pipe connections; no inferred recent-activity timeout.
// State: current-user-only pipes close on client exit, including crashes.
// Scope: presentation metadata only; no device state or network socket ownership.
using System.IO.Pipes;

namespace CvrTool.Application;

public sealed class McpClientPresenceHost : IDisposable
{
    private readonly string pipeName;
    private readonly CancellationTokenSource shutdown = new();
    private readonly object gate = new();
    private readonly HashSet<NamedPipeServerStream> connections = [];
    private readonly HashSet<Task> handlers = [];
    private readonly Task listener;
    private int count;
    public int Count => Volatile.Read(ref count);
    public event Action? Changed;

    public McpClientPresenceHost(string? sessionName = null)
    {
        pipeName = Name(sessionName);
        var first = CreatePipe();
        listener = Task.Run(() => Listen(first));
    }

    internal static string Name(string? sessionName) => SessionDefaults.Resolve(sessionName) + "-mcp";
    private NamedPipeServerStream CreatePipe() => new(pipeName, PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task Listen(NamedPipeServerStream current)
    {
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                await current.WaitForConnectionAsync(shutdown.Token).ConfigureAwait(false);
                var accepted = current;
                // Keep another instance listening before the accepted one may close on Unix.
                current = CreatePipe();
                lock (gate) connections.Add(accepted);
                var task = Observe(accepted);
                lock (gate) handlers.Add(task);
                _ = task.ContinueWith(done => { lock (gate) handlers.Remove(done); }, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (IOException) when (shutdown.IsCancellationRequested) { }
        finally { current.Dispose(); }
    }

    private async Task Observe(NamedPipeServerStream pipe)
    {
        var registered = false;
        try
        {
            using var hello = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            hello.CancelAfter(TimeSpan.FromSeconds(3));
            var marker = new byte[1];
            if (await pipe.ReadAsync(marker, hello.Token).ConfigureAwait(false) != 1 || marker[0] != 0x4d) return;
            registered = true;
            Interlocked.Increment(ref count); Changed?.Invoke();
            await pipe.WriteAsync(new byte[] { 1 }, shutdown.Token).ConfigureAwait(false);
            await pipe.FlushAsync(shutdown.Token).ConfigureAwait(false);
            // EOF is actual transport loss, unlike a gap between tool calls.
            while (await pipe.ReadAsync(marker, shutdown.Token).ConfigureAwait(false) != 0) { }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) when (shutdown.IsCancellationRequested) { }
        finally
        {
            if (registered) { Interlocked.Decrement(ref count); Changed?.Invoke(); }
            lock (gate) connections.Remove(pipe);
            pipe.Dispose();
        }
    }

    public void Dispose()
    {
        shutdown.Cancel();
        NamedPipeServerStream[] active;
        lock (gate) active = connections.ToArray();
        foreach (var pipe in active) pipe.Dispose();
        listener.GetAwaiter().GetResult();
        Task[] pending;
        lock (gate) pending = handlers.ToArray();
        Task.WhenAll(pending).GetAwaiter().GetResult();
        shutdown.Dispose();
    }
}

// Client presence starts only after the MCP initialized notification, not on spawn.
public sealed class McpClientPresence(string? sessionName = null) : IAsyncDisposable
{
    private readonly CancellationTokenSource shutdown = new();
    private Task? worker;
    private int started;
    public void Start()
    {
        if (Interlocked.Exchange(ref started, 1) == 0) worker = Task.Run(Connect);
    }

    private async Task Connect()
    {
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", McpClientPresenceHost.Name(sessionName),
                    PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
                await pipe.WriteAsync(new byte[] { 0x4d }, shutdown.Token).ConfigureAwait(false);
                await pipe.FlushAsync(shutdown.Token).ConfigureAwait(false);
                var response = new byte[1];
                while (await pipe.ReadAsync(response, shutdown.Token).ConfigureAwait(false) != 0) { }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            if (!shutdown.IsCancellationRequested)
                try { await Task.Delay(1000, shutdown.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        shutdown.Cancel();
        if (worker is { } task) await task.ConfigureAwait(false);
        shutdown.Dispose();
    }
}
