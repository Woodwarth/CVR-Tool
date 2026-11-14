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
    public DeviceSnapshot AddDemo(string model)
    {
        var profile = DeviceProfiles.TargetModels.Single(p => p.Model == model);
        lock (gate)
        {
            var id = "demo-" + model.Substring(4).ToLowerInvariant();
            if (!assignedDemoIds.Add(id)) id += "-" + Guid.NewGuid().ToString("N");
            var device = Create(id,profile);
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
