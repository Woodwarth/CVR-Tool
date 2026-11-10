// Protocol / Evidence — inspect captured byte structure without claiming command meaning.
// Purpose: handle explicit network targets and bounded packet data.
// Input: validated addresses or byte sequences.
// State: transport ownership stays with the application session.
// Scope: no discovery scans or firmware operations.
using System.Text;

namespace CvrTool.Protocol;

public enum CapturedFrameKind
{
    Unknown,
    AckCandidate,
    ObservedSingleDatagram,
    FragmentCandidate
}

public sealed record CapturedFrameInspection(CapturedFrameKind Kind, int Length, bool? ChecksumMatches = null, byte? Function = null, byte? Operation = null, string? ModelHint = null);
/// <summary>Conservative read-only recognition of packet shapes observed in the private capture.</summary>
public static class CapturedFrameInspector
{
    public static CapturedFrameInspection Inspect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 10 || bytes.Length > LoopbackUdpTransport.MaximumDatagramBytes || bytes[0] != 3 || bytes[1] != 0xD9)
            return new(CapturedFrameKind.Unknown, bytes.Length);
        if (bytes.Length == 10 && bytes[2] == 0xCA && bytes[3] == 0 && bytes[8] == 1 && bytes[9] == 0x21)
            return new(CapturedFrameKind.AckCandidate, bytes.Length);
        if (bytes.Length is 455 or 460 && bytes[6] == 1)
            return new(CapturedFrameKind.FragmentCandidate, bytes.Length);
        if (bytes.Length is not (23 or 24 or 102 or 115 or 116) || bytes[6] != 0 || bytes[7] != 1 || bytes[10] != 0x55)
            return new(CapturedFrameKind.Unknown, bytes.Length);
        var sum = 0;
        foreach (var value in bytes[10..^1])
            sum = (sum + value) & 255;
        var valid = sum == bytes[^1];
        string? model = null;
        if (valid)
        {
            foreach (var candidate in new[]
            {
                "DSP-1004",
                "DSP-2002",
                "DSP-1502"
            }

            )
                if (bytes[13..^2].IndexOf(Encoding.ASCII.GetBytes(candidate)) >= 0)
                    model = candidate;
        }

        return new(CapturedFrameKind.ObservedSingleDatagram, bytes.Length, valid, bytes[11], bytes[12], model);
    }
}
