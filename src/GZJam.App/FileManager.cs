using System.Diagnostics;

namespace GZJam.App;

/// <summary>Opens folders and reveals files in the OS file manager.</summary>
internal static class FileManager
{
    public static void Open(string folder) => Start(OperatingSystem.IsWindows() ? "explorer.exe" : "xdg-open", folder);

    /// <summary>Opens the containing folder with the file selected (Windows), or just the folder elsewhere.</summary>
    public static void Reveal(string file)
    {
        if (OperatingSystem.IsWindows())
            Start("explorer.exe", $"/select,\"{file}\"", quote: false);
        else
            Open(Path.GetDirectoryName(file)!);
    }

    private static void Start(string program, string argument, bool quote = true)
    {
        try
        {
            var info = new ProcessStartInfo(program) { UseShellExecute = false };
            if (quote)
                info.ArgumentList.Add(argument);
            else
                info.Arguments = argument;
            Process.Start(info)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Trace.WriteLine($"Could not open file manager: {ex.Message}");
        }
    }
}
