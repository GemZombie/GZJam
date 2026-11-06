using Avalonia.Controls;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// Free up space… on a server (#290): what to remove from the laptop, with how much each frees and
/// what's left on the drive. Closes with true once removed.
/// </summary>
public partial class FreeSpaceDialog : Window
{
    private readonly ServersViewModel? _servers;
    private readonly FreeUpOffer? _offer;

    public FreeSpaceDialog() : this(null, null, "")
    {
    }

    public FreeSpaceDialog(ServersViewModel? servers, FreeUpOffer? offer, string serverName)
    {
        InitializeComponent();
        _servers = servers;
        _offer = offer;
        Heading.Text = $"FREE UP SPACE FROM {serverName.ToUpperInvariant()}";
        EverythingName.Text = $"Everything downloaded from {serverName}";
        if (offer is not null)
        {
            LastNightText.Text = Line(offer.LastNight, "Nothing downloaded on the spot during the last night");
            NotPlayedText.Text = Line(offer.NotPlayedLately, "Nothing: every download was played or came in the last 4 weeks");
            EverythingText.Text = Line(offer.Everything, "Nothing downloaded that can be removed now")
                + (offer.Protected > 0 ? $" ({Format.Count(offer.Protected)} kept: on a deck, in the queue or played tonight)" : "");
            LastNightBox.IsEnabled = offer.LastNight.Count > 0;
            NotPlayedBox.IsEnabled = offer.NotPlayedLately.Count > 0;
            EverythingBox.IsEnabled = offer.Everything.Count > 0;
            LastNightBox.IsChecked = offer.LastNight.Count > 0;
        }
        foreach (CheckBox box in (CheckBox[])[LastNightBox, NotPlayedBox, EverythingBox])
            box.IsCheckedChanged += (_, _) => Total();
        Total();
        CancelButton.Click += (_, _) => Close(false);
        RemoveButton.Click += async (_, _) =>
        {
            if (_servers is null)
                return;
            RemoveButton.IsEnabled = false;
            await _servers.RemoveNowAsync(Chosen());
            Close(true);
        };
    }

    private static string Line(IReadOnlyCollection<ServerDownload> downloads, string none) =>
        downloads.Count == 0 ? none : ServersViewModel.Describe(downloads);

    /// <summary>The songs ticked, each once.</summary>
    public IReadOnlyList<ServerDownload> Chosen()
    {
        if (_offer is null)
            return [];
        var chosen = new List<ServerDownload>();
        if (EverythingBox.IsChecked == true)
            chosen.AddRange(_offer.Everything);
        else
        {
            if (LastNightBox.IsChecked == true)
                chosen.AddRange(_offer.LastNight);
            if (NotPlayedBox.IsChecked == true)
                chosen.AddRange(_offer.NotPlayedLately);
        }
        return chosen.DistinctBy(d => d.SongId).ToList();
    }

    private void Total()
    {
        var chosen = Chosen();
        long freed = chosen.Sum(d => d.Size);
        RemoveButton.IsEnabled = chosen.Count > 0;
        TotalText.Text = chosen.Count == 0
            ? "Tick what to remove."
            : $"Removes {ServersViewModel.Describe(chosen)}"
              + (_offer?.DiskFree is { } free ? $": {Format.Size(free)} free on the drive now, {Format.Size(free + freed)} after." : ".");
    }
}
