// Desktop / Update handoff
// Purpose: restart into a staged self-contained version after the old owner exits.
// Input: managed installation pointer and explicit internal handoff arguments.
// State: source builds keep their own executable; existing installs are retained.
// Scope: no shell scripts, administrator rights or additional runtime are required.
using System.Diagnostics;
using CvrTool.Application;

namespace CvrTool.Desktop;

internal static class AppUpdateLifecycle
{
    public static string Version => typeof(AppUpdateLifecycle).Assembly.GetName().Version!.ToString(3);
    public static bool IsPackaged => File.Exists(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "coreclr.dll" : "libcoreclr.dylib"));
    public static Process Start(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)!, CreateNoWindow = true };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new InvalidOperationException("Could not start the updated application.");
    }
    public static bool Redirect(string[] arguments)
    {
        if (!IsPackaged)
            return false;
        string? newer;
        try
        {
            newer = new UpdateInstallation().NewerInstalledExecutable(Version, GitHubUpdates.CurrentRuntime);
        }
        catch (NotSupportedException) { return false; }
        if (newer is null)
            return false;
        using var child = Start(newer, arguments);
        if (arguments.FirstOrDefault() is "--mcp" or "--host-session")
        {
            child.WaitForExit();
            Environment.ExitCode = child.ExitCode;
        }
        return true;
    }
    public static string[] Complete(string[] arguments)
    {
        if (arguments.Length < 2 || !int.TryParse(arguments[1], out var pid) || pid <= 0 || pid == Environment.ProcessId)
            throw new ArgumentException("Invalid update handoff.");
        try
        {
            Console.Error.WriteLine("CVR update: waiting for previous owner to exit.");
            using var previous = Process.GetProcessById(pid);
            if (!previous.WaitForExit(120000))
                throw new TimeoutException("The previous app has not exited; launch the update again after closing it.");
            Console.Error.WriteLine("CVR update: previous owner exited.");
        }
        catch (ArgumentException) { /* The previous owner has already exited. */ }
        return arguments.Skip(2).ToArray();
    }
}
