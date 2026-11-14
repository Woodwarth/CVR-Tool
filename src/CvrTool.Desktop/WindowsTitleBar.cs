// Desktop / Windows title bars follow the operating system's app color preference.
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace CvrTool.Desktop;

internal static class WindowsTitleBar
{
    public static void Attach(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) || Avalonia.Application.Current?.PlatformSettings is not { } settings)
            return;

        void Apply()
        {
            if (window.TryGetPlatformHandle() is not { Handle: var handle } || handle == IntPtr.Zero) return;
            var dark = settings.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark ? 1 : 0;
            // Windows 10 builds before 19041 used attribute 19 for immersive dark mode.
            var attribute = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) ? 20 : 19;
            DwmSetWindowAttribute(handle, attribute, ref dark, sizeof(int));
        }

        void Changed(object? sender, PlatformColorValues values) => Dispatcher.UIThread.Post(Apply);
        window.Opened += (_, _) => { settings.ColorValuesChanged += Changed; Apply(); };
        window.Closed += (_, _) => settings.ColorValuesChanged -= Changed;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
