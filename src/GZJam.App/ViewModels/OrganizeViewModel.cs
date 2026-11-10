using System.Collections.ObjectModel;
using Avalonia.Controls.Selection;
using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;

namespace GZJam.App.ViewModels;

/// <summary>A row of one of Organize's sides: a folder to go into, or a song.</summary>
public sealed partial class OrganizeItem : ObservableObject
{
    public OrganizeItem(string name, string path)
    {
        Name = name;
        Path = path;
    }

    public OrganizeItem(Track track, bool isLocked)
    {
        Name = track.DisplayTitle;
        Path = track.Path;
        Track = track;
        IsLocked = isLocked;
    }

    public string Name { get; }

    public string Path { get; }

    /// <summary>The song; null for a folder.</summary>
    public Track? Track { get; }

    public bool IsSong => Track is not null;

    public bool IsFolder => Track is null;

    public string Title => Name;

    public string Artist => Track?.DisplayArtist ?? "";

    /// <summary>On a deck: it can be copied, not moved.</summary>
    public bool IsLocked { get; }

    /// <summary>Just arrived here: it's lit until the DJ goes on to something else.</summary>
    [ObservableProperty]
    public partial bool IsFresh { get; set; }

    /// <summary>A folder with songs being dragged over it.</summary>
    [ObservableProperty]
    public partial bool IsDropTarget { get; set; }
}

/// <summary>A step of a side's path: its name, and where it goes (null: the source's own top).</summary>
public sealed record OrganizeCrumb(string Name, string? Path, bool IsFirst = false, bool IsLast = false)
{
    public bool HasArrow => !IsFirst;
}

/// <summary>
/// One side of Organize (#325): a source, and one list of the folder you're in, its folders first.
/// The folder you're in is where songs sent here go, so there's nothing else to choose.
/// </summary>
public sealed partial class OrganizePane : ObservableObject
{
    public const string MyMusic = "My music";
    public const string Files = "Files";

    private readonly OrganizeViewModel _owner;
    private List<OrganizeItem> _all = [];
    private HashSet<string> _fresh = new(StringComparer.Ordinal);
    private int _listing;

    internal OrganizePane(OrganizeViewModel owner)
    {
        _owner = owner;
        Selection.Source = Items;
        Selection.SingleSelect = false;
        Selection.SelectionChanged += (_, _) => _owner.SelectionChanged(this);
    }

    public IReadOnlyList<string> Sources { get; } = [MyMusic, Files];

    public ObservableCollection<OrganizeItem> Items { get; } = [];

    /// <summary>What's selected: the list's own selection, so a click, Shift and Ctrl work as in the song list.</summary>
    public SelectionModel<OrganizeItem> Selection { get; } = new();

    public ObservableCollection<OrganizeCrumb> Crumbs { get; } = [];

    public IEnumerable<OrganizeItem> Songs => Items.Where(i => i.IsSong);

    /// <summary>The songs selected (a folder selected is only somewhere to go).</summary>
    public IReadOnlyList<OrganizeItem> Selected => Selection.SelectedItems.OfType<OrganizeItem>().Where(i => i.IsSong).ToList();

    [ObservableProperty]
    public partial string? SelectedSource { get; set; }

