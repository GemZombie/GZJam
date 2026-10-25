using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using GZJam.App.Views;
using ToggleButton = Avalonia.Controls.Primitives.ToggleButton;

namespace GZJam.App.Tests;

/// <summary>Saying when a new version of GZJam is out (#86): quietly, at startup, never during a set.</summary>
public sealed class NewVersionTests
{
    private static readonly Release Newer = new("0.9.0", "https://github.com/GemZombie/GZJam/releases/tag/v0.9.0");

    /// <summary>GZJam 0.7.3 starting, with GitHub's answer <paramref name="release"/> (null: no answer).</summary>
    private static Task Startup(AppHarness app, Release? release)
    {
        GitHubSays(app, release);
        return app.ViewModel.CheckForNewVersionAsync();
    }

    private static void GitHubSays(AppHarness app, Release? release)
    {
        app.ViewModel.CurrentVersion = "0.7.3";
        app.ViewModel.LatestRelease = () => Task.FromResult(release);
    }

    private static Button CheckNow(AppHarness app)
    {
        app.Window.OpenSettings(SettingsSection.About);
        var about = app.Window.FindControl<SettingsPanel>("SettingsPanel")!;
        var button = about.FindControl<Button>("CheckNowButton")!;
        button.Command!.Execute(null);
        AppHarness.WaitFor(() => button.Command.CanExecute(null));
        return button;
    }

