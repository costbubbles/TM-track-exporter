namespace Tm2Ac.Core.Tests;

public class SteamLibrariesTests
{
    private const string Vdf = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"label"		""
        		"apps"
        		{
        			"228980"		"405460009"
        		}
        	}
        	"1"
        	{
        		"path"		"F:\\SteamLibrary"
        		"label"		""
        		"totalsize"		"0"
        		"apps"
        		{
        			"244210"		"44949470831"
        			"2225070"		"7162121885"
        		}
        	}
        }
        """;

    [Fact]
    public void ParsesPathsAndAppIds()
    {
        var libraries = SteamLibraries.ParseLibraryFolders(Vdf);

        Assert.Equal(2, libraries.Count);
        Assert.Equal(@"C:\Program Files (x86)\Steam", libraries[0].Path);
        Assert.Equal(@"F:\SteamLibrary", libraries[1].Path);
        Assert.Contains(SteamLibraries.AssettoCorsaAppId, libraries[1].AppIds);
        Assert.Contains(SteamLibraries.Trackmania2020AppId, libraries[1].AppIds);
        Assert.DoesNotContain(SteamLibraries.AssettoCorsaAppId, libraries[0].AppIds);
    }
}
