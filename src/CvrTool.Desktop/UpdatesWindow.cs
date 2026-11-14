// Desktop / GitHub updates
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: explicitly check, download, verify and restart into a native release.
// Input: selected release repository and the current platform/version.
// State: cancellation leaves the current app intact; one update dialog is allowed.
// Scope: local session settings must be exported before restarting.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using CvrTool.Application;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private Window? updatesWindow;
    private void OpenUpdates()
    {
        if (updatesWindow is not null)
        {
            updatesWindow.Activate();
            return;
        }
        GitHubUpdates? updates = null;
        var installation = new UpdateInstallation();
        AppUpdate? available = null;
        var cancellation = new CancellationTokenSource();
        var content = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        content.Children.Add(Heading("Software updates"));
        content.Children.Add(Text("Installed version: " + AppUpdateLifecycle.Version, 14, Muted));
        var sourcePath = Path.Combine(installation.Root, "repository.txt");
        var previousSource = File.Exists(sourcePath) ? File.ReadAllText(sourcePath).Trim() : "";
        var source = new TextBox { Text = previousSource.Length > 0 ? previousSource :
            Environment.GetEnvironmentVariable("CVR_UPDATE_REPOSITORY") ?? GitHubUpdates.Repository };
        var credential = new TextBox { PasswordChar = '●', PlaceholderText = "New read token (optional)" };
        var fields = new StackPanel { Spacing = 6 };
        fields.Children.Add(Text("GitHub repository (owner/name)", 12, Muted));
        fields.Children.Add(source);
        fields.Children.Add(credential);
        var forget = new Button { Content = "Forget saved token" };
        fields.Children.Add(forget);
        var options = new Expander { Header = "Update source", Content = fields };
        content.Children.Add(options);
        var message = Text("Check GitHub for the latest stable release for this computer.", 13, Muted);
        message.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        content.Children.Add(message);
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, IsVisible = false };
        content.Children.Add(progress);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var check = new Button { Content = "Check for updates", Tag = "check-for-updates" };
        var install = new Button { Content = "Download and restart", IsEnabled = false, Tag = "install-update" };
        actions.Children.Add(check);
        actions.Children.Add(install);
        content.Children.Add(actions);
        content.Children.Add(Text("Updates are installed in your user profile. Existing versions are kept.\nRestart closes this session; export settings you want to retain first.", 12, Muted));
        var close = new Button { Content = "Close / Cancel" };
        content.Children.Add(close);
        var dialog = EditorWindow("CVR Tool · Updates", content, 650, 530);
        updatesWindow = dialog;
        dialog.Closed += (_, _) => { cancellation.Cancel(); updatesWindow = null; };
        void SourceChanged()
        {
            available = null;
            install.IsEnabled = false;
        }
        source.TextChanged += (_, _) => SourceChanged();
        credential.TextChanged += (_, _) => SourceChanged();
        forget.Click += (_, _) =>
        {
            try
            {
                var repository = source.Text?.Trim() ?? "";
                _ = new GitHubUpdates(source: repository);
                UpdateCredentialStore.Write(repository, null);
                credential.Text = "";
                SourceChanged();
                updates = null;
                message.Text = "Saved token removed for this repository.";
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        close.Click += (_, _) => dialog.Close();
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            install.IsEnabled = false;
            options.IsEnabled = false;
            available = null;
            message.Text = "Checking GitHub…";
            try
            {
                var repository = source.Text?.Trim() ?? "";
                var entered = credential.Text?.Trim();
                updates = new GitHubUpdates(source: repository, credential: entered);
                if (string.IsNullOrEmpty(entered))
                    updates = new GitHubUpdates(source: repository, credential: UpdateCredentialStore.Read(repository));
                else
                    UpdateCredentialStore.Write(repository, entered);
                Directory.CreateDirectory(installation.Root);
                File.WriteAllText(sourcePath, repository);
                credential.Text = "";
                available = await updates.CheckAsync(AppUpdateLifecycle.Version, GitHubUpdates.CurrentRuntime, cancellation.Token);
                message.Text = available is null ? "You are up to date." : $"Version {available.Version} is available · {available.Runtime} · {available.Size / 1048576.0:0.0} MB";
                install.IsEnabled = available is not null;
            }
            catch (OperationCanceledException) { if (!cancellation.IsCancellationRequested) message.Text = "Update timed out. Try again."; }
            catch (Exception ex) { message.Text = "Update check failed: " + ex.Message; }
            finally { options.IsEnabled = check.IsEnabled = true; }
        };
        install.Click += async (_, _) =>
        {
            if (available is null || updates is null)
                return;
            options.IsEnabled = check.IsEnabled = install.IsEnabled = false;
            progress.IsVisible = true;
            message.Text = "Downloading and verifying update…";
            try
            {
                var package = await updates.DownloadAsync(available, Path.Combine(installation.Root, "downloads"), new Progress<double>(value => progress.Value = value * 100), cancellation.Token);
                message.Text = "Preparing update…";
                var executable = await installation.StageAsync(available, package, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                installation.Activate(available);
                var args = new List<string> { "--finish-update", Environment.ProcessId.ToString() };
                if (Program.SessionName is { } session)
                {
                    args.Add("--session");
                    args.Add(session);
                }
                using var process = AppUpdateLifecycle.Start(executable, args);
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
                else
                    Close();
            }
            catch (OperationCanceledException) { if (!cancellation.IsCancellationRequested) message.Text = "Update timed out. Try again."; }
            catch (Exception ex) { message.Text = "Update failed: " + ex.Message; }
            finally
            {
                if (!cancellation.IsCancellationRequested)
                {
                    options.IsEnabled = check.IsEnabled = true;
                    install.IsEnabled = available is not null;
                }
            }
        };
        dialog.Show(this);
    }
}
