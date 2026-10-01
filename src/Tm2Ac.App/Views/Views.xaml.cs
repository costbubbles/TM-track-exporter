using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tm2Ac.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}

public partial class BrowseView : UserControl
{
    public BrowseView() => InitializeComponent();

    /// <summary>Enter in a search box pushes the text to the view model, then searches.</summary>
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box && DataContext is ViewModels.BrowseViewModel vm)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            vm.SearchCommand.Execute(null);
        }
    }
}

public partial class LibraryView : UserControl
{
    public LibraryView() => InitializeComponent();
}

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
}
