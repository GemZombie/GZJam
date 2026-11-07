namespace GZJam.Library.Tests;

/// <summary>What file names say about their songs (#273), for old transfers with empty tags but good names.</summary>
public sealed class FileNameTagsTests
{
    [Theory]
    [InlineData("1924 Ma Rainey - Lucky Rock Blues.mp3", "Ma Rainey", "Lucky Rock Blues", 1924, null)]
    [InlineData("Count Basie - 04 - Jumpin' at the Woodside.flac", "Count Basie", "Jumpin' at the Woodside", null, 4)]
    [InlineData("04 - Jumpin' at the Woodside.mp3", null, "Jumpin' at the Woodside", null, 4)]
    [InlineData("04. Jumpin' at the Woodside.mp3", null, "Jumpin' at the Woodside", null, 4)]
    [InlineData("04 Jumpin' at the Woodside.mp3", null, "Jumpin' at the Woodside", null, 4)]
    [InlineData("07 - Ella Fitzgerald - Air Mail Special.mp3", "Ella Fitzgerald", "Air Mail Special", null, 7)]
    [InlineData("01_Count_Basie_-_One_O'Clock_Jump.mp3", "Count Basie", "One O'Clock Jump", null, 1)]
    [InlineData("Ella Fitzgerald – Air Mail Special.mp3", "Ella Fitzgerald", "Air Mail Special", null, null)]   // an en dash
    [InlineData("Artie Shaw - 1938 - Begin the Beguine.mp3", "Artie Shaw", "Begin the Beguine", 1938, null)]
    [InlineData("Louis Prima - Sing Sing Sing - Live.mp3", "Louis Prima", "Sing Sing Sing - Live", null, null)]
    [InlineData("7 Come 11.mp3", null, "7 Come 11", null, null)]   // a title that starts with a number stays whole
    [InlineData("Jitterbug Waltz.mp3", null, "Jitterbug Waltz", null, null)]
    [InlineData(@"C:\music\Swing\Benny Goodman - Stompin' at the Savoy.mp3", "Benny Goodman", "Stompin' at the Savoy", null, null)]
    public void A_name_says_what_it_can(string name, string? artist, string title, int? year, int? track)
    {
        Assert.Equal(new NameTags(artist, title, year, track), FileNameTags.Guess(name));
    }

    [Fact]
    public void The_pattern_most_names_fit_is_offered_the_more_particular_first()
    {
        Assert.Equal("{year} {artist} - {title}", FileNameTags.BestPattern(
            ["1924 Ma Rainey - Lucky Rock Blues.mp3", "1925 Bessie Smith - St. Louis Blues.mp3", "Jitterbug Waltz.mp3"]));
        Assert.Equal("{artist} - {track} - {title}", FileNameTags.BestPattern(
            ["Count Basie - 04 - Jumpin' at the Woodside.mp3", "Count Basie - 05 - Topsy.mp3"]));
        Assert.Equal("{artist} - {title}", FileNameTags.BestPattern(["Jitterbug Waltz.mp3"]));   // none fit: the plainest
    }

    [Fact]
    public void A_pattern_of_ones_own_fills_what_it_names_and_skips_the_rest()
    {
        Assert.Equal(new NameTags("Ma Rainey", "Lucky Rock Blues", Album: "Mother of the Blues"),
            FileNameTags.Match("[{skip}] {album} - {artist} - {title}", "[78rpm] Mother of the Blues - Ma Rainey - Lucky Rock Blues.mp3"));
        Assert.Null(FileNameTags.Match("{year} {artist} - {title}", "Ma Rainey - Lucky Rock Blues.mp3"));
        Assert.Null(FileNameTags.Match("{year} {artist} - {title}", "1066 Ma Rainey - Lucky Rock Blues.mp3"));   // not a year for a record
    }

    [Theory]
    [InlineData("{artist} - {title}", true)]
    [InlineData("{ARTIST} - {Title}", true)]
    [InlineData("artist - title", false)]   // nothing to fill
    [InlineData("{skip} - {skip}", false)]
    [InlineData("{artist} - {artist}", false)]   // twice
    [InlineData("{artist} - {composer}", true)]   // an unknown part is just text
    public void A_pattern_has_a_part_to_fill_each_once(string pattern, bool valid)
    {
        Assert.Equal(valid, FileNameTags.IsValid(pattern));
    }
}
