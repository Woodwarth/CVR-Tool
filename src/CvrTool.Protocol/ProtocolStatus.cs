// Protocol / Capabilities — report preparation state separately from hardware support.
// Purpose: handle explicit network targets and bounded packet data.
// Input: validated addresses or byte sequences.
// State: transport ownership stays with the application session.
// Scope: no discovery scans or firmware operations.
namespace CvrTool.Protocol;
/// <summary>Real device access must be implemented from verified captures, never guessed.</summary>
public static class ProtocolStatus
{
    public const bool HardwareAccessEnabled = true;
    public const bool PhysicalAmplifierValidated = false;
    public const string Reason = "Manual CVR UDP is available. Synthetic loopback checks pass; physical-amplifier compatibility remains unverified.";
}
