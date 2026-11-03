using Avalonia.Controls;
using Avalonia.Interactivity;
using GZJam.App.ViewModels;

namespace GZJam.App.Views;

/// <summary>
/// A song's info (#129). Not modal, so the decks are a click away while it's open, during a
/// night too. One at a time: another song's info takes its place. Escape closes it.
/// </summary>
public partial class SongInfoWindow : Window
{
    public SongInfoWindow()
    {
        InitializeComponent();
        RevealButton.Content = FileManager.RevealLabel.ToUpperInvariant();
    }

    public SongInfoWindow(SongInfoViewModel vm) : this()
    {
        DataContext = vm;
        RevealButton.IsEnabled = vm.Problem is null;
        Opened += async (_, _) => await vm.LoadAsync();
    }

    internal SongInfoViewModel Vm => (SongInfoViewModel)DataContext!;

    private void OnRevealClick(object? sender, RoutedEventArgs e) => FileManager.Reveal(Vm.Location);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
