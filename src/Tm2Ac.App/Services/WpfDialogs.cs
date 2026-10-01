using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using Tm2Ac.App.ViewModels;

namespace Tm2Ac.App.Services;

internal sealed class WpfDialogs : IDialogs
{
    public Window? Owner { get; set; }

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Error(string title, string message) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public string? PickFolder(string title, string? initial)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(initial) && System.IO.Directory.Exists(initial))
        {
            dialog.InitialDirectory = initial;
        }

        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    public void Open(string pathOrUri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(pathOrUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Error("Couldn't open", $"{pathOrUri}\n\n{e.Message}");
        }
    }

    private MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is null ? MessageBox.Show(message, title, buttons, image) : MessageBox.Show(Owner, message, title, buttons, image);
}
