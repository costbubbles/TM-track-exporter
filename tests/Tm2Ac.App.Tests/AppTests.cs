using System.IO;
using System.Globalization;
using System.Windows.Data;
using Tm2Ac.App.Services;
using Tm2Ac.App.ViewModels;
using Tm2Ac.App.Views;
using Tm2Ac.Core;
using Tm2Ac.Tmx;

namespace Tm2Ac.App.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(null, "–")]
    [InlineData(0, "–")]
    [InlineData(27_390, "27.390 s")]
    [InlineData(199_010, "3:19.010")]
    public void Times(int? ms, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, Format.Time(ms));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void StripsBbCodeButKeepsText() =>
        Assert.Equal("Bold and a link and done", TrackDetailViewModel.StripBbCode("[b]Bold[/b] and [url=https://x.y]a link[/url] and [i]done[/i]"));
}

public class ConverterTests
{
    private static readonly EqualsConverter Equal = new();

    [Fact]
    public void EnumChipsRoundTrip()
    {
        Assert.Equal(true, Equal.Convert(TmGame.Tmuf, typeof(bool), "Tmuf", CultureInfo.InvariantCulture));
        Assert.Equal(false, Equal.Convert(TmGame.Tmnf, typeof(bool), "Tmuf", CultureInfo.InvariantCulture));
        Assert.Equal(TmxTrackOrder.Newest, Equal.ConvertBack(true, typeof(TmxTrackOrder), "Newest", CultureInfo.InvariantCulture));
        Assert.Equal(Binding.DoNothing, Equal.ConvertBack(false, typeof(TmxTrackOrder), "Newest", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NullableIntChipsUseEmptyForAny()
    {
        Assert.Equal(true, Equal.Convert(null, typeof(bool), "", CultureInfo.InvariantCulture));
        Assert.Equal(true, Equal.Convert(5, typeof(bool), "5", CultureInfo.InvariantCulture));
        Assert.Null(Equal.ConvertBack(true, typeof(int?), "", CultureInfo.InvariantCulture));
        Assert.Equal(5, Equal.ConvertBack(true, typeof(int?), "5", CultureInfo.InvariantCulture));
    }
}

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("tm2ac-app-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class NoDialogs : IDialogs
    {
        public bool Confirm(string title, string message) => false;
        public void Error(string title, string message) { }
        public string? PickFolder(string title, string? initial) => null;
        public void Open(string pathOrUri) { }
    }

    [Fact]
    public void SaveValidatesAndPersistsToTheChosenFile()
    {
        var path = Path.Combine(_root, "settings.json");
        var settings = new AppSettings { FilePath = path };
        using var services = new AppServices(settings, TmxClient.CreateDefault(Path.Combine(_root, "cache")));
        var vm = new SettingsViewModel(services, new NoDialogs()) { DefaultScale = "1,5", DefaultPitboxes = 500, TmnfPath = "  " };

        vm.Save();

        var saved = AppSettings.Load(path);
        Assert.Equal(1.5f, saved.DefaultScale);
        Assert.Equal(64, saved.DefaultPitboxes);
        Assert.Null(saved.TmnfPath);
        Assert.Equal("1.5", vm.DefaultScale);
    }

    [Fact]
    public void OutOfRangeScaleKeepsThePreviousValue()
    {
        var settings = new AppSettings { FilePath = Path.Combine(_root, "settings.json"), DefaultScale = 2f };
        using var services = new AppServices(settings, TmxClient.CreateDefault(Path.Combine(_root, "cache")));
        var vm = new SettingsViewModel(services, new NoDialogs()) { DefaultScale = "9" };

        vm.Save();

        Assert.Equal(2f, settings.DefaultScale);
        Assert.Equal("2", vm.DefaultScale);
    }
}