    [Theory]
    [InlineData("0.8.0", "0.7.3", true)]
    [InlineData("0.7.10", "0.7.9", true)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.8.0", "0.8.0-dev", true)]   // a build made here, on its way to 0.8.0
    [InlineData("0.8.0", "0.8.0-beta.1", true)]
    [InlineData("0.7.3", "0.7.3", false)]
    [InlineData("0.7.3", "0.8.0-dev", false)]
    [InlineData("0.7.2", "0.7.3", false)]
    [InlineData("0.8.0", "?", false)]           // a build that can't say its version
    [InlineData("nonsense", "0.7.3", false)]
    public void A_release_is_newer_by_its_version_numbers(string release, string current, bool newer) =>
        Assert.Equal(newer, UpdateCheck.IsNewer(release, current));

    [Fact]
    public void GitHubs_answer_gives_the_version_and_the_release_page()
    {
        var release = UpdateCheck.Parse("""
            {"tag_name": "v0.8.0", "html_url": "https://github.com/GemZombie/GZJam/releases/tag/v0.8.0",
             "draft": false, "prerelease": false}
            """);
        Assert.Equal(new Release("0.8.0", "https://github.com/GemZombie/GZJam/releases/tag/v0.8.0"), release);
    }

    [Theory]
    [InlineData("""{"tag_name": "v0.8.0-beta.1", "html_url": "https://github.com/GemZombie/GZJam/releases/tag/v0.8.0-beta.1"}""")]
    [InlineData("""{"tag_name": "v0.8.0", "prerelease": true}""")]
    [InlineData("""{"tag_name": "v0.8.0", "draft": true}""")]
    [InlineData("""{"message": "API rate limit exceeded"}""")]
    [InlineData("""[]""")]
    [InlineData("""<html>""")]
    public void Pre_releases_drafts_and_odd_answers_are_ignored(string json) =>
        Assert.Null(UpdateCheck.Parse(json));

    [Fact]
    public void A_link_away_from_gzjams_page_goes_to_its_releases_instead()
    {
        var release = UpdateCheck.Parse("""{"tag_name": "v0.8.0", "html_url": "https://example.com/elsewhere"}""");
        Assert.Equal("https://github.com/GemZombie/GZJam/releases/latest", release!.Url);
    }

    [AvaloniaFact]
    public void A_newer_version_is_a_quiet_line_in_the_top_bar()
    {
        using var app = new AppHarness();
        var line = app.Window.FindControl<Border>("NewVersionLine")!;
        Assert.False(line.IsVisible);

        AppHarness.Await(Startup(app, Newer));
        app.Screenshot("new-version");

        Assert.True(line.IsVisible);
        Assert.Equal("GZJam 0.9.0 is out.", app.ViewModel.NewVersionText);
        Assert.True(app.Window.FindControl<Button>("NewVersionLink")!.IsVisible);
    }

    [AvaloniaFact]
    public void Nothing_is_said_when_up_to_date_or_github_cant_be_reached()
    {
        using var app = new AppHarness();
        AppHarness.Await(Startup(app, new Release("0.7.3", Newer.Url)));
        Assert.Null(app.ViewModel.NewVersion);
        AppHarness.Await(Startup(app, null));
        Assert.Null(app.ViewModel.NewVersion);
    }

    [AvaloniaFact]
    public void A_dismissed_version_isnt_mentioned_again_but_a_later_one_is()
    {
        using var app = new AppHarness();
        AppHarness.Await(Startup(app, Newer));
        app.Window.FindControl<Button>("NewVersionDismiss")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.False(app.Window.FindControl<Border>("NewVersionLine")!.IsVisible);
        Assert.Equal("0.9.0", app.Settings.DismissedVersion);

        // The next start
        AppHarness.Await(Startup(app, Newer));
        Assert.Null(app.ViewModel.NewVersion);
        AppHarness.Await(Startup(app, new Release("0.9.1", Newer.Url)));
        Assert.Equal("0.9.1", app.ViewModel.NewVersion?.Version);
    }

    [AvaloniaFact]
    public void The_line_goes_once_the_room_hears_music_and_doesnt_come_back()
    {
        using var app = new AppHarness();
        AppHarness.Await(Startup(app, Newer));
        var deck = app.ViewModel.Deck1!;
        Assert.True(AppHarness.Await(app.ViewModel.LoadToDeckAsync(deck, app.ToneTrack("One"))));
        deck.TogglePlayCommand.Execute(null);
        app.Play(0.2);
        Assert.True(deck.IsOnAir);
        Assert.False(app.Window.FindControl<Border>("NewVersionLine")!.IsVisible);

        deck.TogglePlayCommand.Execute(null);   // a pause between songs
        app.Play(0.2);
        Assert.False(deck.IsOnAir);
        Assert.Null(app.ViewModel.NewVersion);
        Assert.Null(app.Settings.DismissedVersion);   // not dismissed: said again at the next start

        // A slow answer that arrives once the night has started says nothing either.
        AppHarness.Await(Startup(app, Newer));
        Assert.Null(app.ViewModel.NewVersion);
    }

    [AvaloniaFact]
    public void Turned_off_in_settings_about_github_isnt_asked()
    {
        using var app = new AppHarness();
        var about = app.Window.FindControl<SettingsPanel>("SettingsPanel")!;
        app.Window.OpenSettings(SettingsSection.About);
        var toggle = about.FindControl<ToggleButton>("CheckForNewVersionSwitch")!;
        Assert.True(toggle.IsChecked);

        toggle.IsChecked = false;
        Assert.False(app.Settings.CheckForNewVersion);
        bool asked = false;
        app.ViewModel.LatestRelease = () => { asked = true; return Task.FromResult<Release?>(Newer); };
        AppHarness.Await(app.ViewModel.CheckForNewVersionAsync());
        Assert.False(asked);
        Assert.Null(app.ViewModel.NewVersion);
    }

    [AvaloniaFact]
    public void Turning_it_off_takes_the_line_away()
    {
        using var app = new AppHarness();
        AppHarness.Await(Startup(app, Newer));
        app.ViewModel.Settings.CheckForNewVersion = false;
        Assert.Null(app.ViewModel.NewVersion);
    }

    [AvaloniaFact]
    public void Check_now_in_settings_about_says_whats_out()
    {
        using var app = new AppHarness();
        var about = app.Window.FindControl<SettingsPanel>("SettingsPanel")!;
        var link = about.FindControl<Button>("FoundReleaseLink")!;

        GitHubSays(app, Newer);
        CheckNow(app);
        app.Screenshot("check-now");
        Assert.Equal("GZJam 0.9.0 is out.", app.ViewModel.Settings.CheckResult);
        Assert.True(link.IsVisible);

        GitHubSays(app, new Release("0.7.3", Newer.Url));
        CheckNow(app);
        Assert.Equal("GZJam 0.7.3 is the newest version.", app.ViewModel.Settings.CheckResult);
        Assert.False(link.IsVisible);

        GitHubSays(app, null);
        CheckNow(app);
        Assert.StartsWith("Couldn't reach GitHub", app.ViewModel.Settings.CheckResult);
        Assert.False(link.IsVisible);
    }

    [AvaloniaFact]
    public void Check_now_works_with_the_startup_check_off_and_after_dismissing()
    {
        using var app = new AppHarness();
        AppHarness.Await(Startup(app, Newer));
        app.ViewModel.DismissNewVersion();
        app.ViewModel.Settings.CheckForNewVersion = false;

        CheckNow(app);
        Assert.Equal("GZJam 0.9.0 is out.", app.ViewModel.Settings.CheckResult);
        Assert.Null(app.ViewModel.NewVersion);   // said here, not in the top bar
    }
}
