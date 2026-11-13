// Desktop / MCP presence indicator
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: show actual initialized MCP connections in the main app header.
// Units: active client count supplied by the shared session owner.
// State: pipe disconnect/crash clears presence; idle initialized clients stay connected.
// Scope: independent of amplifier connectivity and browser-server status.
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CvrTool.Application;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private readonly Border mcpIndicator = new()
    {
        Tag = "mcp-connection", Width = 52, Height = 34, Padding = new Avalonia.Thickness(10,6),
        BorderThickness = new Avalonia.Thickness(1), CornerRadius = new Avalonia.CornerRadius(4),
        VerticalAlignment = VerticalAlignment.Center
    };

    public void ObserveMcpClients(SessionHost session)
    {
        void Refresh() => UpdateMcpStatus(session.McpClientCount);
        void Changed() => Dispatcher.UIThread.Post(Refresh);
        session.McpClientsChanged += Changed;
        Closed += (_, _) => session.McpClientsChanged -= Changed;
        Refresh();
    }

    private void UpdateMcpStatus(int count)
    {
        var connected = count > 0;
        mcpIndicator.Child = Text("MCP", 12, connected ? Accent : Muted);
        ((TextBlock)mcpIndicator.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((TextBlock)mcpIndicator.Child).VerticalAlignment = VerticalAlignment.Center;
        mcpIndicator.BorderBrush = connected ? Accent : Line;
        mcpIndicator.Background = Brush.Parse(connected ? "#12362F" : "#182129");
    }
}
