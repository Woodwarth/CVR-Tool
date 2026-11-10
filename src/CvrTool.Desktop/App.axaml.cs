// Desktop / Application lifecycle — English culture, theme loading and main window.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CvrTool.Application;

namespace CvrTool.Desktop;

public partial class App : Avalonia.Application
{
    private SessionHost? session;
    public override void Initialize()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Globalization.CultureInfo.CurrentCulture = culture;
        System.Globalization.CultureInfo.CurrentUICulture = culture;
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var backend = new ConnectionBackend();
            try
            {
                session = new SessionHost(backend, Program.SessionName);
            }
            catch (IOException) { Console.Error.WriteLine("CVR Tool session is already running."); desktop.Shutdown(); return; }
            var window = new MainWindow(new AmplifierService(backend));
            desktop.MainWindow = window;
            session.Changed += () => Dispatcher.UIThread.Post(window.ReloadSession);
            desktop.Exit += (_, _) => { session.Dispose(); backend.Dispose(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
