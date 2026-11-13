// Web / Exclusive browser session
// Module: Serves the local browser interface and routes authenticated requests to the shared session.
// Purpose: allow one active browser page across its parallel HTTP requests.
// Units: UTC heartbeat times; abandoned sessions expire after twelve seconds.
// State: ownership is synchronized; another client cannot release or renew it.
// Scope: this limits logical browsers, not the HTTP connections needed by a page.
namespace CvrTool.Web;

internal sealed class BrowserClientLease(TimeProvider clock)
{
    private readonly object gate = new();
    private string? owner;
    private DateTimeOffset lastSeen;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);

    public bool Connected { get { lock (gate) return owner is not null && clock.GetUtcNow() - lastSeen < Timeout; } }
    public bool IsOwner(string client) { lock (gate) return owner == client && clock.GetUtcNow() - lastSeen < Timeout; }

    public bool TryUse(string client, out bool changed)
    {
        lock (gate)
        {
            changed = ExpireLocked();
            if (owner is not null && owner != client) return false;
            if (owner is null) { owner = client; changed = true; }
            lastSeen = clock.GetUtcNow();
            return true;
        }
    }

    public bool Release(string client)
    {
        lock (gate)
        {
            if (owner != client) return false;
            owner = null;
            return true;
        }
    }

    public void Reset() { lock (gate) owner = null; }
    public bool Expire() { lock (gate) return ExpireLocked(); }
    private bool ExpireLocked()
    {
        if (owner is null || clock.GetUtcNow() - lastSeen < Timeout) return false;
        owner = null;
        return true;
    }
}
