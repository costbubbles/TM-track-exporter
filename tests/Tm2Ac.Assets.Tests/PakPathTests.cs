namespace Tm2Ac.Assets.Tests;

public class PakPathTests
{
    [Theory]
    [InlineData(@"Stadium\Media\Solid\..\Material\X.Material.Gbx", @"Stadium\Media\Material\X.Material.Gbx")]
    [InlineData("Stadium/Media/./Texture/Image/A.dds", @"Stadium\Media\Texture\Image\A.dds")]
    [InlineData(@"\Stadium\\Mobil\M.Gbx", @"Stadium\Mobil\M.Gbx")]
    [InlineData(@"..\..\Techno2\Media\X.Gbx", @"Techno2\Media\X.Gbx")]
    public void NormalizesGamePaths(string input, string expected) => Assert.Equal(expected, TmnfPakFileSystem.NormalizePath(input));
}
