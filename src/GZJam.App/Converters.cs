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

    /// <summary>How long ago, briefly: "Today", "3 days", "2 wk", "5 mo", "2 yr"; "" for never.</summary>
    public static string Ago(DateTime? utc, DateTime? nowUtc = null)
    {
        if (utc is not { } then)
            return "";
        DateTime now = (nowUtc ?? DateTime.UtcNow).ToLocalTime();
        DateTime local = then.ToLocalTime();
        int days = (now.Date - local.Date).Days;
        return days switch
        {
            <= 0 => "Today",
            1 => "1 day",
            < 14 => $"{days} days",
            < 60 => $"{days / 7} wk",
            < 730 => $"{days / 30} mo",
            _ => $"{days / 365} yr",
        };
    }
}

public static class Converters
{
    public static readonly IValueConverter Duration = new FuncValueConverter<TimeSpan, string>(t => Format.Duration(t));
    public static readonly IValueConverter Bpm = new FuncValueConverter<double?, string>(Format.Bpm);
    public static readonly IValueConverter Count = new FuncValueConverter<int, string>(Format.Count);
    public static readonly IValueConverter Ago = new FuncValueConverter<DateTime?, string>(t => Format.Ago(t));

    /// <summary>Played tonight: dimmed, so it isn't played again by mistake.</summary>
    public static readonly IValueConverter Dimmed = new FuncValueConverter<bool, double>(played => played ? 0.4 : 1);
}
