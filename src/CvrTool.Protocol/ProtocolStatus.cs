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
    public const string Reason = "Selected bulk-profile operations passed on DSP-1004; other models and the individual-query profile remain unverified.";
}