    /// <summary>The folder this side is in; null at the top of a source with several places.</summary>
    [ObservableProperty]
    public partial string? Folder { get; private set; }

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty]
    public partial bool IsListing { get; private set; }

    /// <summary>Songs are being dragged over this side, to go in the folder it's in.</summary>
    [ObservableProperty]
    public partial bool IsDropTarget { get; set; }

    /// <summary>This side's songs are the ones going: its edge is lit.</summary>
    [ObservableProperty]
    public partial bool IsSending { get; set; }

    public bool IsEmpty => !IsListing && Items.Count == 0;

    public string EmptyText => Filter.Length > 0 ? $"Nothing here has \"{Filter.Trim()}\" in its name." : "No folders or songs here.";

    /// <summary>Where this side is, by name: "Music › Lindy › Slow".</summary>
    public string FolderText => string.Join(" › ", Crumbs.Select(c => c.Name));

    /// <summary>The last step of where this side is: "Slow".</summary>
    public string FolderName => Crumbs.LastOrDefault()?.Name ?? "";

    partial void OnSelectedSourceChanged(string? value)
    {
        if (!_restoring)
            _ = GoToAsync(_owner.StartOf(value));
    }

    partial void OnFilterChanged(string value) => ShowItems();

    private bool _restoring;

    /// <summary>Opens on a source and a folder of it.</summary>
    internal async Task StartAsync(string source, string? folder)
    {
        _restoring = true;
        SelectedSource = source;
        _restoring = false;
        await GoToAsync(folder ?? _owner.StartOf(source));
    }

    /// <summary>Goes to a folder (null: the top of the source), and reads what's in it.</summary>
    public async Task GoToAsync(string? folder)
    {
        Filter = "";
        _fresh.Clear();
        Folder = folder;
        SetCrumbs();
        _owner.Went(this);
        await ListAsync();
    }

    /// <summary>Goes up a folder, if there's anywhere to go.</summary>
    public Task UpAsync() => Crumbs.Count > 1 ? GoToAsync(Crumbs[^2].Path) : Task.CompletedTask;

    /// <summary>Reads what's here again, as it is on the disk now.</summary>
    public async Task ListAsync()
    {
        int version = ++_listing;
        IsListing = true;
        OnPropertyChanged(nameof(IsEmpty));
        string? folder = Folder;
        List<OrganizeItem> items = await _owner.ReadAsync(SelectedSource, folder);
        if (version != _listing)
            return;
        foreach (OrganizeItem item in items)
            item.IsFresh = _fresh.Contains(item.Path);
        _all = items;
        IsListing = false;
        ShowItems();
        _owner.Changed();
    }

    /// <summary>These just arrived: lit, until the DJ goes on to something else.</summary>
    internal void MarkFresh(IEnumerable<string> paths) => _fresh = paths.ToHashSet(StringComparer.Ordinal);

    private void ShowItems()
    {
        string[] words = Filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Items.Clear();
        foreach (OrganizeItem item in _all)
            if (words.All(w => item.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase) || item.Artist.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                               || System.IO.Path.GetFileName(item.Path).Contains(w, StringComparison.CurrentCultureIgnoreCase)))
                Items.Add(item);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void SetCrumbs()
    {
        Crumbs.Clear();
        var places = _owner.PlacesOf(SelectedSource);
        if (places.Count != 1 || Folder is null)
            Crumbs.Add(new OrganizeCrumb(SelectedSource ?? "", null));
        if (Folder is { } folder && places.Where(p => FileSources.IsIn(folder, p.Path)).MaxBy(p => p.Path.Length) is { } place)
        {
            Crumbs.Add(new OrganizeCrumb(place.Name, place.Path));
            string walked = place.Path;
            string inside = System.IO.Path.GetRelativePath(place.Path, folder);
            if (inside != ".")
                foreach (string part in inside.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
                {
                    walked = System.IO.Path.Combine(walked, part);
                    Crumbs.Add(new OrganizeCrumb(part, walked));
                }
        }
        for (int i = 0; i < Crumbs.Count; i++)
            Crumbs[i] = Crumbs[i] with { IsFirst = i == 0, IsLast = i == Crumbs.Count - 1 };
        OnPropertyChanged(nameof(FolderText));
        OnPropertyChanged(nameof(FolderName));
    }
}

/// <summary>
/// Organize music (#325): two sides, each any source, and songs sent between them. GZJam does what
/// the file system would, and the library shows where the files ended up. In the main screen a
/// drag never moves a file without the DJ saying so (#335). A move never loses what the DJ gave a song.
/// </summary>
public sealed partial class OrganizeViewModel : ObservableObject
{
    private readonly MainWindowViewModel _owner;
    private readonly LibraryDatabase _database;
    private readonly AppSettings _settings;
    private OrganizePane? _from;
    private CancellationTokenSource? _stop;
    private List<(string From, string To)> _lastMoves = [];
    private bool _starting;

    public OrganizeViewModel(MainWindowViewModel owner, LibraryDatabase database, AppSettings settings)
    {
        _owner = owner;
        _database = database;
        _settings = settings;
        Left = new OrganizePane(this);
        Right = new OrganizePane(this);
    }

    public OrganizePane Left { get; }

