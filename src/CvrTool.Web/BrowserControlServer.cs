// Web / Local browser control
// Module: Serves the local browser interface and routes authenticated requests to the shared session.
// Purpose: serve embedded touch controls through the existing Application service.
// Units: the same dB/ms/Hz/Q values as Desktop and MCP.
// State: explicit start/stop, per-start access key and serialized browser writes.
// Scope: one selected LAN address; no new amplifier sockets, reset or firmware API.
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using CvrTool.Application;
using CvrTool.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CvrTool.Web;

public sealed class BrowserControlServer(AmplifierService service, TimeProvider? timeProvider = null) : IAsyncDisposable
{
    private WebApplication? host;
    private readonly SemaphoreSlim writes = new(1, 1);
    private readonly BrowserClientLease clientLease = new(timeProvider ?? TimeProvider.System);
    private ITimer? leaseTimer;
    public string AccessKey { get; private set; } = "";
    public string? Address { get; private set; }
    public string[] ConnectionUrls { get; private set; } = [];
    public string? ConnectionUrl => Address is null ? null : Address + "/#" + AccessKey;
    public event Action? Changed;
    public event Action? StatusChanged;
    public bool IsRunning => Address is not null;
    public bool ClientConnected => IsRunning && clientLease.Connected;

    public static bool IsLocalAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return IPAddress.IsLoopback(address) || b[0] == 10 ||
            (b[0] == 172 && b[1] is >= 16 and <= 31) ||
            (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    public static IReadOnlyList<BrowserInterface> Interfaces() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses
            .Where(a => IsLocalAddress(a.Address) && !IPAddress.IsLoopback(a.Address))
            .Select(a => new BrowserInterface(n.Name, a.Address.ToString())))
        .OrderBy(i => i.Address.StartsWith("169.254.", StringComparison.Ordinal)).ToArray();

