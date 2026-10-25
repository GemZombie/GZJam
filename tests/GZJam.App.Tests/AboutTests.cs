using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>Settings › About, and the license notices every package carries.</summary>
public sealed class AboutTests
{
    [AvaloniaFact]
    public void Settings_About_shows_the_version_the_maker_and_what_gzjam_is_built_with()
    {
        using var app = new AppHarness();
        var about = app.Window.FindControl<SettingsPanel>("SettingsPanel")!;
        Assert.Matches(@"^GZJam \d+\.\d+\.\d+", about.FindControl<TextBlock>("NavVersionText")!.Text);

        app.Window.OpenSettings(SettingsSection.About);
        app.Screenshot("about");

        Assert.Equal(About.Version, about.FindControl<TextBlock>("VersionText")!.Text);
        Assert.Contains("Jesse Gearhart", about.FindControl<TextBlock>("MakerText")!.Text);
        string builtWith = about.FindControl<SelectableTextBlock>("BuiltWithText")!.Text!;
        Assert.StartsWith("GZJam is built with", builtWith);
        Assert.Contains("BASS", builtWith);
        Assert.DoesNotContain("GNU LESSER GENERAL PUBLIC LICENSE", builtWith);   // the full texts open separately
        Assert.True(about.FindControl<Button>("NoticesButton")!.IsVisible);
        Assert.True(about.FindControl<Button>("GuideLink")!.IsVisible);
    }

    /// <summary>The guide Settings › About links to is in the repository, where the link says.</summary>
    [Fact]
    public void The_user_guide_is_where_about_links_to()
    {
        string repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "GZJam.slnx")))
            repo = Path.GetDirectoryName(repo)!;
        Assert.StartsWith(About.ProjectUrl + "/blob/main/", About.GuideUrl);
        string relative = About.GuideUrl[(About.ProjectUrl + "/blob/main/").Length..];
        string guide = Path.Combine(repo, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(guide), guide);

        // Every picture it shows is there too.
        foreach (System.Text.RegularExpressions.Match image in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(guide), @"!\[[^\]]*\]\(([^)]+)\)"))
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(guide)!, image.Groups[1].Value)), image.Groups[1].Value);
    }

    /// <summary>
    /// After a release, VersionPrefix in GZJam.App.csproj goes up to the next version, so builds
    /// made here read "0.6.0-dev" after 0.5.0, not "0.5.0-dev". This fails until it does. Only
    /// for builds made here ("-dev"); release builds take the tag's version, and a checkout
    /// without tags (CI) has nothing to compare with.
    /// </summary>
    [Fact]
    public void A_build_made_here_is_ahead_of_the_newest_release()
    {
        if (!About.Version.EndsWith("-dev", StringComparison.Ordinal))
            return;
        var dev = System.Version.Parse(About.Version[..^"-dev".Length]);

        string repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "GZJam.slnx")))
            repo = Path.GetDirectoryName(repo)!;
        string tags;
        try
        {
            using var git = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "tag --list v*")
            {
                WorkingDirectory = repo,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            })!;
            tags = git.StandardOutput.ReadToEnd();
            git.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return;   // no git
        }

        // Released versions; a pre-release (v0.6.0-beta.1) is on the way to its version, so a tie is fine.
        var released = tags.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => (Tag: t, Match: Regex.Match(t, @"^v(\d+\.\d+\.\d+)(-.+)?$")))
            .Where(t => t.Match.Success)
            .Select(t => (t.Tag, Version: System.Version.Parse(t.Match.Groups[1].Value), Final: !t.Match.Groups[2].Success))
            .ToList();
        foreach (var (tag, version, final) in released)
        {
            Assert.True(final ? dev > version : dev >= version,
                $"Builds made here say {About.Version}, but {tag} is already released: raise VersionPrefix in src/GZJam.App/GZJam.App.csproj to the next version.");
        }
    }

    /// <summary>
    /// A library added to GZJam needs its license in THIRD-PARTY-NOTICES.txt: this fails until its
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
