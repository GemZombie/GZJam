using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>
/// Songs moved or renamed on the server (#286): when its song list is read, a song on this
/// computer whose song moved there follows it, rather than being downloaded again. A downloaded
/// one moves to its new place in the downloads folder, with its hearts, cues, labels, playlists
/// and plays; one kept in the DJ's music, or their own copy, stays where it is, linked to the
/// song as it is now. One on a deck is left for the next time.
/// </summary>
public sealed partial class ServersViewModel
{
    private async Task FollowServerMovesAsync(ServerItemViewModel item)
    {
        long id = item.Server.Id;
        var moves = await Task.Run(() => _database.FindServerMoves(id));
        if (moves.Count == 0)
            return;
        var onDecks = _owner.Decks.Select(d => d.Track?.Path).OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (_owner.Preview?.Track?.Path is { } heard)
            onDecks.Add(heard);
        string serverFolder = ServerPaths.ServerFolder(DownloadsFolder, item.Name);
        List<(string From, string To)> moved;
        int followed = 0;
        await _oneSync.WaitAsync();
        try
        {
            moved = await _owner.Library.WithoutScansAsync(() => Task.Run(() =>
            {
                var files = new List<(string From, string To)>();
                foreach (ServerMove move in moves)
                {
                    if (onDecks.Contains(move.Path))
                        continue;   // in use: next time
                    string? to = null;
                    if (!move.OwnCopy && IsUnder(move.Path, serverFolder) && File.Exists(move.Path))
                    {
                        string target = ServerPaths.Place(serverFolder, ServerPaths.RelativeParts(move.Song),
                            p => !string.Equals(p, move.Path, StringComparison.OrdinalIgnoreCase) && (File.Exists(p) || _database.IsServerSongPath(p)));
                        if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(move.Path), StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                                DateTime modified = File.GetLastWriteTimeUtc(move.Path);
                                File.Move(move.Path, target);
                                File.SetLastWriteTimeUtc(target, modified);
                                _database.MoveSongFiles([(move.Path, target)]);
                                CopyArtAlong(Path.GetDirectoryName(move.Path)!, Path.GetDirectoryName(target)!);
                                RemoveEmptyFolders(Path.GetDirectoryName(move.Path));
                                files.Add((move.Path, target));
                                to = target;
                            }
                            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                            {
                                Log.Info($"Servers: couldn't move {move.Path} to follow {item.Name}: {e.Message}");
                                continue;   // tried again next time
                            }
                        }
                    }
                    _database.FollowServerMove(id, move.OldId, move.Song, to);
                    followed++;
                }
                return files;
            }));
        }
        finally
        {
            _oneSync.Release();
        }
        Log.Info($"Servers: {Format.Count(followed)} of {item.Name}'s songs on this computer moved or were renamed there: followed"
            + (moved.Count > 0 ? $", {Format.Count(moved.Count)} moved here too" : ""));
        if (moved.Count == 0)
            return;
        _owner.Library.FollowMoves(moved);   // the queue and decks follow
        AfterSync();
    }
}
