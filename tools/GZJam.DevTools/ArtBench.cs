using System.Diagnostics;
using System.Security.Cryptography;
using GZJam.Library;
using SkiaSharp;

namespace GZJam.DevTools;

/// <summary>
/// How much of a library has album art, and how long finding it and making the small copies
/// takes (decoded with SkiaSharp, as the app's Avalonia does). Read-only.
/// </summary>
internal static class ArtBench
{
    private static readonly string[] AudioExtensions = [".mp3", ".flac", ".m4a", ".ogg", ".opus", ".wav", ".aiff", ".aif"];

    public static int Run(string folder, int count)
    {
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => AudioExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();
        var sample = files.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
        Console.WriteLine($"{sample.Count:N0} songs of {files.Count:N0} in {folder}");

        int embedded = 0, fromFolder = 0, none = 0, undecodable = 0;
        var pictures = new HashSet<string>();
        var find = new Stopwatch();
        var decode = new Stopwatch();
        long biggest = 0;
        foreach (string file in sample)
        {
            find.Start();
            AlbumArtSource? source = AlbumArtFinder.Find(file);
            find.Stop();
            if (source is null)
            {
                none++;
                continue;
            }
            if (source.FromFolder) fromFolder++; else embedded++;
            biggest = Math.Max(biggest, source.Data.Length);
            if (!pictures.Add(Convert.ToHexStringLower(SHA256.HashData(source.Data))))
                continue;   // the app makes one small copy per picture
            decode.Start();
            try
            {
                using var bitmap = SKBitmap.Decode(source.Data);
                using var small = bitmap.Resize(new SKImageInfo(128, Math.Max(1, 128 * bitmap.Height / bitmap.Width)), new SKSamplingOptions(SKCubicResampler.Mitchell));
            }
            catch (Exception)
            {
                undecodable++;   // the app shows these songs without art
                Console.WriteLine($"  can't decode the picture of {file} ({source.Data.Length:N0} bytes, {(source.FromFolder ? "folder" : "file")})");
            }
            decode.Stop();
        }

        int n = Math.Max(1, sample.Count);
        Console.WriteLine($"  in the file   {embedded,6:N0}  {100.0 * embedded / n,5:0.0}%");
        Console.WriteLine($"  in the folder {fromFolder,6:N0}  {100.0 * fromFolder / n,5:0.0}%");
        Console.WriteLine($"  none          {none,6:N0}  {100.0 * none / n,5:0.0}%");
        Console.WriteLine($"  can't decode  {undecodable,6:N0}");
        Console.WriteLine($"  {pictures.Count:N0} different pictures; biggest {biggest / 1024:N0} KB");
        Console.WriteLine($"  finding: {find.Elapsed.TotalMilliseconds / n:0.0} ms a song; small copy: {decode.Elapsed.TotalMilliseconds / Math.Max(1, pictures.Count):0.0} ms a picture");
        return 0;
    }
}
