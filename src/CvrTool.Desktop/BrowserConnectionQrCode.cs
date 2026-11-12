// Desktop / Local browser pairing QR
// Purpose: render the complete private connection URL without an external service.
// Units: black/white QR modules including the encoder's four-module quiet zone.
// State: clear the modules when browser control stops or its access key changes.
// Scope: vector drawing only; no System.Drawing renderer or network calls.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using QRCoder;

namespace CvrTool.Desktop;

public sealed class BrowserConnectionQrCode : Control
{
    private bool[][] modules = [];
    public string? ConnectionUrl { get; private set; }
    public BrowserConnectionQrCode() => IsVisible = false;

    public void SetConnectionUrl(string? url)
    {
        if (ConnectionUrl == url) return;
        ConnectionUrl = url;
        modules = [];
        if (!string.IsNullOrEmpty(url))
        {
            using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            modules = data.ModuleMatrix.Select(row => row.Cast<bool>().ToArray()).ToArray();
        }
        IsVisible = modules.Length > 0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.White, null, new Rect(Bounds.Size));
        if (modules.Length == 0) return;
        var cell = Math.Floor(Math.Min(Bounds.Width, Bounds.Height) / modules.Length);
        if (cell < 1) return;
        var left = Math.Floor((Bounds.Width - cell * modules.Length) / 2);
        var top = Math.Floor((Bounds.Height - cell * modules.Length) / 2);
        for (var y = 0; y < modules.Length; y++)
            for (var x = 0; x < modules.Length; x++)
                if (modules[y][x])
                    context.DrawRectangle(Brushes.Black, null, new Rect(left + x * cell, top + y * cell, cell, cell));
    }
}
