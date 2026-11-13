// Desktop / Branding — embedded assets shared by the header, window and platform packages.
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CvrTool.Desktop;

internal static class Branding
{
    private static Uri Asset(string name) => new($"avares://CvrTool.Desktop/Assets/{name}");
    public static WindowIcon WindowIcon()
    {
        using var stream = AssetLoader.Open(Asset("AppIcon.ico"));
        return new WindowIcon(stream);
    }

    public static Bitmap Bitmap()
    {
        using var stream = AssetLoader.Open(Asset("AppIcon.png"));
        return new Bitmap(stream);
    }
}
