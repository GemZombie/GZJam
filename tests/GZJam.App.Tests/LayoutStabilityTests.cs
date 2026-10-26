using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.VisualTree;
using GZJam.App.Views;

namespace GZJam.App.Tests;

/// <summary>Nothing on screen moves or changes size as a deck starts playing or goes on air.</summary>
public sealed class LayoutStabilityTests
{
    /// <summary>
    /// What may change: the play key's icon (play or pause), the words ON AIR / OFF AIR, and the
    /// mixer's centerd LIVE / BLEND label. Their surroundings must stay put.
    /// </summary>
    /// <summary>
    /// What may change: a deck's own status, and the top bar's background progress (the library
    /// being scanned or analyzed goes on by itself, and on a slow machine it's still going here).
    /// </summary>
    private static readonly string[] AllowedToChange = ["#PlayButton/", "#StatusLine/", "#CompactStatusLine/", "#LiveLabel", "#BackgroundStatusPanel"];

    /// <summary>Where every visible control is, by its place in the visual tree.</summary>
    private static Dictionary<string, Rect> Layout(AppHarness app)
    {
        var result = new Dictionary<string, Rect>();
        void Walk(Visual visual, string path)
        {
            int i = 0;
            foreach (Visual child in visual.GetVisualChildren())
            {
                string at = $"{path}/{i++}:{child.GetType().Name}{(child is Avalonia.Controls.Control { Name: { } name } ? "#" + name : "")}";
                if (!child.IsEffectivelyVisible)
                    continue;
                if (child.TranslatePoint(default, app.Window) is { } topLeft)
                    result[at] = new Rect(topLeft, child.Bounds.Size);
                Walk(child, at);
            }
        }
        Walk(app.Window, "");
        return result;
    }

    private static IEnumerable<string> Moved(string step, Dictionary<string, Rect> before, Dictionary<string, Rect> after) =>
        from pair in before
        where !AllowedToChange.Any(allowed => pair.Key.Contains(allowed, StringComparison.Ordinal))
        where after.TryGetValue(pair.Key, out Rect now) && !Close(pair.Value, now)
        select $"{step}: {pair.Key}  {pair.Value} -> {after[pair.Key]}";

    private static bool Close(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) < 0.5 && Math.Abs(a.Width - b.Width) < 0.5 && Math.Abs(a.Height - b.Height) < 0.5;

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Nothing_moves_when_a_deck_starts_or_goes_on_air(bool fourDecks, bool compact)
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.Settings.FourDecks = fourDecks;
        vm.CompactDecks = compact;
        vm.LoadToDeckAsync(vm.Deck1!, app.ToneTrack("Tone A")).GetAwaiter().GetResult();
        vm.LoadToDeckAsync(vm.Deck2!, app.ToneTrack("Tone B")).GetAwaiter().GetResult();
        vm.Mixer!.Crossfader = 1;   // the room hears Deck 2's side
        app.Play(0.1);
        AppHarness.Settle(150);
        var loaded = Layout(app);

        vm.Deck1!.TogglePlayCommand.Execute(null);   // playing, off air
        app.Play(0.1);
        AppHarness.Settle(150);
        var offAir = Layout(app);

        vm.Mixer.Crossfader = 0;   // on air
        app.Play(0.1);
        AppHarness.Settle(150);
        var onAir = Layout(app);

        var moved = Moved("play", loaded, offAir).Concat(Moved("on air", offAir, onAir)).ToList();
        Assert.True(moved.Count == 0, string.Join(Environment.NewLine, moved));
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void The_decks_are_all_the_same_height_and_the_mixer_lines_up_with_them(bool touch, bool fourDecks)
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.Settings.TouchMode = touch;
        vm.Settings.FourDecks = fourDecks;
        foreach (var deck in vm.Decks)
            vm.LoadToDeckAsync(deck, app.ToneTrack("Tone " + deck.Label)).GetAwaiter().GetResult();
        AppHarness.Settle(200);

        Rect At(Control control) => new(control.TranslatePoint(default, app.Window)!.Value, control.Bounds.Size);
        var decks = new[] { "Deck1View", "Deck2View", "Deck3View", "Deck4View" }
            .Select(name => app.Window.FindControl<DeckView>(name)!).Where(d => d.IsVisible).Select(At).ToList();
        Assert.Equal(fourDecks ? 4 : 2, decks.Count);
        var mixer = At(app.Window.FindControl<Control>("Crossfader")!.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("panel")));

        foreach (Rect deck in decks)
            Assert.True(Math.Abs(deck.Height - decks[0].Height) <= 1, $"decks {string.Join(", ", decks.Select(d => d.Height))} high");
        Assert.Equal(mixer.Top, decks.Min(d => d.Top), 1);
        Assert.Equal(mixer.Bottom, decks.Max(d => d.Bottom), 1);
        Assert.True(app.Window.FindControl<Control>("PhonesMixFader")!.Bounds.Height >= (touch ? 44 : 28));

        // A hint over the waveform (here: why the room's deck can't be sought) never makes it taller.
        vm.Mixer!.Crossfader = 0;
        vm.Deck1!.TogglePlayCommand.Execute(null);
        app.Play(0.1);
        var deck1 = app.Window.FindControl<DeckView>("Deck1View")!;
        double before = deck1.Bounds.Height;
        vm.Deck1.ExplainSeekLock();
        AppHarness.Settle(100);
        Assert.NotNull(vm.Deck1.Hint);
        Assert.Equal(before, deck1.Bounds.Height, 1);
    }
}
