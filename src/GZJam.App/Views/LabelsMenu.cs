using Avalonia.Controls;
using GZJam.App.ViewModels;
using GZJam.Library;

namespace GZJam.App.Views;

/// <summary>
/// The "Labels" submenu for the selected tracks: every label, ticked if all of them carry it
/// (a tap puts it on all of them, or takes it off), and "New label…".
/// </summary>
internal static class LabelsMenu
{
    public static void Fill(MenuItem menu, MainWindowViewModel vm, IReadOnlyList<Track> tracks)
    {
        menu.Items.Clear();
        bool any = tracks.Count > 0;
        foreach (LabelInfo label in vm.GetLabels())
        {
            int carrying = tracks.Count(t => t.Labels.Contains(label.Name, StringComparer.OrdinalIgnoreCase));
            bool all = any && carrying == tracks.Count;
            var item = new MenuItem
            {
                Header = carrying > 0 && !all ? $"{label.Name}  (some)" : label.Name,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = all,
                IsEnabled = any,
            };
            item.Click += (_, _) => vm.SetLabel(tracks, label.Name, on: !all);
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0)
            menu.Items.Add(new Separator());
        var create = new MenuItem { Header = "New label…", IsEnabled = any };
        create.Click += async (_, _) =>
        {
            if (vm.PromptAsync is { } prompt &&
                await prompt("New label", tracks.Count == 1 ? $"For \"{tracks[0].DisplayTitle}\"" : $"For {tracks.Count} songs", "Add") is { } name)
                vm.SetLabel(tracks, name, on: true);
        };
        menu.Items.Add(create);
    }
}
