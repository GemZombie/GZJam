using Avalonia.Controls;
using GZJam.App.ViewModels;
using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.Views;

/// <summary>
/// CHOOSE… on a server's card (#283): what's starred there and each of its playlists, ticked to
/// keep on the laptop. Asks the server what it has when it opens.
/// </summary>
public partial class ServerChoicesDialog : Window
{
    private readonly ServersViewModel? _servers;
    private readonly ServerItemViewModel? _item;
    private readonly CancellationTokenSource _cancel = new();
    private readonly List<(CheckBox Box, ServerChoice Choice)> _choices = [];

    public ServerChoicesDialog() : this(null, null)
    {
    }

    public ServerChoicesDialog(ServersViewModel? servers, ServerItemViewModel? item)
    {
        InitializeComponent();
        _servers = servers;
        _item = item;
        if (item is not null)
            Heading.Text = $"KEEP ON THIS LAPTOP FROM {item.Name.ToUpperInvariant()}";
        CancelButton.Click += (_, _) => Close(false);
        SaveButton.Click += (_, _) => Save(sync: false);
        SyncButton.Click += (_, _) => Save(sync: true);
        Closed += (_, _) => _cancel.Cancel();
        Opened += async (_, _) => await LoadAsync();
    }

    /// <summary>The choices shown, ticked or not: for tests.</summary>
    public IReadOnlyList<(CheckBox Box, ServerChoice Choice)> Choices => _choices;

    public async Task LoadAsync()
    {
        if (_servers is null || _item is null)
            return;
        ServerOffer offer;
        try
        {
            offer = await _servers.LoadOfferAsync(_item, _cancel.Token);
        }
        catch (ServerException e)
        {
            Say($"Couldn't ask the server: {e.Message}", problem: true);
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var chosen = offer.Chosen.Select(c => (c.Kind, c.RemoteId)).ToHashSet();
        Add(ServerChoice.Starred, "What's starred", StarredText(offer.Starred), chosen.Contains((ServerChoiceKind.Starred, "")));
        foreach (ServerPlaylist playlist in offer.Playlists.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            Add(new ServerChoice(ServerChoiceKind.Playlist, playlist.Id, playlist.Name), playlist.Name,
                $"{(playlist.SongCount == 1 ? "1 song" : Format.Count(playlist.SongCount) + " songs")} · {Format.Duration(playlist.Length)}"
                + (playlist.Owner is { Length: > 0 } owner && owner != _item.Server.UserName ? $" · {owner}'s" : ""),
                chosen.Contains((ServerChoiceKind.Playlist, playlist.Id)));
        // A playlist chosen before that the server no longer has: shown, so it can be let go.
        foreach (ServerChoice gone in offer.Chosen.Where(c => c.Kind == ServerChoiceKind.Playlist && offer.Playlists.All(p => p.Id != c.RemoteId)))
            Add(gone, gone.Name, "No longer on the server", ticked: true);
        // Albums, artists and songs downloaded while browsing (#284): untick to stop keeping them up to date.
        foreach (ServerChoice browsed in offer.Chosen.Where(c => c.Kind is ServerChoiceKind.Album or ServerChoiceKind.Artist or ServerChoiceKind.Song))
            Add(browsed, browsed.Name, browsed.Kind switch
            {
                ServerChoiceKind.Album => "Album, downloaded while browsing",
                ServerChoiceKind.Artist => "Artist, downloaded while browsing",
                _ => "Song, downloaded while browsing",
            }, ticked: true);

        Say(offer.Playlists.Count == 0
            ? "The server has no playlists for this account yet: make one in its web page, or star songs and albums there."
            : "", problem: false);
        SaveButton.IsEnabled = SyncButton.IsEnabled = true;
    }

    private static string StarredText(ServerStarred starred)
    {
        if (starred.IsEmpty)
            return "Nothing starred yet: star songs, albums or artists in the server's web page or app";
        var parts = new List<string>();
        if (starred.Songs.Count > 0)
            parts.Add(starred.Songs.Count == 1 ? "1 song" : $"{Format.Count(starred.Songs.Count)} songs");
        if (starred.Albums.Count > 0)
            parts.Add(starred.Albums.Count == 1 ? "1 album" : $"{Format.Count(starred.Albums.Count)} albums");
        if (starred.Artists.Count > 0)
            parts.Add(starred.Artists.Count == 1 ? "1 artist" : $"{Format.Count(starred.Artists.Count)} artists");
        return string.Join(", ", parts);
    }

    private void Add(ServerChoice choice, string name, string detail, bool ticked)
    {
        var box = new CheckBox
        {
            IsChecked = ticked,
            Content = new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock { Text = name, Classes = { "setting" } },
                    new TextBlock { Text = detail, Classes = { "setting-help" } },
                },
            },
        };
        _choices.Add((box, choice));
        ChoiceList.Children.Add(box);
    }

    private void Save(bool sync)
    {
        if (_servers is null || _item is null)
            return;
        _servers.SaveChoices(_item, _choices.Where(c => c.Box.IsChecked == true).Select(c => c.Choice));
        Close(true);
        if (sync && _item.CanSync)
            _ = _servers.SyncAsync(_item);
    }

    private void Say(string text, bool problem)
    {
        Message.Text = text;
        Message.IsVisible = text.Length > 0;
        Message.Classes.Set("problem", problem);
    }
}
