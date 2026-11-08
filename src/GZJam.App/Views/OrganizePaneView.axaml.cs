using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>One side of Organize music (#325): a folder's rows, opened and heard as in the song list.</summary>
public partial class OrganizePaneView : UserControl
{
    public OrganizePaneView()
    {
        InitializeComponent();
        SongList.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is OrganizeItem item && item.IsFolder)
                _ = Pane?.GoToAsync(item.Path);
            else if ((e.Source as Control)?.DataContext is OrganizeItem song)
                Organize?.Preview(song);
        };
        SongList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Back)
                _ = Pane?.UpAsync();
            else if (e.Key == Key.Enter && SongList.SelectedItem is OrganizeItem { IsFolder: true } folder)
                _ = Pane?.GoToAsync(folder.Path);
            else if (e.Key == Key.Space && SongList.SelectedItem is OrganizeItem { IsSong: true } song)
                Organize?.Preview(song);
            else
                return;
            e.Handled = true;
        };
    }

    private OrganizePane? Pane => DataContext as OrganizePane;

    /// <summary>The window's view model: set by the window.</summary>
    internal OrganizeViewModel? Organize { get; set; }

    private void OnCrumbClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is OrganizeCrumb crumb)
            _ = Pane?.GoToAsync(crumb.Path);
    }

    private void OnPreviewClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is OrganizeItem item)
            Organize?.Preview(item);
    }
}
