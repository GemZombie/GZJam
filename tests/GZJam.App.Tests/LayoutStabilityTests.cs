using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace GZJam.App.Tests;

/// <summary>Nothing on screen moves or changes size as a deck starts playing or goes on air.</summary>
public sealed class LayoutStabilityTests
{
    /// <summary>
    /// What may change: the play key's icon (play or pause), the words ON AIR / OFF AIR, and the
    /// mixer's centred LIVE / BLEND label. Their surroundings must stay put.
    /// </summary>
    private static readonly string[] AllowedToChange = ["#PlayButton/", "#StatusLine/", "#CompactStatusLine/", "#LiveLabel"];

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
    [InlineData(false)]
    [InlineData(true)]
    public void Nothing_moves_when_a_deck_starts_or_goes_on_air(bool fourDecks)
    {
        using var app = new AppHarness();
        var vm = app.ViewModel;
        vm.Settings.FourDecks = fourDecks;
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
}
