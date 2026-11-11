// Application / Processing payloads
// Purpose: map packed EQ and FIR blocks independently of UI and transport.
// Units: little-endian floats, ms, Hz, dB, Q; 512 FIR taps at 48 kHz.
// State: encoding patches a supplied template to preserve untouched bytes.
// Scope: ten-record EQ and 2048/2080-byte FIR layouts only.
using System.Text;
using CvrTool.Core;

namespace CvrTool.Application;

public static class ProcessingWire
{
    public static double Float(byte[] data, int offset)
    {
        var value = BitConverter.ToSingle(data, offset);
        if (!float.IsFinite(value))
            throw new InvalidDataException("Non-finite processing value.");
        return value;
    }
    public static void Put(byte[] data, int offset, double value) => BitConverter.GetBytes((float)value).CopyTo(data, offset);
    public static InputProcessingState DecodeEq(int channel, double delay, byte[] raw)
    {
        if (raw.Length != 141 || raw[140] > 1)
            throw new NotSupportedException("Unsupported EQ layout.");
        int Type(int offset) => raw[offset] < 128 ? raw[offset] : 255 - raw[offset];
        CrossoverFilter Crossover(int offset) => new(Float(raw, offset + 5), Type(offset), raw[offset] < 128);
        var bands = Enumerable.Range(1, 8).Select(n => new EqualizerBand(n, Float(raw, n * 14 + 5), Float(raw, n * 14 + 1), Float(raw, n * 14 + 9), raw[n * 14] < 128, Type(n * 14))).ToArray();
        return new(channel, delay, raw[140] == 0, bands, Crossover(0), Crossover(126));
    }
    public static byte[] EncodeEq(InputProcessingState state, byte[]? template = null)
    {
        var raw = template?.ToArray() ?? new byte[141];
        if (raw.Length != 141)
            throw new InvalidDataException("Invalid EQ template.");
        void Type(int offset, int type, bool enabled) => raw[offset] = unchecked((byte)(enabled ? type : -type - 1));
        foreach (var band in state.Bands)
        {
            var offset = band.Number * 14;
            Type(offset, band.FilterType, band.Enabled);
            Put(raw, offset + 1, band.GainDb);
            Put(raw, offset + 5, band.FrequencyHz);
            Put(raw, offset + 9, band.Q);
        }
        void Crossover(int offset, CrossoverFilter? filter)
        {
            if (filter is null)
                return;
            Type(offset, filter.FilterType, filter.Enabled);
            Put(raw, offset + 5, filter.FrequencyHz);
            if (template is null)
                Put(raw, offset + 9, 1);
        }
        Crossover(0, state.HighPass);
        Crossover(126, state.LowPass);
        raw[140] = (byte)(state.EqEnabled ? 0 : 1);
        return raw;
    }
    public static FirState DecodeFir(int channel, byte[] raw, bool enabled)
    {
        if (raw.Length is not (2048 or 2080))
            throw new NotSupportedException("FIR must contain 512 float taps, optionally preceded by a 32-byte name.");
        var offset = raw.Length == 2080 ? 32 : 0;
        return new(channel, offset == 32 ? Text(raw[..32]) : "", Enumerable.Range(0, 512).Select(n => Float(raw, offset + n * 4)).ToArray(), enabled);
    }
    public static byte[] EncodeFir(FirState state, bool includeName = true)
    {
        var raw = new byte[includeName ? 2080 : 2048];
        if (includeName)
            PutText(raw, 0, state.Name, 32);
        for (var n = 0; n < state.Coefficients.Count; n++)
            Put(raw, (includeName ? 32 : 0) + n * 4, state.Coefficients[n]);
        return raw;
    }
    public static string Text(byte[] raw) => Encoding.Latin1.GetString(raw.TakeWhile(b => b != 0).ToArray()).Trim();
    public static void PutText(byte[] raw, int offset, string value, int length)
    {
        if (value.Length > length || value.Any(c => c > 255 || char.IsControl(c)))
            throw new ArgumentException("Native names must fit the controller's single-byte text field.");
        Array.Clear(raw, offset, length);
        Encoding.Latin1.GetBytes(value).CopyTo(raw, offset);
    }
}
