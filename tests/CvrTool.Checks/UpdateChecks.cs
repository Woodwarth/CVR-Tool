// Checks / GitHub updates
// Purpose: exercise metadata, transfer, staging and failure behavior without launching code.
// Input: synthetic HTTP responses and isolated archives under artifacts.
// State: the user's installation pointer is never changed by these checks.
// Scope: native package extraction is additionally exercised by each release job.
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CvrTool.Application;

internal static class UpdateChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var version = typeof(AmplifierService).Assembly.GetName().Version!.ToString(3);
        var package = Package();
        var release = new
        {
            tag_name = "v" + version,
            draft = false,
            prerelease = false,
            assets = new[] { new { id = 123, name = $"CVR-Tool-{version}-win-x64.zip", state = "uploaded", browser_download_url = $"https://github.com/Woodwarth/CVR-Tool/releases/download/v{version}/CVR-Tool-{version}-win-x64.zip", digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(package)), size = package.Length } }
        };
        var handler = new Replies { Metadata = JsonSerializer.Serialize(release), Package = package };
        using var http = new HttpClient(handler);
        var updates = new GitHubUpdates(http);
        var update = await updates.CheckAsync("0.0.0", "win-x64");
        check(update is not null && update.Version == version && update.Runtime == "win-x64", "Updater selects the exact stable native asset");
        check(await updates.CheckAsync(version, "win-x64") is null, "Current release is not offered as an update");
        var root = Path.GetFullPath(Path.Combine("artifacts", "update-tests-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var file = await updates.DownloadAsync(update!, Path.Combine(root, "downloads"));
        check(File.Exists(file) && new FileInfo(file).Length == package.Length, "Download verifies size and SHA-256 before committing the archive");
        const string privateSource = "example/private-releases";
        var restricted = new Replies
        {
            Metadata = handler.Metadata.Replace(GitHubUpdates.Repository, privateSource),
            Package = package,
            ExpectedToken = "test-only"
        };
        using var privateHttp = new HttpClient(restricted);
        var authenticated = new GitHubUpdates(privateHttp, privateSource, restricted.ExpectedToken);
        var privateUpdate = await authenticated.CheckAsync("0.0.0", "win-x64");
        var privateFile = await authenticated.DownloadAsync(privateUpdate!, Path.Combine(root, "private"));
        check(File.Exists(privateFile) && restricted.LastPath == "/repos/example/private-releases/releases/assets/123",
            "Private updates use the authenticated asset API and verify the package");
        await Reject(async () => await updates.DownloadAsync(privateUpdate!, root), check, "Update cannot switch repository after checking");
        restricted.Status = HttpStatusCode.NotFound;
        try
        {
            await authenticated.CheckAsync(version, "win-x64");
            throw new Exception("Hidden repository reported as current");
        }
        catch (InvalidOperationException) { check(true, "Hidden releases report missing access"); }
        var installer = new UpdateInstallation(Path.Combine(root, "app"));
        var executable = await installer.StageAsync(update!, file);
        check(File.Exists(executable) && installer.NewerInstalledExecutable("0.0.0", "win-x64") is null, "Staging does not activate or replace the current installation");
        installer.Activate(update!);
        check(installer.NewerInstalledExecutable("0.0.0", "win-x64") == executable && installer.NewerInstalledExecutable(version, "win-x64") is null, "Activation selects newer installed versions without redirecting the same version");
        check(await installer.StageAsync(update!, file) == executable, "Repeated staging reuses the verified version rather than overwriting it");
        handler.Package = package.ToArray();
        handler.Package[^1] ^= 1;
        await Reject(async () => await updates.DownloadAsync(update!, Path.Combine(root, "bad-download")), check, "Modified downloads fail integrity checks");
        check(!Directory.EnumerateFiles(Path.Combine(root, "bad-download")).Any(), "Failed transfers remove temporary files");
        handler.Package = package;
        foreach (var path in new[] { "../escape.txt", "/absolute.txt", "desktop/../../escape.txt" })
        {
            var unsafePackage = Package(path);
            var unsafeFile = Path.Combine(root, Guid.NewGuid() + ".zip");
            await File.WriteAllBytesAsync(unsafeFile, unsafePackage);
            var unsafeUpdate = update! with
            {
                Size = unsafePackage.Length,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(unsafePackage))
            };
            await Reject(async () => await new UpdateInstallation(Path.Combine(root, "unsafe-" + Guid.NewGuid())).StageAsync(unsafeUpdate, unsafeFile), check, "Unsafe archive path rejected: " + path);
        }
        handler.Metadata = handler.Metadata.Replace("\"draft\":false", "\"draft\":true");
        check(await updates.CheckAsync("0.0.0", "win-x64") is null, "Draft releases are never installed");
        handler.Metadata = JsonSerializer.Serialize(release).Replace("\"prerelease\":false", "\"prerelease\":true");
        check(await updates.CheckAsync("0.0.0", "win-x64") is null, "Prereleases are not silently selected");
        handler.Metadata = JsonSerializer.Serialize(release).Replace("github.com/Woodwarth/CVR-Tool/releases/download", "example.com/download");
        await Reject(async () => await updates.CheckAsync("0.0.0", "win-x64"), check, "Foreign asset URLs are rejected");
        var peak = new CvrTool.Core.EqualizerBand(1, 1000, 6, 1.41);
        check(Math.Abs(EqualizerResponse.GainDb(peak, 1000) - 6) < 1e-9, "EQ response equals gain at band centre");
        check(new[] { 20d, 100, 1000, 10000, 20000 }.All(f => Math.Abs(EqualizerResponse.GainDb(peak with { GainDb = 0 }, f)) < 1e-9), "Zero-gain peak EQ is flat");
        check(new[] { 50d, 500, 5000 }.All(f => Math.Abs(EqualizerResponse.GainDb(peak, f) + EqualizerResponse.GainDb(peak with { GainDb = -6 }, f)) < 1e-9), "Matching peak boost and cut cancel analytically");
        check(EqualizerResponse.GainDb(peak with
        {
            Enabled = false
        }, 1000) == 0, "Bypassed EQ contributes no response");
        try
        {
            EqualizerResponse.GainDb(peak with
            {
                FilterType = 99
            }, 1000);
            throw new Exception("Unknown type accepted");
        }
        catch (NotSupportedException) { check(true, "Unknown device filter types are not plotted as peaks"); }
    }
    private static async Task Reject(Func<Task> action, Action<bool, string> check, string message)
    {
        try
        {
            await action();
        }
        catch (InvalidDataException) { check(true, message); return; }
        throw new Exception(message);
    }
    private static byte[] Package(string? unsafePath = null)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            void File(string name, byte[] bytes)
            {
                using var output = zip.CreateEntry(name).Open();
                output.Write(bytes);
            }
            File("desktop/CvrTool.Desktop.exe", [1, 2, 3]);
            File("desktop/CvrTool.Desktop.dll", System.IO.File.ReadAllBytes(typeof(AmplifierService).Assembly.Location));
            File("desktop/CvrTool.Desktop.runtimeconfig.json", Encoding.UTF8.GetBytes("{\"runtimeOptions\":{\"includedFrameworks\":[]}}"));
            if (unsafePath is not null)
                File(unsafePath, [1]);
        }
        return memory.ToArray();
    }
    public static async Task StageNativePackage(string[] args)
    {
        var at = Array.IndexOf(args, "--update-package");
        if (at < 0)
            return;
        var package = Path.GetFullPath(args[at + 1]);
        var runtime = args[at + 2];
        var version = args[at + 3];
        await using var stream = System.IO.File.OpenRead(package);
        var size = stream.Length;
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
        var update = new AppUpdate(version, runtime, Path.GetFileName(package), new($"https://github.com/Woodwarth/CVR-Tool/releases/download/v{version}/{Path.GetFileName(package)}"), hash, size);
        var installer = new UpdateInstallation(Path.GetFullPath("artifacts/update-install-check"));
        var executable = await installer.StageAsync(update, package);
        Console.WriteLine("PASS: native self-contained update staged at " + executable);
    }
    private sealed class Replies : HttpMessageHandler
    {
        public required string Metadata; public required byte[] Package;
        public string? ExpectedToken;
        public string? LastPath;
        public HttpStatusCode Status = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (!request.Headers.UserAgent.Any())
                throw new Exception("Missing updater User-Agent");
            LastPath = request.RequestUri!.AbsolutePath;
            if (request.Headers.Authorization?.Parameter != ExpectedToken ||
                (ExpectedToken is not null && (request.RequestUri.Host != "api.github.com" ||
                 request.Headers.Authorization?.Scheme != "Bearer")))
                throw new Exception("Unexpected update credentials");
            if (LastPath.Contains("/assets/") && !request.Headers.Accept.Any(a => a.MediaType == "application/octet-stream"))
                throw new Exception("Expected binary asset request");
            return Task.FromResult(new HttpResponseMessage(Status) { RequestMessage = request, Content = LastPath.EndsWith("/latest") ? new StringContent(Metadata) : new ByteArrayContent(Package) });
        }
    }
}