    public OrganizePane Right { get; }

    /// <summary>The headphones, for the player under the two sides.</summary>
    public PreviewViewModel? Player => _owner.Preview;

    /// <summary>The DJ went back to the library with songs still on their way: a notice says how it ended.</summary>
    internal bool TellWhenDone { get; set; }

    /// <summary>How a file is copied. Tests make it fail partway.</summary>
    internal Action<string, string> CopyFile { get; set; } = (from, to) => File.Copy(from, to, overwrite: true);

    /// <summary>Whether two paths are on one drive, so a move is only a rename. Tests say no.</summary>
    internal Func<string, string, bool> SameDrive { get; set; } = (a, b) =>
        string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? Status { get; private set; }

    [ObservableProperty]
    public partial bool CanUndo { get; private set; }

    public bool CanStop => IsBusy;

    private OrganizePane? From => _from is { Selected.Count: > 0 } ? _from : null;

    private OrganizePane? To => From == Left ? Right : From == Right ? Left : null;

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether songs of this side can go to a folder at all.</summary>
    private bool CanGo(OrganizePane? from, string? to) =>
        !IsBusy && from is { Folder: { } here } && to is not null && !SamePath(here, to) && !IsProtected(to);

    public bool CanCopy => CanGo(From, To?.Folder);

    public bool CanMove => CanCopy && !IsProtected(From!.Folder!) && From.Selected.Any(s => !s.IsLocked);

    /// <summary>True when songs go left to right: the arrows on the buttons say so.</summary>
    public bool GoesRight => From != Right;

    public string MoveLabel => From is null ? "MOVE" : GoesRight ? "MOVE  →" : "←  MOVE";

    public string CopyLabel => From is null ? "COPY" : GoesRight ? "COPY  →" : "←  COPY";

    public int SelectedCount => From?.Selected.Count ?? 0;

    public bool HasSelected => SelectedCount > 0;

    public string CountWord => SelectedCount == 1 ? "song" : "songs";

    /// <summary>What Move will do to the library, under its button.</summary>
    public string MoveNote => From is { Folder: { } from } && To is { Folder: { } to } ? NoteFor(from, to, move: true, From.Selected) : "";

    /// <summary>What Copy will do to the library, under its button.</summary>
    public string CopyNote => From is { Folder: { } from } && To is { Folder: { } to } ? NoteFor(from, to, move: false, From.Selected) : "";

    public string Hint => From is null ? "Select songs, then drag them across or use the buttons."
        : To?.Folder is null ? "Open a folder on the other side for them to go to."
        : $"To {To.FolderText}";

    private string NoteFor(string from, string to, bool move, IReadOnlyList<OrganizeItem> songs)
    {
        if (SamePath(from, to))
            return "already here";
        if (IsProtected(to))
            return "GZJam looks after the downloads";
        if (move && IsProtected(from))
            return "GZJam looks after the downloads";
        if (move && songs.All(s => s.IsLocked))
            return songs.Count == 1 ? "it's on a deck" : "they're on a deck";
        bool fromMine = InMyMusic(from), toMine = InMyMusic(to);
        string note = (move, fromMine, toMine) switch
        {
            (true, false, true) => "join My music",
            (true, true, false) => "leave My music",
            (true, true, true) => "same songs, new place",
            (true, false, false) => "My music isn't affected",
            (false, false, true) => "originals stay, copies join My music",
            (false, true, false) => "stay in My music",
            (false, true, true) => "a second copy in My music",
            _ => "originals stay",
        };
        int locked = songs.Count(s => s.IsLocked);
        return move && locked > 0 ? $"{note}; {locked} on a deck stay{(locked == 1 ? "s" : "")}" : note;
    }

    /// <summary>What the tag on a drag says; null if the songs can't go there.</summary>
    public string? DragText(OrganizePane from, string? to, bool copy)
    {
        if (!CanGo(from, to) || (!copy && (IsProtected(from.Folder!) || from.Selected.All(s => s.IsLocked))))
            return null;
        string count = from.Selected.Count == 1 ? from.Selected[0].Name : SongCount(from.Selected.Count);
        return $"{(copy ? "Copy" : "Move")} {count} to {Path.GetFileName(Path.TrimEndingDirectorySeparator(to!))}";
    }

