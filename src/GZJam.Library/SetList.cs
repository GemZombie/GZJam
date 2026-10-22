using System.Globalization;
using System.Text;

namespace GZJam.Library;

/// <summary>
/// A session's songs written out to share: a readable list (for the organisers, a Facebook post,
/// the DJ's notes) or CSV for a spreadsheet. Times are local; BPMs are as the room heard them,
/// with the tempo change applied.
/// </summary>
public static class SetList
{
    /// <summary>
    /// A plain text set list:
    /// <code>
    /// Friday Night, Oct 14
    /// Saturday, October 17, 2026 · 7:42 PM to 9:58 PM · 24 songs · 1:12:34
    ///
    ///  1  7:42 PM  Jumpin' at the Woodside · Count Basie · 236 BPM · 3:10
    /// </code>
    /// </summary>
    public static string Text(PlaySession session, IReadOnlyList<PlayRecord> plays, CultureInfo culture)
    {
        var text = new StringBuilder();
        DateTime started = (plays.Count > 0 ? plays[0].PlayedUtc : session.StartedUtc).ToLocalTime();
        text.AppendLine(session.Name ?? started.ToString("D", culture));

        var summary = new List<string>();
        if (session.Name is not null)
            summary.Add(started.ToString("D", culture));
        if (plays.Count > 0)
        {
            DateTime ended = (plays[^1].PlayedUtc + plays[^1].Duration).ToLocalTime();
            summary.Add($"{started.ToString("t", culture)} to {ended.ToString("t", culture)}");
        }
        summary.Add(plays.Count == 1 ? "1 song" : $"{plays.Count} songs");
        summary.Add(Length(TimeSpan.FromTicks(plays.Sum(p => p.Duration.Ticks))));
        text.AppendLine(string.Join(" · ", summary));
        text.AppendLine();

        int width = plays.Count.ToString(CultureInfo.InvariantCulture).Length;
        string[] times = plays.Select(p => p.PlayedUtc.ToLocalTime().ToString("t", culture)).ToArray();
        int timeWidth = times.Length == 0 ? 0 : times.Max(t => t.Length);
        for (int i = 0; i < plays.Count; i++)
        {
            PlayRecord play = plays[i];
            var parts = new List<string> { play.DisplayTitle };
            if (!string.IsNullOrWhiteSpace(play.Artist))
                parts.Add(play.Artist);
            if (PlayedBpm(play) is { } bpm)
                parts.Add($"{Math.Round(bpm).ToString(culture)} BPM");
            parts.Add(Length(play.Duration));
            text.Append((i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width + 1))
                .Append("  ").Append(times[i].PadLeft(timeWidth))
                .Append("  ").AppendLine(string.Join(" · ", parts));
        }
        return text.ToString();
    }

    /// <summary>The same songs as CSV, one row each, for a spreadsheet. Numbers and dates are culture-neutral.</summary>
    public static string Csv(IReadOnlyList<PlayRecord> plays)
    {
        var csv = new StringBuilder();
        csv.AppendLine("#,Date,Time,Title,Artist,Album,BPM,Tempo,Length,Deck,File");
        for (int i = 0; i < plays.Count; i++)
        {
            PlayRecord play = plays[i];
            DateTime local = play.PlayedUtc.ToLocalTime();
            string[] fields =
            [
                (i + 1).ToString(CultureInfo.InvariantCulture),
                local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                local.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                play.DisplayTitle,
                play.Artist ?? "",
                play.Album ?? "",
                PlayedBpm(play) is { } bpm ? bpm.ToString("0.#", CultureInfo.InvariantCulture) : "",
                play.TempoPercent == 0 ? "" : play.TempoPercent.ToString("+0.0'%';-0.0'%'", CultureInfo.InvariantCulture),
                Length(play.Duration),
                play.Deck ?? "",
                play.Path,
            ];
            csv.AppendLine(string.Join(",", fields.Select(Quote)));
        }
        return csv.ToString();
    }

    /// <summary>A file name for a session's set list, without the extension: "Set list - Friday Night, Oct 14 - 2026-10-17".</summary>
    public static string FileName(PlaySession session, IReadOnlyList<PlayRecord> plays)
    {
        DateTime started = (plays.Count > 0 ? plays[0].PlayedUtc : session.StartedUtc).ToLocalTime();
        string date = started.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string name = session.Name is { } n ? $"Set list - {n} - {date}" : $"Set list - {date}";
        // Characters no file system accepts (or that Windows rejects) become dashes.
        char[] invalid = [.. Path.GetInvalidFileNameChars(), '<', '>', ':', '"', '/', '\\', '|', '?', '*'];
        return string.Concat(name.Select(c => invalid.Contains(c) ? '-' : c)).Trim().TrimEnd('.');
    }

    /// <summary>The tempo the room heard: the track's BPM with the deck's tempo change.</summary>
    private static double? PlayedBpm(PlayRecord play) => play.Bpm is { } bpm ? bpm * (1 + play.TempoPercent / 100) : null;

    private static string Length(TimeSpan length) =>
        length.TotalHours >= 1
            ? $"{(int)length.TotalHours}:{length.Minutes:00}:{length.Seconds:00}"
            : $"{(int)length.TotalMinutes}:{length.Seconds:00}";

    private static string Quote(string field) =>
        field.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
}
