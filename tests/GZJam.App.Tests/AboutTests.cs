using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>Settings › About, and the licence notices every package carries.</summary>
public sealed class AboutTests
{
    [AvaloniaFact]
    public void Settings_and_the_about_window_show_the_version_the_maker_and_what_gzjam_is_built_with()
    {
        using var app = new AppHarness();
        Assert.Matches(@"^GZJam \d+\.\d+\.\d+", app.Window.FindControl<TextBlock>("AboutVersionText")!.Text);

        var about = new AboutDialog();
        about.Show();
        AppHarness.Settle(100);
        Directory.CreateDirectory(AppHarness.ScreenshotDirectory);
        about.CaptureRenderedFrame()?.Save(Path.Combine(AppHarness.ScreenshotDirectory, "about.png"));

        Assert.Equal(About.Version, about.FindControl<TextBlock>("VersionText")!.Text);
        Assert.Contains("Jesse Gearhart", about.FindControl<TextBlock>("MakerText")!.Text);
        string builtWith = about.FindControl<SelectableTextBlock>("BuiltWithText")!.Text!;
        Assert.StartsWith("GZJam is built with", builtWith);
        Assert.Contains("BASS", builtWith);
        Assert.DoesNotContain("GNU LESSER GENERAL PUBLIC LICENSE", builtWith);   // the full texts open separately
        Assert.True(about.FindControl<Button>("NoticesButton")!.IsVisible);
        about.Close();
    }

    /// <summary>
    /// A library added to GZJam needs its licence in THIRD-PARTY-NOTICES.txt: this fails until its
    /// name (or its family's, like Avalonia for Avalonia.Win32) is in the opening list.
    /// </summary>
    [Fact]
    public void Every_library_gzjam_ships_is_in_the_notices()
    {
        string repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "GZJam.slnx")))
            repo = Path.GetDirectoryName(repo)!;
        string notices = File.ReadAllText(Path.Combine(repo, "THIRD-PARTY-NOTICES.txt"));
        string list = notices[..notices.IndexOf("\n####", StringComparison.Ordinal)];

        using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo, "src", "GZJam.App", "obj", "project.assets.json")));
        string[] buildOnly = ["Avalonia.BuildServices"];
        var missing = assets.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(l => l.Value.GetProperty("type").GetString() == "package")
            .Select(l => l.Name.Split('/')[0])
            .Where(id => !buildOnly.Contains(id))
            .Where(id => !Families(id).Any(name => Regex.IsMatch(list, $@"(?<![\w.]){Regex.Escape(name)}(?![\w])")))
            .ToList();
        Assert.Empty(missing);
    }

    /// <summary>"Avalonia.X11" → "Avalonia.X11", "Avalonia".</summary>
    private static IEnumerable<string> Families(string id)
    {
        string[] parts = id.Split('.');
        for (int n = parts.Length; n > 0; n--)
            yield return string.Join('.', parts[..n]);
    }
}
