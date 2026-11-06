using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GZJam.Library;
using GZJam.Library.Servers;

namespace GZJam.App.ViewModels;

/// <summary>What the DJ filled in to add or change a server.</summary>
public sealed record ServerEdit(string Name, string Address, string UserName, string? Password);

/// <summary>How adding or changing a server went: saved, or why not, and whether it may be saved anyway.</summary>
public sealed record ServerSaveResult(bool Saved, string? Problem = null, bool CanSaveAnyway = false);

/// <summary>
/// Settings › Servers (#282): music servers the DJ signed in to, Navidrome and others that speak
/// the Subsonic API. The servers are kept in the library; their passwords in the computer's
/// keychain (<see cref="PasswordStore"/>).
/// </summary>
public sealed partial class ServersViewModel : ObservableObject
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders = { { "User-Agent", $"GZJam/{About.Version}" } },
    };

    private readonly LibraryDatabase _database;
    private readonly MainWindowViewModel _owner;

    public ServersViewModel(MainWindowViewModel owner, LibraryDatabase database, AppSettings settings, PasswordStore passwords)
    {
        _owner = owner;
        _database = database;
        _settings = settings;
        Passwords = passwords;
        Refresh();
    }

    public PasswordStore Passwords { get; }

    /// <summary>Checks a server: the Subsonic API over the network. Tests put a pretend server here.</summary>
    public Func<string, string, string, CancellationToken, Task<ServerCheck>> CheckServer { get; set; } =
        (address, user, password, cancel) => new SubsonicClient(Http, address, user, password).CheckAsync(cancel);

    public ObservableCollection<ServerItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoServers))]
    public partial bool HasServers { get; private set; }

    public bool HasNoServers => !HasServers;

    /// <summary>Where the passwords are kept, said in Settings. Asked when shown: on Linux, finding out can bring up the keychain's unlock prompt.</summary>
    public string PasswordsText => Passwords.IsKeychain
        ? $"Passwords are kept in {Passwords.Where}, never in GZJam's settings or a backup."
        : "This desktop has no keychain, so passwords are kept in a file only you can read, never in GZJam's settings or a backup.";

    /// <summary>Settings › Servers is showing: say where the passwords are.</summary>
    public void Shown() => OnPropertyChanged(nameof(PasswordsText));

    public void Refresh()
    {
        var known = Items.ToDictionary(i => i.Server.Id);
        Items.Clear();
        foreach (MusicServer server in _database.GetServers())
        {
            var item = known.GetValueOrDefault(server.Id) ?? new ServerItemViewModel(this, server);
            item.Server = server;
            RefreshKept(item);
            Items.Add(item);
        }
        HasServers = Items.Count > 0;
    }

    /// <summary>The name a server gets when none is typed: its host ("synology").</summary>
    public static string DefaultName(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ? uri.Host : address;

    /// <summary>
    /// Adds a server, or changes <paramref name="existing"/>, after signing in to it. A wrong
    /// password, or something that isn't a music server, isn't saved; a server that can't be
    /// reached (at a venue, say) can be saved anyway, with <paramref name="evenIfUnreachable"/>.
    /// </summary>
    public async Task<ServerSaveResult> SaveAsync(ServerItemViewModel? existing, ServerEdit edit, bool evenIfUnreachable = false,
        CancellationToken cancel = default)
    {
        string? address = SubsonicClient.NormalizeAddress(edit.Address);
        if (address is null)
            return new(false, "That isn't a server address. Navidrome's is the address you open it at, such as synology:4533.");
        string user = edit.UserName.Trim();
        if (user.Length == 0)
            return new(false, "Type the user name you sign in to the server with.");
        string key = PasswordStore.KeyFor(address, user);
        if (Items.FirstOrDefault(i => i != existing && i.Server.PasswordKey == key) is { } twin)
            return new(false, $"That server and user name are already here, as {twin.Name}.");

        string? password = string.IsNullOrEmpty(edit.Password) ? null : edit.Password;
        if (password is null && existing is not null)
            password = TryGetPassword(existing.Server.PasswordKey, out _);   // left blank: unchanged, even at a new address
        if (password is null)
            return new(false, "Type the password too.");

        ServerCheck check = await CheckServer(address, user, password, cancel);
        if (!check.Connected && !(evenIfUnreachable && check.Outcome == ServerCheckOutcome.Unreachable))
            return new(false, ServerItemViewModel.Describe(check), CanSaveAnyway: check.Outcome == ServerCheckOutcome.Unreachable);

        string name = edit.Name.Trim() is { Length: > 0 } typed ? typed : DefaultName(address);
        string? oldKey = existing?.Server.PasswordKey;
        try
        {
            Passwords.Set(key, password);
        }
        catch (Exception e)
        {
            Log.Error($"Servers: couldn't keep the password for {key} in {Passwords.Where}: {e.Message}");
            return new(false, $"Couldn't keep the password in {Passwords.Where}: {e.Message}");
        }
        MusicServer saved = existing is null
            ? _database.AddServer(name, address, user)
            : _database.UpdateServer(existing.Server.Id, name, address, user);
        if (oldKey is not null && oldKey != key)
            TryForget(oldKey);
        _database.SaveServerCheck(saved.Id, check, DateTime.UtcNow);
        Log.Info($"Servers: {(existing is null ? "added" : "changed")} {name} ({user} at {address}): {check.Outcome}");

        Refresh();
        if (Items.FirstOrDefault(i => i.Server.Id == saved.Id) is { } item)
        {
            if (check.Connected)
                item.Show(check);
            else
                item.ShowProblem("Not signed in yet: nothing answered at that address. Press TEST when the server can be reached.");
        }
        return new(true);
    }

    /// <summary>Forgets a server and its password, after asking.</summary>
    public async Task RemoveAsync(ServerItemViewModel item)
    {
        if (item.IsSyncing)
            StopSync(item);
        int downloaded = _database.GetDownloadedTotals(item.Server.Id).Songs;
        string songs = downloaded == 0 ? ""
            : $" The {(downloaded == 1 ? "song" : Format.Count(downloaded) + " songs")} downloaded from it stay on this computer, in {DownloadsFolder}, as ordinary songs, and its playlists stay too.";
        if (_owner.ConfirmAsync is null || !await _owner.ConfirmAsync($"Remove {item.Name}?",
                $"GZJam forgets this server, and its password is removed from {Passwords.Where}. The server and its music aren't touched.{songs}",
                "Remove"))
            return;
        _database.RemoveServer(item.Server.Id);
        TryForget(item.Server.PasswordKey);
        Log.Info($"Servers: removed {item.Name} ({item.Server.UserName} at {item.Server.Address})");
        Refresh();
    }

    /// <summary>Signs in to a server again, and shows what's found.</summary>
    internal async Task TestAsync(ServerItemViewModel item, CancellationToken cancel = default)
    {
        string? password = TryGetPassword(item.Server.PasswordKey, out string? problem);
        if (password is null)
        {
            item.ShowProblem(problem ?? "Sign in again: the password isn't on this computer. A backup doesn't carry it. Press EDIT to type it.");
            return;
        }
        item.IsChecking = true;
        try
        {
            ServerCheck check = await CheckServer(item.Server.Address, item.Server.UserName, password, cancel);
            _database.SaveServerCheck(item.Server.Id, check, DateTime.UtcNow);
            if (_database.GetServer(item.Server.Id) is { } server)
                item.Server = server;
            item.Show(check);
        }
        finally
        {
            item.IsChecking = false;
        }
    }

    private string? TryGetPassword(string key, out string? problem)
    {
        problem = null;
        try
        {
            return Passwords.Get(key);
        }
        catch (Exception e)
        {
            problem = $"Couldn't read the password from {Passwords.Where}: {e.Message}";
            return null;
        }
    }

    private void TryForget(string key)
    {
        try
        {
            Passwords.Remove(key);
        }
        catch (Exception e)
        {
            Log.Error($"Servers: couldn't remove the password for {key} from {Passwords.Where}: {e.Message}");
        }
    }
}

