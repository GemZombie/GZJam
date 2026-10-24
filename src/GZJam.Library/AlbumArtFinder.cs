namespace GZJam.Library;

/// <summary>A song's picture, as found: the encoded image (JPEG, PNG…) and where it came from.</summary>
public sealed record AlbumArtSource(byte[] Data, bool FromFolder);

/// <summary>
/// Finds a song's album art: the picture in the file, or failing that an image in its folder
/// (cover.jpg, folder.jpg…). Only reads; music files are never changed, and nothing is downloaded.
/// </summary>
public static class AlbumArtFinder
{
    /// <summary>
    /// Which way of finding pictures a song's art was found with. Raise it whenever pictures are
    /// found better: every song's art is looked for again. 1: the first.
    /// </summary>
    public const int Version = 1;

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>Folder images by name, best first; any other name is only used if it's the folder's one image.</summary>
    private static readonly string[] PreferredNames = ["cover", "folder", "front"];

    /// <summary>Parts of names that mean the image isn't the front cover.</summary>
    private static readonly string[] NotTheFront = ["back", "disc", "disk", "cd", "inlay", "inside", "tray", "booklet", "small"];

    /// <summary>The song's picture, or null if it has none (or can't be read).</summary>
    public static AlbumArtSource? Find(string path)
    {
        if (Embedded(path) is { } data)
            return new AlbumArtSource(data, FromFolder: false);
        if (Path.GetDirectoryName(path) is { } folder && FolderImage(folder) is { } image)
        {
            try
            {
                return new AlbumArtSource(File.ReadAllBytes(image), FromFolder: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return null;
    }

    /// <summary>The picture in the file: the front cover if it's marked, otherwise the first.</summary>
    private static byte[]? Embedded(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            TagLib.IPicture[] pictures = file.Tag.Pictures;
            TagLib.IPicture? picture = pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover && p.Data.Count > 0)
                ?? pictures.FirstOrDefault(p => p.Data.Count > 0);
            return picture?.Data.Data;
        }
        catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The folder's cover image, if it has one.</summary>
    internal static string? FolderImage(string folder)
    {
        try
        {
            string? name = ChooseFolderImage(Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>());
            return name is null ? null : Path.Combine(folder, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Of a folder's files, the one that's the front cover: cover.*, then folder.*, then
    /// front.* (also as part of a longer name, like "Album - Front.jpg"), else the only image
    /// that isn't plainly the back, the disc or the inlay.
    /// </summary>
    internal static string? ChooseFolderImage(IEnumerable<string> fileNames)
    {
        List<string> images = fileNames
            .Where(n => ImageExtensions.Contains(Path.GetExtension(n), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (string preferred in PreferredNames)
        {
            if (images.FirstOrDefault(n => Path.GetFileNameWithoutExtension(n).Equals(preferred, StringComparison.OrdinalIgnoreCase)) is { } exact)
                return exact;
        }
        foreach (string preferred in PreferredNames)
        {
            if (images.FirstOrDefault(n => Words(n).Contains(preferred) && !IsNotTheFront(n)) is { } named)
                return named;
        }
        List<string> others = images.Where(n => !IsNotTheFront(n)).ToList();
        return others.Count == 1 ? others[0] : null;
    }

    private static bool IsNotTheFront(string fileName) => Words(fileName).Any(NotTheFront.Contains);

    /// <summary>The words of a file name, lower case: "AlbumArt_Small.jpg" → albumart, small.</summary>
    private static string[] Words(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant()
            .Split([' ', '_', '-', '.', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries);
}
