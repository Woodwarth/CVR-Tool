// Checks / Native routing coordinates
// Purpose: keep input/output coordinates consistent with captured output-major records.
// Units: output records stride 515 bytes; input route entries stride five bytes.
// State: off-diagonal gain edits preserve transposed cells and opaque settings.
// Scope: synthetic native fixtures only; physical captures stay private.
using CvrTool.Application;

static class RoutingAxisChecks
{
    public static void Run(Action<bool, string> check)
    {
        var service = new AmplifierService(new DemoBackend());
        const string id = "demo-1004";
        var native = service.ExportNativePreset(id);
        var input2OutputA = 36 + 24 + 5;
        var input1OutputB = 36 + 515 + 24;
        BitConverter.GetBytes(.2f).CopyTo(native, input2OutputA);
        native[input2OutputA + 4] = 0;
        BitConverter.GetBytes(-3f).CopyTo(native, input1OutputB);
        native[input1OutputB + 4] = 1;
        var decoded = NativePresetCodec.Decode(native);
        var routes = decoded.Routing!;
        check(routes.Single(c => c.Input == 2 && c.Output == 1) is { Muted: true } route &&
            Math.Abs(route.GainDb - .2) < .000001, "Native output A record contains input 2 route at its second entry");
        check(routes.Single(c => c.Input == 1 && c.Output == 2) is { GainDb: -3, Muted: false }, "Transposed native route remains a separate cell");
        check(NativePresetCodec.Encode(decoded).SequenceEqual(native), "Off-diagonal native routes round-trip byte-for-byte");
        service.LoadPresetFile(id, "matrix.sd", native);
        service.ApplyRouting(service.PlanRouting(new(id, 2, 1, GainDb: -1.25)).Id);
        var expected = native.ToArray(); BitConverter.GetBytes(-1.25f).CopyTo(expected, input2OutputA);
        check(service.ExportNativePreset(id).SequenceEqual(expected), "Off-diagonal gain edit changes only the captured output-major entry");
        service.ApplyRouting(service.PlanRouting(new(id, 2, 1, Muted: false)).Id);
        expected[input2OutputA + 4] = 1;
        check(service.ExportNativePreset(id).SequenceEqual(expected), "Off-diagonal unmute changes only the matching route flag");
    }
}
