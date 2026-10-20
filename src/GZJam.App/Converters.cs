using System.Globalization;
using Avalonia.Data.Converters;

namespace GZJam.App;

public static class Format
{
    /// <summary>"3:05", or "1:02:05" past an hour.</summary>
    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    /// <summary>Whole BPMs as "132"; half-steps keep their decimal ("98.5").</summary>
    public static string Bpm(double? bpm) => bpm switch
    {
        null => "",
        var b when Math.Abs(b.Value - Math.Round(b.Value)) < 0.05 => b.Value.ToString("0", CultureInfo.CurrentCulture),
        var b => b.Value.ToString("0.0", CultureInfo.CurrentCulture),
    };

    public static string Count(int n) => n.ToString("N0", CultureInfo.CurrentCulture);
}

public static class Converters
{
    public static readonly IValueConverter Duration = new FuncValueConverter<TimeSpan, string>(t => Format.Duration(t));
    public static readonly IValueConverter Bpm = new FuncValueConverter<double?, string>(Format.Bpm);
    public static readonly IValueConverter Count = new FuncValueConverter<int, string>(Format.Count);
}
