namespace GZJam.Library;

/// <summary>
/// The library was upgraded by a newer GZJam, in a way this one can't read: this GZJam won't
/// open it (nor change it), so nothing is lost; the newer GZJam will.
/// </summary>
public sealed class LibraryTooNewException(long version, long known)
    : InvalidOperationException($"The library is from a newer version of GZJam (upgrade {version}; this version knows up to {known}).")
{
    /// <summary>The library's upgrade number.</summary>
    public long Version { get; } = version;

    /// <summary>The newest upgrade this GZJam knows.</summary>
    public long Known { get; } = known;
}
