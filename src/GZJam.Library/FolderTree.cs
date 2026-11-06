namespace GZJam.Library;

/// <summary>A folder in the library tree, mirroring the folder on disk.</summary>
public sealed class FolderNode
{
    private readonly List<FolderNode> _children = [];

    internal FolderNode(long rootId, string name, string relativePath, string fullPath)
    {
        RootId = rootId;
        Name = name;
        RelativePath = relativePath;
        FullPath = fullPath;
    }

    public long RootId { get; }
    public string Name { get; }

    /// <summary>Relative to the root, '/'-separated; empty for the root node.</summary>
    public string RelativePath { get; }

    public string FullPath { get; }

    /// <summary>Tracks in this folder and all folders beneath it.</summary>
    public int TrackCount { get; internal set; }

    public bool IsRoot => RelativePath.Length == 0;

    /// <summary>A music server in the tree (#290), beside the music folders; null for a folder.</summary>
    public long? ServerId { get; internal init; }

    public bool IsServer => ServerId is not null;

    /// <summary>The "Playlists" under a server, holding its playlists (not a folder on the server).</summary>
    public bool IsServerPlaylists { get; internal init; }

    /// <summary>One of a server's playlists, by its ID there; null for anything else.</summary>
    public string? ServerPlaylistId { get; internal init; }

    /// <summary>
    /// The place in a server's tree of its "Playlists", and of each playlist under it: names no
    /// folder on a server has, starting with a character no file name holds.
    /// </summary>
    public const string ServerPlaylistsPath = "\u0001playlists";

    public static string ServerPlaylistPath(string playlistId) => $"{ServerPlaylistsPath}/{playlistId}";

    /// <summary>A server's place in the tree: no folders under it, its songs are the server's.</summary>
    public static FolderNode ForServer(long serverId, string name) =>
        new(-serverId, name, "", Servers.ServerTrack.PathPrefix + serverId) { ServerId = serverId };
    public IReadOnlyList<FolderNode> Children => _children;
    public FolderScope Scope => new(RootId, RelativePath);

    internal void AddChild(FolderNode child) => _children.Add(child);
    internal void InsertChild(int index, FolderNode child) => _children.Insert(index, child);
    internal void SortChildren()
    {
        _children.Sort((a, b) => NaturalComparer.Instance.Compare(a.Name, b.Name));
        foreach (FolderNode child in _children)
            child.SortChildren();
    }

    public override string ToString() => $"{Name} ({TrackCount})";
}

public static class FolderTree
{
    /// <summary>
    /// A server's tree (#290): the server, and under it its folders, from its catalog's per-folder
    /// song counts ('/'-separated, as the server lays them out).
    /// </summary>
    /// <param name="playlists">The server's playlists (ID, name, songs): under "Playlists", first, if it has any.</param>
    public static FolderNode BuildServer(long serverId, string name, IEnumerable<(string Folder, int Count)> folderCounts,
        IReadOnlyList<(string Id, string Name, int Count)>? playlists = null)
    {
        FolderNode server = FolderNode.ForServer(serverId, name);
        var nodes = new Dictionary<string, FolderNode> { [""] = server };
        foreach (var (folder, count) in folderCounts)
        {
            server.TrackCount += count;
            if (folder.Length == 0)
                continue;
            FolderNode parent = server;
            int start = 0;
            while (true)
            {
                int slash = folder.IndexOf('/', start);
                string relative = slash < 0 ? folder : folder[..slash];
                if (!nodes.TryGetValue(relative, out FolderNode? node))
                {
                    string part = slash < 0 ? folder[start..] : folder[start..slash];
                    node = new FolderNode(-serverId, part, relative, $"{Servers.ServerTrack.PathPrefix}{serverId}/{relative}") { ServerId = serverId };
                    nodes[relative] = node;
                    parent.AddChild(node);
                }
                node.TrackCount += count;
                if (slash < 0)
                    break;
                parent = node;
                start = slash + 1;
            }
        }
        server.SortChildren();
        if (playlists is { Count: > 0 })
        {
            string prefix = $"{Servers.ServerTrack.PathPrefix}{serverId}/";
            var group = new FolderNode(-serverId, "Playlists", FolderNode.ServerPlaylistsPath, prefix + FolderNode.ServerPlaylistsPath)
            {
                ServerId = serverId,
                IsServerPlaylists = true,
                TrackCount = playlists.Count,
            };
            foreach (var (id, list, count) in playlists)
            {
                string path = FolderNode.ServerPlaylistPath(id);
                group.AddChild(new FolderNode(-serverId, list, path, prefix + path) { ServerId = serverId, ServerPlaylistId = id, TrackCount = count });
            }
            server.InsertChild(0, group);   // in the server's order (by name), before its folders
        }
        return server;
    }

    /// <summary>
    /// Builds one tree per root from per-folder track counts. Only folders that contain audio
    /// (directly or below) appear, so cover-art and download folders stay out of the way.
    /// </summary>
    public static IReadOnlyList<FolderNode> Build(IReadOnlyList<LibraryRoot> roots, IEnumerable<(long RootId, string Folder, int Count)> folderCounts)
    {
        var rootNodes = roots.ToDictionary(r => r.Id, r => new FolderNode(r.Id, r.Name, "", r.Path));
        var nodes = new Dictionary<(long, string), FolderNode>();
        foreach (var (id, node) in rootNodes)
            nodes[(id, "")] = node;

        foreach (var (rootId, folder, count) in folderCounts)
        {
            if (!rootNodes.TryGetValue(rootId, out FolderNode? rootNode))
                continue;

            rootNode.TrackCount += count;
            if (folder.Length == 0)
                continue;

            // Walk down the path, creating intermediate folders and adding the count to each.
            FolderNode parent = rootNode;
            int start = 0;
            while (true)
            {
                int slash = folder.IndexOf('/', start);
                string relative = slash < 0 ? folder : folder[..slash];
                if (!nodes.TryGetValue((rootId, relative), out FolderNode? node))
                {
                    string name = slash < 0 ? folder[start..] : folder[start..slash];
                    node = new FolderNode(rootId, name, relative, Path.Combine(rootNode.FullPath, relative.Replace('/', Path.DirectorySeparatorChar)));
                    nodes[(rootId, relative)] = node;
                    parent.AddChild(node);
                }
                node.TrackCount += count;
                if (slash < 0)
                    break;
                parent = node;
                start = slash + 1;
            }
        }

        var result = roots.Select(r => rootNodes[r.Id]).ToList();
        foreach (FolderNode root in result)
            root.SortChildren();
        return result;
    }
}

/// <summary>Case-insensitive ordering that puts "Disc 2" before "Disc 10".</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static NaturalComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int si = i, sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                string nx = x[si..i].TrimStart('0'), ny = y[sj..j].TrimStart('0');
                int cmp = nx.Length != ny.Length ? nx.Length.CompareTo(ny.Length) : string.CompareOrdinal(nx, ny);
                if (cmp != 0) return cmp;
            }
            else
            {
                int cmp = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (cmp != 0) return cmp;
                i++; j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