    /// <summary>Opens: on a folder if one was right-clicked, or where each side was last time.</summary>
    public async Task StartAsync(string? folder = null)
    {
        _starting = true;
        string left = folder is not null ? (InMyMusic(folder) ? OrganizePane.MyMusic : OrganizePane.Files) : _settings.OrganizeLeftSource ?? OrganizePane.MyMusic;
        string right = _settings.OrganizeRightSource ?? (left == OrganizePane.MyMusic ? OrganizePane.Files : OrganizePane.MyMusic);
        await Left.StartAsync(left, folder ?? Usable(left, _settings.OrganizeLeftFolder));
        await Right.StartAsync(right, Usable(right, _settings.OrganizeRightFolder));
        _starting = false;
    }

    private string? Usable(string source, string? folder) =>
        folder is not null && Directory.Exists(folder) && PlacesOf(source).Any(p => FileSources.IsIn(folder, p.Path)) && !IsProtected(folder) ? folder : null;

    internal void Went(OrganizePane pane)
    {
        Changed();
        if (_starting)
            return;
        if (pane == Left)
        {
            _settings.OrganizeLeftSource = pane.SelectedSource;
            _settings.OrganizeLeftFolder = pane.Folder;
        }
        else
        {
            _settings.OrganizeRightSource = pane.SelectedSource;
            _settings.OrganizeRightFolder = pane.Folder;
        }
    }

