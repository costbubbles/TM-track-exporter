using System.Windows;
using System.Windows.Threading;
using Tm2Ac.App.Services;
using Tm2Ac.App.ViewModels;
using Tm2Ac.App.Views;
using Tm2Ac.Core;
using Tm2Ac.Tmx;

namespace Tm2Ac.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Services are disposed in OnExit, the WPF application lifetime hook.")]
public partial class App : Application
{
    private AppServices? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // --settings <file>: use another settings file (portable installs, testing).
        var index = Array.IndexOf(e.Args, "--settings");
        var settingsPath = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null;
        var settings = AppSettings.Load(settingsPath);
        settings.FilePath = settingsPath;
        if (!settings.LegalNoticeAccepted)
        {
            var answer = MessageBox.Show(
                SettingsViewModel.LegalNotice + "\n\nDo you accept?", "Tm2Ac", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes)
            {
                Shutdown();
                return;
            }

            settings.LegalNoticeAccepted = true;
            settings.Save();
        }

        _services = new AppServices(settings, TmxClient.CreateDefault());
        var dialogs = new WpfDialogs();
        var main = new MainViewModel(_services, dialogs);
        var window = new MainWindow { DataContext = main };
        dialogs.Owner = window;
        MainWindow = window;
        window.Show();
        await main.StartAsync().ConfigureAwait(true);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write(e.Exception);
        MessageBox.Show($"Something went wrong: {e.Exception.Message}\n\nDetails are in {AppLog.PathName}.", "Tm2Ac", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
