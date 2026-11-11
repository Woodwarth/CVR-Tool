// Application / Native CVR preset codec
// Purpose: read/write the B-series 118/119 .sd layouts evidenced by local exports.
// Units: packed 515-byte channel records and 512-tap FIR blocks.
// State: opaque source bytes preserve settings not exposed by this application.
// Scope: reject unknown firmware/layouts; no general object deserialization.
using System.Text;
using CvrTool.Core;

namespace CvrTool.Application;

public static class NativePresetCodec
{
    public const int MaximumBytes = 1024 * 1024;
    public static (int Count, int SyncLength, int FirLength, string Model) Inspect(byte[] file)
    {
        if (file.Length < 32 || file.Length > MaximumBytes || file[5] != 'B' || file[2] is not ((byte)'2' or (byte)'4') || file[4] != file[2])
            throw new NotSupportedException("Native preset is not a supported two-/four-channel B-series layout.");
        var version = Encoding.ASCII.GetString(file, 9, 6);
        if (version is not ("106118" or "106119"))
            throw new NotSupportedException("This native firmware version is not supported.");
        var count = file[4] - '0';
        var syncLength = version == "106119" ? 2687 : 2232;
        var firLength = 0;
        if (file.Length != syncLength)
        {
            var footer = ProcessingWire.Text(file[^16..]);
            if (!footer.StartsWith("FIR==", StringComparison.Ordinal) || !int.TryParse(footer[5..], out firLength) || firLength is not (2048 or 2080) || file.Length != syncLength + count * firLength + 16)
                throw new InvalidDataException("Native preset has an inconsistent FIR footer or length.");
        }
        return (count, syncLength, firLength, ProcessingWire.Text(file[16..32]));
    }
    public static PresetDocument Decode(byte[] file, bool isDemo = false)
    {
        var info = Inspect(file);
        var outputs = new List<ChannelState>();
        var inputs = new List<InputProcessingState>();
        var processing = new List<InputProcessingState>();
        var routes = new List<RoutingCellState>();
        var firs = new List<FirState>();
        for (var n = 0; n < info.Count; n++)
        {
            var offset = 36 + n * 515;
            var ch = n + 1;
            outputs.Add(new(ch, ((char)('A' + n)).ToString(), ProcessingWire.Float(file, offset + 44), file[offset + 48] != 0, ProcessingWire.Float(file, offset + 54), file[offset + 58] != 0));
            inputs.Add(ProcessingWire.DecodeEq(ch, ProcessingWire.Float(file, offset + 50), file[(offset + 85)..(offset + 226)]));
            processing.Add(ProcessingWire.DecodeEq(ch, ProcessingWire.Float(file, offset + 54), file[(offset + 226)..(offset + 367)]));
            for (var output = 0; output < info.Count; output++)
                routes.Add(new(ch, output + 1, ProcessingWire.Float(file, offset + 24 + output * 5), file[offset + 28 + output * 5] != 0));
            if (info.FirLength > 0)
                firs.Add(ProcessingWire.DecodeFir(ch, file[(info.SyncLength + n * info.FirLength)..(info.SyncLength + (n + 1) * info.FirLength)], file[offset + 368] == 0));
        }
        return new(4, info.Model, isDemo, outputs, null, routes, inputs, processing, info.FirLength > 0 ? firs : null, file.ToArray());
    }
    public static byte[] Encode(PresetDocument preset)
    {
        var count = preset.Outputs.Count;
        if (count is not (2 or 4))
            throw new ArgumentException("A native preset requires two or four outputs.");
        byte[] sync;
        int firLength;
        if (preset.NativeData is { } original)
        {
            var info = Inspect(original);
            if (info.Count != count || info.Model != preset.Model)
                throw new ArgumentException("Native template model/topology mismatch.");
            sync = original[..info.SyncLength];
            firLength = info.FirLength == 2048 ? 2048 : 2080;
        }
        else
        {
            sync = new byte[2687];
            firLength = 2080;
            // These are local reference profiles, not a substitute for a connected firmware header.
            var gain = preset.Model == "DSP-1502" ? 41 : 39;
            ProcessingWire.PutText(sync, 0, $"{gain}{count}0{count}B06-106119-{preset.Model}", 32);
            for (var n = 0; n < 4; n++)
            {
                sync[36 + n * 515 + 368] = 1;
                for (var band = 0; band < 10; band++)
                {
                    ProcessingWire.Put(sync, 36 + n * 515 + 85 + band * 14 + 9, 1);
                    ProcessingWire.Put(sync, 36 + n * 515 + 226 + band * 14 + 9, 1);
                }
            }
        }
        for (var n = 0; n < count; n++)
        {
            var offset = 36 + n * 515;
            var output = preset.Outputs.Single(c => c.Number == n + 1);
            ProcessingWire.Put(sync, offset + 44, output.GainDb);
            sync[offset + 48] = (byte)(output.Muted ? 1 : 0);
            ProcessingWire.Put(sync, offset + 54, output.DelayMs);
            sync[offset + 58] = (byte)(output.Inverted ? 1 : 0);
            if (preset.Inputs?.SingleOrDefault(c => c.Channel == n + 1) is { } input)
            {
                ProcessingWire.Put(sync, offset + 50, input.DelayMs);
                ProcessingWire.EncodeEq(input, sync[(offset + 85)..(offset + 226)]).CopyTo(sync, offset + 85);
            }
            if (preset.OutputProcessing?.SingleOrDefault(c => c.Channel == n + 1) is { } eq)
                ProcessingWire.EncodeEq(eq, sync[(offset + 226)..(offset + 367)]).CopyTo(sync, offset + 226);
            foreach (var route in preset.Routing?.Where(r => r.Input == n + 1) ?? [])
            {
                ProcessingWire.Put(sync, offset + 24 + (route.Output - 1) * 5, route.GainDb);
                sync[offset + 28 + (route.Output - 1) * 5] = (byte)(route.Muted ? 1 : 0);
            }
            if (preset.Firs?.SingleOrDefault(f => f.Channel == n + 1) is { } fir)
                sync[offset + 368] = (byte)(fir.Enabled ? 0 : 1);
        }
        using var data = new MemoryStream();
        data.Write(sync);
        for (var n = 0; n < count; n++)
        {
            var fir = preset.Firs?.SingleOrDefault(f => f.Channel == n + 1);
            if (fir is not null)
                data.Write(ProcessingWire.EncodeFir(fir, firLength == 2080));
            else if (preset.NativeData is { } originalFir && Inspect(originalFir).FirLength > 0)
            {
                var info = Inspect(originalFir);
                data.Write(originalFir.AsSpan(info.SyncLength + n * info.FirLength, info.FirLength));
            }
            else
                data.Write(ProcessingWire.EncodeFir(new(n + 1, "", new double[] { 1 }), firLength == 2080));
        }
        var footer = new byte[16];
        Encoding.ASCII.GetBytes("FIR==" + firLength).CopyTo(footer, 0);
        data.Write(footer);
        return data.ToArray();
    }
}
