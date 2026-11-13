// Application / GitHub release updates
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: find stable releases and download the native package with integrity checks.
// Input: public metadata from the fixed project repository; no account/token needed.
// State: checks are explicit; downloading never changes the running installation.
// Scope: SHA-256 checks integrity; packages retain their existing signing status.
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CvrTool.Application;

public sealed record AppUpdate(string Version, string Runtime, string AssetName, Uri DownloadUrl, string Sha256, long Size);

public sealed class GitHubUpdates
{
    public const string Repository = "Woodwarth/CVR-Tool";
    public const string ReleasePage = "https://github.com/" + Repository + "/releases/latest";
    public static string CurrentRuntime => OperatingSystem.IsWindows() ? RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "win-x64",
        Architecture.Arm64 => "win-arm64",
        _ => throw new NotSupportedException("No update package for this Windows architecture.")
    } : OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : throw new NotSupportedException("Updates support Windows x64/ARM64 and Apple Silicon.");
    private readonly HttpClient http;
    public GitHubUpdates(HttpClient? client = null) => http = client ?? SharedClient;
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromMinutes(15) };
    public static Version ParseVersion(string value)
    {
        if (!Regex.IsMatch(value, "^v?[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant) || !Version.TryParse(value.TrimStart('v'), out var version))
            throw new InvalidDataException("Unsupported release version.");
        return version;
    }
    private static HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CVR-Tool", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        return request;
    }
    public async Task<AppUpdate?> CheckAsync(string currentVersion, string runtime, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        token = timeout.Token;
        using var request = Request(new("https://api.github.com/repos/" + Repository + "/releases/latest"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        using var data = new MemoryStream();
        await CopyBounded(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), data, 2 * 1024 * 1024, null, token).ConfigureAwait(false);
        using var json = JsonDocument.Parse(data.ToArray());
        var release = json.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            return null;
        var tag = release.GetProperty("tag_name").GetString()!;
        var version = ParseVersion(tag);
        if (version <= ParseVersion(currentVersion))
            return null;
        if (runtime is not ("win-x64" or "win-arm64" or "osx-arm64"))
            throw new NotSupportedException("Unsupported update runtime.");
        var assetName = $"CVR-Tool-{version}-{runtime}." + (runtime.StartsWith("win-") ? "zip" : "tar.gz");
        var candidates = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == assetName && a.GetProperty("state").GetString() == "uploaded").ToArray();
        if (candidates.Length != 1)
            throw new InvalidDataException("This release has no unique package for your platform.");
        var asset = candidates[0];
        var expected = $"https://github.com/{Repository}/releases/download/{tag}/{assetName}";
        if (asset.GetProperty("browser_download_url").GetString() != expected)
            throw new InvalidDataException("Unexpected update download URL.");
        var digest = asset.GetProperty("digest").GetString() ?? "";
        if (!Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Release package has no SHA-256 digest.");
        var size = asset.GetProperty("size").GetInt64();
        if (size is <= 0 or > 536870912)
            throw new InvalidDataException("Unexpected update package size.");
        return new(version.ToString(), runtime, assetName, new(expected), digest[7..].ToLowerInvariant(), size);
    }
    public async Task<string> DownloadAsync(AppUpdate update, string directory, IProgress<double>? progress = null, CancellationToken token = default)
    {
        Validate(update);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, update.AssetName);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var request = Request(update.DownloadUrl);
            request.Headers.Accept.Clear();
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme is { } scheme && scheme != "https")
                throw new InvalidDataException("Update transport must use HTTPS.");
            if (response.Content.Headers.ContentLength is { } length && length != update.Size)
                throw new InvalidDataException("Package size changed.");
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await CopyBounded(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), output, update.Size, progress, token).ConfigureAwait(false);
            await VerifyAsync(update, temporary, token).ConfigureAwait(false);
            File.Move(temporary, destination, true);
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Validate(AppUpdate update)
    {
        var version = ParseVersion(update.Version);
        if (update.Runtime is not ("win-x64" or "win-arm64" or "osx-arm64"))
            throw new InvalidDataException("Invalid update runtime.");
        var name = $"CVR-Tool-{version}-{update.Runtime}." + (update.Runtime.StartsWith("win-") ? "zip" : "tar.gz");
        if (update.AssetName != name || update.DownloadUrl != new Uri($"https://github.com/{Repository}/releases/download/v{version}/{name}") ||
            !Regex.IsMatch(update.Sha256, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant) || update.Size is <= 0 or > 536870912)
            throw new InvalidDataException("Invalid update identity.");
    }
    public static async Task VerifyAsync(AppUpdate update, string file, CancellationToken token = default)
    {
        Validate(update);
        await using var input = File.OpenRead(file);
        if (input.Length != update.Size || !Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update integrity check failed. The package will not be installed.");
    }
    private static async Task CopyBounded(Stream input, Stream output, long maximum, IProgress<double>? progress, CancellationToken token)
    {
        await using (input)
        {
            var buffer = new byte[81920];
            long total = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                total += count;
                if (total > maximum)
                    throw new InvalidDataException("Download exceeds expected size.");
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                progress?.Report((double)total / maximum);
            }
        }
    }
}
