// Desktop / Process entry
// Purpose: open the UI or serve its integrated MCP stdio endpoint.
// Input: --mcp forwards to the shared MCP host; --session selects the owner pipe.
// State: normal UI and AI clients operate on one backend instance.
// Scope: no separate executable or development environment is required by users.
using Avalonia;
using CvrTool.Mcp;

namespace CvrTool.Desktop;

internal static class Program
{
    public static string? SessionName
    {
        get; private set;
    }
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.FirstOrDefault() == "--finish-update")
            args = AppUpdateLifecycle.Complete(args);
        else if (AppUpdateLifecycle.Redirect(args))
            return;
        if (args.FirstOrDefault() == "--mcp")
        {
            McpHost.RunAsync(args.Skip(1).ToArray()).GetAwaiter().GetResult();
            return;
        }
        if (args.FirstOrDefault() == "--host-session")
        {
            McpHost.RunAsync(args).GetAwaiter().GetResult();
            return;
        }
        if (args.Length == 2 && args[0] == "--session")
            SessionName = args[1];
        else if (args.Length != 0)
            throw new ArgumentException("Usage: [--mcp] [--session NAME]");
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(Array.Empty<string>());
    }
}