    public async Task StartAsync(IPAddress address, int port = 8787)
    {
        if (host is not null) throw new InvalidOperationException("Browser control is already running.");
        if (!address.Equals(IPAddress.Any) && !IsLocalAddress(address)) throw new ArgumentException("Choose a local IPv4 network address.");
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders(); // Never pollute integrated MCP stdout.
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Listen(address, port);
            k.Limits.MaxRequestBodySize = 262144;
            k.Limits.MaxConcurrentConnections = 32;
            k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
        });
        var app = builder.Build();
        AccessKey = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            var requestedHost = context.Request.Host.Host;
            var local = context.Connection.LocalIpAddress;
            if (requestedHost != local?.ToString() && !(local is not null && IPAddress.IsLoopback(local) && requestedHost == "localhost"))
            { context.Response.StatusCode = 403; return; }
            if (context.Request.Headers.Origin is { Count: > 0 } origin &&
                !string.Equals(origin.ToString(), $"http://{context.Request.Host}", StringComparison.OrdinalIgnoreCase))
            { context.Response.StatusCode = 403; return; }
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                var supplied = context.Request.Headers.Authorization.ToString();
                if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied), System.Text.Encoding.UTF8.GetBytes("Bearer " + AccessKey)))
                { context.Response.StatusCode = 401; return; }
                var client = context.Request.Headers["X-CVR-Client"].ToString();
                if (client.Length != 32 || client.Any(c => !Uri.IsHexDigit(c)))
                { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "A browser session identifier is required." }); return; }
                if (context.Request.Path == "/api/disconnect" && HttpMethods.IsPost(context.Request.Method))
                {
                    if (clientLease.Release(client)) StatusChanged?.Invoke();
                    context.Response.StatusCode = 204;
                    return;
                }
                if (!clientLease.TryUse(client, out var leaseChanged))
                {
                    context.Response.StatusCode = 423;
                    await context.Response.WriteAsJsonAsync(new { error = "Another browser is connected. Close it before connecting here." });
                    return;
                }
                if (leaseChanged) StatusChanged?.Invoke();
            }
            try { await next(context); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or IOException or FormatException)
            { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
        });
        app.MapGet("/favicon.ico", () => Results.Stream(typeof(BrowserControlServer).Assembly.GetManifestResourceStream("CvrTool.Web.Assets.favicon.ico")!, "image/x-icon"));
        app.MapGet("/", () => Asset("index.html", "text/html; charset=utf-8"));
        app.MapGet("/app.js", () => Asset("app.js", "text/javascript; charset=utf-8"));
        app.MapGet("/style.css", () => Asset("style.css", "text/css; charset=utf-8"));
        app.MapGet("/api/health", () => new { status = "ok" });
        app.MapGet("/api/devices", () => service.ListDevices());
        app.MapGet("/api/state", (string id) => State(id));
        app.MapPost("/api/eq-preview", (BrowserEqDraft draft) =>
        {
            draft.Validate();
            return Enumerable.Range(0, 257).Select(i =>
            {
                var hz = 20 * Math.Pow(1000, i / 256d);
                var response = System.Numerics.Complex.One;
                if (draft.Enabled)
                {
                    response = CrossoverResponse.Transfer(draft.HighPass, true, hz) * CrossoverResponse.Transfer(draft.LowPass, false, hz);
                    foreach (var band in draft.Bands) response *= EqualizerResponse.Transfer(band, hz);
                }
                return new { db = Math.Clamp(20 * Math.Log10(response.Magnitude), -24, 24), phase = response.Phase * 180 / Math.PI };
            }).ToArray();
        });
        app.MapPost("/api/eq-fir", (BrowserEqDraft draft) =>
        {
            draft.Validate();
            var taps = EqualizerResponse.GenerateFir(draft.Bands, draft.HighPass, draft.LowPass, draft.Enabled);
            return Results.Text(string.Join("\n", taps.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))), "text/plain");
        });
        app.MapGet("/api/preset", (string id) => Results.File(service.ExportNativePreset(id), "application/octet-stream", "CVR-preset.sd"));
        app.MapPost("/api/change", async (BrowserChange change, HttpContext context) =>
        {
            await writes.WaitAsync(context.RequestAborted);
            try
            {
                // Recheck after waiting: a closed browser must not leave queued writes behind.
                if (!clientLease.IsOwner(context.Request.Headers["X-CVR-Client"].ToString()))
                    return Results.Json(new { error = "This browser session is no longer active." }, statusCode: 423);
                var device = service.GetDevice(change.DeviceId);
                if (!device.IsDemo && (device.Readings is not { Error: null, TelemetryObservedAt: { } at } || DateTimeOffset.UtcNow - at > TimeSpan.FromSeconds(3)))
                    throw new InvalidOperationException("Device is offline; refresh before editing.");
                var result = Apply(change);
                Changed?.Invoke();
                return Results.Json(result);
            }
            finally { writes.Release(); }
        });
        try
        {
            await app.StartAsync().ConfigureAwait(false);
            Address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');
            if (address.Equals(IPAddress.Any))
            {
                var preferred = Interfaces().FirstOrDefault()?.Address ?? "127.0.0.1";
                var bound = Address;
                Address = new UriBuilder(bound) { Host = preferred }.Uri.GetLeftPart(UriPartial.Authority);
                ConnectionUrls = Interfaces().Select(i => i.Address).Append("127.0.0.1").Distinct()
                    .Select(ip => new UriBuilder(bound) { Host = ip }.Uri.GetLeftPart(UriPartial.Authority) + "/#" + AccessKey).ToArray();
            }
            host = app;
            leaseTimer = (timeProvider ?? TimeProvider.System).CreateTimer(_ =>
            {
                if (clientLease.Expire()) StatusChanged?.Invoke();
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            StatusChanged?.Invoke();
        }
        catch { await app.DisposeAsync().ConfigureAwait(false); AccessKey = ""; throw; }
    }

    private object State(string id)
    {
        var device = service.GetDevice(id);
        var count = device.Capabilities.InputCount ?? 0;
        return new
        {
            device, controls = service.GetControlState(id), telemetry = service.GetTelemetry(id),
            inputs = Enumerable.Range(1, count).Select(c => service.GetInputProcessing(id, c)).ToArray(),
            outputProcessing = device.Outputs.Select(c => service.GetOutputProcessing(id, c.Number)).ToArray(),
            dynamics = device.Outputs.Select(c => service.GetOutputDynamics(id, c.Number)).ToArray(),
            fir = device.Outputs.Select(c => service.GetFir(id, c.Number)).ToArray(),
            routing = service.GetRouting(id), presets = service.GetDevicePresets(id),
            crossoverOptions = CrossoverFilters.Options,
            liveCrossoverTypes = ObservedInputCrossovers.Options.Select(o => o.Code).ToArray()
        };
    }

    private object Apply(BrowserChange c) => c.Operation switch
    {
        "input" => service.ApplyInputProcessing(service.PlanInputProcessing((c.Processing ?? throw new ArgumentException("Input settings required.")) with { DeviceId = c.DeviceId, Channel = c.Channel }).Id),
        "output-eq" => service.ApplyOutputProcessing(service.PlanOutputProcessing((c.Processing ?? throw new ArgumentException("EQ settings required.")) with { DeviceId = c.DeviceId, Channel = c.Channel }).Id),
        "output" => service.Apply(service.Plan(new(c.DeviceId, c.Channel, c.Value, c.Enabled, c.DelayMs, c.Inverted)).Id),
        "route" => service.ApplyRouting(service.PlanRouting(new(c.DeviceId, c.Input, c.Output, c.Value, c.Enabled)).Id),
        "dynamics" => service.ApplyOutputDynamics(service.PlanOutputDynamics((c.Dynamics ?? throw new ArgumentException("Dynamics settings required.")) with { DeviceId = c.DeviceId, Channel = c.Channel }).Id),
        "fir" => service.SetFirEnabled(c.DeviceId, c.Channel, c.Enabled ?? throw new ArgumentException("FIR state required.")),
        "lock" => service.SetControlState(c.DeviceId, displayLocked: c.Enabled),
        "standby" => service.SetControlState(c.DeviceId, standby: c.Enabled),
        "wink" => service.SetWink(c.DeviceId, c.Enabled ?? false),
        "rename" => service.RenameDevice(c.DeviceId, c.Name ?? ""),
        "load-preset" => c.Confirmed ? service.LoadPresetFile(c.DeviceId, "browser.sd", Convert.FromBase64String(c.Data ?? "")) : throw new ArgumentException("Preset load needs confirmation."),
        "store-preset" => c.Confirmed ? service.StoreDevicePreset(c.DeviceId, c.Slot, c.Name ?? "Preset") : throw new ArgumentException("Store needs confirmation."),
        "recall-preset" => c.Confirmed ? service.RecallDevicePreset(c.DeviceId, c.Slot) : throw new ArgumentException("Recall needs confirmation."),
        "clear-presets" => c.Confirmed ? service.ClearDevicePresets(c.DeviceId) : throw new ArgumentException("Clear needs confirmation."),
        _ => throw new NotSupportedException("Browser operation is unavailable.")
    };

    private static IResult Asset(string name, string type)
    {
        using var stream = typeof(BrowserControlServer).Assembly.GetManifestResourceStream("CvrTool.Web.Assets." + name)!;
        using var reader = new StreamReader(stream);
        return Results.Content(reader.ReadToEnd(), type);
    }

    public async ValueTask DisposeAsync()
    {
        if (host is { } app)
        {
            // Desktop's Closed event drains shutdown synchronously. Host internals must
            // not capture that UI context while waiting for outstanding HTTP requests.
            await Task.Run(async () =>
            {
                await app.StopAsync().ConfigureAwait(false);
                await app.DisposeAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        if (leaseTimer is { } timer) await timer.DisposeAsync().ConfigureAwait(false);
        leaseTimer = null;
        clientLease.Reset();
        host = null; ConnectionUrls = []; Address = null; AccessKey = "";
        StatusChanged?.Invoke();
    }
}

public sealed record BrowserInterface(string Name, string Address)
{
    public override string ToString() => $"{Name} · {Address}";
}
public sealed record BrowserChange(string DeviceId, string Operation, int Channel = 1,
    double? Value = null, bool? Enabled = null, double? DelayMs = null, bool? Inverted = null,
    int Input = 1, int Output = 1, InputProcessingChange? Processing = null,
    OutputDynamicsChange? Dynamics = null, string? Name = null, string? Data = null, int Slot = 1, bool Confirmed = false);

public sealed record BrowserEqDraft(EqualizerBand[] Bands, CrossoverFilter? HighPass, CrossoverFilter? LowPass, bool Enabled)
{
    public void Validate()
    {
        if (Bands is null || Bands.Length > 8 || Bands.Any(b => b is null || b.FrequencyHz is < 20 or > 20000 ||
            b.Q is < 0.1 or > 20 || b.GainDb is < -18 or > 18) ||
            new[] { HighPass, LowPass }.Any(f => f is not null && f.FrequencyHz is < 20 or > 20000))
            throw new ArgumentException("Invalid EQ parameters.");
    }
}