    /// <summary>The places at the top of a source.</summary>
    internal IReadOnlyList<FilePlace> PlacesOf(string? source)
    {
        if (source == OrganizePane.MyMusic)
            return _owner.Library.MusicFolders.Select(f => new FilePlace(Name(f), f, FilesKind.Folder)).ToList();
        if (source == OrganizePane.Files)
            return [.. _owner.Files.Places(), .. _owner.Files.Drives(), .. _owner.Files.Sticks()];
        return [];

        static string Name(string folder) => Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) is { Length: > 0 } name ? name : folder;
    }

    /// <summary>Where a source opens: inside its only place, or at the top of several.</summary>
    internal string? StartOf(string? source) => PlacesOf(source) is { Count: 1 } one ? one[0].Path : null;

    /// <summary>A folder's rows, its folders first; or a source's places.</summary>
    internal Task<List<OrganizeItem>> ReadAsync(string? source, string? folder)
    {
        var places = folder is null ? PlacesOf(source) : [];
        var locked = OnDecks();
        string? notThis = source == OrganizePane.MyMusic ? _owner.Servers.DownloadsFolder : null;
        return Task.Run(() =>
        {
            if (folder is null)
                return places.Select(p => new OrganizeItem(p.Name, p.Path)).ToList();
            var rows = FileSources.Folders(folder).Where(f => notThis is null || !FileSources.IsIn(f.Path, notThis))
                .Select(f => new OrganizeItem(f.Name, f.Path)).ToList();
            rows.AddRange(_database.TracksForFiles(FileSources.Songs(folder).Select(f => f.FullName).ToList())
                .Select(t => new OrganizeItem(t, locked.Contains(t.Path))));
            return rows;
        });
    }

    internal HashSet<string> OnDecks() => _owner.Decks.Select(d => d.Track?.Path).OfType<string>().ToHashSet(StringComparer.Ordinal);

    internal void SelectionChanged(OrganizePane pane)
    {
        if (pane.Selected.Count > 0 && _from != pane)
        {
            _from = pane;
            (pane == Left ? Right : Left).Selection.Clear();   // one side at a time: the arrows say which
        }
        Changed();
    }

    internal void Changed()
    {
        Left.IsSending = From == Left;
        Right.IsSending = From == Right;
        foreach (string name in (string[])[nameof(CanCopy), nameof(CanMove), nameof(GoesRight), nameof(MoveLabel), nameof(CopyLabel), nameof(SelectedCount),
                     nameof(HasSelected), nameof(CountWord), nameof(MoveNote), nameof(CopyNote), nameof(Hint)])
            OnPropertyChanged(name);
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStop));
        Changed();
    }

    private bool InMyMusic(string path) => _owner.Library.MusicFolders.Any(f => FileSources.IsIn(path, f)) && !IsProtected(path);

    private bool IsProtected(string path) => FileSources.IsIn(path, _owner.Servers.DownloadsFolder);

    /// <summary>Plays a song in the headphones, or stops it.</summary>
    public void Preview(OrganizeItem item)
    {
        if (item.Track is { } track)
            _owner.Preview?.Toggle(track);
    }

    public void Stop() => _stop?.Cancel();

    public Task CopyAsync() => From is { } from && To is { Folder: { } to } ? SendAsync(from, to, move: false) : Task.CompletedTask;

    public Task MoveAsync() => From is { } from && To is { Folder: { } to } ? ConfirmedMoveAsync(from, to) : Task.CompletedTask;

    /// <summary>Songs dropped: into a folder on either side, or the folder the other side is in.</summary>
    public Task DropAsync(OrganizePane from, string to, bool copy) => copy ? SendAsync(from, to, move: false) : ConfirmedMoveAsync(from, to);

    private async Task ConfirmedMoveAsync(OrganizePane from, string to)
    {
        if (await WarnOfFirstMoveAsync())
            await SendAsync(from, to, move: true);
    }

    /// <summary>The first move ever says other apps lose track of moved files, once. False if the DJ said no.</summary>
    internal async Task<bool> WarnOfFirstMoveAsync()
    {
        if (_settings.OrganizeMoveWarned)
            return true;
        if (_owner.ConfirmAsync is not { } confirm || !await confirm("Move these files?",
                "Other apps that know these files, such as MediaMonkey, iTunes or foobar2000, will lose track of them: their ratings and playlists there still point at the old place. GZJam's own hearts, cues and playlists come along. GZJam won't ask again.",
                "Move"))
            return false;
        _settings.OrganizeMoveWarned = true;
        return true;
    }

    /// <summary>
    /// Songs from the queue moved to a folder (#335): the same move as between the sides, so
    /// whole in one place or the other however it ends, with a notice and Undo.
    /// </summary>
    internal async Task MoveSongsAsync(IReadOnlyList<string> songs, string folder)
    {
        var done = await RunAsync(songs.Select(s => (s, Path.Combine(folder, Path.GetFileName(s)))).ToList(), move: true, numbered: true);
        TellWhenDone = false;
        Report($"Moved {SongCount(done.Finished.Count)} to {Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))}.", done);
        _lastMoves = done.Finished;
        CanUndo = _lastMoves.Count > 0;
        await AfterAsync(done.Finished, move: true);
        _owner.ShowNotice(Status!, 8, undo: CanUndo ? () =>
        {
            TellWhenDone = true;   // Organize may not be open to say it
            _ = UndoAsync();
        } : null);
    }

    private async Task SendAsync(OrganizePane from, string folder, bool move)
    {
        if (!CanGo(from, folder) || (move && (IsProtected(from.Folder!) || from.Selected.All(s => s.IsLocked))))
            return;
        var songs = from.Selected.Where(s => !move || !s.IsLocked).Select(s => s.Path).ToList();
        var done = await RunAsync(songs.Select(s => (s, Path.Combine(folder, Path.GetFileName(s)))).ToList(), move, numbered: true);
        string verb = move ? "Moved" : "Copied";
        Report($"{verb} {SongCount(done.Finished.Count)} to {Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))}.", done);
        _lastMoves = move ? done.Finished : [];
        CanUndo = _lastMoves.Count > 0;
        await AfterAsync(done.Finished, move);
    }

    /// <summary>Puts the last move back, and what GZJam knows of the songs with it.</summary>
    public async Task UndoAsync()
    {
        if (!CanUndo || IsBusy)
            return;
        var back = _lastMoves.Select(m => (m.To, m.From)).ToList();
        _lastMoves = [];
        CanUndo = false;
        var done = await RunAsync(back, move: true, numbered: false);
        Report($"Put {SongCount(done.Finished.Count)} back.", done);
        await AfterAsync(done.Finished, true);
    }

    private sealed record Outcome(List<(string From, string To)> Finished, string? Failed, bool Stopped);

    private void Report(string text, Outcome done)
    {
        Status = done.Failed is { } failed ? $"{text} {failed}" : done.Stopped ? $"Stopped. {text}" : text;
        if (TellWhenDone)
            _owner.ShowNotice(Status, 8);
        TellWhenDone = false;
    }

    private static string SongCount(int n) => n == 1 ? "1 song" : $"{Format.Count(n)} songs";

    private async Task<Outcome> RunAsync(List<(string From, string To)> steps, bool move, bool numbered)
    {
        IsBusy = true;
        _stop = new CancellationTokenSource();
        var finished = new List<(string From, string To)>();
        string? failed = null;
        bool stopped = false;
        var locked = OnDecks();
        if (move && _owner.Preview?.Track is { } heard && steps.Any(s => s.From == heard.Path))
            _owner.Preview.Stop();
        try
        {
            await _owner.Library.WithoutScansAsync(async () =>
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    if (_stop.IsCancellationRequested)
                    {
                        stopped = true;
                        break;
                    }
                    var (source, wanted) = steps[i];
                    if (move && locked.Contains(source))
                        continue;
                    Status = $"{(move ? "Moving" : "Copying")} {i + 1} of {steps.Count}: {Path.GetFileName(source)}";
                    try
                    {
                        string to = await Task.Run(() => Transfer(source, wanted, move, numbered));
                        finished.Add((source, to));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        Log.Info($"Organize: couldn't {(move ? "move" : "copy")} {source}: {e.Message}");
                        failed ??= $"Couldn't {(move ? "move" : "copy")} {Path.GetFileName(source)}: {e.Message}";
                    }
                }
                return true;
            });
        }
        finally
        {
            _stop = null;
            IsBusy = false;
        }
        Log.Info($"Organize: {(move ? "moved" : "copied")} {finished.Count} of {steps.Count} songs");
        return new Outcome(finished, failed, stopped);
    }

    /// <summary>One song, on its way: whole in one place or the other, however it ends. Returns where it went.</summary>
    private string Transfer(string from, string wanted, bool move, bool numbered)
    {
        string to = numbered ? FreePath(wanted) : wanted;
        if (!numbered && File.Exists(to))
            throw new IOException($"There's a file at {to} now.");
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (move && SameDrive(from, to))
            BusyFile.Move(from, to);   // never over a file: one there stops it
        else
            CopyWhole(from, to);
        if (!move)
        {
            _database.CopySongFiles([(from, to)]);
            return to;
        }
        if (File.Exists(from))
        {
            try
            {
                BusyFile.Delete(from);   // only now the copy is whole
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                File.Delete(to);
                throw;
            }
        }
        if (InMyMusic(from) && !InMyMusic(to))
            _database.MoveSongFilesOut([(from, to)]);
        else
            _database.MoveSongFiles([(from, to)]);
        return to;
    }

    private void CopyWhole(string from, string to)
    {
        string partial = to + ".part";
        try
        {
            CopyFile(from, partial);
            File.SetLastWriteTimeUtc(partial, File.GetLastWriteTimeUtc(from));
            File.Move(partial, to);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(partial))
                File.Delete(partial);
            throw;
        }
    }

    /// <summary>A path nothing's at: a song never replaces another, it gets a number.</summary>
    private static string FreePath(string path)
    {
        string folder = Path.GetDirectoryName(path)!, name = Path.GetFileNameWithoutExtension(path), extension = Path.GetExtension(path);
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{name} ({n}){extension}");
        return path;
    }

    /// <summary>The library shows where the files ended up; both sides show them too, the new ones lit.</summary>
    private async Task AfterAsync(List<(string From, string To)> finished, bool move)
    {
        var roots = finished.SelectMany(f => new[] { f.From, f.To })
            .Where(InMyMusic).Select(p => _owner.Servers.RootOf(p)?.Id).OfType<long>().Distinct().ToList();
        foreach (long root in roots)
            await _owner.Library.ScanRootAsync(root);
        if (move && finished.Count > 0)
            _owner.Library.FollowMoves(finished);
        else
            _owner.Library.RefreshResults();
        _owner.Library.RefreshPlaylists();
        var fresh = finished.Select(f => f.To).ToList();
        Left.MarkFresh(fresh);
        Right.MarkFresh(fresh);
        await Left.ListAsync();
        await Right.ListAsync();
    }
}
