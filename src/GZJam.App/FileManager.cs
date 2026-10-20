using System.Diagnostics;

namespace GZJam.App;

/// <summary>Opens folders and reveals files in the OS file manager (Explorer, Finder, or the Linux default).</summary>
internal static class FileManager
{
    /// <summary>Menu text for revealing a file, in each platform's own words.</summary>
    public static string RevealLabel =>
        OperatingSystem.IsWindows() ? "Show in Explorer"
        : OperatingSystem.IsMacOS() ? "Show in Finder"
        : "Open containing folder";

    public static void Open(string folder)
    {
        if (OperatingSystem.IsWindows())
            Start("explorer.exe", folder);
        else if (OperatingSystem.IsMacOS())
            Start("open", folder);
        else
            Start("xdg-open", folder);
    }

    /// <summary>Opens the containing folder with the file selected (Windows, macOS), or just the folder on Linux.</summary>
    public static void Reveal(string file)
    {
        if (OperatingSystem.IsWindows())
            StartRaw("explorer.exe", $"/select,\"{file}\"");   // explorer's /select doesn't accept standard quoting
        else if (OperatingSystem.IsMacOS())
            Start("open", "-R", file);
        else
            Open(Path.GetDirectoryName(file)!);
    }

    private static void Start(string program, params string[] arguments)
    {
        var info = new ProcessStartInfo(program) { UseShellExecute = false };
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);
        Run(info);
    }

    private static void StartRaw(string program, string arguments) =>
        Run(new ProcessStartInfo(program, arguments) { UseShellExecute = false });

    private static void Run(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Trace.WriteLine($"Could not open file manager: {ex.Message}");
        }
    }
}
