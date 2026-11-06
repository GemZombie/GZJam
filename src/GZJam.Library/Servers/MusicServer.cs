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
