// Web / Local browser control
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

public sealed class BrowserControlServer(AmplifierService service) : IAsyncDisposable
{
    private WebApplication? host;
    private readonly SemaphoreSlim writes = new(1, 1);
    public string AccessKey { get; private set; } = "";
    public string? Address { get; private set; }
    public string? ConnectionUrl => Address is null ? null : Address + "/#" + AccessKey;
    public event Action? Changed;

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
        if (!IsLocalAddress(address)) throw new ArgumentException("Choose a local IPv4 network address.");
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
            if (requestedHost != address.ToString() && !(IPAddress.IsLoopback(address) && requestedHost == "localhost"))
            { context.Response.StatusCode = 403; return; }
            if (context.Request.Headers.Origin is { Count: > 0 } origin &&
                !string.Equals(origin.ToString(), $"http://{context.Request.Host}", StringComparison.OrdinalIgnoreCase))
            { context.Response.StatusCode = 403; return; }
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                var supplied = context.Request.Headers.Authorization.ToString();
                if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied), System.Text.Encoding.UTF8.GetBytes("Bearer " + AccessKey)))
                { context.Response.StatusCode = 401; return; }
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
        app.MapGet("/api/preset", (string id) => Results.File(service.ExportNativePreset(id), "application/octet-stream", "CVR-preset.sd"));
        app.MapPost("/api/change", async (BrowserChange change, HttpContext context) =>
        {
            await writes.WaitAsync(context.RequestAborted);
            try
            {
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
            await app.StartAsync();
            Address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');
            host = app;
        }
        catch { await app.DisposeAsync(); AccessKey = ""; throw; }
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
        if (host is { } app) { await app.StopAsync(); await app.DisposeAsync(); }
        host = null; Address = null; AccessKey = "";
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
