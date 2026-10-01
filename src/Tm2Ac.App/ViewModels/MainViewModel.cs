using CommunityToolkit.Mvvm.ComponentModel;
using Tm2Ac.App.Services;

namespace Tm2Ac.App.ViewModels;

internal sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(AppServices services, IDialogs dialogs)
    {
        Browse = new BrowseViewModel(services, dialogs);
        Library = new LibraryViewModel(services, dialogs);
        Settings = new SettingsViewModel(services, dialogs);
        CurrentPage = Browse;
    }

    public BrowseViewModel Browse { get; }
    public LibraryViewModel Library { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    public partial ObservableObject CurrentPage { get; set; }

    // Bound two-way to the navigation radio buttons (works for mouse, keyboard and UI Automation alike).
    public bool IsBrowse { get => CurrentPage == Browse; set => Show(value, Browse); }
    public bool IsLibrary { get => CurrentPage == Library; set => Show(value, Library); }
    public bool IsSettings { get => CurrentPage == Settings; set => Show(value, Settings); }

    private void Show(bool selected, ObservableObject page)
    {
        if (selected)
        {
            CurrentPage = page;
        }
    }

    partial void OnCurrentPageChanged(ObservableObject value)
    {
        OnPropertyChanged(nameof(IsBrowse));
        OnPropertyChanged(nameof(IsLibrary));
        OnPropertyChanged(nameof(IsSettings));
        if (value == Library)
        {
            Library.Refresh();
        }
    }

    /// <summary>First load: search TMX, and send the user to Settings when something required is missing.</summary>
    public async Task StartAsync()
    {
        Settings.Refresh();
        if (!Settings.IsReady)
        {
            CurrentPage = Settings;
        }

        await Browse.SearchCommand.ExecuteAsync(null).ConfigureAwait(true);
    }
}

/// <summary>Message boxes and pickers, behind an interface so view models stay testable.</summary>
internal interface IDialogs
{
    bool Confirm(string title, string message);
    void Error(string title, string message);
    string? PickFolder(string title, string? initial);
    void Open(string pathOrUri);
}
