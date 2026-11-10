// MCP / Host lifecycle
// Purpose: run stdio tools against the shared application session.
// Input: no mode flag is required; --session selects a local owner.
// State: --host-session runs a headless shared owner for automation.
// Scope: --demo remains a compatibility-only isolated test mode.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CvrTool.Application;
using CvrTool.Core;

namespace CvrTool.Mcp;

public static class McpHost
{
    public static async Task RunAsync(string[] args)
    {
        string? pipe = null;
        var isolated = false;
        var owner = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--demo":
                    isolated = true;
                    break;
                case "--host-session":
                    owner = true;
                    break;
                case "--session" when i + 1 < args.Length:
                    pipe = args[++i];
                    break;
                default:
                    throw new ArgumentException("Usage: [--session NAME] [--host-session]");
            }
        }
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        if (owner)
        {
            using var backend = new ConnectionBackend();
            using var session = new SessionHost(backend, pipe);
            await builder.Build().RunAsync();
            return;
        }
        builder.Services.AddSingleton<IAmplifierBackend>(isolated ? new DemoBackend() : new SessionBackend(pipe));
        builder.Services.AddSingleton<AmplifierService>();
        builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly(typeof(AmplifierTools).Assembly);
        await builder.Build().RunAsync();
    }
}
