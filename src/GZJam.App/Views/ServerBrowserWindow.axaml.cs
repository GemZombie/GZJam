using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>
/// A music server's songs, in a window of their own beside GZJam (#284). A double-click (or
/// Enter) opens an artist or album, or previews a song.
/// </summary>
public partial class ServerBrowserWindow : Window
{
    public ServerBrowserWindow()
    {
        InitializeComponent();
        RowList.DoubleTapped += async (_, _) => await OpenSelectedAsync();
        RowList.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await OpenSelectedAsync();
            }
            else if (e.Key == Key.Back && Vm is { CanGoBack: true } vm)
            {
                e.Handled = true;
                vm.GoBack();
            }
        };
        Opened += async (_, _) =>
        {
            SearchBox.Focus();
            if (Vm is { } vm)
                await vm.ShowHomeAsync();
        };
    }

    public ServerBrowserWindow(ServerBrowserViewModel vm) : this() => DataContext = vm;

    private static readonly Dictionary<long, ServerBrowserWindow> Open = [];

    /// <summary>Shows the server's browser beside <paramref name="owner"/>: the one already open, or a new one.</summary>
    public static ServerBrowserWindow Show(Window owner, ServersViewModel servers, ServerItemViewModel server)
    {
        if (Open.TryGetValue(server.Server.Id, out ServerBrowserWindow? open))
        {
            open.Activate();
            return open;
        }
        var window = new ServerBrowserWindow(new ServerBrowserViewModel(servers, server));
        Open[server.Server.Id] = window;
        window.Closed += (_, _) => Open.Remove(server.Server.Id);
        window.Show(owner);
        return window;
    }

    private ServerBrowserViewModel? Vm => DataContext as ServerBrowserViewModel;

    private IEnumerable<BrowseRow> Selected => RowList.SelectedItems?.OfType<BrowseRow>().ToList() ?? [];

    private async Task OpenSelectedAsync()
    {
        if (Vm is { } vm && RowList.SelectedItem is BrowseRow row)
            await vm.OpenAsync(row);
    }

    private void OnBackClick(object? sender, RoutedEventArgs e) => Vm?.GoBack();

    private async void OnPreviewClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && Selected.FirstOrDefault(r => r.Kind == BrowseRowKind.Song) is { } song)
            await vm.PreviewAsync(song);
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && Selected.ToList() is { Count: > 0 } rows)
            await vm.DownloadAsync(rows);
    }

    private async void OnDownloadAllClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
            await vm.DownloadAllAsync();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
