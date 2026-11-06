using System.Text.Json;

namespace GZJam.Library.Servers;

/// <summary>
/// Where the passwords for music servers are kept (#282): the computer's own keychain, never
/// GZJam's settings, its library or a backup. Windows Credential Manager, the macOS Keychain, or on
/// Linux the Secret Service (GNOME Keyring, KDE Wallet). A Linux desktop that runs none of them
/// (Hyprland and other minimal ones) gets a file only its user can read, and Settings says so.
/// </summary>
public abstract class PasswordStore
{
    /// <summary>One password per server address and user name.</summary>
    public static string KeyFor(string address, string userName) => $"{userName}@{address}";

    /// <summary>The password kept under <paramref name="key"/>, or null if there's none.</summary>
    public abstract string? Get(string key);

    /// <summary>Keeps <paramref name="password"/> under <paramref name="key"/>, replacing any there.</summary>
    public abstract void Set(string key, string password);

    /// <summary>Forgets the password under <paramref name="key"/>, if there is one.</summary>
    public abstract void Remove(string key);

    /// <summary>Where the passwords are, in words for Settings: "Windows Credential Manager".</summary>
    public abstract string Where { get; }

    /// <summary>Whether this is the computer's keychain, rather than GZJam's own file.</summary>
    public virtual bool IsKeychain => true;

    /// <summary>The store this computer has.</summary>
    /// <param name="dataDirectory">GZJam's data folder, for the file a Linux desktop without a keychain gets.</param>
    public static PasswordStore ForThisComputer(string dataDirectory)
    {
        if (OperatingSystem.IsWindows())
            return new WindowsCredentials();
        if (OperatingSystem.IsMacOS())
            return new MacKeychain();
        return new LinuxPasswords(new FilePasswordStore(Path.Combine(dataDirectory, FilePasswordStore.FileName)));
    }
}

/// <summary>Passwords kept only while GZJam runs: for tests.</summary>
public sealed class MemoryPasswordStore : PasswordStore
{
    private readonly Dictionary<string, string> _passwords = [];

    public override string? Get(string key)
    {
        lock (_passwords)
            return _passwords.GetValueOrDefault(key);
    }

    public override void Set(string key, string password)
    {
        lock (_passwords)
            _passwords[key] = password;
    }

    public override void Remove(string key)
    {
        lock (_passwords)
            _passwords.Remove(key);
    }

    public override string Where => "GZJam's memory";
    public override bool IsKeychain => false;
}

/// <summary>
/// Passwords in a file in GZJam's data folder that only its user can read (permissions 600), for
/// a Linux desktop with no keychain. Not in a backup: a backup holds the library and Settings only.
/// </summary>
public sealed class FilePasswordStore(string path) : PasswordStore
{
    public const string FileName = "server-passwords.json";

    private readonly Lock _lock = new();

    public string Path { get; } = path;

    public override string? Get(string key)
    {
        lock (_lock)
            return Read().GetValueOrDefault(key);
    }

    public override void Set(string key, string password)
    {
        lock (_lock)
        {
            var passwords = Read();
            passwords[key] = password;
            Write(passwords);
        }
    }

    public override void Remove(string key)
    {
        lock (_lock)
        {
            var passwords = Read();
            if (passwords.Remove(key))
                Write(passwords);
        }
    }

    public override string Where => "a file only you can read";
    public override bool IsKeychain => false;

    private Dictionary<string, string> Read()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path)) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Written to a temporary file made unreadable to others from the start, then moved over the old one.</summary>
    private void Write(Dictionary<string, string> passwords)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        string temp = Path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(temp, options))
            JsonSerializer.Serialize(stream, passwords);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);   // even over an older file
        File.Move(temp, Path, overwrite: true);
    }
}

/// <summary>
/// Linux: the Secret Service when the desktop runs one, else <see cref="FilePasswordStore"/>.
/// Found out the first time a password is wanted, not at startup, since asking the Secret Service
/// can bring up its unlock prompt. A password kept in the file before a keychain was set up is
/// still found, and moves into the keychain when it's next saved.
/// </summary>
internal sealed class LinuxPasswords(FilePasswordStore file) : PasswordStore
{
    private readonly Lazy<LinuxSecretService?> _keychain = new(LinuxSecretService.TryOpen);

    private PasswordStore Current => (PasswordStore?)_keychain.Value ?? file;

    public override string? Get(string key) => _keychain.Value?.Get(key) ?? file.Get(key);

    public override void Set(string key, string password)
    {
        Current.Set(key, password);
        if (_keychain.Value is not null)
            file.Remove(key);
    }

    public override void Remove(string key)
    {
        _keychain.Value?.Remove(key);
        file.Remove(key);
    }

    public override string Where => Current.Where;
    public override bool IsKeychain => Current.IsKeychain;
}
