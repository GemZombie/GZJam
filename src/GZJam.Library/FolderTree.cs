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
    public IReadOnlyList<FolderNode> Children => _children;
    public FolderScope Scope => new(RootId, RelativePath);

    internal void AddChild(FolderNode child) => _children.Add(child);
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
