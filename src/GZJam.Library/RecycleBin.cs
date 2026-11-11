using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace GZJam.Library;

/// <summary>
/// The computer's Recycle Bin (the Trash on macOS and Linux), for songs a DJ deletes from Files
/// (#385): nothing GZJam deletes is gone for good unless the DJ was told so first. Windows' shell
/// delete with undo; macOS' NSFileManager; on Linux the freedesktop.org trash, as the file
/// managers keep it.
/// </summary>
public static class RecycleBin
{
    /// <summary>What it's called here: "Recycle Bin" on Windows, "Trash" elsewhere.</summary>
    public static string Name => OperatingSystem.IsWindows() ? "Recycle Bin" : "Trash";

    /// <summary>
    /// Whether a file there can go to the bin: not on a network drive, nor a removable one, whose
    /// files a delete removes for good.
    /// </summary>
    public static bool CanRecycle(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            DriveInfo? drive = DriveInfo.GetDrives()
                .Where(d => FileSources.IsIn(full, d.RootDirectory.FullName) || PathsEqual(full, d.RootDirectory.FullName))
                .MaxBy(d => d.RootDirectory.FullName.Length);
            return drive?.DriveType is not (DriveType.Network or DriveType.Removable);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>Puts a file in the bin. Throws <see cref="IOException"/> if it couldn't, the file left where it was.</summary>
    public static void Recycle(string path)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException("The file isn't there.", full);
        if (OperatingSystem.IsWindows())
            Windows.Recycle(full);
        else if (OperatingSystem.IsMacOS())
            Mac.Recycle(full);
        else
            Freedesktop.Recycle(full);
        if (File.Exists(full))
            throw new IOException($"It couldn't be moved to the {Name}.");
    }

    // ───────────── Windows ─────────────

    private static class Windows
    {
        private const uint FO_DELETE = 3;
        private const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCTW
        {
            public IntPtr hwnd;
            public uint wFunc;
            public IntPtr pFrom;
            public IntPtr pTo;
            public ushort fFlags;
            public int fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public IntPtr lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW operation);

        public static void Recycle(string path)
        {
            IntPtr from = Marshal.StringToHGlobalUni(path + "\0");   // a list, ended by a second null
            try
            {
                var op = new SHFILEOPSTRUCTW
                {
                    wFunc = FO_DELETE,
                    pFrom = from,
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
                };
                int result = SHFileOperationW(ref op);
                if (result != 0 || op.fAnyOperationsAborted != 0)
                    throw new IOException($"Windows couldn't move it to the Recycle Bin (error 0x{result:X}).");
            }
            finally
            {
                Marshal.FreeHGlobal(from);
            }
        }
    }

    // ───────────── macOS ─────────────

    private static class Mac
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";

        [DllImport(ObjC)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(ObjC)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SendBool(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b, IntPtr c);

        [DllImport(ObjC)]
        private static extern IntPtr objc_autoreleasePoolPush();

        [DllImport(ObjC)]
        private static extern void objc_autoreleasePoolPop(IntPtr pool);

        public static void Recycle(string path)
        {
            IntPtr pool = objc_autoreleasePoolPush();
            IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(path);
            try
            {
                IntPtr text = Send(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), utf8);
                IntPtr url = Send(objc_getClass("NSURL"), sel_registerName("fileURLWithPath:"), text);
                IntPtr manager = Send(objc_getClass("NSFileManager"), sel_registerName("defaultManager"));
                if (!SendBool(manager, sel_registerName("trashItemAtURL:resultingItemURL:error:"), url, IntPtr.Zero, IntPtr.Zero))
                    throw new IOException("macOS couldn't move it to the Trash.");
            }
            finally
            {
                Marshal.ZeroFreeCoTaskMemUTF8(utf8);
                objc_autoreleasePoolPop(pool);
            }
        }
    }

    // ───────────── Linux: the freedesktop.org trash ─────────────

    /// <summary>
    /// The trash as the file managers (Dolphin, Files, Thunar) keep it: the file in the trash's
    /// files folder, with a .trashinfo saying where it was and when, so it can be put back. The
    /// home trash for files on the home's drive; another drive's own .Trash-uid folder at its top.
    /// </summary>
    internal static class Freedesktop
    {
        [DllImport("libc")]
        private static extern uint getuid();

        /// <summary>The home trash, from $XDG_DATA_HOME (~/.local/share by default).</summary>
        internal static string HomeTrash
        {
            get
            {
                string? data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (string.IsNullOrEmpty(data))
                    data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
                return Path.Combine(data, "Trash");
            }
        }

        public static void Recycle(string path)
        {
            string home = HomeTrash;
            Directory.CreateDirectory(home);
            string mount = MountOf(path);
            string trash;
            string recorded;
            if (MountOf(home) == mount)
            {
                trash = home;
                recorded = path;   // the home trash says where a file was in full
            }
            else
            {
                trash = Path.Combine(mount, $".Trash-{getuid()}");   // another drive's own, its paths from its top
                recorded = Path.GetRelativePath(mount, path);
            }
            Recycle(path, trash, recorded);
        }

        /// <summary>Into <paramref name="trash"/>: its info first (claiming the name), then the file itself, renamed there.</summary>
        internal static void Recycle(string path, string trash, string recorded)
        {
            string files = Path.Combine(trash, "files"), info = Path.Combine(trash, "info");
            if (!Directory.Exists(trash) && !OperatingSystem.IsWindows())
                Directory.CreateDirectory(trash, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);   // the DJ's alone
            Directory.CreateDirectory(files);
            Directory.CreateDirectory(info);
            string name = Path.GetFileName(path), stem = Path.GetFileNameWithoutExtension(path), extension = Path.GetExtension(path);
            for (int n = 1; ; n++)
            {
                string candidate = n == 1 ? name : $"{stem}.{n}{extension}";
                string infoFile = Path.Combine(info, candidate + ".trashinfo");
                FileStream stream;
                try
                {
                    stream = new FileStream(infoFile, FileMode.CreateNew, FileAccess.Write);
                }
                catch (IOException) when (File.Exists(infoFile))
                {
                    continue;   // a file of that name is in the trash already
                }
                using (stream)
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write($"[Trash Info]\nPath={Escape(recorded)}\nDeletionDate={DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}\n");
                }
                string target = Path.Combine(files, candidate);
                if (File.Exists(target) || Directory.Exists(target))
                {
                    File.Delete(infoFile);   // a file without its info, left by something else: not that name
                    continue;
                }
                try
                {
                    File.Move(path, target);
                    return;
                }
                catch
                {
                    File.Delete(infoFile);
                    throw;
                }
            }
        }

        /// <summary>A path as the .trashinfo has it: URL-escaped, its slashes kept.</summary>
        internal static string Escape(string path) =>
            string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

        /// <summary>The mount point a path is on, from /proc/self/mounts: the longest that holds it.</summary>
        private static string MountOf(string path)
        {
            string full = Path.GetFullPath(path);
            string best = "/";
            try
            {
                foreach (string line in File.ReadLines("/proc/self/mounts"))
                {
                    string[] parts = line.Split(' ');
                    if (parts.Length < 2)
                        continue;
                    string point = parts[1].Replace("\\040", " ").Replace("\\011", "\t").Replace("\\134", "\\");
                    if ((full == point || full.StartsWith(point.TrimEnd('/') + "/", StringComparison.Ordinal)) && point.Length > best.Length)
                        best = point;
                }
            }
            catch (IOException)
            {
            }
            return best;
        }
    }
}
