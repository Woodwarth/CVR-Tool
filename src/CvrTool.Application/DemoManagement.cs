// Application / Session demo devices
// Adds independent model instances and discards their local state on removal.
using CvrTool.Core;

namespace CvrTool.Application;

public interface IDemoManagement
{
    DeviceSnapshot AddDemo(string model);
    void RemoveDemo(string id);
}

public sealed partial class DemoBackend : IDemoManagement
{
    private readonly HashSet<string> assignedDemoIds = ["demo-1004"];
    private sealed record SavedDemo(string Id, string Model, string Name);
    private readonly string? listPath;

    public static string ListPath(string? session)
    {
        var suffix = session is null ? "" : "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(session)));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CvrTool", $"demos{suffix}.json");
    }

    public DemoBackend(string? listPath = null)
    {
        this.listPath = listPath;
        if (listPath is null || !File.Exists(listPath)) return;
        try
        {
            var saved = System.Text.Json.JsonSerializer.Deserialize<SavedDemo[]>(File.ReadAllText(listPath));
            if (saved is null) return;
            var restored = new Dictionary<string, DeviceSnapshot>();
            foreach (var demo in saved)
            {
                var profile = DeviceProfiles.TargetModels.FirstOrDefault(p => p.Model == demo?.Model);
                if (demo is null || profile is null || string.IsNullOrWhiteSpace(demo.Name) || string.IsNullOrEmpty(demo.Id) || !demo.Id.StartsWith("demo-")) continue;
                restored.TryAdd(demo.Id, Create(demo.Id, profile) with { Name = demo.Name });
            }
            devices.Clear();
            routing.Clear();
            foreach (var device in restored.Values)
            {
                devices.Add(device.Id, (device, 0));
                assignedDemoIds.Add(device.Id);
                routing[device.Id] = CreateRouting(device.Capabilities.OutputCount);
            }
        }
        catch (System.Text.Json.JsonException) { /* Keep the default device for an unreadable list. */ }
    }

    private void SaveList(IEnumerable<DeviceSnapshot> next)
    {
        if (listPath is null) return;
        var saved = next.Select(d => new SavedDemo(d.Id, d.Capabilities.Model, d.Name));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(listPath))!);
        var temp = listPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(saved));
            File.Move(temp, listPath, true);
        }
        finally { File.Delete(temp); }
    }

    public DeviceSnapshot AddDemo(string model)
    {
        var profile = DeviceProfiles.TargetModels.Single(p => p.Model == model);
        lock (gate)
        {
            var id = "demo-" + model.Substring(4).ToLowerInvariant();
            if (!assignedDemoIds.Add(id)) id += "-" + Guid.NewGuid().ToString("N");
            var device = Create(id,profile);
            SaveList(devices.Values.Select(e => e.Device).Append(device));
            routing[id] = CreateRouting(profile.OutputCount);
            devices.Add(id,(device,0));
            return Copy(device);
        }
    }

    public void RemoveDemo(string id)
    {
        lock (gate)
        {
            var device = Find(id).Device;
            SaveList(devices.Values.Select(e => e.Device).Where(d => d.Id != id));
            foreach (var output in device.Outputs)
            {
                outputProcessing.Remove((id,output.Number));
                firs.Remove((id,output.Number));
                temperatures.Remove((id, output.Number));
                dynamics.Remove((id,output.Number));
            }
            routing.Remove(id); inputs.Remove(id); controls.Remove(id);
            nativeTemplates.Remove(id); currentPresetNames.Remove(id); presetSlots.Remove(id);
            devices.Remove(id);
        }
    }
}

public sealed partial class AmplifierService
{
    public DeviceSnapshot AddDemo(string model) => ((IDemoManagement)backend).AddDemo(model);
    public void RemoveDemo(string id) => ((IDemoManagement)backend).RemoveDemo(id);
}
