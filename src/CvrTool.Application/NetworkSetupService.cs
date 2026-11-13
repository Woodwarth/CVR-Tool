// Application / Network setup — validate and store manual targets without connecting.
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: keep device behavior shared between desktop and MCP.
// Input: explicit operations validated at the application boundary.
// State: revisions protect pending edits; backend owners serialize mutations.
// Scope: transport and physical-validation limits are documented separately.
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CvrTool.Protocol;

namespace CvrTool.Application;
/// <summary>Local configuration only. No sockets, discovery, DNS or device writes.</summary>
public sealed class NetworkSetupService
{
    private sealed record StoredTarget(int SchemaVersion, NetworkTarget Target);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public string ConfigurationPath
    {
        get;
    }

    public NetworkSetupService(string? configurationPath = null)
    {
        ConfigurationPath = Path.GetFullPath(configurationPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CvrTool", "network-target.json"));
    }

    public NetworkConnectionPlan Prepare(NetworkTarget target) => NetworkPreparation.Validate(target);
    public async Task SaveAsync(NetworkTarget target, CancellationToken cancellation = default)
    {
        var plan = Prepare(target);
        var content = JsonSerializer.Serialize(new StoredTarget(1, plan.Target), JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationPath)!);
        var temporaryPath = ConfigurationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporaryPath, ConfigurationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task<NetworkConnectionPlan?> LoadAsync(CancellationToken cancellation = default)
    {
        if (!File.Exists(ConfigurationPath))
            return null;
        await using var file = File.OpenRead(ConfigurationPath);
        using var content = new MemoryStream();
        var buffer = new byte[2048];
        while (true)
        {
            var count = await file.ReadAsync(buffer, cancellation).ConfigureAwait(false);
            if (count == 0)
                break;
            if (content.Length + count > 16384)
                throw new InvalidDataException("Network configuration exceeds the size limit.");
            content.Write(buffer, 0, count);
        }

        StoredTarget? document;
        try
        {
            document = JsonSerializer.Deserialize<StoredTarget>(content.ToArray(), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Network configuration contains invalid JSON.", ex);
        }

        if (document is null || document.SchemaVersion != 1 || document.Target is null)
            throw new InvalidDataException("Unknown network configuration format.");
        return Prepare(document.Target);
    }
}
