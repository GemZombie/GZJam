namespace GZJam.Library.Servers;

/// <summary>
/// A music server the DJ signed in to (#184): Navidrome, or another that speaks the Subsonic API.
/// Kept in the library, so a backup carries it; the password never is (see <see cref="PasswordStore"/>).
/// </summary>
/// <param name="Name">What the DJ calls it: "Navidrome (mine)", "Dave's server".</param>
/// <param name="Address">Where it answers, as <see cref="SubsonicClient.NormalizeAddress"/> leaves it: "http://synology:4533".</param>
/// <param name="CanDownload">
/// Whether this account may download songs, as the last check found; null until checked. Without
/// it the server is preview only: its songs can be heard in the headphones, never played to the room.
/// </param>
/// <param name="ServerType">What the server said it is ("navidrome"), as last checked.</param>
/// <param name="ServerVersion">Its version ("0.64.2"), as last checked.</param>
/// <param name="CheckedUtc">When it was last reached.</param>
public sealed record MusicServer(
    long Id,
    string Name,
    string Address,
    string UserName,
    bool? CanDownload = null,
    string? ServerType = null,
    string? ServerVersion = null,
    DateTime? CheckedUtc = null)
{
    /// <summary>Where its password is kept in the keychain: the same server and account share one.</summary>
    public string PasswordKey => PasswordStore.KeyFor(Address, UserName);
}

/// <summary>How a check of a server went.</summary>
public enum ServerCheckOutcome
{
    /// <summary>Signed in.</summary>
    Connected,

    /// <summary>The server answered, but not to this user name and password.</summary>
    WrongSignIn,

    /// <summary>Nothing answered at the address (no network, a typo, the server off).</summary>
    Unreachable,

    /// <summary>Something answered, but not a Subsonic server (a web page, a router).</summary>
    NotSubsonic,

    /// <summary>The server said no for another reason; see <see cref="ServerCheck.Detail"/>.</summary>
    Failed,
}

/// <summary>What checking a server found: whether it signed in, what it is, and whether it lets this account download.</summary>
public sealed record ServerCheck(
    ServerCheckOutcome Outcome,
    string? ServerType = null,
    string? ServerVersion = null,
    bool OpenSubsonic = false,
    bool? CanDownload = null,
    int? SongCount = null,
    string? Detail = null)
{
    public bool Connected => Outcome == ServerCheckOutcome.Connected;

    /// <summary>"Navidrome 0.64.2", or the API version when the server doesn't say what it is.</summary>
    public string? ServerName => ServerType is { Length: > 0 } type
        ? (char.ToUpperInvariant(type[0]) + type[1..] + (ServerVersion is { } v ? " " + v : "")).Trim()
        : null;
}

/// <summary>A playlist on a server, as its list of playlists says.</summary>
public sealed record ServerPlaylist(string Id, string Name, int SongCount, TimeSpan Length, string? Owner);

/// <summary>What an account starred on a server: songs, and whole albums and artists by ID and name.</summary>
public sealed record ServerStarred(
    IReadOnlyList<ServerSong> Songs,
    IReadOnlyList<(string Id, string Name)> Albums,
    IReadOnlyList<(string Id, string Name)> Artists)
{
    public bool IsEmpty => Songs.Count == 0 && Albums.Count == 0 && Artists.Count == 0;
}

/// <summary>
/// A song on a server: its ID there, what it says about the song, and its file. <see cref="Path"/>
/// is the file's place on the server, real or made up from the tags (Navidrome reports real
/// paths only to the apps it's told to).
/// </summary>
public sealed record ServerSong(
    string Id,
    string? Title,
    string? Artist,
    string? Album,
    string? AlbumArtist,
    int? Track,
    int? Disc,
    int? Year,
    string? Path,
    string? Suffix,
    long? Size,
    TimeSpan Duration,
    string? Genre = null,
    double? Bpm = null,
    string? AlbumId = null,
    string? CoverArt = null);

/// <summary>A server call that didn't work, with words for why.</summary>
public sealed class ServerException(string message, Exception? inner = null, ServerCheckOutcome outcome = ServerCheckOutcome.Failed)
    : Exception(message, inner)
{
    public ServerCheckOutcome Outcome { get; } = outcome;
}

/// <summary>What a search of a server found.</summary>
public sealed record ServerSearch(IReadOnlyList<ServerSong> Songs, IReadOnlyList<ServerAlbum> Albums, IReadOnlyList<ServerArtist> Artists)
{
    public bool IsEmpty => Songs.Count == 0 && Albums.Count == 0 && Artists.Count == 0;
}

/// <summary>An album on a server.</summary>
public sealed record ServerAlbum(string Id, string Name, string? Artist, int? Year, int SongCount, TimeSpan Length);

/// <summary>An artist on a server.</summary>
public sealed record ServerArtist(string Id, string Name, int AlbumCount);

/// <summary>
/// A song on a server that isn't on this computer, as a row in the library's song list (#290): it
/// can be heard in the headphones and downloaded, but goes on a deck or into the queue only once
/// it's here. Its <see cref="Track.Path"/> is <c>gzjam-server:&lt;server&gt;/&lt;song&gt;</c>, never a file.
/// </summary>
public sealed record ServerTrack(long ServerId, string ServerName, ServerSong Song)
{
    public const string PathPrefix = "gzjam-server:";

    public string PseudoPath => $"{PathPrefix}{ServerId}/{Song.Id}";

    /// <summary>The server couldn't be reached when this row was made: shown as such, and not asked for it.</summary>
    public bool Unreachable { get; init; }

    /// <summary>The server and song in a <see cref="PseudoPath"/>; null for any other path.</summary>
    public static (long ServerId, string SongId)? Parse(string path)
    {
        if (!path.StartsWith(PathPrefix, StringComparison.Ordinal))
            return null;
        string rest = path[PathPrefix.Length..];
        int slash = rest.IndexOf('/');
        return slash > 0 && long.TryParse(rest[..slash], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long id)
            ? (id, rest[(slash + 1)..])
            : null;
    }

    public Track ToTrack() => new()
    {
        Path = PseudoPath,
        Folder = "",
        FileName = System.IO.Path.GetFileName(Song.Path ?? (Song.Title ?? Song.Id)),
        FileSize = Song.Size ?? 0,
        Title = Song.Title,
        Artist = Song.Artist,
        Album = Song.Album,
        AlbumArtist = Song.AlbumArtist,
        Genre = Song.Genre,
        Year = Song.Year is > 0 ? Song.Year : null,
        TrackNumber = Song.Track is > 0 ? Song.Track : null,
        TagBpm = Song.Bpm is > 0 ? Song.Bpm : null,
        Duration = Song.Duration,
        OnServer = this,
    };
}
