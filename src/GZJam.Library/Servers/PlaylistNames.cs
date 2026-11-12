namespace GZJam.Library.Servers;

/// <summary>
/// Folders in a server's playlist names (#355). Navidrome has no playlist folders; some of its
/// apps (Feishin, Navibeat) make them from a separator in the name, "WCS/Setlists/Nov 8" being
/// the playlist Nov 8 in the folders WCS › Setlists, and GZJam shows them the same way when the
/// DJ says that server's apps do (<see cref="MusicServer.PlaylistSeparator"/>).
/// </summary>
public static class PlaylistNames
{
    /// <summary>
    /// A name's folders and its own name: "WCS/Setlists/Nov 8" is ([WCS, Setlists], Nov 8). As
    /// Feishin splits them (its buildPlaylistTree, checked 2026-11-12), so a playlist is in the same
    /// folder here as there: empty parts ("a//b", a separator at either end) are skipped, spaces
    /// aren't trimmed ("WCS / x" is in "WCS ", not "WCS"), and a name of one part, or none, stays
    /// as it is, at the top.
    /// </summary>
    public static (IReadOnlyList<string> Folders, string Name) Split(string name, string? separator)
    {
        if (string.IsNullOrEmpty(separator) || !name.Contains(separator, StringComparison.Ordinal))
            return ([], name);
        var parts = name.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 1 ? ([], name) : (parts[..^1], parts[^1]);
    }

    /// <summary>
    /// The name a playlist is sent to a server with: its folders and its own name joined by the
    /// separator ("WCS/Setlists/Nov 8"), so it shows in the same folders in the server's apps; its
    /// name alone with no separator, or none of folders.
    /// </summary>
    public static string Join(IReadOnlyList<string> folders, string name, string? separator) =>
        string.IsNullOrEmpty(separator) || folders.Count == 0 ? name : string.Join(separator, [.. folders, name]);
}