/// <summary>One server's card in Settings › Servers.</summary>
public sealed partial class ServerItemViewModel : ObservableObject
{
    private readonly ServersViewModel _owner;

    public ServerItemViewModel(ServersViewModel owner, MusicServer server)
    {
        _owner = owner;
        Server = server;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(SignIn), nameof(IsPreviewOnly), nameof(Status))]
    public partial MusicServer Server { get; set; }

    public string Name => Server.Name;

    /// <summary>"gzjam at http://synology:4533".</summary>
    public string SignIn => $"{Server.UserName} at {Server.Address}";

    /// <summary>The account may not download, so nothing from this server can be played to the room.</summary>
    public bool IsPreviewOnly => Server.CanDownload == false;

    /// <summary>What the last check this session found, said plainly; null until one is made.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(IsProblem))]
    public partial string? Found { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProblem))]
    public partial bool FoundProblem { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    public partial bool IsChecking { get; set; }

    public bool IsProblem => Found is not null && FoundProblem;

    /// <summary>The card's line under the name.</summary>
    public string Status => IsChecking ? "Signing in…" : Found ?? Known;

    /// <summary>What the library remembers from the last time the server was reached.</summary>
    private string Known
    {
        get
        {
            if (Server.CheckedUtc is not { } checkedUtc)
                return "Not signed in yet.";
            string what = new ServerCheck(ServerCheckOutcome.Connected, Server.ServerType, Server.ServerVersion).ServerName ?? "Signed in";
            string when = checkedUtc.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture);
            return $"{what} · {DownloadsText(Server.CanDownload)} · last reached {when}";
        }
    }

    private static string DownloadsText(bool? canDownload) => canDownload switch
    {
        true => "downloads allowed",
        false => "preview only",
        null => "downloads not known",
    };

    internal void Show(ServerCheck check)
    {
        FoundProblem = !check.Connected;
        if (check.Connected)
        {
            var parts = new List<string> { check.ServerName ?? "Signed in" };
            if (check.SongCount is { } songs)
                parts.Add(songs == 1 ? "1 song" : $"{songs.ToString("N0", CultureInfo.CurrentCulture)} songs");
            parts.Add(DownloadsText(check.CanDownload));
            Found = string.Join(" · ", parts);
        }
        else
            Found = Describe(check);
    }

    internal void ShowProblem(string problem)
    {
        FoundProblem = true;
        Found = problem;
    }

    /// <summary>A check that didn't sign in, in words a DJ can act on.</summary>
    public static string Describe(ServerCheck check) => check.Outcome switch
    {
        ServerCheckOutcome.WrongSignIn => "The server answered, but not to that user name and password.",
        ServerCheckOutcome.Unreachable =>
            "Nothing answered at that address. Check it, and that this computer is on the server's network (or Tailscale).",
        ServerCheckOutcome.NotSubsonic =>
            "Something answers there, but it isn't a music server GZJam knows. Navidrome's address ends in its port, such as synology:4533.",
        _ => check.Detail is { Length: > 0 } detail ? $"The server said: {detail}" : "The server said no, without saying why.",
    };

    public Task TestAsync() => _owner.TestAsync(this);
}
