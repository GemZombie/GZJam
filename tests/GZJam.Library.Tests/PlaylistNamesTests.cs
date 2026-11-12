using GZJam.Library.Servers;

namespace GZJam.Library.Tests;

/// <summary>
/// Folders in a server's playlist names (#355), split as Feishin splits them, so a playlist is in
/// the same folder in GZJam as in the DJ's other apps; and the name one of the DJ's goes with.
/// </summary>
public sealed class PlaylistNamesTests
{
    [Theory]
    [InlineData("WCS/Setlists/Nov 8", "/", "WCS|Setlists", "Nov 8")]
    [InlineData("Friday", "/", "", "Friday")]                    // no separator in it: at the top
    [InlineData("AC/DC favorites", null, "", "AC/DC favorites")]  // folders in names off: as it is
    [InlineData("a//b", "/", "a", "b")]                          // empty parts skipped
    [InlineData("/Friday/", "/", "", "/Friday/")]                // one part: the name as it is
    [InlineData("WCS / Nov 8", "/", "WCS ", " Nov 8")]          // spaces kept, as Feishin keeps them
    [InlineData("WCS - Setlists - Nov 8", " - ", "WCS|Setlists", "Nov 8")]
    public void A_name_splits_into_its_folders_and_its_own_name(string name, string? separator, string folders, string own)
    {
        var (gotFolders, gotName) = PlaylistNames.Split(name, separator);
        Assert.Equal(folders, string.Join('|', gotFolders));
        Assert.Equal(own, gotName);
    }

    [Fact]
    public void A_playlist_in_folders_is_sent_with_them_in_its_name_only_with_a_separator()
    {
        Assert.Equal("WCS/Setlists/Nov 8", PlaylistNames.Join(["WCS", "Setlists"], "Nov 8", "/"));
        Assert.Equal("Nov 8", PlaylistNames.Join(["WCS", "Setlists"], "Nov 8", null));
        Assert.Equal("Nov 8", PlaylistNames.Join([], "Nov 8", "/"));
        var (folders, name) = PlaylistNames.Split(PlaylistNames.Join(["WCS", "Setlists"], "Nov 8", "/"), "/");
        Assert.Equal(["WCS", "Setlists"], folders);
        Assert.Equal("Nov 8", name);
    }
}
