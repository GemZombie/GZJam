using Avalonia.Controls;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// "BPM": Automatic, detection in a range, or a BPM typed by hand, with the tracks' current choice
/// ticked. Used in the library and queue menus and on the decks' BPM.
/// </summary>
internal static class BpmMenu
{
    public const string Title = "BPM";

    /// <summary>Replaces <paramref name="menu"/>'s items. <paramref name="withTitle"/> adds a heading (for a menu of its own).</summary>
    public static void Fill(ItemsControl menu, MainWindowViewModel vm, IReadOnlyList<Track> tracks, bool withTitle = false)
    {
        menu.Items.Clear();
        bool enabled = tracks.Count > 0 && vm.Analysis is not null;
        var typed = tracks.Select(t => t.ManualBpm).Distinct().ToList();
        bool allTyped = tracks.Count > 0 && typed.All(b => b is not null);
        var chosen = tracks.Select(t => t.BpmRange).Distinct().ToList();
        BpmRange? current = chosen.Count == 1 ? chosen[0] : null;
        bool mixed = chosen.Count != 1 || typed.Count != 1;

        if (withTitle)
            menu.Items.Add(new MenuItem { Header = Title, IsEnabled = false });
        menu.Items.Add(Item("Automatic", null, !mixed && !allTyped && current is null));
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Detect in a range", IsEnabled = false });
        foreach (var (range, label) in MainWindowViewModel.BpmRanges)
            menu.Items.Add(Item(label, range, !mixed && !allTyped && current == range));
        menu.Items.Add(new Separator());

        // Typed by hand: for when the tag and detection are both wrong (or to fix one exactly).
        double? shown = typed.Count == 1 ? typed[0] : null;
        var type = new MenuItem
        {
            Header = shown is { } bpm ? $"Typed: {Format.Bpm(bpm)} BPM…" : "Type the BPM…",
            ToggleType = MenuItemToggleType.Radio,
            GroupName = "bpm-range",
            IsChecked = allTyped && typed.Count == 1,
            IsEnabled = tracks.Count > 0,
        };
        type.Click += async (_, _) =>
        {
            if (vm.PromptAsync is not { } prompt)
                return;
            double? start = shown ?? (tracks.Count == 1 ? tracks[0].Bpm : null);
            string? text = await prompt("Type the BPM",
                tracks.Count == 1 ? $"For \"{tracks[0].DisplayTitle}\". Clear it to go back to automatic." : $"For {tracks.Count} songs.",
                "Set", start is { } s ? Format.Bpm(s) : "");
            if (text is null)
            {
                if (shown is not null && tracks.Count > 0)
                    vm.SetManualBpm(tracks, null);   // cleared: back to automatic
                return;
            }
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double value) && value is > 20 and < 500)
                vm.SetManualBpm(tracks, Math.Round(value, 2));
            else
                vm.ShowNotice($"\"{text}\" isn't a BPM: type a number like 156 or 128.5.");
        };
        menu.Items.Add(type);

        MenuItem Item(string header, BpmRange? range, bool isChecked)
        {
            var item = new MenuItem
            {
                Header = header,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "bpm-range",
                IsChecked = isChecked,
                IsEnabled = enabled,
            };
            item.Click += (_, _) => _ = vm.DetectBpmAsync(tracks, range);
            return item;
        }
    }
}
