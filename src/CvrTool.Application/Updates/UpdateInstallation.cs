// Application / Managed update installation
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: stage verified packages beside the existing app and remember the active version.
// Input: bounded ZIP/tar.gz archives; links and escaping paths are rejected.
// State: installation completes before the version pointer is changed.
// Scope: existing installations are retained; development builds never redirect themselves.
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;

namespace CvrTool.Application;

public sealed class UpdateInstallation(string? root = null)
{
    public string Root { get; } = Path.GetFullPath(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CvrTool", "updates"));
    private sealed record InstalledVersion(string Version, string Runtime, string Sha256);
    public static string Executable(string directory, string runtime) => Path.Combine(directory, runtime.StartsWith("win-") ? "desktop/CvrTool.Desktop.exe" : "CVR Tool.app/Contents/MacOS/CvrTool.Desktop");
    private string VersionDirectory(string version, string runtime) => Path.Combine(Root, "versions", GitHubUpdates.ParseVersion(version).ToString(), runtime);
    public async Task<string> StageAsync(AppUpdate update, string package, CancellationToken token = default)
    {
        await GitHubUpdates.VerifyAsync(update, package, token).ConfigureAwait(false);
        var destination = VersionDirectory(update.Version, update.Runtime);
        if (Directory.Exists(destination))
        {
            var existing = JsonSerializer.Deserialize<InstalledVersion>(await File.ReadAllTextAsync(Path.Combine(destination, "installation.json"), token).ConfigureAwait(false));
            if (existing != new InstalledVersion(update.Version, update.Runtime, update.Sha256) || !File.Exists(Executable(destination, update.Runtime)))
                throw new InvalidDataException("Existing update installation does not match this package.");
            return Executable(destination, update.Runtime);
        }
        var staging = Path.Combine(Root, "staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            await Task.Run(() => Extract(package, staging, update.Runtime, token), token).ConfigureAwait(false);
            var executable = Executable(staging, update.Runtime);
            if (!File.Exists(executable))
                throw new InvalidDataException("The update package has no desktop executable.");
            var assembly = update.Runtime.StartsWith("win-") ? Path.ChangeExtension(executable, ".dll") : executable + ".dll";
            if (System.Reflection.AssemblyName.GetAssemblyName(assembly).Version?.ToString(3) != update.Version)
                throw new InvalidDataException("Package application version does not match its release.");
            var runtimeConfig = Path.ChangeExtension(executable, ".runtimeconfig.json");
            if (!update.Runtime.StartsWith("win-"))
                runtimeConfig = executable + ".runtimeconfig.json";
            using (var config = JsonDocument.Parse(await File.ReadAllTextAsync(runtimeConfig, token).ConfigureAwait(false)))
                if (!config.RootElement.GetProperty("runtimeOptions").TryGetProperty("includedFrameworks", out _))
                    throw new InvalidDataException("Update is not self-contained.");
            await File.WriteAllTextAsync(Path.Combine(staging, "installation.json"), JsonSerializer.Serialize(new InstalledVersion(update.Version, update.Runtime, update.Sha256)), token).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);
            return Executable(destination, update.Runtime);
        }
        finally
        {
            // Only remove the unique staging directory created above, under this update root.
            if (Directory.Exists(staging) && Contained(Path.Combine(Root, "staging"), staging))
                Directory.Delete(staging, true);
        }
    }
    public void Activate(AppUpdate update)
    {
        GitHubUpdates.Validate(update);
        if (!File.Exists(Executable(VersionDirectory(update.Version, update.Runtime), update.Runtime)))
            throw new InvalidOperationException("Stage the update before activation.");
        var temporary = Path.Combine(Root, "current-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(temporary, JsonSerializer.Serialize(new InstalledVersion(update.Version, update.Runtime, update.Sha256)));
        File.Move(temporary, Path.Combine(Root, "current.json"), true);
    }
    public string? NewerInstalledExecutable(string currentVersion, string runtime)
    {
        try
        {
            var pointer = Path.Combine(Root, "current.json");
            if (!File.Exists(pointer) || new FileInfo(pointer).Length > 4096)
                return null;
            var installed = JsonSerializer.Deserialize<InstalledVersion>(File.ReadAllText(pointer));
            if (installed is null || installed.Runtime != runtime || GitHubUpdates.ParseVersion(installed.Version) <= GitHubUpdates.ParseVersion(currentVersion))
                return null;
            var directory = VersionDirectory(installed.Version, runtime);
            var executable = Executable(directory, runtime);
            if (!File.Exists(executable) || !File.Exists(Path.Combine(directory, "installation.json")))
                return null;
            var manifest = JsonSerializer.Deserialize<InstalledVersion>(File.ReadAllText(Path.Combine(directory, "installation.json")));
            var assembly = runtime.StartsWith("win-") ? Path.ChangeExtension(executable, ".dll") : executable + ".dll";
            return manifest == installed && System.Reflection.AssemblyName.GetAssemblyName(assembly).Version?.ToString(3) == installed.Version ? executable : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or BadImageFormatException or UnauthorizedAccessException) { return null; }
    }
    private static bool Contained(string root, string path) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static void Extract(string package, string directory, string runtime, CancellationToken token)
    {
        var names = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        long expanded = 0;
        int entries = 0;
        string Target(string name, long size)
        {
            token.ThrowIfCancellationRequested();
            if (++entries > 10000 || size < 0 || (expanded += size) > 2147483648)
                throw new InvalidDataException("Update archive exceeds extraction limits.");
            name = name.Replace('\\', '/');
            while (name.StartsWith("./", StringComparison.Ordinal))
                name = name[2..];
            if (string.IsNullOrEmpty(name) || name == ".")
                return directory;
            if (name.Contains(':') || name.Split('/').Contains(".."))
                throw new InvalidDataException("Unsafe update archive path.");
            var target = Path.GetFullPath(Path.Combine(directory, name));
            if (!Contained(directory, target) || !names.Add(target.TrimEnd(Path.DirectorySeparatorChar)))
                throw new InvalidDataException("Escaping or duplicate update archive path.");
            return target;
        }
        if (runtime.StartsWith("win-"))
        {
            using var zip = ZipFile.OpenRead(package);
            foreach (var entry in zip.Entries)
            {
                if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                    throw new InvalidDataException("Archive links are not supported.");
                var target = Target(entry.FullName, entry.Length);
                if (entry.FullName.EndsWith('/'))
                    Directory.CreateDirectory(target);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target);
                }
            }
        }
        else
        {
            using var input = File.OpenRead(package);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            TarEntry? entry;
            while ((entry = tar.GetNextEntry()) is not null)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.Directory))
                    throw new InvalidDataException("Unsupported archive entry; links are not allowed.");
                var target = Target(entry.Name, entry.Length);
                if (entry.EntryType == TarEntryType.Directory)
                    Directory.CreateDirectory(target);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using (var output = new FileStream(target, FileMode.CreateNew))
                        entry.DataStream?.CopyTo(output);
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(target, entry.Mode);
                }
            }
        }
    }
}
