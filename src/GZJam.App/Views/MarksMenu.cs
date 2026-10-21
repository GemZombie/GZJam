using Avalonia.Controls;
using GZJam.App.Controls;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// The "Rating" and "Energy" submenus for the selected tracks, with their current choice ticked.
/// Used in the library and queue menus and on the decks.
/// </summary>
internal static class MarksMenu
{
    private static readonly (Rating Value, string Label)[] Ratings =
    [
        (Rating.Loved, "I'd play it"),
        (Rating.Rejected, "I wouldn't play it"),
        (Rating.Unrated, "Unrated"),
    ];

    private static readonly (Energy Value, string Label)[] Energies =
    [
        (Energy.High, "High energy"),
        (Energy.Medium, "Medium energy"),
        (Energy.Low, "Low energy"),
        (Energy.Unset, "Not set"),
    ];

    public static void FillRating(ItemsControl menu, MainWindowViewModel vm, IReadOnlyList<Track> tracks)
    {
        var current = tracks.Select(t => t.Rating).Distinct().ToList();
        Fill(menu, tracks.Count > 0, Ratings, "rating", r => current is [var only] && only == r,
            r => new RatingIcon { Rating = r, Width = 14, Height = 14 }, r => vm.SetRating(tracks, r));
    }

    public static void FillEnergy(ItemsControl menu, MainWindowViewModel vm, IReadOnlyList<Track> tracks)
    {
        var current = tracks.Select(t => t.Energy).Distinct().ToList();
        Fill(menu, tracks.Count > 0, Energies, "energy", e => current is [var only] && only == e,
            e => new EnergyIcon { Energy = e, ShowUnset = false, Width = 14, Height = 12 }, e => vm.SetEnergy(tracks, e));
    }

    private static void Fill<T>(ItemsControl menu, bool enabled, (T Value, string Label)[] choices, string group,
        Func<T, bool> isChecked, Func<T, Control> icon, Action<T> choose)
    {
        menu.Items.Clear();
        foreach (var (value, label) in choices)
        {
            var item = new MenuItem
            {
                Header = label,
                Icon = icon(value),
                ToggleType = MenuItemToggleType.Radio,
                GroupName = group,
                IsChecked = isChecked(value),
                IsEnabled = enabled,
            };
            item.Click += (_, _) => choose(value);
            menu.Items.Add(item);
        }
    }
}
